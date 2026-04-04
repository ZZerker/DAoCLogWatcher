namespace DAoCLogWatcher.UI.Services;

public interface INotificationService
{
	void ShowToast(string title, string body);

	void PlayNotificationSound();
}
