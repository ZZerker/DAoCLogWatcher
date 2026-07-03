using System;
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
				                           this.vm.ZoneActivity.KillActivityUpdated -= this.OnKillActivityUpdated;
				                           this.vm.SettingsPopup.PropertyChanged -= this.OnViewModelPropertyChanged;
				                           this.vm = null;
			                           }

			                           if(this.DataContext is MainWindowViewModel newVm)
			                           {
				                           this.vm = newVm;
				                           newVm.ZoneActivity.KillActivityUpdated += this.OnKillActivityUpdated;
				                           newVm.SettingsPopup.PropertyChanged += this.OnViewModelPropertyChanged;
				                           ChartHelper.ApplyTheme(newVm.SettingsPopup.IsDarkTheme, this.GlobalActivityChart);
			                           }
		                           };
	}

	private void InitializeGlobalActivityChart()
	{
		(this.globalActivityHighlight, this.globalActivityTooltip) = ChartHelper.InitActivityChart(this.GlobalActivityChart, "Kills per window", "#7CDAFF");
	}

	private void OnKillActivityUpdated(object? sender, EventArgs e)
	{
		if(this.vm == null)
		{
			return;
		}

		var points = this.vm.ZoneActivity.GetSessionKillActivityPoints().Select(p => (p.Time, (double)p.KillCount)).ToList();
		(this.globalActivityScatter, this.globalActivityHighlight, this.globalActivityTooltip) =
				ChartHelper.UpdateActivityChart(this.GlobalActivityChart, points, this.vm.Summary.SessionStartTime, "#7CDAFF");
	}

	private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if(sender is not SettingsPopupViewModel settings)
		{
			return;
		}

		if(e.PropertyName == nameof(SettingsPopupViewModel.IsDarkTheme))
		{
			ChartHelper.ApplyTheme(settings.IsDarkTheme, this.GlobalActivityChart);
		}
	}
}
