using System.Threading.Tasks;

namespace DAoCLogWatcher.UI.Services;

public interface IUpdateService
{
	Task<(string? VersionText, bool Available)> CheckForUpdatesAsync();

	void ApplyAndRestart();
}
