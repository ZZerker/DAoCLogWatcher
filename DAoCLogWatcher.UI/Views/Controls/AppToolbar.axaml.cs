using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DAoCLogWatcher.UI.Views.Controls;

public partial class AppToolbar: UserControl
{
	public static readonly RoutedEvent<RoutedEventArgs> ScreenshotRequestedEvent = RoutedEvent.Register<AppToolbar, RoutedEventArgs>(nameof(ScreenshotRequested), RoutingStrategies.Bubble);

	public event EventHandler<RoutedEventArgs>? ScreenshotRequested
	{
		add => this.AddHandler(ScreenshotRequestedEvent, value);
		remove => this.RemoveHandler(ScreenshotRequestedEvent, value);
	}

	public AppToolbar()
	{
		this.InitializeComponent();
	}

	private void OnScreenshotButtonClick(object? sender, RoutedEventArgs e)
	{
		this.RaiseEvent(new RoutedEventArgs(ScreenshotRequestedEvent));
	}

	private void OnDiscordButtonClick(object? sender, RoutedEventArgs e)
	{
		System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://discord.gg/V7Z5y3Ke9v") { UseShellExecute = true });
	}
}
