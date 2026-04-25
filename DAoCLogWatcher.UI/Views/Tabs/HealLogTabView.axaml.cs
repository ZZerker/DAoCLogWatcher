using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using DAoCLogWatcher.UI.ViewModels;

namespace DAoCLogWatcher.UI.Views.Tabs;

public partial class HealLogTabView: UserControl
{
	private MainWindowViewModel? vm;

	public HealLogTabView()
	{
		this.InitializeComponent();

		this.InitializeHealsByHealerChart();
		this.InitializeHealsByTargetChart();

		this.DataContextChanged += (s, e) =>
		                           {
			                           if(this.vm != null)
			                           {
				                           this.vm.CombatChartsUpdateNeeded -= this.OnCombatChartsUpdateNeeded;
				                           this.vm.CombatSummary.ResetRequested -= this.OnCombatSummaryResetRequested;
				                           this.vm.PropertyChanged -= this.OnViewModelPropertyChanged;
				                           this.vm = null;
			                           }

			                           if(this.DataContext is MainWindowViewModel newVm)
			                           {
				                           this.vm = newVm;
				                           newVm.CombatChartsUpdateNeeded += this.OnCombatChartsUpdateNeeded;
				                           newVm.CombatSummary.ResetRequested += this.OnCombatSummaryResetRequested;
				                           newVm.PropertyChanged += this.OnViewModelPropertyChanged;
				                           ChartHelper.ApplyTheme(newVm.IsDarkTheme, this.HealsByHealerChart, this.HealsByTargetChart);
			                           }
		                           };
	}

	private void InitializeHealsByHealerChart()
	{
		ChartHelper.ApplyChartStyle(this.HealsByHealerChart, "#252525", "#1E1E1E", "#3A3A3A", "#2A2A2A", "#CCCCCC");
		this.HealsByHealerChart.Plot.YLabel("HP healed");
		this.HealsByHealerChart.UserInputProcessor.IsEnabled = false;
		this.HealsByHealerChart.Refresh();
	}

	private void InitializeHealsByTargetChart()
	{
		ChartHelper.ApplyChartStyle(this.HealsByTargetChart, "#252525", "#1E1E1E", "#3A3A3A", "#2A2A2A", "#CCCCCC");
		this.HealsByTargetChart.Plot.YLabel("HP healed");
		this.HealsByTargetChart.UserInputProcessor.IsEnabled = false;
		this.HealsByTargetChart.Refresh();
	}

	private void OnCombatChartsUpdateNeeded(object? sender, EventArgs e)
	{
		Dispatcher.UIThread.InvokeAsync(this.UpdateAllCharts);
	}

	private void OnCombatSummaryResetRequested(object? sender, EventArgs e)
	{
		this.UpdateAllCharts();
	}

	private void UpdateAllCharts()
	{
		ChartHelper.UpdateBarChart(
			this.HealsByHealerChart,
			this.vm?.CombatSummary.HealsByHealer.Select(kv => (kv.Key, (double)kv.Value)) ?? [],
			"#00D968");
		ChartHelper.UpdateBarChart(
			this.HealsByTargetChart,
			this.vm?.CombatSummary.HealsByTarget.Select(kv => (kv.Key, (double)kv.Value)) ?? [],
			"#00D968");
	}

	private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if(sender is not MainWindowViewModel newVm)
		{
			return;
		}

		if(e.PropertyName == nameof(MainWindowViewModel.IsDarkTheme))
		{
			ChartHelper.ApplyTheme(newVm.IsDarkTheme, this.HealsByHealerChart, this.HealsByTargetChart);
		}
	}
}
