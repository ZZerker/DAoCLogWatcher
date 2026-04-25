using System;
using System.Timers;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DAoCLogWatcher.UI.ViewModels;

public sealed partial class SendNotificationController: ObservableObject, IDisposable
{
	private const int TIMEOUT_MS = 5000;

	private Timer? timer;

	[ObservableProperty] private bool isVisible;
	[ObservableProperty] private string? sender;
	[ObservableProperty] private string? message;

	public void Show(string sender, string message)
	{
		this.Sender = sender;
		this.Message = message;
		this.IsVisible = true;

		if(this.timer == null)
		{
			this.timer = new Timer(TIMEOUT_MS)
			             {
					             AutoReset = false
			             };
			this.timer.Elapsed += (_, _) => Dispatcher.UIThread.InvokeAsync(() => this.IsVisible = false);
		}

		this.timer.Stop();
		this.timer.Start();
	}

	[RelayCommand]
	private void Dismiss()
	{
		this.IsVisible = false;
	}

	public void Dispose()
	{
		this.timer?.Stop();
		this.timer?.Dispose();
		this.timer = null;
	}
}
