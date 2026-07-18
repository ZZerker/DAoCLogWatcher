using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DAoCLogWatcher.Core;
using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Helpers;
using DAoCLogWatcher.UI.Models;
using DAoCLogWatcher.UI.Services;
using DAoCLogWatcher.UI.Views;

namespace DAoCLogWatcher.UI.ViewModels;

public partial class MainWindowViewModel: ViewModelBase, IDisposable
{
	private const int PARSING_DEBOUNCE_INTERVAL_MS = 1000;
	private const int SEND_NOTIFICATION_MAX_AGE_SECONDS = 60;
	private const int SESSION_HISTORY_FLUSH_INTERVAL_SECONDS = 60;

	private readonly System.Timers.Timer parsingDebounceTimer;

	private readonly IWatchSession watchSession;
	private readonly IUpdateService updateService;
	private readonly INotificationService notificationService;
	private readonly IDaocLogPathService daocLogPathService;
	private readonly AppSettings settings;
	private readonly ISettingsService settingsService;
	private readonly ILogWatcherFactory logWatcherFactory;
	private readonly IRealmPointProcessor processor;
	private readonly ICombatProcessor combatProcessor;
	private readonly WatchController watchController;
	private readonly ISessionHistoryService sessionHistoryService;
	private readonly SessionHistoryRecorder sessionHistoryRecorder;
	private DateTime? sessionHistoryStartTime;
	private DateTime lastSessionHistoryFlushUtc = DateTime.MinValue;
	private readonly OverlayViewModel overlay;
	private OverlayWindow? overlayWindow;

	public bool IsOverlaySupported => OperatingSystem.IsWindows()||OperatingSystem.IsLinux();

	public OverlayViewModel Overlay => this.overlay;

	[ObservableProperty] private bool isOverlayOpen;

	[ObservableProperty] private string? currentFilePath;

	public string? CurrentFileName => string.IsNullOrEmpty(this.CurrentFilePath)?null:Path.GetFileName(this.CurrentFilePath);

	partial void OnCurrentFilePathChanged(string? value)
	{
		this.OnPropertyChanged(nameof(this.CurrentFileName));
		_ = this.SessionPicker.LoadRecentSessionsFromPathAsync(value);
	}

	public SessionPickerViewModel SessionPicker { get; }

	[RelayCommand]
	private async Task OpenLastSession()
	{
		var path = this.CurrentFilePath ?? this.GetLogPath();
		if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))
		{
			return;
		}

		var sessions = await Task.Run(() => LogSessionScanner.Scan(path));
		var last = sessions.FirstOrDefault();
		if(last == null)
		{
			return;
		}

		this.CurrentFilePath = path;
		await this.StartWatchingFromSession(last);
	}

	private async Task OpenRecentSessionCoreAsync(string path, LogSession session)
	{
		this.CurrentFilePath = path;
		await this.StartWatchingFromSession(session);
	}

	private string? GetLogPath()
	{
		if(!string.IsNullOrWhiteSpace(this.SettingsPopup.CustomChatLogPath)&&File.Exists(this.SettingsPopup.CustomChatLogPath))
		{
			return this.SettingsPopup.CustomChatLogPath;
		}

		return this.daocLogPathService.FindDaocLogPath();
	}

	public TimeFilterService TimeFilter { get; } = new();

	[ObservableProperty] private bool highlightMultiKills;

	partial void OnHighlightMultiKillsChanged(bool value)
	{
		this.settings.HighlightMultiKills = value;
		this.settingsService.Save(this.settings);
	}

	[ObservableProperty] private bool highlightMultiHits;

	partial void OnHighlightMultiHitsChanged(bool value)
	{
		this.settings.HighlightMultiHits = value;
		this.settingsService.Save(this.settings);
	}

	[ObservableProperty] private bool isWatching;

	public ToggleState RpsChart { get; } = new();

	public ToggleState CumulativeRpChart { get; } = new();

	public ToggleState RpSourceBreakdown { get; } = new();

	public ToggleState AvgDmgChart { get; } = new();

	public ToggleState DmgByAttackerChart { get; } = new();

	public ToggleState HealsByHealerChart { get; } = new();

	public ToggleState HealsByTargetChart { get; } = new();

	// Tab visibility toggles are initialized in the constructor since their initial
	// values come from persisted AppSettings.
	public ToggleState DashboardTab { get; private set; } = null!;

	public ToggleState RealmPointsTab { get; private set; } = null!;

	public ToggleState CombatTab { get; private set; } = null!;

	public ToggleState HealLogTab { get; private set; } = null!;

	private bool _isCombatSubStats = true;

	public bool IsCombatSubStats
	{
		get => this._isCombatSubStats;
		set
		{
			if(this.SetProperty(ref this._isCombatSubStats, value))
			{
				this.OnPropertyChanged(nameof(this.IsCombatSubLog));
				this.OnPropertyChanged(nameof(this.CombatSubDescription));
			}
		}
	}

	public bool IsCombatSubLog
	{
		get => !this._isCombatSubStats;
		set
		{
			if(value != this.IsCombatSubLog)
			{
				this.IsCombatSubStats = !value;
			}
		}
	}

	public string CombatSubDescription => this._isCombatSubStats?"Damage dealt & taken — rates, attack breakdown, top opponents":"Raw per-hit combat event stream — outgoing, incoming, misses, procs";

	private bool _isMapSubZones;

	public bool IsMapSubZones
	{
		get => this._isMapSubZones;
		set
		{
			if(this.SetProperty(ref this._isMapSubZones, value))
			{
				this.OnPropertyChanged(nameof(this.IsMapSubHeatmap));
				this.OnPropertyChanged(nameof(this.MapSubDescription));
			}
		}
	}

	public bool IsMapSubHeatmap
	{
		get => !this._isMapSubZones;
		set
		{
			if(value != this.IsMapSubHeatmap)
			{
				this.IsMapSubZones = !value;
			}
		}
	}

	public string MapSubDescription => this._isMapSubZones?"Per-zone kill / death / RP breakdown, ranked by activity":"Density of all kills reported in the killspam, every fight on the frontier";

	public DashboardViewModel Dashboard { get; }

	public SettingsPopupViewModel SettingsPopup { get; }

	public CombatStatsViewModel CombatStats { get; }

	public FrontierMapData FrontierMap { get; private set; } = null!;

	public IReadOnlyDictionary<string, int> CurrentZoneKills => this.processor.CurrentZoneKills;

	public ZoneActivityViewModel ZoneActivity { get; }

	[ObservableProperty] private bool showSendNotifications;

	partial void OnShowSendNotificationsChanged(bool value)
	{
		this.settings.ShowSendNotifications = value;
		this.settingsService.Save(this.settings);
	}

	public SendNotificationController SendNotification { get; } = new();

	[ObservableProperty] private bool isParsing;

	[ObservableProperty] private bool isUpdateAvailable;
	[ObservableProperty] private string? updateVersionText;
	[ObservableProperty] private string? updateError;

	/// <summary>True once the update has downloaded and only a restart is left to install it.</summary>
	[ObservableProperty] private bool isRestartRequired;

	[ObservableProperty] private string? watchError;

	[RelayCommand]
	private void DismissWatchError()
	{
		this.WatchError = null;
	}

	[ObservableProperty] private string? detectedCharacterName;
	[ObservableProperty] private int kills;
	[ObservableProperty] private int deaths;
	[ObservableProperty] private int bestMultiKill;
	[ObservableProperty] private string? currentMapLocation;

	public event EventHandler? MinimapLocationChanged;

	partial void OnCurrentMapLocationChanged(string? value)
	{
		this.MinimapLocationChanged?.Invoke(this, EventArgs.Empty);
	}

	public double KdRatio => StatMath.KdRatio(this.Kills, this.Deaths);

	public bool HasBestMultiKill => this.BestMultiKill > 0;

	partial void OnKillsChanged(int value)
	{
		this.OnPropertyChanged(nameof(this.KdRatio));
	}

	partial void OnDeathsChanged(int value)
	{
		this.OnPropertyChanged(nameof(this.KdRatio));
	}

	partial void OnBestMultiKillChanged(int value)
	{
		this.OnPropertyChanged(nameof(this.HasBestMultiKill));
	}

	public FilteredCollection<RealmPointLogEntry> RpLog { get; } = new(static (e, f) => string.IsNullOrWhiteSpace(f)||e.Details.Contains(f, StringComparison.OrdinalIgnoreCase)||e.Source.Contains(f, StringComparison.OrdinalIgnoreCase));

	public RealmPointSummary Summary { get; }

	public RpsChartData ChartData { get; }

	public CombatSummary CombatSummary { get; }

	public MainWindowViewModel(IWatchSession watchSession,
	                           IRealmPointProcessor processor,
	                           ICombatProcessor combatProcessor,
	                           IUpdateService updateService,
	                           INotificationService notificationService,
	                           IDaocLogPathService daocLogPathService,
	                           ISettingsService settingsService,
	                           ILogWatcherFactory logWatcherFactory,
	                           AppSettings settings,
	                           RealmPointSummary summary,
	                           RpsChartData chartData,
	                           CombatSummary combatSummary,
	                           IFrontierMapService frontierMapService,
	                           ISessionHistoryService sessionHistoryService,
	                           SessionHistoryRecorder sessionHistoryRecorder)
	{
		this.watchSession = watchSession;
		this.notificationService = notificationService;
		this.daocLogPathService = daocLogPathService;
		this.processor = processor;
		this.combatProcessor = combatProcessor;
		this.updateService = updateService;
		this.settingsService = settingsService;
		this.logWatcherFactory = logWatcherFactory;
		this.settings = settings;
		this.sessionHistoryService = sessionHistoryService;
		this.sessionHistoryRecorder = sessionHistoryRecorder;
		this.watchController = new WatchController(watchSession);
		this.Summary = summary;
		this.ChartData = chartData;
		this.CombatSummary = combatSummary;
		this.overlay = new OverlayViewModel(summary, settings);
		this.highlightMultiKills = this.settings.HighlightMultiKills;
		this.highlightMultiHits = this.settings.HighlightMultiHits;
		this.showSendNotifications = this.settings.ShowSendNotifications;
		this.DashboardTab = new ToggleState(this.settings.ShowDashboardTab,
		                                    v =>
		                                    {
			                                    this.settings.ShowDashboardTab = v;
			                                    this.settingsService.Save(this.settings);
		                                    });
		this.RealmPointsTab = new ToggleState(this.settings.ShowRealmPointsTab,
		                                      v =>
		                                      {
			                                      this.settings.ShowRealmPointsTab = v;
			                                      this.settingsService.Save(this.settings);
		                                      });
		this.CombatTab = new ToggleState(this.settings.ShowCombatTab,
		                                 v =>
		                                 {
			                                 this.settings.ShowCombatTab = v;
			                                 this.settingsService.Save(this.settings);
		                                 });
		this.HealLogTab = new ToggleState(this.settings.ShowHealLogTab,
		                                  v =>
		                                  {
			                                  this.settings.ShowHealLogTab = v;
			                                  this.settingsService.Save(this.settings);
		                                  });
		this.Dashboard = new DashboardViewModel(this.settings, this.settingsService);
		this.SettingsPopup = new SettingsPopupViewModel(this.settings, this.settingsService);
		this.SessionPicker = new SessionPickerViewModel(() => this.CurrentFilePath ?? this.GetLogPath(), this.OpenRecentSessionCoreAsync, msg => this.WatchError = msg);
		this.CombatStats = new CombatStatsViewModel(this.combatProcessor, this.CombatSummary);
		this.FrontierMap = frontierMapService.Load();
		this.ZoneActivity = new ZoneActivityViewModel(this.processor, this.settings, this.settingsService, () => this.Summary.SessionStartTime);
		this.processor.EntryProcessed += this.OnEntryProcessed;
		this.processor.MultiKillDetected += this.OnMultiKillDetected;
		this.TimeFilter.FilterChanged += this.OnTimeFilterChanged;
		this.watchSession.ErrorOccurred += this.OnWatchSessionError;
		this.updateService.ErrorOccurred += this.OnUpdateError;
		this.updateService.UpdateReady += this.OnUpdateReady;
		this.parsingDebounceTimer = new System.Timers.Timer(PARSING_DEBOUNCE_INTERVAL_MS)
		                            {
				                            AutoReset = false
		                            };
		this.parsingDebounceTimer.Elapsed += (_, _) => Dispatcher.UIThread.InvokeAsync(() => this.IsParsing = false);
		this.FireAndForget(this.CheckForUpdatesAsync());
	}

	/// <summary>Forwards to <see cref="SessionPicker"/>'s window-activation rescan debounce.</summary>
	public bool OnWindowActivated()
	{
		return this.SessionPicker.OnWindowActivated();
	}

	private void FireAndForget(Task task)
	{
		task.ContinueWith(t => Dispatcher.UIThread.InvokeAsync(() => this.WatchError = t.Exception!.Flatten().InnerExceptions[0].Message), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
	}

	private void OnTimeFilterChanged(object? sender, EventArgs e)
	{
		if(this.IsWatching)
		{
			this.FireAndForget(this.RestartAsync());
		}
	}

	private void OnWatchSessionError(object? sender, string message)
	{
		Dispatcher.UIThread.InvokeAsync(() => this.WatchError = message);
	}

	private void OnUpdateError(object? sender, string message)
	{
		Dispatcher.UIThread.InvokeAsync(() => this.UpdateError = message);
	}

	private void OnUpdateReady(object? sender, EventArgs e)
	{
		Dispatcher.UIThread.InvokeAsync(() => this.IsRestartRequired = true);
	}

	private async Task RestartAsync()
	{
		await this.watchSession.StopAndWaitAsync();
		if(string.IsNullOrWhiteSpace(this.CurrentFilePath))
		{
			return;
		}

		if(await this.TryStartCurrentSessionAsync())
		{
			return;
		}

		var (filterEnabled, filterHours) = this.TimeFilter.GetFilterParameters();
		await this.RunWatchLoop(this.logWatcherFactory.Create(this.CurrentFilePath, 0, filterEnabled, filterHours));
	}

	[RelayCommand]
	private async Task OpenDaocLog()
	{
		var path = this.GetLogPath();
		if(path != null&&File.Exists(path))
		{
			this.CurrentFilePath = path;
			await this.StartWatching();
		}
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
	private async Task OpenSessionHistory(Window? ownerWindow)
	{
		if(ownerWindow == null)
		{
			return;
		}

		var records = this.sessionHistoryService.Load();
		var dialog = new SessionHistoryDialog(new SessionHistoryViewModel(records));
		await dialog.ShowDialog(ownerWindow);
	}

	[RelayCommand]
	private void ToggleOverlay()
	{
		if(!this.IsOverlaySupported)
		{
			return;
		}

		if(this.overlayWindow != null)
		{
			this.overlayWindow.Close();
			this.settings.OverlayEnabled = false;
			this.settingsService.Save(this.settings);
			return;
		}

		this.ShowOverlay();
		this.settings.OverlayEnabled = true;
		this.settingsService.Save(this.settings);
	}

	private void ShowOverlay()
	{
		if(this.overlayWindow != null)
		{
			return;
		}

		this.overlay.IsLive = this.Summary.IsLive;
		this.overlay.CharacterName = this.DetectedCharacterName;
		this.SyncOverlayCombatTotals();
		this.overlayWindow = new OverlayWindow(this.overlay, this.settings, this.settingsService);
		this.overlayWindow.Closed += (_, _) =>
		{
			this.overlayWindow = null;
			this.IsOverlayOpen = false;
		};
		this.overlayWindow.Show();
		this.IsOverlayOpen = true;
	}

	// Closes the window without touching OverlayEnabled so the overlay reopens next start.
	public void CloseOverlay()
	{
		this.overlayWindow?.Close();
	}

	public void AutoOpenOverlayIfEnabled()
	{
		if(this.IsOverlaySupported&&this.settings.OverlayEnabled)
		{
			this.ShowOverlay();
		}
	}

	[RelayCommand]
	private async Task OpenSessionPicker(Window? ownerWindow)
	{
		if(ownerWindow == null)
		{
			return;
		}

		var path = this.GetLogPath();
		if(path == null||!File.Exists(path))
		{
			return;
		}

		var sessions = await Task.Run(() => LogSessionScanner.Scan(path));
		if(sessions.Count == 0)
		{
			return;
		}

		var dialog = new LogSessionDialog(sessions);
		var selected = await dialog.ShowDialog<LogSession?>(ownerWindow);
		if(selected == null)
		{
			return;
		}

		this.CurrentFilePath = path;
		await this.StartWatchingFromSession(selected);
	}

	private async Task StartWatchingFromSession(LogSession session)
	{
		if(string.IsNullOrWhiteSpace(this.CurrentFilePath))
		{
			return;
		}

		if(this.IsWatching)
		{
			await this.watchSession.StopAndWaitAsync();
		}

		this.Summary.SessionStartTime = session.StartTime;
		this.TimeFilter.SetIndexSilent(0);
		var endPos = session.EndTime.HasValue?session.EndFilePosition:-1;
		var watcher = this.logWatcherFactory.Create(this.CurrentFilePath, session.FilePosition, false, endPosition: endPos);
		await this.RunWatchLoop(watcher, session.StartTime);
	}

	private void ProcessLogLine(LogLine logLine)
	{
		this.ResetParsingDebounce();

		this.processor.Process(logLine, this.watchSession.CurrentSessionStart, out var characterChanged, out var killStatsChanged);

		this.combatProcessor.Process(logLine);
		this.SyncOverlayCombatTotals();

		if(characterChanged)
		{
			this.DetectedCharacterName = this.processor.DetectedCharacterName;
			this.overlay.CharacterName = this.DetectedCharacterName;
		}

		if(killStatsChanged)
		{
			this.Kills = this.processor.Kills;
			this.Deaths = this.processor.Deaths;
		}

		if(logLine is RegionLogLine { Event: var region })
		{
			this.CurrentMapLocation = region.Location;
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

	private void ShowSendToast(string toastSender, string toastMessage)
	{
		this.SendNotification.Show(toastSender, toastMessage);
		this.notificationService.ShowToast(toastSender, toastMessage);
		this.notificationService.PlayNotificationSound();
	}

	private void ResetParsingDebounce()
	{
		if(!this.IsParsing)
		{
			this.IsParsing = true;
		}

		this.parsingDebounceTimer.Stop();
		this.parsingDebounceTimer.Start();
	}

	private void OnEntryProcessed(object? sender, RealmPointLogEntry e)
	{
		this.RpLog.Add(e);
		this.overlay.AddKillFeedEntry(FormatKillFeed(e));
	}

	private void OnMultiKillDetected(object? sender, RealmPointLogEntry e)
	{
		this.BestMultiKill = Math.Max(this.BestMultiKill, e.KillCount);
		this.RpLog.Add(e);
		this.overlay.AddKillFeedEntry(FormatKillFeed(e));
	}

	private void SyncOverlayCombatTotals()
	{
		this.overlay.DamageTotal = this.CombatSummary.TotalDamageDealt;
		this.overlay.HealTotal = this.CombatSummary.TotalHealingDone;
	}

	private static string FormatKillFeed(RealmPointLogEntry e)
	{
		var who = string.IsNullOrWhiteSpace(e.VictimName)?e.Source:e.VictimName;
		return $"{who} +{e.Points} RP";
	}

	[RelayCommand]
	private async Task StartWatching()
	{
		if(string.IsNullOrWhiteSpace(this.CurrentFilePath)||this.IsWatching)
		{
			return;
		}

		if(await this.TryStartCurrentSessionAsync())
		{
			return;
		}

		var (filterEnabled, filterHours) = this.TimeFilter.GetFilterParameters();
		await this.RunWatchLoop(this.logWatcherFactory.Create(this.CurrentFilePath, 0, filterEnabled, filterHours));
	}

	/// <summary>When the "Current session" filter (index 0) is active, start watching from the last
	/// session boundary instead of reading the entire log. Returns true if a session watch was started.</summary>
	private async Task<bool> TryStartCurrentSessionAsync()
	{
		if(this.TimeFilter.SelectedTimeFilterIndex != 0||string.IsNullOrWhiteSpace(this.CurrentFilePath))
		{
			return false;
		}

		var path = this.CurrentFilePath;
		var sessions = await Task.Run(() => LogSessionScanner.Scan(path));
		var last = sessions.FirstOrDefault();
		if(last == null)
		{
			return false;
		}

		await this.StartWatchingFromSession(last);
		return true;
	}

	private Task RunWatchLoop(LogWatcher watcher, DateTime? sessionStartTimeOverride = null)
	{
		return this.watchController.RunAsync(watcher,
		                                     () =>
		                                     {
			                                     this.IsWatching = true;
			                                     this.ResetAllState();
			                                     this.Summary.IsLive = true;
			                                     this.sessionHistoryStartTime = sessionStartTimeOverride ?? DateTime.Now;
			                                     this.lastSessionHistoryFlushUtc = DateTime.MinValue;
		                                     },
		                                     () =>
		                                     {
			                                     this.Summary.RefreshRpsPerHour();
			                                     this.processor.RefreshZoneKills();
			                                     this.FlushSessionHistoryThrottled();
		                                     },
		                                     line =>
		                                     {
			                                     this.ProcessLogLine(line);
			                                     return Task.CompletedTask;
		                                     },
		                                     this.CleanupWatchState);
	}

	private void FlushSessionHistoryThrottled()
	{
		if((DateTime.UtcNow - this.lastSessionHistoryFlushUtc).TotalSeconds < SESSION_HISTORY_FLUSH_INTERVAL_SECONDS)
		{
			return;
		}

		this.FlushSessionHistory(endTime: null);
	}

	private void FlushSessionHistory(DateTime? endTime)
	{
		if(!this.sessionHistoryStartTime.HasValue)
		{
			return;
		}

		this.lastSessionHistoryFlushUtc = DateTime.UtcNow;
		var record = this.sessionHistoryRecorder.BuildRecord(this.sessionHistoryStartTime.Value, this.DetectedCharacterName, endTime, this.BestMultiKill);
		if(endTime.HasValue)
		{
			this.sessionHistoryService.Upsert(record);
		}
		else
		{
			// Periodic flush: keep the file write off the UI thread; the service lock serializes writers.
			Task.Run(() => this.sessionHistoryService.Upsert(record));
		}
	}

	private void CleanupWatchState()
	{
		this.parsingDebounceTimer.Stop();
		this.CombatStats.StopChartDebounce();
		this.IsParsing = false;
		this.IsWatching = false;
		this.Summary.IsLive = false;
		this.Summary.RefreshRpsPerHour();
		this.CombatStats.RefreshHealStats();
		this.CombatStats.RefreshAttackTypeStats();
		this.FlushSessionHistory(DateTime.Now);

		// A watch loop just finished — rescan so the completed session appears in the list.
		this.SessionPicker.RescanAfterWatchStopped();
	}

	[RelayCommand]
	private void StopWatching()
	{
		this.watchController.Stop();
		this.IsWatching = false;
	}

	private void ResetAllState()
	{
		this.Summary.Reset();
		this.RpLog.Clear();
		this.ChartData.Reset();
		this.CombatSummary.Reset();
		this.processor.Reset();
		this.combatProcessor.Reset();
		this.CombatStats.Reset();
		this.ZoneActivity.Reset();
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
	private void DismissUpdate()
	{
		this.IsUpdateAvailable = false;
	}

	[RelayCommand]
	private Task ApplyUpdateAndRestart()
	{
		this.UpdateError = null;
		return this.updateService.ApplyAndRestartAsync();
	}

	public void Dispose()
	{
		this.processor.EntryProcessed -= this.OnEntryProcessed;
		this.processor.MultiKillDetected -= this.OnMultiKillDetected;
		this.ZoneActivity.Dispose();
		this.CombatStats.Dispose();
		this.TimeFilter.FilterChanged -= this.OnTimeFilterChanged;
		this.watchSession.ErrorOccurred -= this.OnWatchSessionError;
		this.updateService.ErrorOccurred -= this.OnUpdateError;
		this.updateService.UpdateReady -= this.OnUpdateReady;
		this.watchController.Stop();
		this.SessionPicker.Dispose();
		this.parsingDebounceTimer.Stop();
		this.parsingDebounceTimer.Dispose();
		this.SendNotification.Dispose();
		GC.SuppressFinalize(this);
	}
}
