using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using DAoCLogWatcher.UI.Services;

namespace DAoCLogWatcher.UI.Views;

public partial class MainWindow: Window
{
	private ViewModels.MainWindowViewModel? vm;

	public MainWindow()
	{
		this.InitializeComponent();

		this.Toolbar.ScreenshotRequested += async (s, e) => await this.OnScreenshotClickAsync();

		this.DataContextChanged += (s, e) =>
		                           {
			                           if(this.vm != null)
			                           {
				                           this.vm.PropertyChanged -= this.OnViewModelPropertyChanged;
				                           this.vm = null;
			                           }

			                           if(this.DataContext is ViewModels.MainWindowViewModel newVm)
			                           {
				                           this.vm = newVm;
				                           newVm.PropertyChanged += this.OnViewModelPropertyChanged;
				                           this.ApplyTheme(newVm.IsDarkTheme);
			                           }
		                           };
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

	protected override void OnOpened(EventArgs e)
	{
		base.OnOpened(e);

		this.ApplyDarkTitleBar();

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
				newVm.AbsoluteNumbers.Value = false;
				newVm.AbsoluteRps.Value = false;
			}
		}
	}

	private void ApplyTheme(bool isDark)
	{
		Application.Current!.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
	}

	private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if(sender is not ViewModels.MainWindowViewModel newVm)
		{
			return;
		}

		if(e.PropertyName == nameof(ViewModels.MainWindowViewModel.IsDarkTheme))
		{
			this.ApplyTheme(newVm.IsDarkTheme);
		}
		else if(e.PropertyName == nameof(ViewModels.MainWindowViewModel.IsSidebarVisible))
		{
			this.MainContentGrid.ColumnDefinitions[0].Width = newVm.IsSidebarVisible ? new GridLength(320) : new GridLength(0);
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
								             Setters = { new Setter(OpacityProperty, 0.0) }
						             },
						             new KeyFrame
						             {
								             Cue = new Cue(1),
								             Setters = { new Setter(OpacityProperty, 1.0) }
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
								              Setters = { new Setter(OpacityProperty, 1.0) }
						              },
						              new KeyFrame
						              {
								              Cue = new Cue(1),
								              Setters = { new Setter(OpacityProperty, 0.0) }
						              }
				              }
		              };
		await fadeOut.RunAsync(this.ScreenshotToast);

		this.ScreenshotToast.IsVisible = false;
	}
}
