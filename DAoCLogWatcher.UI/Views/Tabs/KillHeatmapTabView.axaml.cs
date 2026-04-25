using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using DAoCLogWatcher.UI.Services;
using DAoCLogWatcher.UI.ViewModels;
using DAoCLogWatcher.UI;
using Microsoft.Extensions.DependencyInjection;

namespace DAoCLogWatcher.UI.Views.Tabs;

public partial class KillHeatmapTabView: UserControl
{
	private MainWindowViewModel? vm;
	private ZoneMapService? zoneMapService;
	private WarmapWebSocketService? warmapService;
	private DispatcherTimer? renderTimer;
	private bool isDirty;

	public KillHeatmapTabView()
	{
		this.InitializeComponent();

		this.renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
		this.renderTimer.Tick += (s, e) =>
		                         {
			                         if(this.isDirty)
			                         {
				                         this.isDirty = false;
				                         this.RenderHeatmap();
			                         }
		                         };
		this.renderTimer.Start();

		this.DataContextChanged += (s, e) =>
		                           {
			                           if(this.vm != null)
			                           {
				                           this.vm.KillActivityUpdated -= this.OnKillActivityUpdated;
				                           this.vm = null;
			                           }

			                           if(this.DataContext is MainWindowViewModel newVm)
			                           {
				                           this.vm = newVm;
				                           var app = (App)Application.Current!;
				                           this.zoneMapService ??= app.Services.GetRequiredService<ZoneMapService>();
				                           if(this.warmapService == null)
				                           {
					                           this.warmapService = app.Services.GetRequiredService<WarmapWebSocketService>();
					                           this.warmapService.KeepsUpdated += this.OnKeepsUpdated;
				                           }

				                           this.InitKillHeatmapChart();
				                           newVm.KillActivityUpdated += this.OnKillActivityUpdated;
			                           }
		                           };
	}

	private void InitKillHeatmapChart()
	{
		if(this.vm == null || this.zoneMapService == null)
		{
			return;
		}

		this.zoneMapService.InitializePlot(this.KillHeatmapPlot.Plot, this.vm.FrontierMap);
		this.KillHeatmapPlot.Refresh();
	}

	private void OnKillActivityUpdated(object? sender, EventArgs e)
	{
		this.isDirty = true;
	}

	private void OnKeepsUpdated(object? sender, EventArgs e)
	{
		this.isDirty = true;
	}

	private void RenderHeatmap()
	{
		if(this.vm == null || this.zoneMapService == null)
		{
			return;
		}

		var liveKeeps = this.warmapService?.GetSnapshot();

		lock(this.KillHeatmapPlot.Plot.Sync)
		{
			this.zoneMapService.ApplyHeatmapOverlay(this.KillHeatmapPlot.Plot, this.vm.FrontierMap, this.vm.CurrentZoneKills, liveKeeps);
		}

		this.KillHeatmapPlot.Refresh();
	}
}
