using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
	private const int SEND_NOTIFICATION_MAX_AGE_SECONDS = 60;
	private const int COMBAT_CHART_DEBOUNCE_MS = 250;

	private System.Timers.Timer? parsingDebounceTimer;
	private System.Timers.Timer? combatChartDebounceTimer;

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

	public TimeFilterService TimeFilter { get; } = new();

	[ObservableProperty] private bool isSettingsPopupVisible;

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

	[ObservableProperty] private string? customChatLogPath;

	partial void OnCustomChatLogPathChanged(string? value)
	{
		this.settings.CustomChatLogPath = string.IsNullOrWhiteSpace(value)?null:value;
		this.settingsService.Save(this.settings);
	}

	[RelayCommand]
	private async Task BrowseChatLogPath(IStorageProvider? storageProvider)
	{
		if(storageProvider == null)
		{
			return;
		}

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
	private void ClearChatLogPath()
	{
		this.CustomChatLogPath = null;
	}

	[RelayCommand]
	private void ToggleSettingsPopup()
	{
		this.IsSettingsPopupVisible = !this.IsSettingsPopupVisible;
	}

	[RelayCommand]
	private void CloseSettingsPopup()
	{
		this.IsSettingsPopupVisible = false;
	}

	[ObservableProperty] private bool isWatching;

	[ObservableProperty] private bool isDarkTheme = true;

	public string ThemeIcon => this.IsDarkTheme?"☀ Light":"🌙 Dark";

	partial void OnIsDarkThemeChanged(bool value)
	{
		this.OnPropertyChanged(nameof(this.ThemeIcon));
	}

	[RelayCommand]
	private void ToggleTheme()
	{
		this.IsDarkTheme = !this.IsDarkTheme;
	}

	[ObservableProperty] private bool isSidebarVisible = true;

	public string SidebarToggleIcon => "◀";

	partial void OnIsSidebarVisibleChanged(bool value)
	{
		this.OnPropertyChanged(nameof(this.SidebarToggleIcon));
	}

	[RelayCommand]
	private void ToggleSidebar()
	{
		this.IsSidebarVisible = !this.IsSidebarVisible;
	}

	public ToggleState RpsChart { get; } = new();

	public ToggleState CumulativeRpChart { get; } = new();

	public ToggleState AbsoluteNumbers { get; } = new();

	public ToggleState AbsoluteRps { get; } = new();

	public ToggleState Percentages { get; } = new();

	public ToggleState AvgDmgChart { get; } = new();

	public ToggleState DmgByAttackerChart { get; } = new();

	public ToggleState HealsByHealerChart { get; } = new();

	public ToggleState HealsByTargetChart { get; } = new();

	// Tab visibility toggles are initialized in the constructor since their initial
	// values come from persisted AppSettings.
	public ToggleState RealmPointsTab { get; private set; } = null!;

	public ToggleState CombatTab { get; private set; } = null!;

	public ToggleState ZoneKillsTab { get; private set; } = null!;

	public ToggleState HealLogTab { get; private set; } = null!;

	public ToggleState CombatLogTab { get; private set; } = null!;


	public ToggleState KillHeatmapTab { get; private set; } = null!;

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

	public ObservableCollection<KillActivityPoint> KillActivityPoints { get; } = new();

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
	                           FrontierMapService frontierMapService)
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
		this.customChatLogPath = this.settings.CustomChatLogPath;
		this.showSendNotifications = this.settings.ShowSendNotifications;
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
		this.ZoneKillsTab = new ToggleState(this.settings.ShowZoneKillsTab,
		                                    v =>
		                                    {
			                                    this.settings.ShowZoneKillsTab = v;
			                                    this.settingsService.Save(this.settings);
		                                    });
		this.HealLogTab = new ToggleState(this.settings.ShowHealLogTab,
		                                  v =>
		                                  {
			                                  this.settings.ShowHealLogTab = v;
			                                  this.settingsService.Save(this.settings);
		                                  });
		this.CombatLogTab = new ToggleState(this.settings.ShowCombatLogTab,
		                                    v =>
		                                    {
			                                    this.settings.ShowCombatLogTab = v;
			                                    this.settingsService.Save(this.settings);
		                                    });
		this.KillHeatmapTab = new ToggleState(this.settings.ShowKillHeatmapTab,
		                                      v =>
		                                      {
			                                      this.settings.ShowKillHeatmapTab = v;
			                                      this.settingsService.Save(this.settings);
		                                      });
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
		_ = this.CheckForUpdatesAsync();
	}

	private void OnTimeFilterChanged(object? sender, EventArgs e)
	{
		if(this.IsWatching)
		{
			_ = this.RestartAsync();
		}
	}

	private void OnWatchSessionError(object? sender, string message)
	{
		Dispatcher.UIThread.InvokeAsync(() => this.WatchError = message);
	}

	private void OnCombatSummaryPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if(e.PropertyName is not ("TotalHealsReceived" or "TotalDamageDealt" or "TotalDamageTaken" or "TotalHealingDone"))
		{
			return;
		}

		if(this.combatChartDebounceTimer == null)
		{
			this.combatChartDebounceTimer = new System.Timers.Timer(COMBAT_CHART_DEBOUNCE_MS)
			                                {
					                                AutoReset = false
			                                };
			this.combatChartDebounceTimer.Elapsed += (_, _) => this.CombatChartsUpdateNeeded?.Invoke(this, EventArgs.Empty);
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

		string? path = null;
		if(!string.IsNullOrWhiteSpace(this.CustomChatLogPath)&&File.Exists(this.CustomChatLogPath))
		{
			path = this.CustomChatLogPath;
		}
		else
		{
			path = this.daocLogPathService.FindDaocLogPath();
		}

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
					                   HeatColor = GetZoneHeatColor(heatRatio)
			                   });
		}

		this.RecentZoneKillCount = totalCount;
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

	private static string GetZoneHeatColor(double ratio)
	{
		// Use a smooth green->yellow->red gradient based on hue.
		// 0.0 -> green (120°), 0.5 -> yellow (60°), 1.0 -> red (0°).
		ratio = Math.Clamp(ratio, 0.0, 1.0);
		var hue = 120.0 - 120.0 * ratio;
		return HslToHex(hue, 0.95, 0.55);
	}

	private static string HslToHex(double hue, double saturation, double lightness)
	{
		hue %= 360.0;
		if(hue < 0)
		{
			hue += 360.0;
		}

		var c = (1.0 - Math.Abs(2.0 * lightness - 1.0)) * saturation;
		var x = c * (1.0 - Math.Abs(hue / 60.0 % 2.0 - 1.0));
		var m = lightness - c / 2.0;

		double r1, g1, b1;
		if(hue < 60)
		{
			r1 = c;
			g1 = x;
			b1 = 0;
		}
		else if(hue < 120)
		{
			r1 = x;
			g1 = c;
			b1 = 0;
		}
		else if(hue < 180)
		{
			r1 = 0;
			g1 = c;
			b1 = x;
		}
		else if(hue < 240)
		{
			r1 = 0;
			g1 = x;
			b1 = c;
		}
		else if(hue < 300)
		{
			r1 = x;
			g1 = 0;
			b1 = c;
		}
		else
		{
			r1 = c;
			g1 = 0;
			b1 = x;
		}

		var r = (int)Math.Round((r1 + m) * 255.0);
		var g = (int)Math.Round((g1 + m) * 255.0);
		var b = (int)Math.Round((b1 + m) * 255.0);

		return $"#{r:X2}{g:X2}{b:X2}";
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
		this.parsingDebounceTimer?.Stop();
		this.parsingDebounceTimer?.Dispose();
		this.parsingDebounceTimer = null;
		this.IsParsing = false;
		this.IsWatching = false;
		this.Summary.IsLive = false;
		this.Summary.RefreshRpsPerHour();
	}

	[RelayCommand]
	private void StopWatching()
	{
		this.watchController.Stop();
		this.IsWatching = false;
		this.ResetAllState();
	}

	private void ResetAllState()
	{
		this.Summary.Reset();
		this.RpLog.Clear();
		this.HealLog.Clear();
		this.CombatLog.Clear();
		this.ChartData.Reset();
		this.CombatSummary.Reset();
		this.processor.Reset();
		this.combatProcessor.Reset();
		this.ZoneKills.Clear();
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
	private void ApplyUpdateAndRestart()
	{
		this.updateService.ApplyAndRestart();
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
		this.watchController.Stop();
		this.parsingDebounceTimer?.Stop();
		this.parsingDebounceTimer?.Dispose();
		this.combatChartDebounceTimer?.Stop();
		this.combatChartDebounceTimer?.Dispose();
		this.SendNotification.Dispose();
		GC.SuppressFinalize(this);
	}
}
