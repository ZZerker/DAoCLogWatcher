using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using DAoCLogWatcher.UI.ViewModels;

namespace DAoCLogWatcher.UI.Views.Tabs;

public partial class CombatTabView: UserControl
{
	private MainWindowViewModel? vm;

	public CombatTabView()
	{
		this.InitializeComponent();

		this.InitializeAvgDmgBySpellChart();
		this.InitializeDmgByTargetChart();
		this.InitializeHealsByHealerChartCombat();
		this.InitializeHealsByTargetChartCombat();

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
				                           ChartHelper.ApplyTheme(newVm.IsDarkTheme, this.AvgDmgBySpellChart, this.DmgByTargetChart, this.HealsByHealerChartCombat, this.HealsByTargetChartCombat);
			                           }
		                           };
	}

	private void InitializeAvgDmgBySpellChart()
	{
		ChartHelper.ApplyChartStyle(this.AvgDmgBySpellChart, "#252525", "#1E1E1E", "#3A3A3A", "#2A2A2A", "#CCCCCC");
		this.AvgDmgBySpellChart.Plot.YLabel("Avg dmg / hit");
		this.AvgDmgBySpellChart.UserInputProcessor.IsEnabled = false;
		this.AvgDmgBySpellChart.Refresh();
	}

	private void InitializeDmgByTargetChart()
	{
		ChartHelper.ApplyChartStyle(this.DmgByTargetChart, "#252525", "#1E1E1E", "#3A3A3A", "#2A2A2A", "#CCCCCC");
		this.DmgByTargetChart.Plot.YLabel("Dmg taken");
		this.DmgByTargetChart.UserInputProcessor.IsEnabled = false;
		this.DmgByTargetChart.Refresh();
	}

	private void InitializeHealsByHealerChartCombat()
	{
		ChartHelper.ApplyChartStyle(this.HealsByHealerChartCombat, "#252525", "#1E1E1E", "#3A3A3A", "#2A2A2A", "#CCCCCC");
		this.HealsByHealerChartCombat.Plot.YLabel("HP healed");
		this.HealsByHealerChartCombat.UserInputProcessor.IsEnabled = false;
		this.HealsByHealerChartCombat.Refresh();
	}

	private void InitializeHealsByTargetChartCombat()
	{
		ChartHelper.ApplyChartStyle(this.HealsByTargetChartCombat, "#252525", "#1E1E1E", "#3A3A3A", "#2A2A2A", "#CCCCCC");
		this.HealsByTargetChartCombat.Plot.YLabel("HP healed");
		this.HealsByTargetChartCombat.UserInputProcessor.IsEnabled = false;
		this.HealsByTargetChartCombat.Refresh();
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
		this.UpdateAvgDmgBySpellChart();
		this.UpdateDmgByTargetChart();
		this.UpdateHealsByHealerChartCombat();
		this.UpdateHealsByTargetChartCombat();
	}

	private void UpdateAvgDmgBySpellChart()
	{
		ChartHelper.UpdateBarChart(
			this.AvgDmgBySpellChart,
			this.vm?.CombatSummary.DamageBySpell.Where(kv => kv.Value.HitCount > 0).Select(kv => (kv.Key, (double)kv.Value.TotalDamage / kv.Value.HitCount)) ?? [],
			"#FF6644", 14);
	}

	private void UpdateDmgByTargetChart()
	{
		ChartHelper.UpdateBarChart(
			this.DmgByTargetChart,
			this.vm?.CombatSummary.DamageTakenByAttacker.Select(kv => (kv.Key, (double)kv.Value)) ?? [],
			"#DC3545", 14);
	}

	private void UpdateHealsByHealerChartCombat()
	{
		ChartHelper.UpdateBarChart(
			this.HealsByHealerChartCombat,
			this.vm?.CombatSummary.HealsByHealer.Select(kv => (kv.Key, (double)kv.Value)) ?? [],
			"#00D968");
	}

	private void UpdateHealsByTargetChartCombat()
	{
		ChartHelper.UpdateBarChart(
			this.HealsByTargetChartCombat,
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
			ChartHelper.ApplyTheme(newVm.IsDarkTheme, this.AvgDmgBySpellChart, this.DmgByTargetChart, this.HealsByHealerChartCombat, this.HealsByTargetChartCombat);
		}
	}
}
