using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using DAoCLogWatcher.UI.Models;
using DAoCLogWatcher.UI.Services;
using DAoCLogWatcher.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace DAoCLogWatcher.UI.Views.Tabs;

public partial class DashboardTabView : UserControl
{
	private static readonly HashSet<DashboardWidgetId> StatTileWidgets =
	[
		DashboardWidgetId.TotalRp, DashboardWidgetId.RpPerHour, DashboardWidgetId.Session,
		DashboardWidgetId.PlayerKills, DashboardWidgetId.KdRatio, DashboardWidgetId.Deaths,
		DashboardWidgetId.BestMultiKill, DashboardWidgetId.HottestZone,
	];

	private static readonly HashSet<DashboardWidgetId> LogWidgets =
	[
		DashboardWidgetId.RpLog, DashboardWidgetId.HealLog,
		DashboardWidgetId.CombatLog, DashboardWidgetId.DamageOutput,
	];

	private static double GetWidgetWidth(DashboardWidgetId id, DashboardWidgetSize size) =>
		StatTileWidgets.Contains(id)
			? size switch { DashboardWidgetSize.Small => 160, DashboardWidgetSize.Medium => 220, _ => 300 }
			: LogWidgets.Contains(id)
				? size switch { DashboardWidgetSize.Small => 380, DashboardWidgetSize.Medium => 560, _ => 740 }
				: size switch { DashboardWidgetSize.Small => 280, DashboardWidgetSize.Medium => 380, _ => 560 };

	private MainWindowViewModel? vm;
	private Dictionary<DashboardWidgetId, Control>? widgetControls;
	private ZoneMapService? zoneMapService;
	private WarmapWebSocketService? warmapService;
	private DispatcherTimer? minimapRenderTimer;
	private bool minimapDirty;
	private ZoneMapService.MinimapViewSpec? cachedMinimapSpec;

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
		};

		this.minimapRenderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
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
				this.vm.DashboardWidgets.CollectionChanged -= this.OnWidgetsCollectionChanged;
				this.vm.MinimapLocationChanged -= this.OnMinimapLocationChanged;
				this.vm.PropertyChanged -= this.OnVmPropertyChanged;
				foreach(var w in this.vm.DashboardWidgets)
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
			ChartHelper.ApplyTheme(this.vm.IsDarkTheme, this.MinimapZoomPlot);

			this.vm.DashboardWidgets.CollectionChanged += this.OnWidgetsCollectionChanged;
			this.vm.MinimapLocationChanged += this.OnMinimapLocationChanged;
			this.vm.PropertyChanged += this.OnVmPropertyChanged;
			foreach(var w in this.vm.DashboardWidgets)
			{
				w.PropertyChanged += this.OnWidgetPropertyChanged;
			}

			this.RebuildWidgetPanel();
			this.minimapDirty = true;
		};
	}

	private void OnMinimapLocationChanged(object? sender, EventArgs e)
	{
		if(this.vm != null && this.zoneMapService != null)
		{
			var loc = this.vm.CurrentMapLocation;
			this.cachedMinimapSpec = string.IsNullOrEmpty(loc)
				? null
				: this.zoneMapService.GetMinimapViewSpec(loc, this.vm.FrontierMap);
		}

		this.minimapDirty = true;
	}

	private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if(e.PropertyName == nameof(MainWindowViewModel.IsDarkTheme) && this.vm != null)
		{
			ChartHelper.ApplyTheme(this.vm.IsDarkTheme, this.MinimapZoomPlot);
			this.minimapDirty = true;
		}
	}

	private void RenderMinimap()
	{
		var mapService = this.zoneMapService;
		var viewModel = this.vm;
		if(viewModel == null || mapService == null || !this.WidgetMinimap.IsVisible)
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
		if(e.PropertyName is nameof(DashboardWidgetViewModel.IsVisible) or nameof(DashboardWidgetViewModel.Size))
		{
			this.RebuildWidgetPanel();
		}
	}

	private void RebuildWidgetPanel()
	{
		if(this.vm == null || this.widgetControls == null)
		{
			return;
		}

		this.StatWidgetPanel.Children.Clear();
		this.WidgetPanel.Children.Clear();

		foreach(var widget in this.vm.DashboardWidgets)
		{
			if(!this.widgetControls.TryGetValue(widget.Id, out var control))
			{
				continue;
			}

			control.IsVisible = widget.IsVisible;
			control.Width = GetWidgetWidth(widget.Id, widget.Size);

			if(StatTileWidgets.Contains(widget.Id))
				this.StatWidgetPanel.Children.Add(control);
			else
				this.WidgetPanel.Children.Add(control);
		}
	}
}
