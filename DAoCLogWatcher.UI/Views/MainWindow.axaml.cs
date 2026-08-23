using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DAoCLogWatcher.UI.Services;
using DAoCLogWatcher.UI.Views.Tabs;

namespace DAoCLogWatcher.UI.Views;

public partial class MainWindow: Window
{
	private ViewModels.MainWindowViewModel? vm;

	public MainWindow()
	{
		this.InitializeComponent();

		this.Toolbar.ScreenshotRequested += async (s, e) => await this.OnScreenshotClickAsync();

		this.Activated += (s, e) => this.vm?.OnWindowActivated();

		this.DataContextChanged += (s, e) =>
		                           {
			                           if(this.vm != null)
			                           {
				                           this.vm.SettingsPopup.PropertyChanged -= this.OnViewModelPropertyChanged;
				                           this.vm.CampaignEvents.LocateRequested -= this.OnLocateRequested;
				                           this.vm = null;
			                           }

			                           if(this.DataContext is ViewModels.MainWindowViewModel newVm)
			                           {
				                           this.vm = newVm;
				                           newVm.SettingsPopup.PropertyChanged += this.OnViewModelPropertyChanged;
				                           newVm.CampaignEvents.LocateRequested += this.OnLocateRequested;
				                           this.ApplyTheme(newVm.SettingsPopup.IsDarkTheme);
			                           }
		                           };
	}

	/// <summary>
	/// "Show on map" from the Campaign Events widget: the widget is on the Dashboard, so this has to
	/// select the Map tab and its Heatmap sub-view before the map control can zoom to the zone. The
	/// focus call is deferred to Background priority so the tab has been realised by the time it runs.
	/// </summary>
	private void OnLocateRequested(object? sender, int zoneId)
	{
		if(this.vm == null)
		{
			return;
		}

		this.vm.IsMapSubHeatmap = true;

		var mapTab = this.MainTabControl.Items.OfType<TabItem>().FirstOrDefault(t => t.Content is MapTabView);
		if(mapTab != null)
		{
			this.MainTabControl.SelectedItem = mapTab;
		}

		Dispatcher.UIThread.Post(() =>
		                         {
			                         var heatmap = this.MainTabControl.GetVisualDescendants().OfType<KillHeatmapTabView>().FirstOrDefault();
			                         heatmap?.FocusZone(zoneId);
		                         },
		                         DispatcherPriority.Background);
	}

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint attr, ref int attrValue, uint attrSize);

	private const uint IMMERSIVE_DARK_MODE = 20;
	private const uint CAPTION_COLOR = 35;

	private void ApplyDarkTitleBar()
	{
		if(!OperatingSystem.IsWindows())
		{
			return;
		}

		if(this.TryGetPlatformHandle() is not { } handle)
		{
			return;
		}

		var hwnd = handle.Handle;
		var dark = 1;
		DwmSetWindowAttribute(hwnd, IMMERSIVE_DARK_MODE, ref dark, 4u);

		// Pin caption to dark colour — silently no-ops on Windows 10
		var captionColor = 0x00252525;
		DwmSetWindowAttribute(hwnd, CAPTION_COLOR, ref captionColor, 4u);
	}

	protected override void OnClosed(EventArgs e)
	{
		base.OnClosed(e);

		// The overlay has no Owner; close it explicitly so the process can shut down.
		if(this.DataContext is ViewModels.MainWindowViewModel mainVm)
		{
			mainVm.CloseOverlay();
		}
	}

	protected override void OnOpened(EventArgs e)
	{
		base.OnOpened(e);

		this.ApplyDarkTitleBar();
		this.SelectFirstVisibleTab();

		if(this.DataContext is ViewModels.MainWindowViewModel overlayVm)
		{
			overlayVm.AutoOpenOverlayIfEnabled();
		}

		// Move to a secondary screen if one is available — DAoC typically runs full-screen
		// on the primary monitor, so prefer any non-primary screen for the log watcher.
		var secondary = this.Screens.All.FirstOrDefault(s => !s.IsPrimary);
		if(secondary != null)
		{
			this.Position = secondary.WorkingArea.TopLeft;
		}

		var screen = this.Screens.ScreenFromWindow(this);
		if(screen == null)
		{
			return;
		}

		var workH = screen.WorkingArea.Height / screen.Scaling;
		if(workH < 1268)
		{
			this.Height = workH;

			if(this.DataContext is ViewModels.MainWindowViewModel newVm)
			{
				newVm.RpSourceBreakdown.Value = false;
			}
		}
	}

	private void SelectFirstVisibleTab()
	{
		if(this.MainTabControl.SelectedItem is not TabItem selected||selected.IsVisible)
		{
			return;
		}

		var first = this.MainTabControl.Items.OfType<TabItem>().FirstOrDefault(t => t.IsVisible);
		if(first != null)
		{
			this.MainTabControl.SelectedItem = first;
		}
	}

	private void ApplyTheme(bool isDark)
	{
		Application.Current!.RequestedThemeVariant = isDark?ThemeVariant.Dark:ThemeVariant.Light;
	}

	private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if(sender is not ViewModels.SettingsPopupViewModel settings)
		{
			return;
		}

		if(e.PropertyName == nameof(ViewModels.SettingsPopupViewModel.IsDarkTheme))
		{
			this.ApplyTheme(settings.IsDarkTheme);
		}
		else if(e.PropertyName == nameof(ViewModels.SettingsPopupViewModel.IsSidebarVisible))
		{
			this.MainContentGrid.ColumnDefinitions[0].Width = settings.IsSidebarVisible?new GridLength(320):new GridLength(0);
		}
	}

	private async Task OnScreenshotClickAsync()
	{
		await ClipboardService.CaptureWindowToClipboardAsync(this);
		this.ShowScreenshotToast();
	}

	private async void ShowScreenshotToast()
	{
		this.ScreenshotToast.IsVisible = true;
		this.ScreenshotToast.Opacity = 0;

		var fadeIn = new Animation
		             {
				             Duration = TimeSpan.FromMilliseconds(200),
				             FillMode = FillMode.Forward,
				             Children =
				             {
						             new KeyFrame
						             {
								             Cue = new Cue(0),
								             Setters =
								             {
										             new Setter(OpacityProperty, 0.0)
								             }
						             },
						             new KeyFrame
						             {
								             Cue = new Cue(1),
								             Setters =
								             {
										             new Setter(OpacityProperty, 1.0)
								             }
						             }
				             }
		             };
		await fadeIn.RunAsync(this.ScreenshotToast);

		await Task.Delay(1500);

		var fadeOut = new Animation
		              {
				              Duration = TimeSpan.FromMilliseconds(500),
				              FillMode = FillMode.Forward,
				              Easing = new CubicEaseIn(),
				              Children =
				              {
						              new KeyFrame
						              {
								              Cue = new Cue(0),
								              Setters =
								              {
										              new Setter(OpacityProperty, 1.0)
								              }
						              },
						              new KeyFrame
						              {
								              Cue = new Cue(1),
								              Setters =
								              {
										              new Setter(OpacityProperty, 0.0)
								              }
						              }
				              }
		              };
		await fadeOut.RunAsync(this.ScreenshotToast);

		this.ScreenshotToast.IsVisible = false;
	}
}
