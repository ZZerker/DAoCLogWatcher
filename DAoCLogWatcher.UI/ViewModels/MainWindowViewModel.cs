using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DAoCLogWatcher.Core;
using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Models;
using Velopack;
using Velopack.Sources;

namespace DAoCLogWatcher.UI.ViewModels;

public partial class MainWindowViewModel: ViewModelBase
{
	private CancellationTokenSource? cancellationTokenSource;
	private System.Timers.Timer? rpsRefreshTimer;

	[ObservableProperty] private string? currentFilePath;

	[ObservableProperty] [NotifyPropertyChangedFor(nameof(IsAnyFilterEnabled))] private bool enableSixHourFiltering;

	[ObservableProperty] [NotifyPropertyChangedFor(nameof(IsAnyFilterEnabled))] private bool enableTwelveHourFiltering;

	[ObservableProperty] [NotifyPropertyChangedFor(nameof(IsAnyFilterEnabled))] private bool enableTimeFiltering = true;

	[ObservableProperty] private bool isWatching;

	[ObservableProperty] private bool isDarkTheme = true;
	public string ThemeIcon => this.IsDarkTheme ? "☀ Light" : "🌙 Dark";
	partial void OnIsDarkThemeChanged(bool _) => this.OnPropertyChanged(nameof(this.ThemeIcon));

	[RelayCommand]
	private void ToggleTheme() => this.IsDarkTheme = !this.IsDarkTheme;

	[ObservableProperty] private bool isSidebarVisible = true;
	public string SidebarToggleIcon => this.IsSidebarVisible ? "◀ Summary" : "▶ Summary";
	partial void OnIsSidebarVisibleChanged(bool _) => this.OnPropertyChanged(nameof(this.SidebarToggleIcon));

	[RelayCommand]
	private void ToggleSidebar() => this.IsSidebarVisible = !this.IsSidebarVisible;

	[ObservableProperty] private bool isRpsChartVisible = true;
	public string RpsChartToggleIcon => this.IsRpsChartVisible ? "▲" : "▼";
	partial void OnIsRpsChartVisibleChanged(bool _) => this.OnPropertyChanged(nameof(this.RpsChartToggleIcon));

	[RelayCommand]
	private void ToggleRpsChart() => this.IsRpsChartVisible = !this.IsRpsChartVisible;

	[ObservableProperty] private bool isRpChartVisible = true;
	public string RpChartToggleIcon => this.IsRpChartVisible ? "▲" : "▼";
	partial void OnIsRpChartVisibleChanged(bool _) => this.OnPropertyChanged(nameof(this.RpChartToggleIcon));

	[RelayCommand]
	private void ToggleRpChart() => this.IsRpChartVisible = !this.IsRpChartVisible;

	[ObservableProperty] private bool isUpdateAvailable;
	[ObservableProperty] private string? updateVersionText;
	private UpdateInfo? pendingUpdate;

	public ObservableCollection<RealmPointLogEntry> LogEntries { get; } = [];

	private LogWatcher? logWatcher;

	public RealmPointSummary Summary { get; } = new();

	public ObservableCollection<CharacterKillStat> CharacterKillStats { get; } = [];
	private Dictionary<string, CharacterKillStat> characterKillLookup = new(StringComparer.OrdinalIgnoreCase);
	public bool HasCharacters => this.CharacterKillStats.Count > 0;

	public RpsChartData ChartData { get; } = new();

	public MainWindowViewModel()
	{
		this.CharacterKillStats.CollectionChanged += (_, _) => this.OnPropertyChanged(nameof(this.HasCharacters));
		_ = this.CheckForUpdatesAsync();
	}

	public bool IsAnyFilterEnabled => this.EnableTimeFiltering || this.EnableSixHourFiltering || this.EnableTwelveHourFiltering;

	partial void OnEnableSixHourFilteringChanged(bool value)
	{
		if (value)
		{
			this.EnableTimeFiltering = false;
			this.EnableTwelveHourFiltering = false;
		}
	}

	partial void OnEnableTwelveHourFilteringChanged(bool value)
	{
		if (value)
		{
			this.EnableTimeFiltering = false;
			this.EnableSixHourFiltering = false;
		}
	}

	partial void OnEnableTimeFilteringChanged(bool value)
	{
		if (value)
		{
			this.EnableSixHourFiltering = false;
			this.EnableTwelveHourFiltering = false;
		}
	}

	[RelayCommand]
	private async Task OpenDaocLog()
	{
		var path = FindDaocLogPath();

		if (path != null && File.Exists(path))
		{
			this.CurrentFilePath = path;
			await this.StartWatching();
		}
	}

	private static string? FindDaocLogPath()
	{
		if (OperatingSystem.IsWindows())
		{
			var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
			return Path.Combine(documents, "Electronic Arts", "Dark Age of Camelot", "chat.log");
		}

		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

		var candidates = new[]
		{
			// Default Wine prefix
			Path.Combine(home, ".wine", "drive_c", "users", Environment.UserName, "My Documents", "Electronic Arts", "Dark Age of Camelot", "chat.log"),
			// Lutris default Wine prefix
			Path.Combine(home, "Games", "dark-age-of-camelot", "drive_c", "users", Environment.UserName, "My Documents", "Electronic Arts", "Dark Age of Camelot", "chat.log"),
			// Lutris with "Documents" folder name
			Path.Combine(home, "Games", "dark-age-of-camelot", "drive_c", "users", Environment.UserName, "Documents", "Electronic Arts", "Dark Age of Camelot", "chat.log"),
			// Lutris with "Documents" folder name
			Path.Combine(home, "Games", "dark-age-of-camelot-eden", "drive_c", "users", Environment.UserName, "Documents", "Electronic Arts", "Dark Age of Camelot", "chat.log"),
			// Default Users Home  "Documents" folder name
			Path.Combine(home, "Documents", "Electronic Arts", "chat.log"),
			// Default Users Home  "Documents" folder name (German)
			Path.Combine(home, "Dokumente", "Electronic Arts", "chat.log"),
		};

		return Array.Find(candidates, File.Exists);
	}

	[RelayCommand]
	private async Task OpenFile(IStorageProvider? storageProvider)
	{
		if(storageProvider == null)
		{
			return;
		}

		var options = new FilePickerOpenOptions
		              {
				              Title = "Select DAoC Log File",
				              AllowMultiple = false,
				              FileTypeFilter =
				              [
						              new FilePickerFileType("Log Files")
						                               {
								                               Patterns =
								                               [
										                               "*.log"
								                               ]
						                               },
						                               new FilePickerFileType("All Files")
						                               {
								                               Patterns =
								                               [
										                               "*.*"
								                               ]
						                               }
				              ]
		              };

		var result = await storageProvider.OpenFilePickerAsync(options);

		if(result.Count > 0)
		{
			var file = result[0];
			this.CurrentFilePath = file.Path.LocalPath;
			await this.StartWatching();
		}
	}

	private void ProcessLogLine(LogLine logLine)
	{
		var entry = logLine.RealmPointEntry;

		if(entry == null)
		{
			return;
		}

		Console.WriteLine($"[ProcessLogLine] Realm point entry: {entry.Points} RP, Source: {entry.Source}");

		this.Summary.TotalRealmPoints += entry.Points;

		var sessionStart = this.logWatcher?.CurrentSessionStart;
		var sessionDate = sessionStart?.Date ?? DateTime.Now.Date;
		var entryDateTime = sessionDate.Add(entry.Timestamp.ToTimeSpan());
		// Handle midnight crossing: if the reconstructed time is before the session start, the entry is from the next day
		if (sessionStart.HasValue && entryDateTime < sessionStart.Value)
			entryDateTime = entryDateTime.AddDays(1);
		this.Summary.FirstEntryTime ??= entryDateTime;
		this.Summary.LastEntryTime = entryDateTime;

		switch(entry.Source)
		{
			case RealmPointSource.PlayerKill:
				this.Summary.PlayerKills++;
				this.Summary.PlayerKillsRP += entry.Points;
				if (entry.PlayerName != null && this.characterKillLookup.TryGetValue(entry.PlayerName, out var stat))
				{
					if (stat.KillCount == 0)
					{
						this.CharacterKillStats.Add(stat);
					}
					stat.KillCount++;
				}
				break;
			case RealmPointSource.CampaignQuest:
				this.Summary.CampaignQuests++;
				this.Summary.CampaignQuestsRP += entry.Points;
				break;
			case RealmPointSource.Tick:
				this.Summary.Ticks++;
				this.Summary.TicksRP += entry.Points;
				break;
			case RealmPointSource.Siege:
				this.Summary.Siege++;
				this.Summary.SiegeRP += entry.Points;
				break;
			case RealmPointSource.AssaultOrder:
				this.Summary.AssaultOrder++;
				this.Summary.AssaultOrderRP += entry.Points;
				break;
			case RealmPointSource.SupportActivity:
				this.Summary.SupportActivity++;
				this.Summary.SupportActivityRP += entry.Points;
				break;
			case RealmPointSource.RelicCapture:
				this.Summary.RelicCapture++;
				this.Summary.RelicCaptureRP += entry.Points;
				break;
			case RealmPointSource.Misc:
				this.Summary.Misc++;
				this.Summary.MiscRP += entry.Points;
				break;
			case RealmPointSource.WarSupplies:
				this.Summary.Warsupplies++;
				this.Summary.WarsuppliesRP += entry.Points;
				break;
			default:
				throw new ArgumentOutOfRangeException();
		}


		Console.WriteLine($"[ProcessLogLine] Creating log entry for recognized event: {entry.Source}");
		var details = entry.Source switch
		{
				RealmPointSource.PlayerKill => $"Player Kill{(entry.PlayerName != null?$": {entry.PlayerName}":"")}",
				RealmPointSource.CampaignQuest => "Campaign Quest completed",
				RealmPointSource.Tick => "Battle Tick",
				RealmPointSource.Siege => "Siege (Tower/Keep Capture)",
				RealmPointSource.AssaultOrder => "Assault Order",
				RealmPointSource.SupportActivity => "Support Tick",
				RealmPointSource.RelicCapture => "Relic Capture",
				RealmPointSource.WarSupplies => "War Supplies",
				RealmPointSource.Misc => "Other",
				_ => throw new UnreachableException()
		};

		var logEntry = new RealmPointLogEntry
		               {
				               Timestamp = entry.Timestamp.ToString("HH:mm:ss"),
				               Points = entry.Points,
				               Source = entry.Source.ToString(),
				               Details = details
		               };

		Console.WriteLine($"[ProcessLogLine] Adding log entry: {logEntry.Timestamp} | {logEntry.Points} RP | {logEntry.Source} | {logEntry.Details}");
		this.LogEntries.Insert(0, logEntry);
		Console.WriteLine($"[ProcessLogLine] LogEntries.Count is now: {this.LogEntries.Count}");

		// Keep only the last 1000 entries to prevent performance issues
		while(this.LogEntries.Count > 1000)
		{
			this.LogEntries.RemoveAt(this.LogEntries.Count - 1);
		}

		this.ChartData.Add(entryDateTime, this.Summary.TotalRealmPoints, entry.Points);
	}

	[RelayCommand]
	private async Task StartWatching()
	{
		if(string.IsNullOrWhiteSpace(this.CurrentFilePath)||this.IsWatching)
		{
			return;
		}

		this.IsWatching = true;
		this.Summary.Reset();
		this.LogEntries.Clear();
		this.ChartData.Reset();

		this.CharacterKillStats.Clear();
		this.characterKillLookup.Clear();
		foreach (var name in CharacterDiscoveryService.GetCharacterNames())
		{
			this.characterKillLookup[name] = new CharacterKillStat { Name = name };
		}

		var cts = new CancellationTokenSource();
		this.cancellationTokenSource = cts;

		this.rpsRefreshTimer = new System.Timers.Timer(5000);
		this.rpsRefreshTimer.Elapsed += (s, e) => Dispatcher.UIThread.InvokeAsync(() => this.Summary.RefreshRpsPerHour());
		this.rpsRefreshTimer.AutoReset = true;
		this.rpsRefreshTimer.Start();

		var filterEnabled = this.EnableTimeFiltering || this.EnableTwelveHourFiltering || this.EnableSixHourFiltering;
		var filterHours = this.EnableSixHourFiltering ? 6 : this.EnableTwelveHourFiltering ? 12 : 24;
		this.logWatcher = new LogWatcher(this.CurrentFilePath, 0, filterEnabled, filterHours);

		Console.WriteLine($"[StartWatching] Starting watch on: {this.CurrentFilePath}, TimeFilter: {filterEnabled}, FilterHours: {filterHours}");

		try
		{
			await foreach(var logLine in this.logWatcher.WatchAsync(cts.Token))
			{
				var capturedLine = logLine;
				await Dispatcher.UIThread.InvokeAsync(() =>
													  {
														  this.ProcessLogLine(capturedLine);
													  },
													  DispatcherPriority.Normal);
			}
		}
		catch(OperationCanceledException)
		{
		}
		catch(Exception ex)
		{
			Console.WriteLine($"[StartWatching] Error: {ex.Message}");
		}
		finally
		{
			this.rpsRefreshTimer?.Stop();
			this.rpsRefreshTimer?.Dispose();
			this.rpsRefreshTimer = null;
			cts.Dispose();
			if (ReferenceEquals(this.cancellationTokenSource, cts))
				this.cancellationTokenSource = null;
			this.IsWatching = false;
		}
	}

	[RelayCommand]
	private void StopWatching()
	{
		this.cancellationTokenSource?.Cancel();
		this.IsWatching = false;
	}

	private async Task CheckForUpdatesAsync()
	{
		try
		{
			var mgr = new UpdateManager(
				new GithubSource("https://github.com/ZZerker/DAoCLogWatcher", null, false));

			if (!mgr.IsInstalled)
				return;

			var update = await mgr.CheckForUpdatesAsync();
			if (update == null)
				return;

			await mgr.DownloadUpdatesAsync(update);

			this.pendingUpdate = update;
			this.UpdateVersionText = $"v{update.TargetFullRelease.Version} available";
			this.IsUpdateAvailable = true;
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[Update] Check failed: {ex.Message}");
		}
	}

	[RelayCommand]
	private void ApplyUpdateAndRestart()
	{
		if (this.pendingUpdate == null)
			return;

		try
		{
			var mgr = new UpdateManager(
				new GithubSource("https://github.com/ZZerker/DAoCLogWatcher", null, false));
			mgr.ApplyUpdatesAndRestart(this.pendingUpdate);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[Update] Apply failed: {ex.Message}");
		}
	}
}
