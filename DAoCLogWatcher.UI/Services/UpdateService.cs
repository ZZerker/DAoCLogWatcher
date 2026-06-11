using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace DAoCLogWatcher.UI.Services;

public sealed class UpdateService: IUpdateService
{
	private const string GITHUB_URL = "https://github.com/ZZerker/DAoCLogWatcher";

	private static readonly string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DAoCLogWatcher", "update.log");

#if !FLATPAK
	private UpdateInfo? pendingUpdate;
	private Task? downloadTask;
	private bool downloadFailed;
#endif

	public event EventHandler<string>? ErrorOccurred;

	/// <summary>
	/// Checks GitHub for a newer release and returns the version label immediately.
	/// If an update is found, downloads it in the background so it is ready to apply.
	/// Returns (null, false) on any failure or when not installed via Velopack.
	/// </summary>
	public async Task<(string? VersionText, bool Available)> CheckForUpdatesAsync()
	{
#if FLATPAK
		return await Task.FromResult<(string?, bool)>((null, false));
#else
		try
		{
			var mgr = new UpdateManager(new GithubSource(GITHUB_URL, null, false));
			if(!mgr.IsInstalled)
			{
				return (null, false);
			}

			var update = await mgr.CheckForUpdatesAsync();
			if(update == null)
			{
				return (null, false);
			}

			// Download in the background — caller gets the banner immediately.
			// Store the task so ApplyAndRestart can await it if the user clicks before download finishes.
			this.downloadTask = Task.Run(async () =>
			                            {
				                            try
				                            {
					                            await mgr.DownloadUpdatesAsync(update);
					                            this.pendingUpdate = update;
				                            }
				                            catch(Exception ex)
				                            {
					                            this.downloadFailed = true;
					                            this.ReportError("download", ex);
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

	/// <summary>
	/// Applies the downloaded update and restarts the application.
	/// Waits for the background download to complete if it is still in progress.
	/// No-op if no update is available.
	/// </summary>
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
			var mgr = new UpdateManager(new GithubSource(GITHUB_URL, null, false));
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
		var detail = $"{ex.GetType().Name}: {ex.Message}";
		Debug.WriteLine($"[UpdateService.{context}] {detail}");

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
			File.AppendAllText(LogPath, $"{DateTime.Now:u} [{context}] {detail}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
		}
		catch(Exception logEx)
		{
			Debug.WriteLine($"[UpdateService.log] {logEx.GetType().Name}: {logEx.Message}");
		}

		this.RaiseError($"Update {context} failed: {ex.Message}");
	}

	private void RaiseError(string message)
	{
		this.ErrorOccurred?.Invoke(this, message);
	}
}
