using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using DAoCLogWatcher.UI.Services;
using DAoCLogWatcher.UI.ViewModels;
using DAoCLogWatcher.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DAoCLogWatcher.UI.Views.Controls;

public partial class SummarySidebar: UserControl
{
	private MainWindowViewModel? vm;
	private ZoneMapService? zoneMapService;
	private WarmapWebSocketService? warmapService;
	private readonly DispatcherTimer renderTimer;
	private bool isDirty;
	private ZoneMapService.MinimapViewSpec? cachedMinimapSpec;

	public SummarySidebar()
	{
		this.InitializeComponent();

		this.renderTimer = new DispatcherTimer
		                   {
				                   Interval = TimeSpan.FromMilliseconds(1000.0 / 15)
		                   };
		this.renderTimer.Tick += (_, _) =>
		                         {
			                         if(!this.isDirty)
			                         {
				                         return;
			                         }

			                         this.isDirty = false;
			                         this.RenderMinimap();
		                         };
		this.renderTimer.Start();

		this.DataContextChanged += (_, _) =>
		                           {
			                           if(this.vm != null)
			                           {
				                           this.vm.MinimapLocationChanged -= this.OnMinimapLocationChanged;
				                           this.vm.SettingsPopup.PropertyChanged -= this.OnViewModelPropertyChanged;
				                           this.vm = null;
			                           }

			                           if(this.DataContext is not MainWindowViewModel newVm)
			                           {
				                           return;
			                           }

			                           this.vm = newVm;
			                           var app = (App)Application.Current!;
			                           this.zoneMapService ??= app.Services.GetRequiredService<ZoneMapService>();
			                           if(this.warmapService == null)
			                           {
				                           this.warmapService = app.Services.GetRequiredService<WarmapWebSocketService>();
				                           this.warmapService.KeepsUpdated += (_, _) => this.isDirty = true;
				                           this.warmapService.FightsUpdated += (_, _) => this.isDirty = true;
			                           }

			                           this.zoneMapService.InitializeMinimapPlot(this.SidebarMinimap.Plot);
			                           ChartHelper.HideAxes(this.SidebarMinimap.Plot);
			                           ChartHelper.ApplyTheme(newVm.SettingsPopup.IsDarkTheme, this.SidebarMinimap);
			                           newVm.MinimapLocationChanged += this.OnMinimapLocationChanged;
			                           newVm.SettingsPopup.PropertyChanged += this.OnViewModelPropertyChanged;
			                           this.isDirty = true;
		                           };
	}

	private void OnMinimapLocationChanged(object? sender, EventArgs e)
	{
		if(this.vm != null&&this.zoneMapService != null)
		{
			var loc = this.vm.CurrentMapLocation;
			this.cachedMinimapSpec = string.IsNullOrEmpty(loc)?null:this.zoneMapService.GetMinimapViewSpec(loc, this.vm.FrontierMap);
		}

		this.isDirty = true;
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if(e.PropertyName == nameof(SettingsPopupViewModel.IsDarkTheme)&&this.vm != null)
		{
			ChartHelper.ApplyTheme(this.vm.SettingsPopup.IsDarkTheme, this.SidebarMinimap);
			this.isDirty = true;
		}
	}

	private void RenderMinimap()
	{
		if(this.vm == null||this.zoneMapService == null||this.cachedMinimapSpec == null)
		{
			return;
		}

		var liveKeeps = this.warmapService?.GetSnapshot();
		var fights = this.warmapService?.GetFightsSnapshot();
		var groups = this.warmapService?.GetGroupsSnapshot();

		var b = this.cachedMinimapSpec.ZoneBounds;
		const int MARGIN = 6;

		lock(this.SidebarMinimap.Plot.Sync)
		{
			this.zoneMapService.ApplyMinimapOverlay(this.SidebarMinimap.Plot, this.vm.FrontierMap, this.cachedMinimapSpec, liveKeeps, fights, groups);
			this.SidebarMinimap.Plot.Axes.SetLimits(b.X - MARGIN, b.X + b.Width + MARGIN, -(b.Y + b.Height + MARGIN), -(b.Y - MARGIN));
			ChartHelper.HideAxes(this.SidebarMinimap.Plot);
		}

		this.SidebarMinimap.Refresh();
	}
}
