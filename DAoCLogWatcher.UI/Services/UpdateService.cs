using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace DAoCLogWatcher.UI.Services;

public sealed class UpdateService
{
	private const string GithubUrl = "https://github.com/ZZerker/DAoCLogWatcher";
	private UpdateInfo? pendingUpdate;

	/// <summary>
	/// Checks GitHub for a newer release, downloads it if found, and returns
	/// the version label and whether an update is ready. Returns (null, false) on any failure.
	/// </summary>
	public async Task<(string? VersionText, bool Available)> CheckForUpdatesAsync()
	{
		try
		{
			var mgr = new UpdateManager(new GithubSource(GithubUrl, null, false));
			if (!mgr.IsInstalled)
				return (null, false);

			var update = await mgr.CheckForUpdatesAsync();
			if (update == null)
				return (null, false);

			await mgr.DownloadUpdatesAsync(update);

			this.pendingUpdate = update;
			return ($"v{update.TargetFullRelease.Version} available", true);
		}
		catch (Exception ex)
		{
			_ = ex;
			return (null, false);
		}
	}

	/// <summary>
	/// Applies the downloaded update and restarts the application.
	/// No-op if no update has been downloaded.
	/// </summary>
	public void ApplyAndRestart()
	{
		if (this.pendingUpdate == null)
			return;

		try
		{
			var mgr = new UpdateManager(new GithubSource(GithubUrl, null, false));
			mgr.ApplyUpdatesAndRestart(this.pendingUpdate);
		}
		catch (Exception ex)
		{
			_ = ex;
		}
	}
}
