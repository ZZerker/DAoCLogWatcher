using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DAoCLogWatcher.Core;
using DAoCLogWatcher.Core.Models;

namespace DAoCLogWatcher.UI.Services;

public interface IWatchSession: IDisposable
{
	DateTime? CurrentSessionStart { get; }

	event EventHandler<string>? ErrorOccurred;

	Task RunAsync(LogWatcher watcher, Action onRpsRefresh, Func<LogLine, Task> onLine);

	Task StopAndWaitAsync();

	void RequestStop();
}

public sealed class WatchSession: IWatchSession
{
	private const int RPS_REFRESH_INTERVAL_MS = 5000;

	private CancellationTokenSource? cancellationTokenSource;
	private Task? currentTask;
	private System.Timers.Timer? rpsRefreshTimer;
	private LogWatcher? currentWatcher;

	public event EventHandler<string>? ErrorOccurred;

	public DateTime? CurrentSessionStart => this.currentWatcher?.CurrentSessionStart;

	public Task RunAsync(LogWatcher watcher, Action onRpsRefresh, Func<LogLine, Task> onLine)
	{
		var cts = new CancellationTokenSource();
		this.cancellationTokenSource = cts;
		this.currentWatcher = watcher;
		this.currentTask = this.RunCoreAsync(watcher, cts, onRpsRefresh, onLine);
		return this.currentTask;
	}

	private async Task RunCoreAsync(LogWatcher watcher, CancellationTokenSource cts, Action onRpsRefresh, Func<LogLine, Task> onLine)
	{
		this.rpsRefreshTimer = new System.Timers.Timer(RPS_REFRESH_INTERVAL_MS)
		                       {
				                       AutoReset = true
		                       };
		this.rpsRefreshTimer.Elapsed += (_, _) => onRpsRefresh();
		this.rpsRefreshTimer.Start();

		try
		{
			await foreach(var logLine in watcher.WatchAsync(cts.Token))
			{
				var capturedLine = logLine;
				await onLine(capturedLine);
			}
		}
		catch(OperationCanceledException)
		{
		}
		catch(Exception ex)
		{
			Trace.WriteLine($"[WatchSession] {ex}");
			this.ErrorOccurred?.Invoke(this, $"{ex.GetType().Name}: {ex.Message}");
		}
		finally
		{
			this.rpsRefreshTimer.Stop();
			this.rpsRefreshTimer.Dispose();
			this.rpsRefreshTimer = null;
			this.currentWatcher = null;
			cts.Dispose();
			if(ReferenceEquals(this.cancellationTokenSource, cts))
			{
				this.cancellationTokenSource = null;
			}
		}
	}

	public async Task StopAndWaitAsync()
	{
		var task = this.currentTask;
		this.cancellationTokenSource?.Cancel();
		if(task != null)
		{
			await task;
		}
	}

	public void RequestStop()
	{
		this.cancellationTokenSource?.Cancel();
	}

	public void Dispose()
	{
		this.RequestStop();
		this.cancellationTokenSource?.Dispose();
		GC.SuppressFinalize(this);
	}
}
