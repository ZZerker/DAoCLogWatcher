using DAoCLogWatcher.UI.Models;

namespace DAoCLogWatcher.UI.Services;

public interface ISettingsService
{
	AppSettings Load();

	void Save(AppSettings settings);
}
