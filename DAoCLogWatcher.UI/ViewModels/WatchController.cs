using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using DAoCLogWatcher.Core;
using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Services;

namespace DAoCLogWatcher.UI.ViewModels;

internal sealed class WatchController
{
	private readonly IWatchSession watchSession;
	private int generation;

	public WatchController(IWatchSession watchSession)
	{
		this.watchSession = watchSession;
	}

	public async Task RunAsync(LogWatcher watcher, Action onStarted, Action onRpsRefresh, Func<LogLine, Task> onLine, Action onFinished)
	{
		var myGeneration = ++this.generation;
		onStarted();

		await this.watchSession.RunAsync(watcher,
		                                 () => Dispatcher.UIThread.InvokeAsync(onRpsRefresh),
		                                 async line =>
		                                 {
			                                 var captured = line;
			                                 await Dispatcher.UIThread.InvokeAsync(() => onLine(captured), DispatcherPriority.Normal);
		                                 });

		if(myGeneration == this.generation)
		{
			onFinished();
		}
	}

	public void Stop()
	{
		this.watchSession.RequestStop();
	}
}
