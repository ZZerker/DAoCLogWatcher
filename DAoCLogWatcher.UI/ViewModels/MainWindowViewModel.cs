using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
	private const int COMBAT_CHART_DEBOUNCE_MS = 250;

	private readonly System.Timers.Timer parsingDebounceTimer;
	private readonly System.Timers.Timer combatChartDebounceTimer;

	public event EventHandler? CombatChartsUpdateNeeded;

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

	[ObservableProperty] private string? currentFilePath;

	public string? CurrentFileName => string.IsNullOrEmpty(this.CurrentFilePath)?null:Path.GetFileName(this.CurrentFilePath);

	partial void OnCurrentFilePathChanged(string? value)
	{
		this.OnPropertyChanged(nameof(this.CurrentFileName));
		_ = this.LoadRecentSessionsFromPathAsync(value);
	}

	public ObservableCollection<RecentSessionEntry> RecentSessions { get; } = new();

	private CancellationTokenSource? sessionLoadCts;

	[RelayCommand]
	private Task LoadRecentSessions()
	{
		return this.LoadRecentSessionsFromPathAsync(this.CurrentFilePath ?? this.GetLogPath());
	}

	private async Task LoadRecentSessionsFromPathAsync(string? path)
	{
		if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))
		{
			return;
		}

		this.sessionLoadCts?.Cancel();
		this.sessionLoadCts = new CancellationTokenSource();
		var ct = this.sessionLoadCts.Token;

		var sessions = await Task.Run(() => LogSessionScanner.Scan(path));
		if(ct.IsCancellationRequested)
		{
			return;
		}

		this.RecentSessions.Clear();
		foreach(var s in sessions.Take(3))
		{
			var label = s.CharacterName != null?$"{s.StartTime:MMM d, HH:mm} — {s.CharacterName}":s.StartTime.ToString("MMM d, HH:mm");
			this.RecentSessions.Add(new RecentSessionEntry
			                        {
					                        Label = label,
					                        Sublabel = s.DurationFormatted,
					                        OpenCommand = new AsyncRelayCommand(() => this.OpenRecentSessionCoreAsync(path, s))
			                        });
		}
	}

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

	public ObservableCollection<HealStatEntry> DashboardTopTargets { get; } = new();

	public ObservableCollection<HealStatEntry> DashboardTopSpells { get; } = new();

	public ObservableCollection<HealStatEntry> DashboardTopHealers { get; } = new();

	public ObservableCollection<HealStatEntry> DashboardTopDamageTaken { get; } = [];

	public ObservableCollection<HealStatEntry> DashboardTopHealsDone { get; } = [];

	public FrontierMapData FrontierMap { get; private set; } = null!;

	public IReadOnlyDictionary<string, int> CurrentZoneKills => this.processor.CurrentZoneKills;

	public IReadOnlyList<TimeWindowOption> ZoneKillWindowOptions { get; } = new[]
	                                                                        {
			                                                                        new TimeWindowOption
			                                                                        {
					                                                                        Label = "2 min",
					                                                                        Value = TimeSpan.FromMinutes(2)
			                                                                        },
			                                                                        new TimeWindowOption
			                                                                        {
					                                                                        Label = "5 min",
					                                                                        Value = TimeSpan.FromMinutes(5)
			                                                                        },
			                                                                        new TimeWindowOption
			                                                                        {
					                                                                        Label = "10 min",
					                                                                        Value = TimeSpan.FromMinutes(10)
			                                                                        },
			                                                                        new TimeWindowOption
			                                                                        {
					                                                                        Label = "20 min",
					                                                                        Value = TimeSpan.FromMinutes(20)
			                                                                        },
			                                                                        new TimeWindowOption
			                                                                        {
					                                                                        Label = "30 min",
					                                                                        Value = TimeSpan.FromMinutes(30)
			                                                                        },
			                                                                        new TimeWindowOption
			                                                                        {
					                                                                        Label = "60 min",
					                                                                        Value = TimeSpan.FromMinutes(60)
			                                                                        }
	                                                                        };

	public ObservableCollection<ZoneKillSummary> ZoneKills { get; } = new();

	[ObservableProperty] private TimeWindowOption selectedZoneKillWindow = null!;

	[ObservableProperty] private int recentZoneKillCount;

	[ObservableProperty] private ZoneKillSummary? hottestZone;

	public ObservableCollection<KillActivityPoint> KillActivityPoints { get; } = new();

	public IReadOnlyList<KillActivityPoint> GetSessionKillActivityPoints()
	{
		var sessionStart = this.Summary.SessionStartTime ?? DateTime.Now;
		return this.processor.GetSessionActivityPoints(sessionStart, DateTime.Now);
	}

	public event EventHandler? KillActivityUpdated;

	[ObservableProperty] private bool showSendNotifications;

	partial void OnShowSendNotificationsChanged(bool value)
	{
		this.settings.ShowSendNotifications = value;
		this.settingsService.Save(this.settings);
	}

	partial void OnSelectedZoneKillWindowChanged(TimeWindowOption value)
	{
		this.settings.ZoneKillWindowMinutes = (int)value.Value.TotalMinutes;
		this.settingsService.Save(this.settings);
		this.processor.SetZoneKillWindow(value.Value);
		this.UpdateZoneKills();
		this.UpdateKillActivity();
	}

	public SendNotificationController SendNotification { get; } = new();

	[ObservableProperty] private bool isParsing;

	[ObservableProperty] private bool isUpdateAvailable;
	[ObservableProperty] private string? updateVersionText;
	[ObservableProperty] private string? updateError;

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

	public double KdRatio => this.Deaths > 0?(double)this.Kills / this.Deaths:this.Kills;

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

	public FilteredCollection<HealLogEntry> HealLog { get; } = new(static (e, f) =>
			                                                               string.IsNullOrWhiteSpace(f)||(e.Who ?? string.Empty).Contains(f, StringComparison.OrdinalIgnoreCase)||
			                                                               e.DirectionLabel.Contains(f, StringComparison.OrdinalIgnoreCase));

	public FilteredCollection<CombatLogEntry> CombatLog { get; } = new(static (e, f) => string.IsNullOrWhiteSpace(f)||e.Opponent.Contains(f, StringComparison.OrdinalIgnoreCase)||e.Source.Contains(f, StringComparison.OrdinalIgnoreCase));

	public ObservableCollection<HealStatEntry> HealsByHealerStats { get; } = new();

	public ObservableCollection<HealStatEntry> HealsByTargetStats { get; } = new();

	public ObservableCollection<HealStatEntry> DamageTakenByAttackerStats { get; } = new();

	public ObservableCollection<AttackTypeRow> AttackTypeStats { get; } = new();

	public ToggleState CombatLogOutgoing { get; private set; } = null!;

	public ToggleState CombatLogIncoming { get; private set; } = null!;

	public ToggleState CombatLogAoe { get; private set; } = null!;

	public ToggleState CombatLogDot { get; private set; } = null!;

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
	                           IFrontierMapService frontierMapService)
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
		this.watchController = new WatchController(watchSession);
		this.Summary = summary;
		this.ChartData = chartData;
		this.CombatSummary = combatSummary;
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
		this.CombatLogOutgoing = new ToggleState(true, _ => this.RefreshCombatLogFilter());
		this.CombatLogIncoming = new ToggleState(true, _ => this.RefreshCombatLogFilter());
		this.CombatLogAoe = new ToggleState(true, _ => this.RefreshCombatLogFilter());
		this.CombatLogDot = new ToggleState(true, _ => this.RefreshCombatLogFilter());
		this.FrontierMap = frontierMapService.Load();
		this.SelectedZoneKillWindow = this.ZoneKillWindowOptions.FirstOrDefault(option => option.Value.TotalMinutes == this.settings.ZoneKillWindowMinutes) ?? this.ZoneKillWindowOptions[1];
		this.processor.EntryProcessed += this.OnEntryProcessed;
		this.processor.ZoneKillsUpdated += this.OnZoneKillsUpdated;
		this.processor.MultiKillDetected += this.OnMultiKillDetected;
		this.combatProcessor.DamageLogged += this.OnDamageLogged;
		this.combatProcessor.HealLogged += this.OnHealLogged;
		this.combatProcessor.MultiHitDetected += this.OnMultiHitDetected;
		this.CombatSummary.PropertyChanged += this.OnCombatSummaryPropertyChanged;
		this.TimeFilter.FilterChanged += this.OnTimeFilterChanged;
		this.watchSession.ErrorOccurred += this.OnWatchSessionError;
		this.updateService.ErrorOccurred += this.OnUpdateError;
		this.parsingDebounceTimer = new System.Timers.Timer(PARSING_DEBOUNCE_INTERVAL_MS)
		                            {
				                            AutoReset = false
		                            };
		this.parsingDebounceTimer.Elapsed += (_, _) => Dispatcher.UIThread.InvokeAsync(() => this.IsParsing = false);
		this.combatChartDebounceTimer = new System.Timers.Timer(COMBAT_CHART_DEBOUNCE_MS)
		                                {
				                                AutoReset = false
		                                };
		this.combatChartDebounceTimer.Elapsed += (_, _) =>
		                                         {
			                                         this.CombatChartsUpdateNeeded?.Invoke(this, EventArgs.Empty);
			                                         Dispatcher.UIThread.InvokeAsync((Action)this.RefreshHealStats);
			                                         Dispatcher.UIThread.InvokeAsync((Action)this.RefreshAttackTypeStats);
		                                         };
		this.FireAndForget(this.LoadRecentSessions());
		this.FireAndForget(this.CheckForUpdatesAsync());
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

	private void OnCombatSummaryPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if(e.PropertyName is not ("TotalHealsReceived" or "TotalDamageDealt" or "TotalDamageTaken" or "TotalHealingDone"))
		{
			return;
		}

		this.combatChartDebounceTimer.Stop();
		this.combatChartDebounceTimer.Start();
	}

	private async Task RestartAsync()
	{
		await this.watchSession.StopAndWaitAsync();
		if(string.IsNullOrWhiteSpace(this.CurrentFilePath))
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
	}

	private void OnMultiKillDetected(object? sender, RealmPointLogEntry e)
	{
		this.BestMultiKill = Math.Max(this.BestMultiKill, e.KillCount);
		this.RpLog.Add(e);
	}

	private void OnDamageLogged(object? sender, CombatLogEntry e)
	{
		this.CombatLog.Add(e);
	}

	private void OnHealLogged(object? sender, HealLogEntry e)
	{
		this.HealLog.Add(e);
	}

	private void OnZoneKillsUpdated(object? sender, EventArgs e)
	{
		this.UpdateZoneKills();
	}

	private void OnMultiHitDetected(object? sender, CombatLogEntry e)
	{
		this.CombatLog.Add(e);
	}

	private void UpdateZoneKills()
	{
		var zoneCounts = this.processor.CurrentZoneKills.Where(kv => kv.Value > 1).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Take(20).ToList();

		var totalCount = zoneCounts.Sum(kv => kv.Value);
		var maxCount = zoneCounts.Count > 0?zoneCounts.Max(kv => kv.Value):1;

		this.ZoneKills.Clear();
		foreach(var kv in zoneCounts)
		{
			var heatRatio = maxCount == 0?0.0:(double)kv.Value / maxCount;
			this.ZoneKills.Add(new ZoneKillSummary
			                   {
					                   Zone = kv.Key,
					                   KillCount = kv.Value,
					                   Percentage = totalCount == 0?0.0:(double)kv.Value / totalCount * 100.0,
					                   HeatColor = ColorUtil.GetZoneHeatColor(heatRatio)
			                   });
		}

		this.RecentZoneKillCount = totalCount;
		this.HottestZone = this.ZoneKills.Count > 0?this.ZoneKills[0]:null;
		this.UpdateKillActivity();
	}

	private void UpdateKillActivity()
	{
		this.KillActivityPoints.Clear();
		foreach(var point in this.processor.KillActivityPoints)
		{
			this.KillActivityPoints.Add(point);
		}

		this.KillActivityUpdated?.Invoke(this, EventArgs.Empty);
	}

	[RelayCommand]
	private async Task StartWatching()
	{
		if(string.IsNullOrWhiteSpace(this.CurrentFilePath)||this.IsWatching)
		{
			return;
		}

		var (filterEnabled, filterHours) = this.TimeFilter.GetFilterParameters();
		await this.RunWatchLoop(this.logWatcherFactory.Create(this.CurrentFilePath, 0, filterEnabled, filterHours));
	}

	private Task RunWatchLoop(LogWatcher watcher)
	{
		return this.watchController.RunAsync(watcher,
		                                     () =>
		                                     {
			                                     this.IsWatching = true;
			                                     this.ResetAllState();
			                                     this.Summary.IsLive = true;
		                                     },
		                                     () =>
		                                     {
			                                     this.Summary.RefreshRpsPerHour();
			                                     this.processor.RefreshZoneKills();
		                                     },
		                                     line =>
		                                     {
			                                     this.ProcessLogLine(line);
			                                     return Task.CompletedTask;
		                                     },
		                                     this.CleanupWatchState);
	}

	private void CleanupWatchState()
	{
		this.parsingDebounceTimer.Stop();
		this.combatChartDebounceTimer.Stop();
		this.IsParsing = false;
		this.IsWatching = false;
		this.Summary.IsLive = false;
		this.Summary.RefreshRpsPerHour();
		this.RefreshHealStats();
		this.RefreshAttackTypeStats();
	}

	[RelayCommand]
	private void StopWatching()
	{
		this.watchController.Stop();
		this.IsWatching = false;
		this.ResetAllState();
	}

	private void RefreshHealStats()
	{
		RefreshStatCollection(this.HealsByHealerStats, this.CombatSummary.HealsByHealer);
		RefreshStatCollection(this.HealsByTargetStats, this.CombatSummary.HealsByTarget);
		RefreshStatCollection(this.DamageTakenByAttackerStats, this.CombatSummary.DamageTakenByAttacker);
		this.RefreshDashboardTopStats();
	}

	private void RefreshDashboardTopStats()
	{
		RefreshStatCollection(this.DashboardTopTargets, this.CombatSummary.DamageByTarget, 3);

		var spellTotalsById = this.CombatSummary.DamageBySpell.ToDictionary(kv => kv.Key, kv => kv.Value.TotalDamage);
		RefreshStatCollection(this.DashboardTopSpells, spellTotalsById, 3);

		RefreshStatCollection(this.DashboardTopHealers, this.CombatSummary.HealsByHealer, 5);
		RefreshStatCollection(this.DashboardTopDamageTaken, this.CombatSummary.DamageTakenByAttacker, 5);
		RefreshStatCollection(this.DashboardTopHealsDone, this.CombatSummary.HealsByTarget, 5);
	}

	private void RefreshAttackTypeStats()
	{
		this.AttackTypeStats.Clear();
		var source = this.CombatSummary.DamageBySpell;

		var rows = source.Where(kv => kv.Value.HitCount > 0).Select(kv => (kv.Key, kv.Value.HitCount, kv.Value.CritCount, Avg: kv.Value.TotalDamage / kv.Value.HitCount)).OrderByDescending(r => r.Avg).ToList();

		if(rows.Count == 0)
		{
			return;
		}

		var maxAvg = rows[0].Avg;

		foreach(var r in rows)
		{
			this.AttackTypeStats.Add(new AttackTypeRow
			                         {
					                         Name = r.Key,
					                         AvgDamage = r.Avg,
					                         HitCount = r.HitCount,
					                         CritCount = r.CritCount,
					                         Percentage = (double)r.Avg / maxAvg * 100.0
			                         });
		}
	}

	private void RefreshCombatLogFilter()
	{
		this.CombatLog.SetTypeFilter(e => (this.CombatLogOutgoing.Value&&e.ShowDealtLabel)||(this.CombatLogIncoming.Value&&!e.IsDealt)||(this.CombatLogAoe.Value&&e.IsMultiHit)||(this.CombatLogDot.Value&&e.IsDotTick));
	}

	private static void RefreshStatCollection(ObservableCollection<HealStatEntry> collection, Dictionary<string, int> source, int count = int.MaxValue)
	{
		collection.Clear();
		var total = source.Values.Sum();
		if(total == 0)
		{
			return;
		}

		foreach(var kv in source.OrderByDescending(kv => kv.Value).Take(count))
		{
			collection.Add(new HealStatEntry
			               {
					               Name = kv.Key,
					               Total = kv.Value,
					               Percentage = (double)kv.Value / total * 100.0
			               });
		}
	}

	private void ResetAllState()
	{
		this.Summary.Reset();
		this.RpLog.Clear();
		this.HealLog.Clear();
		this.CombatLog.Clear();
		this.HealsByHealerStats.Clear();
		this.HealsByTargetStats.Clear();
		this.DamageTakenByAttackerStats.Clear();
		this.AttackTypeStats.Clear();
		this.ChartData.Reset();
		this.CombatSummary.Reset();
		this.processor.Reset();
		this.combatProcessor.Reset();
		this.DashboardTopTargets.Clear();
		this.DashboardTopSpells.Clear();
		this.DashboardTopHealers.Clear();
		this.DashboardTopDamageTaken.Clear();
		this.DashboardTopHealsDone.Clear();
		this.ZoneKills.Clear();
		this.HottestZone = null;
		this.KillActivityPoints.Clear();
		this.RecentZoneKillCount = 0;
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
		this.processor.ZoneKillsUpdated -= this.OnZoneKillsUpdated;
		this.combatProcessor.DamageLogged -= this.OnDamageLogged;
		this.combatProcessor.HealLogged -= this.OnHealLogged;
		this.combatProcessor.MultiHitDetected -= this.OnMultiHitDetected;
		this.CombatSummary.PropertyChanged -= this.OnCombatSummaryPropertyChanged;
		this.TimeFilter.FilterChanged -= this.OnTimeFilterChanged;
		this.watchSession.ErrorOccurred -= this.OnWatchSessionError;
		this.updateService.ErrorOccurred -= this.OnUpdateError;
		this.watchController.Stop();
		this.parsingDebounceTimer.Stop();
		this.parsingDebounceTimer.Dispose();
		this.combatChartDebounceTimer.Stop();
		this.combatChartDebounceTimer.Dispose();
		this.sessionLoadCts?.Cancel();
		this.sessionLoadCts?.Dispose();
		this.SendNotification.Dispose();
		GC.SuppressFinalize(this);
	}
}
