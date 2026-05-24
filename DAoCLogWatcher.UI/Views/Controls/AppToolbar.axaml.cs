using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using DAoCLogWatcher.UI.ViewModels;

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

	private void OnBrowseSessionsChevronClick(object? sender, RoutedEventArgs e)
	{
		FlyoutBase.ShowAttachedFlyout(this.BrowseSessionsSplitGrid);
		var vm = (MainWindowViewModel)DataContext!;
		_ = vm.LoadRecentSessionsCommand.ExecuteAsync(null);
	}

	private void OnOpenLogChevronClick(object? sender, RoutedEventArgs e)
	{
		FlyoutBase.ShowAttachedFlyout(this.OpenLogSplitGrid);
	}

	private async void OnLastSessionClick(object? sender, RoutedEventArgs e)
	{
		var vm = (MainWindowViewModel)DataContext!;
		await vm.OpenLastSessionCommand.ExecuteAsync(null);
	}

	private async void OnAllSessionsClick(object? sender, RoutedEventArgs e)
	{
		var vm = (MainWindowViewModel)DataContext!;
		var window = TopLevel.GetTopLevel(this) as Window;
		if(window != null)
		{
			await vm.OpenSessionPickerCommand.ExecuteAsync(window);
		}
	}

	private async void OnOpenFileFromFlyout(object? sender, RoutedEventArgs e)
	{
		var vm = (MainWindowViewModel)DataContext!;
		var window = TopLevel.GetTopLevel(this) as Window;
		if(window?.StorageProvider != null)
		{
			await vm.OpenFileCommand.ExecuteAsync(window.StorageProvider);
		}
	}
}
