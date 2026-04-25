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
}
