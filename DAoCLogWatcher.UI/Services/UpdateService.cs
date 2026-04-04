using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace DAoCLogWatcher.UI.Services;

public sealed class UpdateService: IUpdateService
{
	private const string GITHUB_URL = "https://github.com/ZZerker/DAoCLogWatcher";
	private UpdateInfo? pendingUpdate;

	/// <summary>
	/// Checks GitHub for a newer release and returns the version label immediately.
	/// If an update is found, downloads it in the background so it is ready to apply.
	/// Returns (null, false) on any failure or when not installed via Velopack.
	/// </summary>
	public async Task<(string? VersionText, bool Available)> CheckForUpdatesAsync()
	{
		try
		{
			var mgr = new UpdateManager(new GithubSource(GITHUB_URL, null, false));
			if(!mgr.IsInstalled)
				return (null, false);

			var update = await mgr.CheckForUpdatesAsync();
			if(update == null)
				return (null, false);

			// Download in the background — caller gets the banner immediately.
			_ = Task.Run(async () =>
			             {
				             try
				             {
					             await mgr.DownloadUpdatesAsync(update);
					             this.pendingUpdate = update;
				             }
				             catch(Exception ex)
				             {
					             Debug.WriteLine($"[UpdateService.DownloadUpdatesAsync] {ex.GetType().Name}: {ex.Message}");
				             }
			             });

			return ($"v{update.TargetFullRelease.Version} available", true);
		}
		catch(Exception ex)
		{
			Debug.WriteLine($"[UpdateService.CheckForUpdatesAsync] {ex.GetType().Name}: {ex.Message}");
			return (null, false);
		}
	}

	/// <summary>
	/// Applies the downloaded update and restarts the application.
	/// No-op if no update has been downloaded.
	/// </summary>
	public void ApplyAndRestart()
	{
		if(this.pendingUpdate == null)
			return;

		try
		{
			var mgr = new UpdateManager(new GithubSource(GITHUB_URL, null, false));
			mgr.ApplyUpdatesAndRestart(this.pendingUpdate);
		}
		catch(Exception ex)
		{
			Debug.WriteLine($"[UpdateService.ApplyAndRestart] {ex.GetType().Name}: {ex.Message}");
		}
	}
}
