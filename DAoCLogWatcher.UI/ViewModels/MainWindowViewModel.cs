using System;
using System.Collections.Generic;
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

namespace DAoCLogWatcher.UI.ViewModels;

public partial class MainWindowViewModel: ViewModelBase
{
	private CancellationTokenSource? cancellationTokenSource;
	private System.Timers.Timer? rpsRefreshTimer;

	[ObservableProperty] private string? currentFilePath;

	[ObservableProperty] [NotifyPropertyChangedFor(nameof(IsAnyFilterEnabled))] private bool enableSixHourFiltering;

	[ObservableProperty] [NotifyPropertyChangedFor(nameof(IsAnyFilterEnabled))] private bool enableTimeFiltering = true;

	[ObservableProperty] private bool isWatching;

	[ObservableProperty] private ObservableCollection<RealmPointLogEntry> logEntries = [];

	private LogWatcher? logWatcher;

	[ObservableProperty] private RealmPointSummary summary = new();

	// Chart data: (time in seconds from start, cumulative RPs)
	public List<(double Time, double Rps)> ChartDataPoints { get; } = new();
	private DateTime? chartStartTime;

	public event EventHandler? ChartUpdateRequested;

	public MainWindowViewModel()
	{

		Console.WriteLine($"[ViewModel Constructor] Added {this.LogEntries.Count} demo entries");
	}

	public bool IsAnyFilterEnabled => this.EnableTimeFiltering||this.EnableSixHourFiltering;

	partial void OnEnableSixHourFilteringChanged(bool value)
	{
		if(value&&this.EnableTimeFiltering)
		{
			this.EnableTimeFiltering = false;
		}
	}

	partial void OnEnableTimeFilteringChanged(bool value)
	{
		if(value&&this.EnableSixHourFiltering)
		{
			this.EnableSixHourFiltering = false;
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

	/// <summary>
	/// Resolves the DAoC chat.log path for the current platform.
	/// On Windows: %USERPROFILE%\Documents\Electronic Arts\Dark Age of Camelot\chat.log
	/// On Linux: DAoC runs via Wine; checks the default Wine prefix and common Lutris paths.
	/// </summary>
	private static string? FindDaocLogPath()
	{
		if (OperatingSystem.IsWindows())
		{
			var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
			return Path.Combine(documents, "Electronic Arts", "Dark Age of Camelot", "chat.log");
		}

		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

		// Common Wine/Lutris paths for DAoC on Linux
		var candidates = new[]
		{
			// Default Wine prefix
			Path.Combine(home, ".wine", "drive_c", "users", Environment.UserName, "My Documents", "Electronic Arts", "Dark Age of Camelot", "chat.log"),
			// Lutris default Wine prefix
			Path.Combine(home, "Games", "dark-age-of-camelot", "drive_c", "users", Environment.UserName, "My Documents", "Electronic Arts", "Dark Age of Camelot", "chat.log"),
			// Lutris with "Documents" folder name
			Path.Combine(home, "Games", "dark-age-of-camelot", "drive_c", "users", Environment.UserName, "Documents", "Electronic Arts", "Dark Age of Camelot", "chat.log"),
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

		// Track first and last entry timestamps
		// Use the session start from the log file if available, otherwise use today
		var sessionDate = this.logWatcher?.CurrentSessionStart?.Date ?? DateTime.Now.Date;
		var entryDateTime = sessionDate.Add(entry.Timestamp.ToTimeSpan());
		if (!this.Summary.FirstEntryTime.HasValue)
		{
			this.Summary.FirstEntryTime = entryDateTime;
		}
		this.Summary.LastEntryTime = entryDateTime;

		switch(entry.Source)
		{
			case RealmPointSource.PlayerKill:
				this.Summary.PlayerKills++;
				this.Summary.PlayerKillsRp += entry.Points;
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
			case RealmPointSource.Unknown:
				this.Summary.Unknown++;
				this.Summary.UnknownRP += entry.Points;
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
				_ => ""
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

		// Update chart data
		UpdateChartData(entryDateTime, entry.Points);
	}

	private void UpdateChartData(DateTime entryTime, int points)
	{
		if (!this.chartStartTime.HasValue)
		{
			this.chartStartTime = entryTime;
		}

		var timeFromStart = (entryTime - this.chartStartTime.Value).TotalMinutes;
		var cumulativeRps = this.Summary.TotalRealmPoints;

		this.ChartDataPoints.Add((timeFromStart, cumulativeRps));

		// Trigger chart update
		this.ChartUpdateRequested?.Invoke(this, EventArgs.Empty);
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
		this.ChartDataPoints.Clear();
		this.chartStartTime = null;

		this.cancellationTokenSource = new CancellationTokenSource();

		// Start the RPS refresh timer
		this.rpsRefreshTimer = new System.Timers.Timer(5000); // 10 seconds
		this.rpsRefreshTimer.Elapsed += (s, e) => Dispatcher.UIThread.InvokeAsync(() => this.Summary.RefreshRpsPerHour());
		this.rpsRefreshTimer.AutoReset = true;
		this.rpsRefreshTimer.Start();

		var filterEnabled = this.EnableTimeFiltering||this.EnableSixHourFiltering;
		var filterHours = this.EnableSixHourFiltering?6:24;
		this.logWatcher = new LogWatcher(this.CurrentFilePath, 0, filterEnabled, filterHours);

		Console.WriteLine($"[StartWatching] Starting watch on: {this.CurrentFilePath}, TimeFilter: {filterEnabled}, FilterHours: {filterHours}");

		try
		{
			await foreach(var logLine in this.logWatcher.WatchAsync(this.cancellationTokenSource.Token))
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
			this.IsWatching = false;
		}
	}

	[RelayCommand]
	private void StopWatching()
	{
		this.cancellationTokenSource?.Cancel();
		this.rpsRefreshTimer?.Stop();
		this.rpsRefreshTimer?.Dispose();
		this.rpsRefreshTimer = null;
		this.IsWatching = false;
	}
}
