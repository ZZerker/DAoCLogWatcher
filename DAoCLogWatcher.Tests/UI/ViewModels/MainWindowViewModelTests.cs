using DAoCLogWatcher.Core;
using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Models;
using DAoCLogWatcher.UI.Services;
using DAoCLogWatcher.UI.ViewModels;
using FluentAssertions;
using NSubstitute;

namespace DAoCLogWatcher.Tests.UI.ViewModels;

/// <summary>
/// Unit tests for MainWindowViewModel.
/// All dependencies are substituted; Avalonia's Dispatcher is never invoked because
/// the mock IWatchSession.RunAsync returns Task.CompletedTask without calling the
/// onLine/onRpsRefresh lambdas.
/// </summary>
public sealed class MainWindowViewModelTests: IDisposable
{
	private readonly IWatchSession mockSession = Substitute.For<IWatchSession>();
	private readonly IRealmPointProcessor mockProcessor = Substitute.For<IRealmPointProcessor>();
	private readonly ICombatProcessor mockCombat = Substitute.For<ICombatProcessor>();
	private readonly IUpdateService mockUpdateService = Substitute.For<IUpdateService>();
	private readonly INotificationService mockNotify = Substitute.For<INotificationService>();
	private readonly IDaocLogPathService mockPathService = Substitute.For<IDaocLogPathService>();
	private readonly ISettingsService mockSettingsService = Substitute.For<ISettingsService>();
	private readonly ILogWatcherFactory mockLogWatcherFactory = Substitute.For<ILogWatcherFactory>();

	public MainWindowViewModelTests()
	{
		// Prevent null-task from CheckForUpdatesAsync fire-and-forget in constructor
		this.mockUpdateService.CheckForUpdatesAsync().Returns(Task.FromResult<(string?, bool)>((null, false)));

		// Default: RunAsync completes immediately so no Dispatcher is touched
		this.mockSession.RunAsync(Arg.Any<LogWatcher>(), Arg.Any<Action>(), Arg.Any<Func<LogLine, Task>>()).Returns(Task.CompletedTask);
	}

	private MainWindowViewModel Build(AppSettings? settings = null)
	{
		return new MainWindowViewModel(this.mockSession,
		                               this.mockProcessor,
		                               this.mockCombat,
		                               this.mockUpdateService,
		                               this.mockNotify,
		                               this.mockPathService,
		                               this.mockSettingsService,
		                               this.mockLogWatcherFactory,
		                               settings ?? new AppSettings(),
		                               new RealmPointSummary(),
		                               new RpsChartData(),
		                               new CombatSummary());
	}

	public void Dispose()
	{
	}

	// ── Construction ─────────────────────────────────────────────────────────

	[Fact]
	public void Constructor_LoadsHighlightSettingsFromAppSettings()
	{
		var settings = new AppSettings
		               {
				               HighlightMultiKills = false,
				               HighlightMultiHits = false
		               };

		var vm = this.Build(settings);

		vm.HighlightMultiKills.Should().BeFalse();
		vm.HighlightMultiHits.Should().BeFalse();
	}

	[Fact]
	public void Constructor_LoadsTabVisibilityFromAppSettings()
	{
		var settings = new AppSettings
		               {
				               ShowRealmPointsTab = false,
				               ShowCombatTab = false,
				               ShowHealLogTab = false,
				               ShowCombatLogTab = false
		               };

		var vm = this.Build(settings);

		vm.RealmPointsTab.Value.Should().BeFalse();
		vm.CombatTab.Value.Should().BeFalse();
		vm.HealLogTab.Value.Should().BeFalse();
		vm.CombatLogTab.Value.Should().BeFalse();
	}

	[Fact]
	public void Constructor_LoadsCustomChatLogPathFromAppSettings()
	{
		var settings = new AppSettings
		               {
				               CustomChatLogPath = @"C:\Logs\chat.log"
		               };

		var vm = this.Build(settings);

		vm.CustomChatLogPath.Should().Be(@"C:\Logs\chat.log");
	}

	// ── Watch lifecycle ───────────────────────────────────────────────────────

	[Fact]
	public async Task StartWatching_WithCurrentFilePath_CallsRunAsync()
	{
		var vm = this.Build();
		vm.CurrentFilePath = "any_path";

		await vm.StartWatchingCommand.ExecuteAsync(null);

		await this.mockSession.Received(1).RunAsync(Arg.Any<LogWatcher>(), Arg.Any<Action>(), Arg.Any<Func<LogLine, Task>>());
	}

	[Fact]
	public async Task StartWatching_WithNoFilePath_DoesNotCallRunAsync()
	{
		var vm = this.Build();

		// CurrentFilePath is null by default

		await vm.StartWatchingCommand.ExecuteAsync(null);

		await this.mockSession.DidNotReceive().RunAsync(Arg.Any<LogWatcher>(), Arg.Any<Action>(), Arg.Any<Func<LogLine, Task>>());
	}

	[Fact]
	public async Task StartWatching_WhileAlreadyWatching_DoesNotCallRunAsyncAgain()
	{
		// First call completes only after we release it
		var tcs = new TaskCompletionSource();
		this.mockSession.RunAsync(Arg.Any<LogWatcher>(), Arg.Any<Action>(), Arg.Any<Func<LogLine, Task>>()).Returns(tcs.Task);

		var vm = this.Build();
		vm.CurrentFilePath = "path";

		var first = vm.StartWatchingCommand.ExecuteAsync(null);

		// IsWatching is true while first run is in flight
		var secondTask = vm.StartWatchingCommand.ExecuteAsync(null);

		tcs.SetResult();
		await Task.WhenAll(first, secondTask);

		await this.mockSession.Received(1).RunAsync(Arg.Any<LogWatcher>(), Arg.Any<Action>(), Arg.Any<Func<LogLine, Task>>());
	}

	[Fact]
	public void StopWatching_CallsRequestStop()
	{
		var vm = this.Build();

		vm.StopWatchingCommand.Execute(null);

		this.mockSession.Received(1).RequestStop();
	}

	[Fact]
	public void StopWatching_SetsIsWatchingFalse()
	{
		var vm = this.Build();

		vm.StopWatchingCommand.Execute(null);

		vm.IsWatching.Should().BeFalse();
	}

	[Fact]
	public void StopWatching_ResetsCollections()
	{
		var vm = this.Build();

		// Add a fake entry to verify Reset clears it
		vm.RpLog.Items.Add(new RealmPointLogEntry
		                   {
				                   Timestamp = "20:00:00",
				                   Points = 100,
				                   Source = "Player Kill",
				                   Details = "test"
		                   });

		vm.StopWatchingCommand.Execute(null);

		vm.RpLog.Items.Should().BeEmpty();
	}

	// ── Path discovery ────────────────────────────────────────────────────────

	[Fact]
	public async Task OpenDaocLog_WhenPathServiceReturnsNull_DoesNotStartWatching()
	{
		this.mockPathService.FindDaocLogPath().Returns((string?)null);
		var vm = this.Build();

		await vm.OpenDaocLogCommand.ExecuteAsync(null);

		await this.mockSession.DidNotReceive().RunAsync(Arg.Any<LogWatcher>(), Arg.Any<Action>(), Arg.Any<Func<LogLine, Task>>());
	}

	[Fact]
	public async Task OpenDaocLog_WhenCustomPathSetButFileMissing_FallsBackToPathService()
	{
		// "missing_path" does not exist on disk — VM must consult the path service
		this.mockPathService.FindDaocLogPath().Returns((string?)null);

		var vm = this.Build(new AppSettings
		                    {
				                    CustomChatLogPath = "missing_path"
		                    });

		await vm.OpenDaocLogCommand.ExecuteAsync(null);

		this.mockPathService.Received(1).FindDaocLogPath();
	}

	// ── KD ratio ─────────────────────────────────────────────────────────────

	[Fact]
	public void KdRatio_WhenNoDeaths_EqualsKills()
	{
		var vm = this.Build();
		vm.Kills = 5;

		vm.KdRatio.Should().Be(5.0);
	}

	[Fact]
	public void KdRatio_WhenDeathsGtZero_IsKillsDividedByDeaths()
	{
		var vm = this.Build();
		vm.Kills = 10;
		vm.Deaths = 4;

		vm.KdRatio.Should().BeApproximately(2.5, 0.001);
	}

	[Fact]
	public void KdRatio_PropagatesWhenKillsChange()
	{
		var vm = this.Build();
		vm.Deaths = 2;

		using var monitor = vm.Monitor();
		vm.Kills = 6;

		monitor.Should().RaisePropertyChangeFor(x => x.KdRatio);
	}

	[Fact]
	public void KdRatio_PropagatesWhenDeathsChange()
	{
		var vm = this.Build();
		vm.Kills = 6;

		using var monitor = vm.Monitor();
		vm.Deaths = 2;

		monitor.Should().RaisePropertyChangeFor(x => x.KdRatio);
	}

	// ── TimeFilter integration ────────────────────────────────────────────────

	[Fact]
	public async Task TimeFilterChanged_WhenWatching_CallsStopAndWaitAsync()
	{
		var tcs = new TaskCompletionSource();
		this.mockSession.RunAsync(Arg.Any<LogWatcher>(), Arg.Any<Action>(), Arg.Any<Func<LogLine, Task>>()).Returns(tcs.Task, Task.CompletedTask); // first call blocks, second completes

		var vm = this.Build();
		vm.CurrentFilePath = "path";

		var watchTask = vm.StartWatchingCommand.ExecuteAsync(null);

		// IsWatching = true while first RunAsync is in flight

		// Trigger a filter change
		vm.TimeFilter.SelectedTimeFilterIndex = 1;

		// Simulate stop completing so restart can proceed
		tcs.SetResult();

		await watchTask;

		// Allow the fire-and-forget RestartAsync to settle
		await Task.Yield();

		await this.mockSession.Received(1).StopAndWaitAsync();
	}
}
