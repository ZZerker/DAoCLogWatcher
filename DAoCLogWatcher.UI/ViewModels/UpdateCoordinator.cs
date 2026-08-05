using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DAoCLogWatcher.UI.Models;
using DAoCLogWatcher.UI.Services;

namespace DAoCLogWatcher.UI.ViewModels;

/// <summary>
/// Owns the in-app update lifecycle: periodic check timer, banner state and the dismiss/apply
/// commands. All state is UI-thread confined.
/// </summary>
public sealed partial class UpdateCoordinator: ObservableObject, IDisposable
{
	/// <summary>Lower bound for the runtime update-check interval, so a mis-set value can't hammer GitHub.</summary>
	private const int MIN_UPDATE_CHECK_INTERVAL_MINUTES = 15;

	private readonly IUpdateService updateService;
	private readonly SettingsPopupViewModel settingsPopup;
	private readonly System.Timers.Timer checkTimer;

	// UI-thread only. A download can fail before the check continuation runs; without this flag
	// the continuation would then set IsDownloading and stop the timer, wedging retries.
	private bool downloadFailedSinceLastCheck;

	[ObservableProperty] private bool isUpdateAvailable;
	[ObservableProperty] private string? updateVersionText;
	[ObservableProperty] private string? updateError;

	/// <summary>True while the update is downloading in the background.</summary>
	[ObservableProperty] private bool isDownloading;

	/// <summary>True once the update has downloaded and only a restart is left to install it.</summary>
	[ObservableProperty] private bool isRestartRequired;

	public UpdateCoordinator(IUpdateService updateService, AppSettings settings, SettingsPopupViewModel settingsPopup)
	{
		this.updateService = updateService;
		this.settingsPopup = settingsPopup;
		this.updateService.ErrorOccurred += this.OnUpdateError;
		this.updateService.UpdateReady += this.OnUpdateReady;
		this.updateService.DownloadFailed += this.OnDownloadFailed;
		this.settingsPopup.UpdateCheckIntervalChanged += this.OnUpdateCheckIntervalChanged;
		this.checkTimer = new System.Timers.Timer(ResolveUpdateCheckIntervalMs(settings.UpdateCheckIntervalMinutes))
		                  {
				                  AutoReset = true
		                  };
		this.checkTimer.Elapsed += this.OnCheckTimerElapsed;
		this.checkTimer.Start();
		this.FireAndForget(this.CheckForUpdatesAsync());
	}

	private void OnUpdateError(object? sender, string message)
	{
		Dispatcher.UIThread.InvokeAsync(() => this.UpdateError = message);
	}

	private void OnUpdateReady(object? sender, EventArgs e)
	{
		Dispatcher.UIThread.InvokeAsync(() =>
		                                {
			                                this.IsDownloading = false;
			                                this.IsRestartRequired = true;
			                                this.UpdateError = null;
		                                });
	}

	private void OnDownloadFailed(object? sender, EventArgs e)
	{
		// Resume polling — the next check starts a fresh download attempt.
		Dispatcher.UIThread.InvokeAsync(() =>
		                                {
			                                this.downloadFailedSinceLastCheck = true;
			                                this.IsDownloading = false;
			                                this.checkTimer.Start();
		                                });
	}

	private void OnCheckTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
	{
		Dispatcher.UIThread.InvokeAsync(() =>
		                                {
			                                if(this.IsRestartRequired||this.IsDownloading)
			                                {
				                                return;
			                                }

			                                this.FireAndForget(this.CheckForUpdatesAsync());
		                                });
	}

	private void OnUpdateCheckIntervalChanged(object? sender, int minutes)
	{
		this.checkTimer.Interval = ResolveUpdateCheckIntervalMs(minutes);
	}

	private static double ResolveUpdateCheckIntervalMs(int minutes)
	{
		return Math.Max(MIN_UPDATE_CHECK_INTERVAL_MINUTES, minutes) * 60_000.0;
	}

	private async Task CheckForUpdatesAsync()
	{
		this.downloadFailedSinceLastCheck = false;
		var (text, available) = await this.updateService.CheckForUpdatesAsync();
		this.UpdateVersionText = text;
		this.IsUpdateAvailable = available;
		if(available&&!this.IsRestartRequired&&!this.downloadFailedSinceLastCheck)
		{
			// Pause polling while the download runs; OnDownloadFailed resumes it.
			this.IsDownloading = true;
			this.checkTimer.Stop();
		}
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

	private void FireAndForget(Task task)
	{
		task.ContinueWith(t => Dispatcher.UIThread.InvokeAsync(() => this.UpdateError = t.Exception!.Flatten().InnerExceptions[0].Message), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
	}

	public void Dispose()
	{
		this.updateService.ErrorOccurred -= this.OnUpdateError;
		this.updateService.UpdateReady -= this.OnUpdateReady;
		this.updateService.DownloadFailed -= this.OnDownloadFailed;
		this.settingsPopup.UpdateCheckIntervalChanged -= this.OnUpdateCheckIntervalChanged;
		this.checkTimer.Elapsed -= this.OnCheckTimerElapsed;
		this.checkTimer.Stop();
		this.checkTimer.Dispose();
	}
}
