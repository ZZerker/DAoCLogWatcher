using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using DAoCLogWatcher.UI.Services;
using DAoCLogWatcher.UI.ViewModels;
using DAoCLogWatcher.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using ScottPlot;

namespace DAoCLogWatcher.UI.Views.Tabs;

public partial class KillHeatmapTabView: UserControl
{
	private MainWindowViewModel? vm;
	private ZoneMapService? zoneMapService;
	private WarmapWebSocketService? warmapService;
	private readonly DispatcherTimer renderTimer;
	private bool isDirty;

	public KillHeatmapTabView()
	{
		this.InitializeComponent();

		this.renderTimer = new DispatcherTimer
		                   {
				                   Interval = TimeSpan.FromMilliseconds(1000.0 / 30)
		                   };
		this.renderTimer.Tick += (_, _) =>
		                         {
			                         if(!this.isDirty)
			                         {
				                         return;
			                         }

			                         this.isDirty = false;
			                         this.RenderHeatmap();
		                         };
		this.renderTimer.Start();

		this.PropertyChanged += (_, e) =>
		                        {
			                        if(e.Property != IsVisibleProperty)
			                        {
				                        return;
			                        }

			                        if(this.IsVisible)
			                        {
				                        this.renderTimer.Start();
			                        }
			                        else
			                        {
				                        this.renderTimer.Stop();
			                        }
		                        };

		this.DataContextChanged += (_, _) =>
		                           {
			                           if(this.vm != null)
			                           {
				                           this.vm.ZoneActivity.KillActivityUpdated -= this.OnKillActivityUpdated;
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
				                           this.warmapService.KeepsUpdated += this.OnKeepsUpdated;
				                           this.warmapService.FightsUpdated += this.OnFightsUpdated;
			                           }

			                           this.InitKillHeatmapChart();
			                           ChartHelper.ApplyTheme(newVm.SettingsPopup.IsDarkTheme, this.KillHeatmapPlot);
			                           newVm.ZoneActivity.KillActivityUpdated += this.OnKillActivityUpdated;
			                           newVm.SettingsPopup.PropertyChanged += this.OnViewModelPropertyChanged;
		                           };

		this.ShowFightsCheckBox.IsCheckedChanged += (_, _) => this.isDirty = true;

		this.KillHeatmapPlot.PointerMoved += this.OnHeatmapPointerMoved;
		this.KillHeatmapPlot.PointerExited += (_, _) => this.BurnTooltip.IsVisible = false;
	}

	private void InitKillHeatmapChart()
	{
		if(this.vm == null||this.zoneMapService == null)
		{
			return;
		}

		this.zoneMapService.InitializePlot(this.KillHeatmapPlot.Plot, this.vm.FrontierMap);
		ChartHelper.HideAxes(this.KillHeatmapPlot.Plot);
		this.KillHeatmapPlot.Refresh();
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if(e.PropertyName == nameof(SettingsPopupViewModel.IsDarkTheme)&&this.vm != null)
		{
			ChartHelper.ApplyTheme(this.vm.SettingsPopup.IsDarkTheme, this.KillHeatmapPlot);
			this.isDirty = true;
		}
	}

	private void OnKillActivityUpdated(object? sender, EventArgs e)
	{
		this.isDirty = true;
	}

	private void OnKeepsUpdated(object? sender, EventArgs e)
	{
		this.isDirty = true;
	}

	private void OnFightsUpdated(object? sender, EventArgs e)
	{
		this.isDirty = true;
	}

	private void RenderHeatmap()
	{
		if(this.vm == null||this.zoneMapService == null)
		{
			return;
		}

		var liveKeeps = this.warmapService?.GetSnapshot();
		var fights = this.warmapService?.GetFightsSnapshot();
		var groups = this.warmapService?.GetGroupsSnapshot();
		var showFights = this.ShowFightsCheckBox.IsChecked == true;

		lock(this.KillHeatmapPlot.Plot.Sync)
		{
			this.zoneMapService.ApplyHeatmapOverlay(this.KillHeatmapPlot.Plot, this.vm.FrontierMap, this.vm.CurrentZoneKills, liveKeeps, fights, groups, showFights);
		}

		this.KillHeatmapPlot.Refresh();
	}

	private void OnHeatmapPointerMoved(object? sender, PointerEventArgs e)
	{
		if(this.zoneMapService == null||this.warmapService == null)
		{
			return;
		}

		var pos = e.GetPosition(this.KillHeatmapPlot);
		var pixel = new Pixel((float)pos.X, (float)pos.Y);
		var coords = this.KillHeatmapPlot.Plot.GetCoordinates(pixel);

		var combatStarts = this.warmapService.GetCombatStartSnapshot();
		var tip = this.zoneMapService.GetBurnTooltip(coords.X, -coords.Y, combatStarts);

		if(tip != null)
		{
			var mins = (int)tip.Value.Duration.TotalMinutes;
			var secs = tip.Value.Duration.Seconds;
			this.BurnTooltipText.Text = $"{tip.Value.Name}\nBurning for {mins}:{secs:D2}";

			var canvasPos = e.GetPosition(this.TooltipCanvas);
			Canvas.SetLeft(this.BurnTooltip, canvasPos.X + 14);
			Canvas.SetTop(this.BurnTooltip, canvasPos.Y + 14);
			this.BurnTooltip.IsVisible = true;
		}
		else
		{
			this.BurnTooltip.IsVisible = false;
		}
	}
}
