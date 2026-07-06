using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DAoCLogWatcher.UI.Models;
using DAoCLogWatcher.UI.Services;
using DAoCLogWatcher.UI.ViewModels;

namespace DAoCLogWatcher.UI.Views;

public partial class OverlayWindow: Window
{
	// Avalonia's X11 backend reports this descriptor; there is no public constant for it.
	private const string X11_HANDLE_DESCRIPTOR = "XID";

	private readonly OverlayViewModel viewModel;
	private readonly AppSettings settings;
	private readonly ISettingsService settingsService;
	private readonly DispatcherTimer topmostTimer;

	public OverlayWindow()
	{
		this.InitializeComponent();
		this.viewModel = null!;
		this.settings = null!;
		this.settingsService = null!;
		// The game restacks itself above us on re-render; re-assert topmost on a slow tick.
		this.topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
		this.topmostTimer.Tick += (_, _) => this.AssertTopmost();
	}

	public OverlayWindow(OverlayViewModel viewModel, AppSettings settings, ISettingsService settingsService)
			: this()
	{
		this.viewModel = viewModel;
		this.settings = settings;
		this.settingsService = settingsService;
		this.DataContext = viewModel;
		this.viewModel.PropertyChanged += this.OnViewModelPropertyChanged;
	}

	protected override void OnOpened(EventArgs e)
	{
		base.OnOpened(e);
		this.RestorePosition();
		this.ApplyClickThrough();
		this.topmostTimer.Start();
	}

	protected override void OnClosing(WindowClosingEventArgs e)
	{
		this.topmostTimer.Stop();
		this.SavePosition();
		this.viewModel.PropertyChanged -= this.OnViewModelPropertyChanged;
		base.OnClosing(e);
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if(e.PropertyName == nameof(OverlayViewModel.IsLocked))
		{
			this.ApplyClickThrough();
			// Locking is the natural end of an edit session — persist position + opacity.
			this.SavePosition();
		}
	}

	private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		// Presses on the opacity slider must not start a window drag.
		if(e.Source is Control source&&source.FindAncestorOfType<Slider>(includeSelf: true) != null)
		{
			return;
		}

		if(e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
		{
			this.BeginMoveDrag(e);
		}
	}

	private void OnHeaderPointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		this.SavePosition();
	}

	private void ApplyClickThrough()
	{
		if(this.TryGetPlatformHandle() is not { } handle)
		{
			return;
		}

		if(OperatingSystem.IsWindows())
		{
			OverlayInterop.SetClickThrough(handle.Handle, this.viewModel.IsLocked);
		}
		else if(OperatingSystem.IsLinux()&&handle.HandleDescriptor == X11_HANDLE_DESCRIPTOR)
		{
			// On Wayland the descriptor isn't "XID", so this stays a no-op — the overlay still works, just without click-through.
			OverlayInteropX11.SetClickThrough(handle.Handle, this.viewModel.IsLocked);
		}
	}

	private void AssertTopmost()
	{
		if(this.TryGetPlatformHandle() is not { } handle)
		{
			return;
		}

		if(OperatingSystem.IsWindows())
		{
			OverlayInterop.AssertTopmost(handle.Handle);
		}
		else if(OperatingSystem.IsLinux()&&handle.HandleDescriptor == X11_HANDLE_DESCRIPTOR)
		{
			OverlayInteropX11.Raise(handle.Handle);
		}
	}

	private void RestorePosition()
	{
		if(!this.settings.OverlayX.HasValue||!this.settings.OverlayY.HasValue)
		{
			this.PlaceAtDefault();
			return;
		}

		var point = new PixelPoint((int)this.settings.OverlayX.Value, (int)this.settings.OverlayY.Value);
		var onScreen = this.Screens.All.Any(s => s.Bounds.Contains(point));
		if(onScreen)
		{
			this.Position = point;
		}
		else
		{
			this.PlaceAtDefault();
		}
	}

	private void PlaceAtDefault()
	{
		var screen = this.Screens.Primary ?? this.Screens.All.FirstOrDefault();
		if(screen == null)
		{
			return;
		}

		var area = screen.WorkingArea;
		var margin = (int)(24 * screen.Scaling);
		this.Position = new PixelPoint(area.X + margin, area.Y + margin);
	}

	private void SavePosition()
	{
		this.settings.OverlayX = this.Position.X;
		this.settings.OverlayY = this.Position.Y;
		this.settings.OverlayOpacity = this.viewModel.BackgroundOpacity;
		this.settingsService.Save(this.settings);
	}
}
