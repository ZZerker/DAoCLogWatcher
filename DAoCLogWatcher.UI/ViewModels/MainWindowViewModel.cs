using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DAoCLogWatcher.Core;
using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Models;
using DAoCLogWatcher.UI.Services;
using DAoCLogWatcher.UI.Views;

namespace DAoCLogWatcher.UI.ViewModels;

public partial class MainWindowViewModel: ViewModelBase, IDisposable
{
	private const int PARSING_DEBOUNCE_INTERVAL_MS = 1000;
	private const int SEND_NOTIFICATION_TIMEOUT_MS = 5000;
	private const int SEND_NOTIFICATION_MAX_AGE_SECONDS = 60;
	private const int LOG_ENTRIES_CAPACITY = 1000;

	private System.Timers.Timer? parsingDebounceTimer;
	private System.Timers.Timer? sendNotificationTimer;

	// Generation counter — incremented on each new watch loop start.
	// CleanupWatchState only runs if the generation matches, preventing stale
	// cleanup from an old loop from clobbering a newly-started loop's state.
	private int watchGeneration;

	private readonly IWatchSession watchSession;
	private readonly IUpdateService updateService;
	private readonly INotificationService notificationService;
	private readonly IDaocLogPathService daocLogPathService;
	private readonly AppSettings settings;
	private readonly IRealmPointProcessor processor;
	private readonly ICombatProcessor combatProcessor;

	[ObservableProperty] private string? currentFilePath;

	public TimeFilterService TimeFilter { get; } = new();

	[ObservableProperty] private bool isSettingsPopupVisible;

	[ObservableProperty] private bool highlightMultiKills;

	partial void OnHighlightMultiKillsChanged(bool value)
	{
		this.settings.HighlightMultiKills = value;
		SettingsService.Save(this.settings);
	}

	[ObservableProperty] private bool highlightMultiHits;

	partial void OnHighlightMultiHitsChanged(bool value)
	{
		this.settings.HighlightMultiHits = value;
		SettingsService.Save(this.settings);
	}

	[ObservableProperty] private string? customChatLogPath;

	partial void OnCustomChatLogPathChanged(string? value)
	{
		this.settings.CustomChatLogPath = string.IsNullOrWhiteSpace(value)?null:value;
		SettingsService.Save(this.settings);
	}

	[RelayCommand]
	private async Task BrowseChatLogPath(IStorageProvider? storageProvider)
	{
		if(storageProvider == null) return;
		var options = new FilePickerOpenOptions
		              {
				              Title = "Select DAoC Chat Log",
				              AllowMultiple = false,
				              FileTypeFilter =
				              [
						              new FilePickerFileType("Log Files")
						              {
								              Patterns = ["*.log"]
						              },
						              new FilePickerFileType("All Files")
						              {
								              Patterns = ["*.*"]
						              }
				              ]
		              };
		var result = await storageProvider.OpenFilePickerAsync(options);
		if(result.Count > 0)
		{
			this.CustomChatLogPath = result[0].Path.LocalPath;
		}
	}

	[RelayCommand]
	private void ClearChatLogPath() => this.CustomChatLogPath = null;

	[RelayCommand]
	private void ToggleSettingsPopup() => this.IsSettingsPopupVisible = !this.IsSettingsPopupVisible;

	[RelayCommand]
	private void CloseSettingsPopup() => this.IsSettingsPopupVisible = false;

	[ObservableProperty] private bool isWatching;

	[ObservableProperty] private bool isDarkTheme = true;

	public string ThemeIcon => this.IsDarkTheme?"☀ Light":"🌙 Dark";

	partial void OnIsDarkThemeChanged(bool value) => this.OnPropertyChanged(nameof(this.ThemeIcon));

	[RelayCommand]
	private void ToggleTheme() => this.IsDarkTheme = !this.IsDarkTheme;

	[ObservableProperty] private bool isSidebarVisible = true;

	public string SidebarToggleIcon => "◀";

	partial void OnIsSidebarVisibleChanged(bool value) => this.OnPropertyChanged(nameof(this.SidebarToggleIcon));

	[RelayCommand]
	private void ToggleSidebar() => this.IsSidebarVisible = !this.IsSidebarVisible;

	[ObservableProperty] private bool isRpsHourlyChartVisible = true;

	public string RpsChartToggleIcon => this.IsRpsHourlyChartVisible?"▲":"▼";

	partial void OnIsRpsHourlyChartVisibleChanged(bool value) => this.OnPropertyChanged(nameof(this.RpsChartToggleIcon));

	[RelayCommand]
	private void ToggleRpsChart() => this.IsRpsHourlyChartVisible = !this.IsRpsHourlyChartVisible;

	[ObservableProperty] private bool isCumulativeRpChartVisible = true;

	public string RpChartToggleIcon => this.IsCumulativeRpChartVisible?"▲":"▼";

	partial void OnIsCumulativeRpChartVisibleChanged(bool value) => this.OnPropertyChanged(nameof(this.RpChartToggleIcon));

	[RelayCommand]
	private void ToggleRpChart() => this.IsCumulativeRpChartVisible = !this.IsCumulativeRpChartVisible;

	[ObservableProperty] private bool isAbsoluteNumbersVisible = true;

	public string AbsoluteNumbersToggleIcon => this.IsAbsoluteNumbersVisible?"▲":"▼";

	partial void OnIsAbsoluteNumbersVisibleChanged(bool value) => this.OnPropertyChanged(nameof(this.AbsoluteNumbersToggleIcon));

	[RelayCommand]
	private void ToggleAbsoluteNumbers() => this.IsAbsoluteNumbersVisible = !this.IsAbsoluteNumbersVisible;

	[ObservableProperty] private bool isAbsoluteRpsVisible = true;

	public string AbsoluteRpsToggleIcon => this.IsAbsoluteRpsVisible?"▲":"▼";

	partial void OnIsAbsoluteRpsVisibleChanged(bool value) => this.OnPropertyChanged(nameof(this.AbsoluteRpsToggleIcon));

	[RelayCommand]
	private void ToggleAbsoluteRps() => this.IsAbsoluteRpsVisible = !this.IsAbsoluteRpsVisible;

	[ObservableProperty] private bool isPercentagesVisible = true;

	public string PercentagesToggleIcon => this.IsPercentagesVisible?"▲":"▼";

	partial void OnIsPercentagesVisibleChanged(bool value) => this.OnPropertyChanged(nameof(this.PercentagesToggleIcon));

	[RelayCommand]
	private void TogglePercentages() => this.IsPercentagesVisible = !this.IsPercentagesVisible;

	[ObservableProperty] private bool isAvgDmgChartVisible = true;

	public string AvgDmgChartToggleIcon => this.IsAvgDmgChartVisible?"▲":"▼";

	partial void OnIsAvgDmgChartVisibleChanged(bool value) => this.OnPropertyChanged(nameof(this.AvgDmgChartToggleIcon));

	[RelayCommand]
	private void ToggleAvgDmgChart() => this.IsAvgDmgChartVisible = !this.IsAvgDmgChartVisible;

	[ObservableProperty] private bool isDmgByAttackerChartVisible = true;

	public string DmgByAttackerChartToggleIcon => this.IsDmgByAttackerChartVisible?"▲":"▼";

	partial void OnIsDmgByAttackerChartVisibleChanged(bool value) => this.OnPropertyChanged(nameof(this.DmgByAttackerChartToggleIcon));

	[RelayCommand]
	private void ToggleDmgByAttackerChart() => this.IsDmgByAttackerChartVisible = !this.IsDmgByAttackerChartVisible;

	[ObservableProperty] private bool isHealsByHealerChartVisible = true;

	public string HealsByHealerChartToggleIcon => this.IsHealsByHealerChartVisible?"▲":"▼";

	partial void OnIsHealsByHealerChartVisibleChanged(bool value) => this.OnPropertyChanged(nameof(this.HealsByHealerChartToggleIcon));

	[RelayCommand]
	private void ToggleHealsByHealerChart() => this.IsHealsByHealerChartVisible = !this.IsHealsByHealerChartVisible;

	[ObservableProperty] private bool isHealsByTargetChartVisible = true;

	public string HealsByTargetChartToggleIcon => this.IsHealsByTargetChartVisible?"▲":"▼";

	partial void OnIsHealsByTargetChartVisibleChanged(bool value) => this.OnPropertyChanged(nameof(this.HealsByTargetChartToggleIcon));

	[RelayCommand]
	private void ToggleHealsByTargetChart() => this.IsHealsByTargetChartVisible = !this.IsHealsByTargetChartVisible;

	[ObservableProperty] private bool isRealmPointsTabVisible;

	partial void OnIsRealmPointsTabVisibleChanged(bool value)
	{
		this.settings.ShowRealmPointsTab = value;
		SettingsService.Save(this.settings);
	}

	[ObservableProperty] private bool isCombatTabVisible;

	partial void OnIsCombatTabVisibleChanged(bool value)
	{
		this.settings.ShowCombatTab = value;
		SettingsService.Save(this.settings);
	}

	[ObservableProperty] private bool isHealLogTabVisible;

	partial void OnIsHealLogTabVisibleChanged(bool value)
	{
		this.settings.ShowHealLogTab = value;
		SettingsService.Save(this.settings);
	}

	[ObservableProperty] private bool isCombatLogTabVisible;

	partial void OnIsCombatLogTabVisibleChanged(bool value)
	{
		this.settings.ShowCombatLogTab = value;
		SettingsService.Save(this.settings);
	}

	[ObservableProperty] private bool showSendNotifications;

	partial void OnShowSendNotificationsChanged(bool value)
	{
		this.settings.ShowSendNotifications = value;
		SettingsService.Save(this.settings);
	}

	[ObservableProperty] private bool isSendNotificationVisible;
	[ObservableProperty] private string? sendNotificationSender;
	[ObservableProperty] private string? sendNotificationMessage;

	[ObservableProperty] private bool isParsing;

	[ObservableProperty] private bool isUpdateAvailable;
	[ObservableProperty] private string? updateVersionText;

	[ObservableProperty] private string? detectedCharacterName;
	[ObservableProperty] private int kills;
	[ObservableProperty] private int deaths;
	[ObservableProperty] private int bestMultiKill;

	public double KdRatio => this.Deaths > 0?(double)this.Kills / this.Deaths:this.Kills;

	public bool HasBestMultiKill => this.BestMultiKill > 0;

	partial void OnKillsChanged(int value) => this.OnPropertyChanged(nameof(this.KdRatio));

	partial void OnDeathsChanged(int value) => this.OnPropertyChanged(nameof(this.KdRatio));

	partial void OnBestMultiKillChanged(int value) => this.OnPropertyChanged(nameof(this.HasBestMultiKill));

	partial void OnRpFilterTextChanged(string value) => this.RefreshRpFilter();
	partial void OnHealFilterTextChanged(string value) => this.RefreshHealFilter();
	partial void OnCombatFilterTextChanged(string value) => this.RefreshCombatFilter();

	[ObservableProperty] private string rpFilterText = string.Empty;
	[ObservableProperty] private string healFilterText = string.Empty;
	[ObservableProperty] private string combatFilterText = string.Empty;

	public ObservableCollection<RealmPointLogEntry> LogEntries { get; } = [];
	public ObservableCollection<RealmPointLogEntry> FilteredLogEntries { get; } = [];

	public ObservableCollection<HealLogEntry> HealLogEntries { get; } = [];
	public ObservableCollection<HealLogEntry> FilteredHealLogEntries { get; } = [];

	public ObservableCollection<CombatLogEntry> CombatLogEntries { get; } = [];
	public ObservableCollection<CombatLogEntry> FilteredCombatLogEntries { get; } = [];

	public RealmPointSummary Summary { get; }

	public RpsChartData ChartData { get; }

	public CombatSummary CombatSummary { get; }

	public MainWindowViewModel(IWatchSession watchSession,
	                           IRealmPointProcessor processor,
	                           ICombatProcessor combatProcessor,
	                           IUpdateService updateService,
	                           INotificationService notificationService,
	                           IDaocLogPathService daocLogPathService,
	                           AppSettings settings,
	                           RealmPointSummary summary,
	                           RpsChartData chartData,
	                           CombatSummary combatSummary)
	{
		this.watchSession = watchSession;
		this.notificationService = notificationService;
		this.daocLogPathService = daocLogPathService;
		this.processor = processor;
		this.combatProcessor = combatProcessor;
		this.updateService = updateService;
		this.settings = settings;
		this.Summary = summary;
		this.ChartData = chartData;
		this.CombatSummary = combatSummary;
		this.highlightMultiKills = this.settings.HighlightMultiKills;
		this.highlightMultiHits = this.settings.HighlightMultiHits;
		this.customChatLogPath = this.settings.CustomChatLogPath;
		this.showSendNotifications = this.settings.ShowSendNotifications;
		this.isRealmPointsTabVisible = this.settings.ShowRealmPointsTab;
		this.isCombatTabVisible = this.settings.ShowCombatTab;
		this.isHealLogTabVisible = this.settings.ShowHealLogTab;
		this.isCombatLogTabVisible = this.settings.ShowCombatLogTab;
		this.processor.EntryProcessed += this.OnEntryProcessed;
		this.processor.MultiKillDetected += this.OnMultiKillDetected;
		this.combatProcessor.DamageLogged += this.OnDamageLogged;
		this.combatProcessor.HealLogged += this.OnHealLogged;
		this.combatProcessor.MultiHitDetected += this.OnMultiHitDetected;
		this.TimeFilter.FilterChanged += this.OnTimeFilterChanged;
		_ = this.CheckForUpdatesAsync();
	}

	private void OnTimeFilterChanged(object? sender, EventArgs e)
	{
		if(this.IsWatching)
		{
			_ = this.RestartAsync();
		}
	}

	private async Task RestartAsync()
	{
		await this.watchSession.StopAndWaitAsync();
		if(string.IsNullOrWhiteSpace(this.CurrentFilePath)) return;
		var (filterEnabled, filterHours) = this.TimeFilter.GetFilterParameters();
		await this.RunWatchLoop(new LogWatcher(this.CurrentFilePath, 0, filterEnabled, filterHours));
	}

	[RelayCommand]
	private async Task OpenDaocLog()
	{
		string? path = null;
		if(!string.IsNullOrWhiteSpace(this.CustomChatLogPath)&&File.Exists(this.CustomChatLogPath))
		{
			path = this.CustomChatLogPath;
		}
		else
		{
			path = this.daocLogPathService.FindDaocLogPath();
		}

		if(path != null&&File.Exists(path))
		{
			this.CurrentFilePath = path;
			await this.StartWatching();
		}
	}

	[RelayCommand]
	private async Task OpenFile(IStorageProvider? storageProvider)
	{
		if(storageProvider == null) return;

		var options = new FilePickerOpenOptions
		              {
				              Title = "Select DAoC Log File",
				              AllowMultiple = false,
				              FileTypeFilter =
				              [
						              new FilePickerFileType("Log Files")
						              {
								              Patterns = ["*.log"]
						              },
						              new FilePickerFileType("All Files")
						              {
								              Patterns = ["*.*"]
						              }
				              ]
		              };

		var result = await storageProvider.OpenFilePickerAsync(options);
		if(result.Count > 0)
		{
			this.CurrentFilePath = result[0].Path.LocalPath;
			await this.StartWatching();
		}
	}

	[RelayCommand]
	private async Task OpenSessionPicker(Window? ownerWindow)
	{
		if(ownerWindow == null) return;

		string? path = null;
		if(!string.IsNullOrWhiteSpace(this.CustomChatLogPath)&&File.Exists(this.CustomChatLogPath))
		{
			path = this.CustomChatLogPath;
		}
		else
		{
			path = this.daocLogPathService.FindDaocLogPath();
		}

		if(path == null||!File.Exists(path)) return;

		var sessions = await Task.Run(() => LogSessionScanner.Scan(path));
		if(sessions.Count == 0) return;

		var dialog = new LogSessionDialog(sessions);
		var selected = await dialog.ShowDialog<LogSession?>(ownerWindow);
		if(selected == null) return;

		this.CurrentFilePath = path;
		await this.StartWatchingFromSession(selected);
	}

	private async Task StartWatchingFromSession(LogSession session)
	{
		if(string.IsNullOrWhiteSpace(this.CurrentFilePath)) return;

		if(this.IsWatching)
		{
			await this.watchSession.StopAndWaitAsync();
		}

		var endPos = session.EndTime.HasValue?session.EndFilePosition:-1;
		var watcher = new LogWatcher(this.CurrentFilePath, session.FilePosition, enableTimeFiltering: false, endPosition: endPos);
		await this.RunWatchLoop(watcher);
	}

	private void ProcessLogLine(LogLine logLine)
	{
		this.ResetParsingDebounce();

		this.processor.Process(logLine, this.watchSession.CurrentSessionStart, out var characterChanged, out var killStatsChanged);

		this.combatProcessor.Process(logLine);

		if(characterChanged)
		{
			this.DetectedCharacterName = this.processor.DetectedCharacterName;
		}

		if(killStatsChanged)
		{
			this.Kills = this.processor.Kills;
			this.Deaths = this.processor.Deaths;
		}

		if(logLine is SendLogLine { Event: var send }&&this.ShowSendNotifications)
		{
			var now = TimeOnly.FromDateTime(DateTime.Now);
			var diff = TimeHelper.ShortestArcSeconds(now, send.Timestamp);
			if(diff <= SEND_NOTIFICATION_MAX_AGE_SECONDS)
			{
				this.ShowSendToast(send.Sender, send.Message);
			}
		}
	}

	private void ShowSendToast(string sender, string message)
	{
		this.SendNotificationSender = sender;
		this.SendNotificationMessage = message;
		this.IsSendNotificationVisible = true;

		if(this.sendNotificationTimer == null)
		{
			this.sendNotificationTimer = new System.Timers.Timer(SEND_NOTIFICATION_TIMEOUT_MS)
			                             {
					                             AutoReset = false
			                             };
			this.sendNotificationTimer.Elapsed += (s, e) => Dispatcher.UIThread.InvokeAsync(() => this.IsSendNotificationVisible = false);
		}

		this.sendNotificationTimer.Stop();
		this.sendNotificationTimer.Start();

		this.notificationService.ShowToast(sender, message);
		this.notificationService.PlayNotificationSound();
	}

	[RelayCommand]
	private void DismissSendNotification() => this.IsSendNotificationVisible = false;

	private void ResetParsingDebounce()
	{
		if(!this.IsParsing)
		{
			this.IsParsing = true;
		}

		if(this.parsingDebounceTimer == null)
		{
			this.parsingDebounceTimer = new System.Timers.Timer(PARSING_DEBOUNCE_INTERVAL_MS)
			                            {
					                            AutoReset = false
			                            };
			this.parsingDebounceTimer.Elapsed += (s, e) => Dispatcher.UIThread.InvokeAsync(() => this.IsParsing = false);
		}

		this.parsingDebounceTimer.Stop();
		this.parsingDebounceTimer.Start();
	}

	private void OnEntryProcessed(object? sender, RealmPointLogEntry e) => this.AddLogEntry(e);

	private void OnMultiKillDetected(object? sender, RealmPointLogEntry e)
	{
		this.BestMultiKill = Math.Max(this.BestMultiKill, e.KillCount);
		this.AddLogEntry(e);
	}

	private void OnDamageLogged(object? sender, CombatLogEntry e) => this.AddCombatLogEntry(e);

	private void OnHealLogged(object? sender, HealLogEntry e) => this.AddHealLogEntry(e);

	private void OnMultiHitDetected(object? sender, CombatLogEntry e) => this.AddCombatLogEntry(e);

	private void AddLogEntry(RealmPointLogEntry entry)
	{
		AddCapped(this.LogEntries, entry);
		if(this.MatchesRpFilter(entry))
		{
			AddCapped(this.FilteredLogEntries, entry);
		}
	}

	private bool MatchesRpFilter(RealmPointLogEntry e) =>
		string.IsNullOrWhiteSpace(this.RpFilterText) ||
		e.Details.Contains(this.RpFilterText, StringComparison.OrdinalIgnoreCase) ||
		e.Source.Contains(this.RpFilterText, StringComparison.OrdinalIgnoreCase);

	private void RefreshRpFilter()
	{
		this.FilteredLogEntries.Clear();
		foreach(var e in this.LogEntries)
		{
			if(this.MatchesRpFilter(e))
			{
				this.FilteredLogEntries.Add(e);
				if(this.FilteredLogEntries.Count >= LOG_ENTRIES_CAPACITY)
				{
					break;
				}
			}
		}
	}

	[RelayCommand]
	private void ClearRpFilter() => this.RpFilterText = string.Empty;

	private void AddHealLogEntry(HealLogEntry entry)
	{
		AddCapped(this.HealLogEntries, entry);
		if(this.MatchesHealFilter(entry))
		{
			AddCapped(this.FilteredHealLogEntries, entry);
		}
	}

	private bool MatchesHealFilter(HealLogEntry e) =>
		string.IsNullOrWhiteSpace(this.HealFilterText) ||
		(e.Who ?? string.Empty).Contains(this.HealFilterText, StringComparison.OrdinalIgnoreCase) ||
		e.DirectionLabel.Contains(this.HealFilterText, StringComparison.OrdinalIgnoreCase);

	private void RefreshHealFilter()
	{
		this.FilteredHealLogEntries.Clear();
		foreach(var e in this.HealLogEntries)
		{
			if(this.MatchesHealFilter(e))
			{
				this.FilteredHealLogEntries.Add(e);
				if(this.FilteredHealLogEntries.Count >= LOG_ENTRIES_CAPACITY)
				{
					break;
				}
			}
		}
	}

	[RelayCommand]
	private void ClearHealFilter() => this.HealFilterText = string.Empty;

	private void AddCombatLogEntry(CombatLogEntry entry)
	{
		AddCapped(this.CombatLogEntries, entry);
		if(this.MatchesCombatFilter(entry))
		{
			AddCapped(this.FilteredCombatLogEntries, entry);
		}
	}

	private bool MatchesCombatFilter(CombatLogEntry e) =>
		string.IsNullOrWhiteSpace(this.CombatFilterText) ||
		e.Target.Contains(this.CombatFilterText, StringComparison.OrdinalIgnoreCase) ||
		e.Source.Contains(this.CombatFilterText, StringComparison.OrdinalIgnoreCase);

	private void RefreshCombatFilter()
	{
		this.FilteredCombatLogEntries.Clear();
		foreach(var e in this.CombatLogEntries)
		{
			if(this.MatchesCombatFilter(e))
			{
				this.FilteredCombatLogEntries.Add(e);
				if(this.FilteredCombatLogEntries.Count >= LOG_ENTRIES_CAPACITY)
				{
					break;
				}
			}
		}
	}

	[RelayCommand]
	private void ClearCombatFilter() => this.CombatFilterText = string.Empty;

	private static void AddCapped<T>(ObservableCollection<T> collection, T item)
	{
		collection.Insert(0, item);
		if(collection.Count > LOG_ENTRIES_CAPACITY)
		{
			collection.RemoveAt(collection.Count - 1);
		}
	}

	[RelayCommand]
	private async Task StartWatching()
	{
		if(string.IsNullOrWhiteSpace(this.CurrentFilePath)||this.IsWatching) return;

		var (filterEnabled, filterHours) = this.TimeFilter.GetFilterParameters();
		await this.RunWatchLoop(new LogWatcher(this.CurrentFilePath, 0, filterEnabled, filterHours));
	}

	private async Task RunWatchLoop(LogWatcher watcher)
	{
		var myGeneration = ++this.watchGeneration;
		this.IsWatching = true;
		this.ResetAllState();
		this.Summary.IsLive = true;

		await this.watchSession.RunAsync(watcher,
		                                 () => Dispatcher.UIThread.InvokeAsync(() => this.Summary.RefreshRpsPerHour()),
		                                 async line =>
		                                 {
			                                 var capturedLine = line;
			                                 await Dispatcher.UIThread.InvokeAsync(() => this.ProcessLogLine(capturedLine), DispatcherPriority.Normal);
		                                 });

		if(myGeneration == this.watchGeneration)
		{
			this.CleanupWatchState();
		}
	}

	private void CleanupWatchState()
	{
		this.parsingDebounceTimer?.Stop();
		this.parsingDebounceTimer?.Dispose();
		this.parsingDebounceTimer = null;
		this.sendNotificationTimer?.Stop();
		this.sendNotificationTimer?.Dispose();
		this.sendNotificationTimer = null;
		this.IsParsing = false;
		this.IsWatching = false;
		this.Summary.IsLive = false;
		this.Summary.RefreshRpsPerHour();
	}

	[RelayCommand]
	private void StopWatching()
	{
		this.watchSession.RequestStop();
		this.IsWatching = false;
		this.ResetAllState();
	}

	private void ResetAllState()
	{
		this.Summary.Reset();
		this.LogEntries.Clear();
		this.FilteredLogEntries.Clear();
		this.HealLogEntries.Clear();
		this.FilteredHealLogEntries.Clear();
		this.CombatLogEntries.Clear();
		this.FilteredCombatLogEntries.Clear();
		this.ChartData.Reset();
		this.CombatSummary.Reset();
		this.processor.Reset();
		this.combatProcessor.Reset();
		this.DetectedCharacterName = null;
		this.Kills = 0;
		this.Deaths = 0;
		this.BestMultiKill = 0;
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

	public void Dispose()
	{
		this.processor.EntryProcessed -= this.OnEntryProcessed;
		this.processor.MultiKillDetected -= this.OnMultiKillDetected;
		this.combatProcessor.DamageLogged -= this.OnDamageLogged;
		this.combatProcessor.HealLogged -= this.OnHealLogged;
		this.combatProcessor.MultiHitDetected -= this.OnMultiHitDetected;
		this.TimeFilter.FilterChanged -= this.OnTimeFilterChanged;
		this.watchSession.RequestStop();
		GC.SuppressFinalize(this);
	}
}
