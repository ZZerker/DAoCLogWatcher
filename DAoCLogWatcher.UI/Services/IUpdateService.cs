using System;
using System.Threading.Tasks;

namespace DAoCLogWatcher.UI.Services;

public interface IUpdateService
{
	/// <summary>Raised when a download or apply step fails. Argument is a user-facing message.</summary>
	event EventHandler<string>? ErrorOccurred;

	Task<(string? VersionText, bool Available)> CheckForUpdatesAsync();

	Task ApplyAndRestartAsync();
}
