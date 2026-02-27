using System;
using System.Collections.ObjectModel;
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
using DAoCLogWatcher.UI.Services;

namespace DAoCLogWatcher.UI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
	private CancellationTokenSource? cancellationTokenSource;
	private System.Timers.Timer? rpsRefreshTimer;
	private LogWatcher? logWatcher;

	private readonly UpdateService updateService = new();
	private readonly RealmPointProcessor processor;

	[ObservableProperty] private string? currentFilePath;

	[ObservableProperty] [NotifyPropertyChangedFor(nameof(IsAnyFilterEnabled))] private bool enableSixHourFiltering;
	[ObservableProperty] [NotifyPropertyChangedFor(nameof(IsAnyFilterEnabled))] private bool enableTwelveHourFiltering;
	[ObservableProperty] [NotifyPropertyChangedFor(nameof(IsAnyFilterEnabled))] private bool enableTimeFiltering = true;

	[ObservableProperty] private bool isWatching;

	[ObservableProperty] private bool isDarkTheme = true;
	public string ThemeIcon => this.IsDarkTheme ? "☀ Light" : "🌙 Dark";
	partial void OnIsDarkThemeChanged(bool value) => this.OnPropertyChanged(nameof(this.ThemeIcon));

	[RelayCommand]
	private void ToggleTheme() => this.IsDarkTheme = !this.IsDarkTheme;

	[ObservableProperty] private bool isSidebarVisible = true;
	public string SidebarToggleIcon => this.IsSidebarVisible ? "◀ Summary" : "▶ Summary";
	partial void OnIsSidebarVisibleChanged(bool value) => this.OnPropertyChanged(nameof(this.SidebarToggleIcon));

	[RelayCommand]
	private void ToggleSidebar() => this.IsSidebarVisible = !this.IsSidebarVisible;

	[ObservableProperty] private bool isRpsChartVisible = true;
	public string RpsChartToggleIcon => this.IsRpsChartVisible ? "▲" : "▼";
	partial void OnIsRpsChartVisibleChanged(bool value) => this.OnPropertyChanged(nameof(this.RpsChartToggleIcon));

	[RelayCommand]
	private void ToggleRpsChart() => this.IsRpsChartVisible = !this.IsRpsChartVisible;

	[ObservableProperty] private bool isRpChartVisible = true;
	public string RpChartToggleIcon => this.IsRpChartVisible ? "▲" : "▼";
	partial void OnIsRpChartVisibleChanged(bool value) => this.OnPropertyChanged(nameof(this.RpChartToggleIcon));

	[RelayCommand]
	private void ToggleRpChart() => this.IsRpChartVisible = !this.IsRpChartVisible;

	[ObservableProperty] private bool isAbsoluteNumbersVisible = true;
	public string AbsoluteNumbersToggleIcon => this.IsAbsoluteNumbersVisible ? "▲" : "▼";
	partial void OnIsAbsoluteNumbersVisibleChanged(bool value) => this.OnPropertyChanged(nameof(this.AbsoluteNumbersToggleIcon));

	[RelayCommand]
	private void ToggleAbsoluteNumbers() => this.IsAbsoluteNumbersVisible = !this.IsAbsoluteNumbersVisible;

	[ObservableProperty] private bool isAbsoluteRpsVisible = true;
	public string AbsoluteRpsToggleIcon => this.IsAbsoluteRpsVisible ? "▲" : "▼";
	partial void OnIsAbsoluteRpsVisibleChanged(bool value) => this.OnPropertyChanged(nameof(this.AbsoluteRpsToggleIcon));

	[RelayCommand]
	private void ToggleAbsoluteRps() => this.IsAbsoluteRpsVisible = !this.IsAbsoluteRpsVisible;

	[ObservableProperty] private bool isPercentagesVisible = true;
	public string PercentagesToggleIcon => this.IsPercentagesVisible ? "▲" : "▼";
	partial void OnIsPercentagesVisibleChanged(bool value) => this.OnPropertyChanged(nameof(this.PercentagesToggleIcon));

	[RelayCommand]
	private void TogglePercentages() => this.IsPercentagesVisible = !this.IsPercentagesVisible;

	[ObservableProperty] private bool isUpdateAvailable;
	[ObservableProperty] private string? updateVersionText;

	[ObservableProperty] private string? detectedCharacterName;
	[ObservableProperty] private int kills;
	[ObservableProperty] private int deaths;
	public double KdRatio => this.Deaths > 0 ? (double)this.Kills / this.Deaths : this.Kills;
	partial void OnKillsChanged(int value)  => this.OnPropertyChanged(nameof(this.KdRatio));
	partial void OnDeathsChanged(int value) => this.OnPropertyChanged(nameof(this.KdRatio));

	public ObservableCollection<RealmPointLogEntry> LogEntries { get; } = [];
	public RealmPointSummary Summary { get; } = new();
	public RpsChartData ChartData { get; } = new();

	public bool IsAnyFilterEnabled => this.EnableTimeFiltering || this.EnableSixHourFiltering || this.EnableTwelveHourFiltering;

	public MainWindowViewModel()
	{
		this.processor = new RealmPointProcessor(this.Summary, this.ChartData);
		_ = this.CheckForUpdatesAsync();
	}

	partial void OnEnableSixHourFilteringChanged(bool value)
	{
		if (value) { this.EnableTimeFiltering = false; this.EnableTwelveHourFiltering = false; }
	}

	partial void OnEnableTwelveHourFilteringChanged(bool value)
	{
		if (value) { this.EnableTimeFiltering = false; this.EnableSixHourFiltering = false; }
	}

	partial void OnEnableTimeFilteringChanged(bool value)
	{
		if (value) { this.EnableSixHourFiltering = false; this.EnableTwelveHourFiltering = false; }
	}

	[RelayCommand]
	private async Task OpenDaocLog()
	{
		var path = DaocLogPathService.FindDaocLogPath();
		if (path != null && File.Exists(path))
		{
			this.CurrentFilePath = path;
			await this.StartWatching();
		}
	}

	[RelayCommand]
	private async Task OpenFile(IStorageProvider? storageProvider)
	{
		if (storageProvider == null) return;

		var options = new FilePickerOpenOptions
		{
			Title = "Select DAoC Log File",
			AllowMultiple = false,
			FileTypeFilter =
			[
				new FilePickerFileType("Log Files") { Patterns = ["*.log"] },
				new FilePickerFileType("All Files") { Patterns = ["*.*"] }
			]
		};

		var result = await storageProvider.OpenFilePickerAsync(options);
		if (result.Count > 0)
		{
			this.CurrentFilePath = result[0].Path.LocalPath;
			await this.StartWatching();
		}
	}

	private void ProcessLogLine(LogLine logLine)
	{
		var entry = this.processor.Process(
			logLine,
			this.logWatcher?.CurrentSessionStart,
			out var characterChanged,
			out var killStatsChanged);

		if (characterChanged)  this.DetectedCharacterName = this.processor.DetectedCharacterName;
		if (killStatsChanged)  { this.Kills = this.processor.Kills; this.Deaths = this.processor.Deaths; }

		if (entry is not null)
		{
			this.LogEntries.Insert(0, entry);
			while (this.LogEntries.Count > 1000)
				this.LogEntries.RemoveAt(this.LogEntries.Count - 1);
		}
	}

	[RelayCommand]
	private async Task StartWatching()
	{
		if (string.IsNullOrWhiteSpace(this.CurrentFilePath) || this.IsWatching) return;

		this.IsWatching = true;
		this.Summary.Reset();
		this.LogEntries.Clear();
		this.ChartData.Reset();
		this.processor.Reset();
		this.DetectedCharacterName = null;
		this.Kills = 0;
		this.Deaths = 0;

		var cts = new CancellationTokenSource();
		this.cancellationTokenSource = cts;

		this.rpsRefreshTimer = new System.Timers.Timer(5000);
		this.rpsRefreshTimer.Elapsed += (s, e) => Dispatcher.UIThread.InvokeAsync(() => this.Summary.RefreshRpsPerHour());
		this.rpsRefreshTimer.AutoReset = true;
		this.rpsRefreshTimer.Start();

		var filterEnabled = this.EnableTimeFiltering || this.EnableTwelveHourFiltering || this.EnableSixHourFiltering;
		var filterHours   = this.EnableSixHourFiltering ? 6 : this.EnableTwelveHourFiltering ? 12 : 24;
		this.logWatcher = new LogWatcher(this.CurrentFilePath, 0, filterEnabled, filterHours);

		try
		{
			await foreach (var logLine in this.logWatcher.WatchAsync(cts.Token))
			{
				var capturedLine = logLine;
				await Dispatcher.UIThread.InvokeAsync(
					() => this.ProcessLogLine(capturedLine),
					DispatcherPriority.Normal);
			}
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) { _ = ex; }
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
		var (text, available) = await this.updateService.CheckForUpdatesAsync();
		this.UpdateVersionText = text;
		this.IsUpdateAvailable = available;
	}

	[RelayCommand]
	private void DismissUpdate() => this.IsUpdateAvailable = false;

	[RelayCommand]
	private void ApplyUpdateAndRestart() => this.updateService.ApplyAndRestart();
}
