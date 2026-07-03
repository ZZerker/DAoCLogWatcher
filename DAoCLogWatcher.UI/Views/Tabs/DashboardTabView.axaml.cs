using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using DAoCLogWatcher.UI.Models;
using DAoCLogWatcher.UI.Services;
using DAoCLogWatcher.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace DAoCLogWatcher.UI.Views.Tabs;

public partial class DashboardTabView: UserControl
{
	private static readonly HashSet<DashboardWidgetId> StatTileWidgets =
	[
			DashboardWidgetId.TotalRp, DashboardWidgetId.RpPerHour, DashboardWidgetId.Session,
			DashboardWidgetId.PlayerKills, DashboardWidgetId.KdRatio, DashboardWidgetId.Deaths,
			DashboardWidgetId.BestMultiKill, DashboardWidgetId.HottestZone
	];

	private static readonly HashSet<DashboardWidgetId> LogWidgets =
	[
			DashboardWidgetId.RpLog, DashboardWidgetId.HealLog,
			DashboardWidgetId.CombatLog, DashboardWidgetId.DamageOutput
	];

	private static double GetWidgetWidth(DashboardWidgetId id, DashboardWidgetSize size)
	{
		if(StatTileWidgets.Contains(id))
		{
			return size switch
			       {
					       DashboardWidgetSize.XSmall => 140,
					       DashboardWidgetSize.Small => 190,
					       DashboardWidgetSize.Medium => 240,
					       DashboardWidgetSize.Large => 300,
					       _ => 380
			       };
		}

		if(LogWidgets.Contains(id))
		{
			return size switch
			       {
					       DashboardWidgetSize.XSmall => 320,
					       DashboardWidgetSize.Small => 460,
					       DashboardWidgetSize.Medium => 600,
					       DashboardWidgetSize.Large => 760,
					       _ => 920
			       };
		}

		return size switch
		       {
				       DashboardWidgetSize.XSmall => 240,
				       DashboardWidgetSize.Small => 320,
				       DashboardWidgetSize.Medium => 420,
				       DashboardWidgetSize.Large => 520,
				       _ => 640
		       };
	}

	private MainWindowViewModel? vm;
	private Dictionary<DashboardWidgetId, Control>? widgetControls;
	private readonly DashboardArrangeBehavior arrange = new();
	private ZoneMapService? zoneMapService;
	private WarmapWebSocketService? warmapService;
	private DispatcherTimer? minimapRenderTimer;
	private bool minimapDirty;
	private ZoneMapService.MinimapViewSpec? cachedMinimapSpec;

	private ScottPlot.Plottables.Scatter? globalActivityScatter;
	private ScottPlot.Plottables.Marker? globalActivityHighlight;
	private ScottPlot.Plottables.Text? globalActivityTooltip;

	public DashboardTabView()
	{
		this.InitializeComponent();
		this.widgetControls = new Dictionary<DashboardWidgetId, Control>
		                      {
				                      [DashboardWidgetId.TotalRp] = this.WidgetTotalRp,
				                      [DashboardWidgetId.RpPerHour] = this.WidgetRpPerHour,
				                      [DashboardWidgetId.Session] = this.WidgetSession,
				                      [DashboardWidgetId.PlayerKills] = this.WidgetPlayerKills,
				                      [DashboardWidgetId.KdRatio] = this.WidgetKdRatio,
				                      [DashboardWidgetId.Deaths] = this.WidgetDeaths,
				                      [DashboardWidgetId.BestMultiKill] = this.WidgetBestMultiKill,
				                      [DashboardWidgetId.HottestZone] = this.WidgetHottestZone,
				                      [DashboardWidgetId.RpSources] = this.WidgetRpSources,
				                      [DashboardWidgetId.DamageOutput] = this.WidgetDamageOutput,
				                      [DashboardWidgetId.TopTargets] = this.WidgetTopTargets,
				                      [DashboardWidgetId.TopSpells] = this.WidgetTopSpells,
				                      [DashboardWidgetId.TopHealers] = this.WidgetTopHealers,
				                      [DashboardWidgetId.DamageTaken] = this.WidgetDamageTaken,
				                      [DashboardWidgetId.HealsDone] = this.WidgetHealsDone,
				                      [DashboardWidgetId.ZoneActivity] = this.WidgetZoneActivity,
				                      [DashboardWidgetId.RpLog] = this.WidgetRpLog,
				                      [DashboardWidgetId.HealLog] = this.WidgetHealLog,
				                      [DashboardWidgetId.CombatLog] = this.WidgetCombatLog,
				                      [DashboardWidgetId.Minimap] = this.WidgetMinimap,
				                      [DashboardWidgetId.GlobalActivity] = this.WidgetGlobalActivity
		                      };

		this.InitializeGlobalActivityChart();
		this.GlobalActivityPlot.PointerMoved += (_, e) => ChartHelper.HandlePointerMoved(e, this.GlobalActivityPlot, this.globalActivityScatter, this.globalActivityHighlight, this.globalActivityTooltip, "Kills");
		this.GlobalActivityPlot.PointerExited += (_, _) => ChartHelper.HandlePointerExited(this.GlobalActivityPlot, this.globalActivityHighlight, this.globalActivityTooltip);

		this.minimapRenderTimer = new DispatcherTimer
		                          {
				                          Interval = TimeSpan.FromMilliseconds(1000.0 / 30)
		                          };
		this.minimapRenderTimer.Tick += (_, _) =>
		                                {
			                                if(this.minimapDirty)
			                                {
				                                this.minimapDirty = false;
				                                this.RenderMinimap();
			                                }
		                                };
		this.minimapRenderTimer.Start();

		this.DataContextChanged += (_, _) =>
		                           {
			                           if(this.vm != null)
			                           {
				                           this.vm.Dashboard.DashboardWidgets.CollectionChanged -= this.OnWidgetsCollectionChanged;
				                           this.vm.MinimapLocationChanged -= this.OnMinimapLocationChanged;
				                           this.vm.ZoneActivity.KillActivityUpdated -= this.OnKillActivityUpdated;
				                           this.vm.SettingsPopup.PropertyChanged -= this.OnVmPropertyChanged;
				                           this.vm.Dashboard.PropertyChanged -= this.OnDashboardPropertyChanged;
				                           foreach(var w in this.vm.Dashboard.DashboardWidgets)
				                           {
					                           w.PropertyChanged -= this.OnWidgetPropertyChanged;
				                           }
			                           }

			                           this.vm = this.DataContext as MainWindowViewModel;

			                           if(this.vm == null)
			                           {
				                           return;
			                           }

			                           var app = (App)Application.Current!;
			                           this.zoneMapService ??= app.Services.GetRequiredService<ZoneMapService>();
			                           if(this.warmapService == null)
			                           {
				                           this.warmapService = app.Services.GetRequiredService<WarmapWebSocketService>();
				                           this.warmapService.KeepsUpdated += (_, _) => this.minimapDirty = true;
				                           this.warmapService.FightsUpdated += (_, _) => this.minimapDirty = true;
			                           }

			                           this.zoneMapService.InitializeMinimapPlot(this.MinimapZoomPlot.Plot);
			                           ChartHelper.HideAxes(this.MinimapZoomPlot.Plot);
			                           ChartHelper.ApplyTheme(this.vm.SettingsPopup.IsDarkTheme, this.MinimapZoomPlot);

			                           this.vm.Dashboard.DashboardWidgets.CollectionChanged += this.OnWidgetsCollectionChanged;
			                           this.vm.MinimapLocationChanged += this.OnMinimapLocationChanged;
			                           this.vm.ZoneActivity.KillActivityUpdated += this.OnKillActivityUpdated;
			                           this.vm.SettingsPopup.PropertyChanged += this.OnVmPropertyChanged;
			                           this.vm.Dashboard.PropertyChanged += this.OnDashboardPropertyChanged;
			                           foreach(var w in this.vm.Dashboard.DashboardWidgets)
			                           {
				                           w.PropertyChanged += this.OnWidgetPropertyChanged;
			                           }

			                           this.arrange.Vm = this.vm.Dashboard;
			                           this.arrange.RebuildPanels = this.RebuildWidgetPanel;
			                           this.arrange.WidthFor = GetWidgetWidth;
			                           this.arrange.SetEditMode(this.vm.Dashboard.IsDashboardCustomizeVisible);
			                           ChartHelper.ApplyTheme(this.vm.SettingsPopup.IsDarkTheme, this.GlobalActivityPlot);
			                           this.RebuildWidgetPanel();
			                           this.minimapDirty = true;
			                           this.OnKillActivityUpdated(this.vm, EventArgs.Empty);
		                           };
	}

	private void OnMinimapLocationChanged(object? sender, EventArgs e)
	{
		if(this.vm != null&&this.zoneMapService != null)
		{
			var loc = this.vm.CurrentMapLocation;
			this.cachedMinimapSpec = string.IsNullOrEmpty(loc)?null:this.zoneMapService.GetMinimapViewSpec(loc, this.vm.FrontierMap);
		}

		this.minimapDirty = true;
	}

	private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if(e.PropertyName == nameof(SettingsPopupViewModel.IsDarkTheme)&&this.vm != null)
		{
			ChartHelper.ApplyTheme(this.vm.SettingsPopup.IsDarkTheme, this.MinimapZoomPlot);
			ChartHelper.ApplyTheme(this.vm.SettingsPopup.IsDarkTheme, this.GlobalActivityPlot);
			this.minimapDirty = true;
		}
	}

	private void OnDashboardPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if(e.PropertyName == nameof(DashboardViewModel.IsDashboardCustomizeVisible)&&this.vm != null)
		{
			this.arrange.SetEditMode(this.vm.Dashboard.IsDashboardCustomizeVisible);
		}
	}

	private void RenderMinimap()
	{
		var mapService = this.zoneMapService;
		var viewModel = this.vm;
		if(viewModel == null||mapService == null||!this.WidgetMinimap.IsVisible)
		{
			return;
		}

		var spec = this.cachedMinimapSpec;
		if(spec == null)
		{
			return;
		}

		var liveKeeps = this.warmapService?.GetSnapshot();
		var fights = this.warmapService?.GetFightsSnapshot();
		var groups = this.warmapService?.GetGroupsSnapshot();

		var b = spec.ZoneBounds;
		const int MARGIN = 6;
		this.MinimapZoomPanel.Ratio = (b.Width + 2.0 * MARGIN) / (b.Height + 2.0 * MARGIN);
		lock(this.MinimapZoomPlot.Plot.Sync)
		{
			mapService.ApplyMinimapOverlay(this.MinimapZoomPlot.Plot, viewModel.FrontierMap, spec, liveKeeps, fights, groups);
			this.MinimapZoomPlot.Plot.Axes.SetLimits(b.X - MARGIN, b.X + b.Width + MARGIN, -(b.Y + b.Height + MARGIN), -(b.Y - MARGIN));
			ChartHelper.HideAxes(this.MinimapZoomPlot.Plot);
		}

		this.MinimapZoomPlot.Refresh();
	}

	private void OnWidgetsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		// A drag commit moves the same widget instance and has already arranged the
		// panel in place, so skip the rebuild (and the no-op subscription churn).
		if(this.arrange.IsCommitting)
		{
			return;
		}

		if(e.OldItems != null)
		{
			foreach(DashboardWidgetViewModel w in e.OldItems)
			{
				w.PropertyChanged -= this.OnWidgetPropertyChanged;
			}
		}

		if(e.NewItems != null)
		{
			foreach(DashboardWidgetViewModel w in e.NewItems)
			{
				w.PropertyChanged += this.OnWidgetPropertyChanged;
			}
		}

		this.RebuildWidgetPanel();
	}

	private void OnWidgetPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		// A drag-resize commit already applied the exact width in place; rebuilding
		// here would needlessly re-add every tile.
		if(this.arrange.IsCommitting)
		{
			return;
		}

		if(e.PropertyName is nameof(DashboardWidgetViewModel.IsVisible) or nameof(DashboardWidgetViewModel.Size))
		{
			this.RebuildWidgetPanel();
		}
	}

	private void RebuildWidgetPanel()
	{
		if(this.vm == null||this.widgetControls == null)
		{
			return;
		}

		this.StatWidgetPanel.Children.Clear();
		this.WidgetPanel.Children.Clear();

		foreach(var widget in this.vm.Dashboard.DashboardWidgets)
		{
			if(!this.widgetControls.TryGetValue(widget.Id, out var control))
			{
				continue;
			}

			control.Width = GetWidgetWidth(widget.Id, widget.Size);

			var tile = this.arrange.Wrap(widget, control);
			tile.IsVisible = widget.IsVisible;

			if(StatTileWidgets.Contains(widget.Id))
			{
				this.StatWidgetPanel.Children.Add(tile);
			}
			else
			{
				this.WidgetPanel.Children.Add(tile);
			}
		}
	}

	private void InitializeGlobalActivityChart()
	{
		(this.globalActivityHighlight, this.globalActivityTooltip) = ChartHelper.InitActivityChart(this.GlobalActivityPlot, "Kills per window", "#7CDAFF");
	}

	private void OnKillActivityUpdated(object? sender, EventArgs e)
	{
		if(this.vm == null)
		{
			return;
		}

		var points = this.vm.ZoneActivity.GetSessionKillActivityPoints().Select(p => (p.Time, (double)p.KillCount)).ToList();
		(this.globalActivityScatter, this.globalActivityHighlight, this.globalActivityTooltip) =
				ChartHelper.UpdateActivityChart(this.GlobalActivityPlot, points, this.vm.Summary.SessionStartTime, "#7CDAFF");
	}
}
