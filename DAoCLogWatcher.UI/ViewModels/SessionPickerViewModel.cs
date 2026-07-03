using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DAoCLogWatcher.Core;
using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Models;

namespace DAoCLogWatcher.UI.ViewModels;

/// <summary>Owns the recent-sessions list, its background scan, the chat.log poll timer
/// (for when DAoC has not been launched yet), and the window-activation rescan debounce.
/// Opening a session is delegated back to the owner via <paramref name="openSession"/> since
/// that drives the watch lifecycle, which stays owned by <see cref="MainWindowViewModel"/>.</summary>
public sealed partial class SessionPickerViewModel: ObservableObject, IDisposable
{
	private const int LOG_FILE_POLL_INTERVAL_MS = 10000;
	private const int ACTIVATION_RESCAN_MIN_INTERVAL_SECONDS = 30;

	private readonly Func<string?> getLogPath;
	private readonly Func<string, LogSession, Task> openSession;
	private readonly Action<string> reportError;

	// Polls for the chat.log appearing when it (or its directory) does not yet exist at
	// startup, e.g. DAoC has not been launched. Null once the file is found or never missing.
	private System.Timers.Timer? logFilePollTimer;

	// UTC time the most recent recent-sessions scan was launched; used to debounce
	// window-activation rescans. Kept in the VM so the debounce logic stays testable.
	private DateTime lastSessionScanStartedUtc = DateTime.MinValue;

	private CancellationTokenSource? sessionLoadCts;

	public ObservableCollection<RecentSessionEntry> RecentSessions { get; } = new();

	public SessionPickerViewModel(Func<string?> getLogPath, Func<string, LogSession, Task> openSession, Action<string> reportError)
	{
		this.getLogPath = getLogPath;
		this.openSession = openSession;
		this.reportError = reportError;
		this.StartLogFilePollingIfNeeded();
	}

	[RelayCommand]
	private Task LoadRecentSessions()
	{
		return this.LoadRecentSessionsFromPathAsync(this.getLogPath());
	}

	public async Task LoadRecentSessionsFromPathAsync(string? path)
	{
		if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))
		{
			return;
		}

		this.lastSessionScanStartedUtc = DateTime.UtcNow;

		this.sessionLoadCts?.Cancel();
		var cts = new CancellationTokenSource();
		this.sessionLoadCts = cts;
		var ct = cts.Token;

		var sessions = await Task.Run(() => LogSessionScanner.Scan(path), ct);
		if(ct.IsCancellationRequested||!ReferenceEquals(cts, this.sessionLoadCts))
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
					                        OpenCommand = new AsyncRelayCommand(() => this.openSession(path, s))
			                        });
		}
	}

	/// <summary>If the chat.log (or its directory) does not yet exist at startup — e.g. DAoC has
	/// not been launched — poll for it to appear, then load recent sessions and stop polling.</summary>
	private void StartLogFilePollingIfNeeded()
	{
		var path = this.getLogPath();
		if(!string.IsNullOrWhiteSpace(path)&&File.Exists(path))
		{
			return;
		}

		this.logFilePollTimer = new System.Timers.Timer(LOG_FILE_POLL_INTERVAL_MS)
		                        {
				                        AutoReset = true
		                        };
		this.logFilePollTimer.Elapsed += this.OnLogFilePollElapsed;
		this.logFilePollTimer.Start();
	}

	private void OnLogFilePollElapsed(object? sender, System.Timers.ElapsedEventArgs e)
	{
		var path = this.getLogPath();
		if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))
		{
			return;
		}

		this.StopLogFilePolling();
		Dispatcher.UIThread.InvokeAsync(() => this.FireAndForget(this.LoadRecentSessionsCommand.ExecuteAsync(null)));
	}

	private void StopLogFilePolling()
	{
		if(this.logFilePollTimer == null)
		{
			return;
		}

		this.logFilePollTimer.Elapsed -= this.OnLogFilePollElapsed;
		this.logFilePollTimer.Stop();
		this.logFilePollTimer.Dispose();
		this.logFilePollTimer = null;
	}

	/// <summary>Rescans recent sessions when the main window is activated, debounced so an
	/// activation within <see cref="ACTIVATION_RESCAN_MIN_INTERVAL_SECONDS"/> of the last scan
	/// is skipped. The scan is read-only and the stale-continuation guard handles overlap.
	/// Returns true if a rescan was launched.</summary>
	public bool OnWindowActivated()
	{
		var nowUtc = DateTime.UtcNow;
		if((nowUtc - this.lastSessionScanStartedUtc).TotalSeconds < ACTIVATION_RESCAN_MIN_INTERVAL_SECONDS)
		{
			return false;
		}

		this.lastSessionScanStartedUtc = nowUtc;
		this.FireAndForget(this.LoadRecentSessionsCommand.ExecuteAsync(null));
		return true;
	}

	/// <summary>Rescans after a watch loop finishes so the completed session appears in the list.</summary>
	public void RescanAfterWatchStopped()
	{
		this.FireAndForget(this.LoadRecentSessionsCommand.ExecuteAsync(null));
	}

	private void FireAndForget(Task task)
	{
		task.ContinueWith(t => Dispatcher.UIThread.InvokeAsync(() => this.reportError(t.Exception!.Flatten().InnerExceptions[0].Message)), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
	}

	public void Dispose()
	{
		this.StopLogFilePolling();
		this.sessionLoadCts?.Cancel();
		this.sessionLoadCts?.Dispose();
		GC.SuppressFinalize(this);
	}
}
