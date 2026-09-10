using System;
using System.Timers;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DAoCLogWatcher.UI.ViewModels;

public sealed partial class SendNotificationController: ObservableObject, IDisposable
{
	private const int MIN_DURATION_SECONDS = 5;
	private const int MAX_DURATION_SECONDS = 300;
	public const int DEFAULT_DURATION_SECONDS = 60;

	private Timer? timer;

	private int durationSeconds = DEFAULT_DURATION_SECONDS;

	// A queued expiry from the previous toast must not hide the next one.
	private int showGeneration;

	private bool disposed;

	public int DurationSeconds
	{
		get => this.durationSeconds;
		set => this.durationSeconds = Math.Clamp(value, MIN_DURATION_SECONDS, MAX_DURATION_SECONDS);
	}

	private double IntervalMs => this.DurationSeconds * 1000.0;

	[ObservableProperty] private bool isVisible;
	[ObservableProperty] private string? sender;
	[ObservableProperty] private string? message;

	public void Show(string sender, string message)
	{
		if(this.disposed)
		{
			return;
		}

		this.showGeneration++;
		this.Sender = sender;
		this.Message = message;
		this.IsVisible = true;

		if(this.timer == null)
		{
			this.timer = new Timer
			             {
					             AutoReset = false
			             };
			this.timer.Elapsed += (_, _) =>
			                      {
				                      var expired = this.showGeneration;
				                      Dispatcher.UIThread.InvokeAsync(() =>
				                                                      {
					                                                      if(expired == this.showGeneration)
					                                                      {
						                                                      this.IsVisible = false;
					                                                      }
				                                                      });
			                      };
		}

		this.timer.Stop();
		this.timer.Interval = this.IntervalMs;
		this.timer.Start();
	}

	[RelayCommand]
	private void Dismiss()
	{
		this.IsVisible = false;
	}

	public void Dispose()
	{
		this.disposed = true;
		this.timer?.Stop();
		this.timer?.Dispose();
		this.timer = null;
	}
}
