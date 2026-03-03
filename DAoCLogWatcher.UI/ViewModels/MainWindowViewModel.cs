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
	private readonly AppSettings settings = SettingsService.Load();
	private readonly RealmPointProcessor processor;
	private readonly CombatProcessor combatProcessor;

	[ObservableProperty] private string? currentFilePath;

	// 0=All time  1=1 week  2=48h  3=24h  4=12h  5=6h  6=3h  7=2h  8=1h  9=Custom
	[ObservableProperty] private int selectedTimeFilterIndex = 3;
	private int previousFilterIndex = 3;
	private double customTotalHours = 1;
	[ObservableProperty] private bool isCustomPopupVisible;
	[ObservableProperty] private bool isSettingsPopupVisible;

	[ObservableProperty] private bool highlightMultiKills;
	partial void OnHighlightMultiKillsChanged(bool value)
	{
		this.settings.HighlightMultiKills = value;
		SettingsService.Save(this.settings);
	}

	[ObservableProperty] private string? customChatLogPath;
	partial void OnCustomChatLogPathChanged(string? value)
	{
		this.settings.CustomChatLogPath = string.IsNullOrWhiteSpace(value) ? null : value;
		SettingsService.Save(this.settings);
	}

	[RelayCommand]
	private async Task BrowseChatLogPath(IStorageProvider? storageProvider)
	{
		if (storageProvider == null) return;
		var options = new FilePickerOpenOptions
		{
			Title = "Select DAoC Chat Log",
			AllowMultiple = false,
			FileTypeFilter =
			[
				new FilePickerFileType("Log Files") { Patterns = ["*.log"] },
				new FilePickerFileType("All Files") { Patterns = ["*.*"] }
			]
		};
		var result = await storageProvider.OpenFilePickerAsync(options);
		if (result.Count > 0)
			this.CustomChatLogPath = result[0].Path.LocalPath;
	}

	[RelayCommand]
	private void ClearChatLogPath() => this.CustomChatLogPath = null;

	[RelayCommand]
	private void ToggleSettingsPopup() => this.IsSettingsPopupVisible = !this.IsSettingsPopupVisible;

	[RelayCommand]
	private void CloseSettingsPopup() => this.IsSettingsPopupVisible = false;
	[ObservableProperty] private decimal? customInputHours = 1;
	[ObservableProperty] private decimal? customInputMinutes = 0;

	[ObservableProperty] private bool isWatching;

	[ObservableProperty] private bool isDarkTheme = true;
	public string ThemeIcon => this.IsDarkTheme ? "☀ Light" : "🌙 Dark";
	partial void OnIsDarkThemeChanged(bool value) => this.OnPropertyChanged(nameof(this.ThemeIcon));

	[RelayCommand]
	private void ToggleTheme() => this.IsDarkTheme = !this.IsDarkTheme;

	[ObservableProperty] private bool isSidebarVisible = true;
	public string SidebarToggleIcon => "◀";
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

	[ObservableProperty] private bool isAvgDmgChartVisible = true;
	public string AvgDmgChartToggleIcon => this.IsAvgDmgChartVisible ? "▲" : "▼";
	partial void OnIsAvgDmgChartVisibleChanged(bool value) => this.OnPropertyChanged(nameof(this.AvgDmgChartToggleIcon));

	[RelayCommand]
	private void ToggleAvgDmgChart() => this.IsAvgDmgChartVisible = !this.IsAvgDmgChartVisible;

	[ObservableProperty] private bool isDmgByAttackerChartVisible = true;
	public string DmgByAttackerChartToggleIcon => this.IsDmgByAttackerChartVisible ? "▲" : "▼";
	partial void OnIsDmgByAttackerChartVisibleChanged(bool value) => this.OnPropertyChanged(nameof(this.DmgByAttackerChartToggleIcon));

	[RelayCommand]
	private void ToggleDmgByAttackerChart() => this.IsDmgByAttackerChartVisible = !this.IsDmgByAttackerChartVisible;

	[ObservableProperty] private bool isHealsByHealerChartVisible = true;
	public string HealsByHealerChartToggleIcon => this.IsHealsByHealerChartVisible ? "▲" : "▼";
	partial void OnIsHealsByHealerChartVisibleChanged(bool value) => this.OnPropertyChanged(nameof(this.HealsByHealerChartToggleIcon));

	[RelayCommand]
	private void ToggleHealsByHealerChart() => this.IsHealsByHealerChartVisible = !this.IsHealsByHealerChartVisible;

	[ObservableProperty] private bool isHealsByTargetChartVisible = true;
	public string HealsByTargetChartToggleIcon => this.IsHealsByTargetChartVisible ? "▲" : "▼";
	partial void OnIsHealsByTargetChartVisibleChanged(bool value) => this.OnPropertyChanged(nameof(this.HealsByTargetChartToggleIcon));

	[RelayCommand]
	private void ToggleHealsByTargetChart() => this.IsHealsByTargetChartVisible = !this.IsHealsByTargetChartVisible;

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
	public CombatSummary CombatSummary { get; } = new();

	public MainWindowViewModel()
	{
		this.highlightMultiKills = this.settings.HighlightMultiKills;
		this.customChatLogPath   = this.settings.CustomChatLogPath;
		this.processor = new RealmPointProcessor(this.Summary, this.ChartData);
		this.processor.MultiKillDetected += this.OnMultiKillDetected;
		this.combatProcessor = new CombatProcessor(this.CombatSummary);
		_ = this.CheckForUpdatesAsync();
	}

	private bool suppressFilterChange;

	partial void OnSelectedTimeFilterIndexChanged(int value)
	{
		if (this.suppressFilterChange) return;
		if (value == 9)
		{
			this.IsCustomPopupVisible = true;
			return; // wait for Apply before restarting
		}
		this.previousFilterIndex = value;
		this.IsCustomPopupVisible = false;
		if (this.IsWatching)
			_ = this.RestartAsync();
	}

	[RelayCommand]
	private void ApplyCustomFilter()
	{
		this.customTotalHours = (double)(this.CustomInputHours ?? 0) + (double)(this.CustomInputMinutes ?? 0) / 60.0;
		if (this.customTotalHours <= 0) this.customTotalHours = 1.0 / 60; // minimum 1 min
		this.previousFilterIndex = 9;
		this.IsCustomPopupVisible = false;
		if (this.IsWatching)
			_ = this.RestartAsync();
	}

	[RelayCommand]
	private void CancelCustomFilter()
	{
		this.IsCustomPopupVisible = false;
		this.suppressFilterChange = true;
		this.SelectedTimeFilterIndex = this.previousFilterIndex;
		this.suppressFilterChange = false;
	}

	private async Task RestartAsync()
	{
		this.cancellationTokenSource?.Cancel();
		while (this.IsWatching)
			await Task.Delay(50);
		await this.StartWatching();
	}

	[RelayCommand]
	private async Task OpenDaocLog()
	{
		string? path = null;
		if (!string.IsNullOrWhiteSpace(this.CustomChatLogPath) && File.Exists(this.CustomChatLogPath))
			path = this.CustomChatLogPath;
		else
			path = DaocLogPathService.FindDaocLogPath();

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

		this.combatProcessor.Process(logLine);

		if (characterChanged)  this.DetectedCharacterName = this.processor.DetectedCharacterName;
		if (killStatsChanged)  { this.Kills = this.processor.Kills; this.Deaths = this.processor.Deaths; }

		if (entry is not null)
			this.AddLogEntry(entry);
	}

	private void OnMultiKillDetected(object? sender, RealmPointLogEntry e) => this.AddLogEntry(e);

	private void AddLogEntry(RealmPointLogEntry entry)
	{
		this.LogEntries.Insert(0, entry);
		while (this.LogEntries.Count > 1000)
			this.LogEntries.RemoveAt(this.LogEntries.Count - 1);
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
		this.combatProcessor.Reset();
		this.DetectedCharacterName = null;
		this.Kills = 0;
		this.Deaths = 0;

		var cts = new CancellationTokenSource();
		this.cancellationTokenSource = cts;

		this.rpsRefreshTimer = new System.Timers.Timer(5000);
		this.rpsRefreshTimer.Elapsed += (s, e) => Dispatcher.UIThread.InvokeAsync(() => this.Summary.RefreshRpsPerHour());
		this.rpsRefreshTimer.AutoReset = true;
		this.rpsRefreshTimer.Start();

		var filterEnabled = this.SelectedTimeFilterIndex > 0;
		var filterHours = this.SelectedTimeFilterIndex switch
		{
			1 => 168.0, // 1 week
			2 => 48.0,
			3 => 24.0,
			4 => 12.0,
			5 => 6.0,
			6 => 3.0,
			7 => 2.0,
			8 => 1.0,
			9 => this.customTotalHours,
			_ => 24.0
		};
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
