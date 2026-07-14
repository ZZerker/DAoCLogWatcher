using System;
using System.Runtime.InteropServices;

namespace DAoCLogWatcher.UI.Services;

public sealed class NotificationService: INotificationService
{
	public void ShowToast(string title, string body)
	{
#if WINDOWS10_0_17763_OR_GREATER
		try
		{
			new Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder()
				.AddText(title)
				.AddText(body)
				.Show();
		}
		catch (Exception ex)
		{
			AppLog.Exception("NotificationService.ShowToast", ex);
		}
#endif
	}

	public void PlayNotificationSound()
	{
		if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			try
			{
				// Windows "Notification" system sound (MB_ICONASTERISK = 0x40)
				MessageBeep(0x40);
			}
			catch(Exception ex)
			{
				AppLog.Exception("NotificationService.PlayNotificationSound", ex);
			}
		}
	}

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool MessageBeep(uint type);
}
