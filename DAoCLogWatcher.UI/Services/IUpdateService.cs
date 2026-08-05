using System;
using System.Threading.Tasks;

namespace DAoCLogWatcher.UI.Services;

public interface IUpdateService
{
	/// <summary>Raised when a download or apply step fails. Argument is a user-facing message.</summary>
	event EventHandler<string>? ErrorOccurred;

	/// <summary>
	/// Raised once the update has finished downloading in the background and is ready to be
	/// applied — i.e. a restart is now all that is needed to install the new version.
	/// </summary>
	event EventHandler? UpdateReady;

	/// <summary>Raised when the background download fails. A later check retries, so callers should resume polling.</summary>
	event EventHandler? DownloadFailed;

	Task<(string? VersionText, bool Available)> CheckForUpdatesAsync();

	Task ApplyAndRestartAsync();
}
