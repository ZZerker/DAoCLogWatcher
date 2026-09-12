using System;
using System.IO;

namespace DAoCLogWatcher.UI.Services;

/// <summary>
/// The one place that resolves where settings.json, sessions.json, the zone export and the logs live.
/// </summary>
public static class AppPaths
{
	/// <summary>
	/// %AppData%\DAoCLogWatcher on Windows, $XDG_CONFIG_HOME/DAoCLogWatcher or ~/.config/DAoCLogWatcher elsewhere.
	/// DoNotVerify is essential: with the default option .NET returns an empty string when ~/.config does not
	/// exist yet, which turned this into a relative path and made the app write all its data into whatever
	/// directory it was launched from (BUG-008). DoNotVerify never throws; callers create the directory on write.
	/// </summary>
	public static string DataDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify), "DAoCLogWatcher");
}
