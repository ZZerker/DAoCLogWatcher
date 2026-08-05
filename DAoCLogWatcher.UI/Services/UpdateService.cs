using System;
using System.Threading.Tasks;
using DAoCLogWatcher.UI.Models;
#if !FLATPAK
using Velopack;
using Velopack.Sources;
#endif

namespace DAoCLogWatcher.UI.Services;

public sealed class UpdateService: IUpdateService
{
	private const string GITHUB_URL = "https://github.com/ZZerker/DAoCLogWatcher";

	private readonly AppSettings settings;

#if !FLATPAK
	private UpdateInfo? pendingUpdate;
	private Task? downloadTask;
	private bool downloadFailed;
#endif

	public UpdateService(AppSettings settings)
	{
		this.settings = settings;
	}

	public event EventHandler<string>? ErrorOccurred;

	public event EventHandler? UpdateReady;

	public event EventHandler? DownloadFailed;

	/// <summary>
	/// Returns immediately once a newer release is found and downloads it in the background.
	/// Returns (null, false) on any failure or when not installed via Velopack.
	/// </summary>
	public async Task<(string? VersionText, bool Available)> CheckForUpdatesAsync()
	{
#if FLATPAK
		return await Task.FromResult<(string?, bool)>((null, false));
#else
		try
		{
			// A fresh check supersedes any earlier failed download attempt.
			this.downloadTask = null;
			this.downloadFailed = false;

			AppLog.Info("UpdateService", $"Checking for updates (prereleases={this.settings.UsePrereleases}).");

			var mgr = new UpdateManager(new GithubSource(GITHUB_URL, null, this.settings.UsePrereleases));
			if(!mgr.IsInstalled)
			{
				AppLog.Info("UpdateService", "Not a Velopack install (IsInstalled=false) — skipping update check. This is expected for dev/`dotnet run` builds; only a packed AppImage/Setup self-updates.");
				return (null, false);
			}

			var update = await mgr.CheckForUpdatesAsync();
			if(update == null)
			{
				AppLog.Info("UpdateService", "Already up to date — no newer release found on the selected channel.");
				return (null, false);
			}

			AppLog.Info("UpdateService", $"Update found: v{update.TargetFullRelease.Version}. Downloading in the background.");

			// Stored so ApplyAndRestartAsync can await it if the user clicks before the download finishes.
			this.downloadTask = Task.Run(async () =>
			                            {
				                            try
				                            {
					                            await mgr.DownloadUpdatesAsync(update);
					                            this.pendingUpdate = update;
					                            AppLog.Info("UpdateService", $"Update v{update.TargetFullRelease.Version} downloaded — restart required to install.");
					                            this.UpdateReady?.Invoke(this, EventArgs.Empty);
				                            }
				                            catch(Exception ex)
				                            {
					                            this.downloadFailed = true;
					                            this.ReportError("download", ex);
					                            this.DownloadFailed?.Invoke(this, EventArgs.Empty);
				                            }
			                            });

			return ($"v{update.TargetFullRelease.Version} available", true);
		}
		catch(Exception ex)
		{
			this.ReportError("check", ex);
			return (null, false);
		}
#endif
	}

	/// <summary>Waits for the background download if still in progress; no-op when no update is pending.</summary>
	public async Task ApplyAndRestartAsync()
	{
#if FLATPAK
		await Task.CompletedTask;
#else
		if(this.downloadTask != null)
		{
			await this.downloadTask;
		}

		if(this.pendingUpdate == null)
		{
			if(this.downloadFailed)
			{
				this.RaiseError("Update download failed. See update.log for details.");
			}

			return;
		}

		try
		{
			var mgr = new UpdateManager(new GithubSource(GITHUB_URL, null, this.settings.UsePrereleases));
			mgr.ApplyUpdatesAndRestart(this.pendingUpdate);
		}
		catch(Exception ex)
		{
			this.ReportError("apply", ex);
		}
#endif
	}

	private void ReportError(string context, Exception ex)
	{
		AppLog.Exception($"UpdateService.{context}", ex);
		this.RaiseError($"Update {context} failed: {ex.Message}");
	}

	private void RaiseError(string message)
	{
		this.ErrorOccurred?.Invoke(this, message);
	}
}
