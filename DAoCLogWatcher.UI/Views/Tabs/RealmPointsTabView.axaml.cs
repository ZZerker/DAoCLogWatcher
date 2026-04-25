using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using DAoCLogWatcher.UI.ViewModels;
using ScottPlot.Plottables;

namespace DAoCLogWatcher.UI.Views.Tabs;

public partial class RealmPointsTabView: UserControl
{
	private MainWindowViewModel? vm;

	private Scatter? rpScatter;
	private Marker? rpHighlight;
	private Text? rpTooltip;

	private Scatter? rpsScatter;
	private Marker? rpsHighlight;
	private Text? rpsTooltip;

	public RealmPointsTabView()
	{
		this.InitializeComponent();

		this.InitializeRpChart();
		this.InitializeRpsHourlyChart();

		this.RpChart.PointerMoved += (s, e) => ChartHelper.HandlePointerMoved(e, this.RpChart, this.rpScatter, this.rpHighlight, this.rpTooltip, "RP");
		this.RpChart.PointerExited += (s, e) => ChartHelper.HandlePointerExited(this.RpChart, this.rpHighlight, this.rpTooltip);
		this.RpsHourlyChart.PointerMoved += (s, e) => ChartHelper.HandlePointerMoved(e, this.RpsHourlyChart, this.rpsScatter, this.rpsHighlight, this.rpsTooltip, "RP/h");
		this.RpsHourlyChart.PointerExited += (s, e) => ChartHelper.HandlePointerExited(this.RpsHourlyChart, this.rpsHighlight, this.rpsTooltip);

		this.DataContextChanged += (s, e) =>
		                           {
			                           if(this.vm != null)
			                           {
				                           this.vm.ChartData.UpdateRequested -= this.OnChartUpdateRequested;
				                           this.vm.PropertyChanged -= this.OnViewModelPropertyChanged;
				                           this.vm = null;
			                           }

			                           if(this.DataContext is MainWindowViewModel newVm)
			                           {
				                           this.vm = newVm;
				                           newVm.ChartData.UpdateRequested += this.OnChartUpdateRequested;
				                           newVm.PropertyChanged += this.OnViewModelPropertyChanged;
				                           ChartHelper.ApplyTheme(newVm.IsDarkTheme, this.RpChart, this.RpsHourlyChart);
			                           }
		                           };
	}

	private void InitializeRpChart()
	{
		ChartHelper.ApplyChartStyle(this.RpChart, "#252525", "#1E1E1E", "#3A3A3A", "#2A2A2A", "#CCCCCC");
		this.RpChart.Plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.DateTimeAutomatic();
		this.RpChart.Plot.YLabel("Total RPs");
		(this.rpHighlight, this.rpTooltip) = ChartHelper.AddHoverOverlays(this.RpChart, "#00D9FF");
		this.RpChart.Refresh();
	}

	private void InitializeRpsHourlyChart()
	{
		ChartHelper.ApplyChartStyle(this.RpsHourlyChart, "#252525", "#1E1E1E", "#3A3A3A", "#2A2A2A", "#CCCCCC");
		this.RpsHourlyChart.Plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.DateTimeAutomatic();
		this.RpsHourlyChart.Plot.YLabel("RP/h (rolling 1h)");
		(this.rpsHighlight, this.rpsTooltip) = ChartHelper.AddHoverOverlays(this.RpsHourlyChart, "#FFAA44");
		this.RpsHourlyChart.Refresh();
	}

	private void OnChartUpdateRequested(object? sender, EventArgs e)
	{
		if(this.vm == null)
		{
			return;
		}

		this.UpdateRpChart(this.vm.ChartData.CumulativeDataPoints);
		this.UpdateRpsHourlyChart(this.vm.ChartData.HourlyDataPoints);
	}

	private void UpdateRpChart(List<(DateTime Time, double Rps)> dataPoints)
	{
		lock(this.RpChart.Plot.Sync)
		{
			this.RpChart.Plot.Clear();
			this.rpScatter = null;

			if(dataPoints.Count > 0)
			{
				var times = dataPoints.Select(p => p.Time.ToOADate()).ToArray();
				var rps = dataPoints.Select(p => p.Rps).ToArray();

				this.rpScatter = this.RpChart.Plot.Add.Scatter(times, rps);
				this.rpScatter.Color = ScottPlot.Color.FromHex("#00D9FF");
				this.rpScatter.LineWidth = 2;
				this.rpScatter.MarkerSize = 0;

				this.RpChart.Plot.Axes.AutoScale();
			}

			(this.rpHighlight, this.rpTooltip) = ChartHelper.AddHoverOverlays(this.RpChart, "#00D9FF");
		}

		this.RpChart.Refresh();
	}

	private void UpdateRpsHourlyChart(List<(DateTime Time, double RpsPerHour)> dataPoints)
	{
		lock(this.RpsHourlyChart.Plot.Sync)
		{
			this.RpsHourlyChart.Plot.Clear();
			this.rpsScatter = null;

			if(dataPoints.Count > 0)
			{
				var times = dataPoints.Select(p => p.Time.ToOADate()).ToArray();
				var values = dataPoints.Select(p => p.RpsPerHour).ToArray();

				this.rpsScatter = this.RpsHourlyChart.Plot.Add.Scatter(times, values);
				this.rpsScatter.Color = ScottPlot.Color.FromHex("#FFAA44");
				this.rpsScatter.LineWidth = 2;
				this.rpsScatter.MarkerSize = 0;

				this.RpsHourlyChart.Plot.Axes.AutoScale();
			}

			(this.rpsHighlight, this.rpsTooltip) = ChartHelper.AddHoverOverlays(this.RpsHourlyChart, "#FFAA44");
		}

		this.RpsHourlyChart.Refresh();
	}

	private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if(sender is not MainWindowViewModel newVm)
		{
			return;
		}

		if(e.PropertyName == nameof(MainWindowViewModel.IsDarkTheme))
		{
			ChartHelper.ApplyTheme(newVm.IsDarkTheme, this.RpChart, this.RpsHourlyChart);
		}
	}
}
