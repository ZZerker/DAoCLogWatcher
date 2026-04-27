using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using DAoCLogWatcher.UI.ViewModels;
using ScottPlot.Plottables;

namespace DAoCLogWatcher.UI.Views.Tabs;

public partial class KillZonesTabView: UserControl
{
	private MainWindowViewModel? vm;

	private Scatter? globalActivityScatter;
	private Marker? globalActivityHighlight;
	private Text? globalActivityTooltip;

	public KillZonesTabView()
	{
		this.InitializeComponent();

		this.InitializeGlobalActivityChart();

		this.GlobalActivityChart.PointerMoved += (s, e) => ChartHelper.HandlePointerMoved(e, this.GlobalActivityChart, this.globalActivityScatter, this.globalActivityHighlight, this.globalActivityTooltip, "Kills");
		this.GlobalActivityChart.PointerExited += (s, e) => ChartHelper.HandlePointerExited(this.GlobalActivityChart, this.globalActivityHighlight, this.globalActivityTooltip);

		this.DataContextChanged += (s, e) =>
		                           {
			                           if(this.vm != null)
			                           {
				                           this.vm.KillActivityUpdated -= this.OnKillActivityUpdated;
				                           this.vm.PropertyChanged -= this.OnViewModelPropertyChanged;
				                           this.vm = null;
			                           }

			                           if(this.DataContext is MainWindowViewModel newVm)
			                           {
				                           this.vm = newVm;
				                           newVm.KillActivityUpdated += this.OnKillActivityUpdated;
				                           newVm.PropertyChanged += this.OnViewModelPropertyChanged;
				                           ChartHelper.ApplyTheme(newVm.IsDarkTheme, this.GlobalActivityChart);
			                           }
		                           };
	}

	private void InitializeGlobalActivityChart()
	{
		ChartHelper.ApplyChartStyle(this.GlobalActivityChart, "#252525", "#1E1E1E", "#3A3A3A", "#2A2A2A", "#CCCCCC");
		this.GlobalActivityChart.Plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.DateTimeAutomatic();
		this.GlobalActivityChart.Plot.YLabel("Kills per window");
		(this.globalActivityHighlight, this.globalActivityTooltip) = ChartHelper.AddHoverOverlays(this.GlobalActivityChart, "#7CDAFF");
		this.GlobalActivityChart.Refresh();
	}

	private void OnKillActivityUpdated(object? sender, EventArgs e)
	{
		if(this.vm == null)
		{
			return;
		}

		this.UpdateGlobalActivityChart(this.vm.GetSessionKillActivityPoints().Select(p => (p.Time, (double)p.KillCount)).ToList());
	}

	private void UpdateGlobalActivityChart(List<(DateTime Time, double KillCount)> dataPoints)
	{
		lock(this.GlobalActivityChart.Plot.Sync)
		{
			this.GlobalActivityChart.Plot.Clear();
			this.globalActivityScatter = null;

			if(dataPoints.Count > 0)
			{
				var times = dataPoints.Select(p => p.Time.ToOADate()).ToArray();
				var values = dataPoints.Select(p => p.KillCount).ToArray();

				this.globalActivityScatter = this.GlobalActivityChart.Plot.Add.Scatter(times, values);
				this.globalActivityScatter.Color = ScottPlot.Color.FromHex("#7CDAFF");
				this.globalActivityScatter.LineWidth = 2;
				this.globalActivityScatter.MarkerSize = 6;
				this.globalActivityScatter.MarkerShape = ScottPlot.MarkerShape.FilledCircle;

				var parseStart = this.vm?.Summary.SessionStartTime ?? dataPoints[0].Time;
				var xMin = parseStart.ToOADate();
				var xMax = DateTime.Now.ToOADate();
				var yMax = values.Max() * 1.15;
				this.GlobalActivityChart.Plot.Axes.SetLimits(xMin, xMax, 0, yMax);
			}

			(this.globalActivityHighlight, this.globalActivityTooltip) = ChartHelper.AddHoverOverlays(this.GlobalActivityChart, "#7CDAFF");
		}

		this.GlobalActivityChart.Refresh();
	}

	private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if(sender is not MainWindowViewModel newVm)
		{
			return;
		}

		if(e.PropertyName == nameof(MainWindowViewModel.IsDarkTheme))
		{
			ChartHelper.ApplyTheme(newVm.IsDarkTheme, this.GlobalActivityChart);
		}
	}
}
