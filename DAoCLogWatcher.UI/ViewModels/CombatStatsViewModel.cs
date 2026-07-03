using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DAoCLogWatcher.UI.Models;
using DAoCLogWatcher.UI.Services;

namespace DAoCLogWatcher.UI.ViewModels;

/// <summary>Owns the combat/heal log streams, the derived heal/damage/attack-type stat
/// collections (including the dashboard top-N projections), the combat-log filter toggles,
/// and the debounced chart-refresh pipeline driven by <see cref="CombatSummary"/> changes.</summary>
public sealed partial class CombatStatsViewModel: ObservableObject, IDisposable
{
	private const int COMBAT_CHART_DEBOUNCE_MS = 250;

	private readonly ICombatProcessor combatProcessor;
	private readonly CombatSummary combatSummary;
	private readonly System.Timers.Timer combatChartDebounceTimer;

	public event EventHandler? CombatChartsUpdateNeeded;

	public FilteredCollection<HealLogEntry> HealLog { get; } = new(static (e, f) =>
			                                                               string.IsNullOrWhiteSpace(f)||(e.Who ?? string.Empty).Contains(f, StringComparison.OrdinalIgnoreCase)||
			                                                               e.DirectionLabel.Contains(f, StringComparison.OrdinalIgnoreCase));

	public FilteredCollection<CombatLogEntry> CombatLog { get; } = new(static (e, f) => string.IsNullOrWhiteSpace(f)||e.Opponent.Contains(f, StringComparison.OrdinalIgnoreCase)||e.Source.Contains(f, StringComparison.OrdinalIgnoreCase));

	public ObservableCollection<HealStatEntry> HealsByHealerStats { get; } = new();

	public ObservableCollection<HealStatEntry> HealsByTargetStats { get; } = new();

	public ObservableCollection<HealStatEntry> DamageTakenByAttackerStats { get; } = new();

	public ObservableCollection<AttackTypeRow> AttackTypeStats { get; } = new();

	public ObservableCollection<HealStatEntry> DashboardTopTargets { get; } = new();

	public ObservableCollection<HealStatEntry> DashboardTopSpells { get; } = new();

	public ObservableCollection<HealStatEntry> DashboardTopHealers { get; } = new();

	public ObservableCollection<HealStatEntry> DashboardTopDamageTaken { get; } = [];

	public ObservableCollection<HealStatEntry> DashboardTopHealsDone { get; } = [];

	public ToggleState CombatLogOutgoing { get; }

	public ToggleState CombatLogIncoming { get; }

	public ToggleState CombatLogAoe { get; }

	public ToggleState CombatLogDot { get; }

	public CombatStatsViewModel(ICombatProcessor combatProcessor, CombatSummary combatSummary)
	{
		this.combatProcessor = combatProcessor;
		this.combatSummary = combatSummary;
		this.CombatLogOutgoing = new ToggleState(true, _ => this.RefreshCombatLogFilter());
		this.CombatLogIncoming = new ToggleState(true, _ => this.RefreshCombatLogFilter());
		this.CombatLogAoe = new ToggleState(true, _ => this.RefreshCombatLogFilter());
		this.CombatLogDot = new ToggleState(true, _ => this.RefreshCombatLogFilter());
		this.combatProcessor.DamageLogged += this.OnDamageLogged;
		this.combatProcessor.HealLogged += this.OnHealLogged;
		this.combatProcessor.MultiHitDetected += this.OnMultiHitDetected;
		this.combatSummary.PropertyChanged += this.OnCombatSummaryPropertyChanged;
		this.combatChartDebounceTimer = new System.Timers.Timer(COMBAT_CHART_DEBOUNCE_MS)
		                                {
				                                AutoReset = false
		                                };
		this.combatChartDebounceTimer.Elapsed += (_, _) =>
		                                         {
			                                         this.CombatChartsUpdateNeeded?.Invoke(this, EventArgs.Empty);
			                                         Dispatcher.UIThread.InvokeAsync((Action)this.RefreshHealStats);
			                                         Dispatcher.UIThread.InvokeAsync((Action)this.RefreshAttackTypeStats);
		                                         };
	}

	private void OnDamageLogged(object? sender, CombatLogEntry e)
	{
		this.CombatLog.Add(e);
	}

	private void OnHealLogged(object? sender, HealLogEntry e)
	{
		this.HealLog.Add(e);
	}

	private void OnMultiHitDetected(object? sender, CombatLogEntry e)
	{
		this.CombatLog.Add(e);
	}

	private void OnCombatSummaryPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if(e.PropertyName is not ("TotalHealsReceived" or "TotalDamageDealt" or "TotalDamageTaken" or "TotalHealingDone"))
		{
			return;
		}

		this.combatChartDebounceTimer.Stop();
		this.combatChartDebounceTimer.Start();
	}

	public void RefreshHealStats()
	{
		RefreshStatCollection(this.HealsByHealerStats, this.combatSummary.HealsByHealer);
		RefreshStatCollection(this.HealsByTargetStats, this.combatSummary.HealsByTarget);
		RefreshStatCollection(this.DamageTakenByAttackerStats, this.combatSummary.DamageTakenByAttacker);
		this.RefreshDashboardTopStats();
	}

	public void RefreshDashboardTopStats()
	{
		RefreshStatCollection(this.DashboardTopTargets, this.combatSummary.DamageByTarget, 3);

		var spellTotalsById = this.combatSummary.DamageBySpell.ToDictionary(kv => kv.Key, kv => kv.Value.TotalDamage);
		RefreshStatCollection(this.DashboardTopSpells, spellTotalsById, 3);

		RefreshStatCollection(this.DashboardTopHealers, this.combatSummary.HealsByHealer, 5);
		RefreshStatCollection(this.DashboardTopDamageTaken, this.combatSummary.DamageTakenByAttacker, 5);
		RefreshStatCollection(this.DashboardTopHealsDone, this.combatSummary.HealsByTarget, 5);
	}

	public void RefreshAttackTypeStats()
	{
		this.AttackTypeStats.Clear();
		var source = this.combatSummary.DamageBySpell;

		var rows = source.Where(kv => kv.Value.HitCount > 0).Select(kv => (kv.Key, kv.Value.HitCount, kv.Value.CritCount, Avg: kv.Value.TotalDamage / kv.Value.HitCount)).OrderByDescending(r => r.Avg).ToList();

		if(rows.Count == 0)
		{
			return;
		}

		var maxAvg = rows[0].Avg;

		foreach(var r in rows)
		{
			this.AttackTypeStats.Add(new AttackTypeRow
			                         {
					                         Name = r.Key,
					                         AvgDamage = r.Avg,
					                         HitCount = r.HitCount,
					                         CritCount = r.CritCount,
					                         Percentage = (double)r.Avg / maxAvg * 100.0
			                         });
		}
	}

	private void RefreshCombatLogFilter()
	{
		this.CombatLog.SetTypeFilter(e => (this.CombatLogOutgoing.Value&&e.ShowDealtLabel)||(this.CombatLogIncoming.Value&&!e.IsDealt)||(this.CombatLogAoe.Value&&e.IsMultiHit)||(this.CombatLogDot.Value&&e.IsDotTick));
	}

	private static void RefreshStatCollection(ObservableCollection<HealStatEntry> collection, Dictionary<string, int> source, int count = int.MaxValue)
	{
		collection.Clear();
		var total = source.Values.Sum();
		if(total == 0)
		{
			return;
		}

		foreach(var kv in source.OrderByDescending(kv => kv.Value).Take(count))
		{
			collection.Add(new HealStatEntry
			               {
					               Name = kv.Key,
					               Total = kv.Value,
					               Percentage = (double)kv.Value / total * 100.0
			               });
		}
	}

	/// <summary>Stops the pending chart-refresh debounce so a completed watch loop doesn't
	/// fire a stale refresh after <see cref="Reset"/> has already run.</summary>
	public void StopChartDebounce()
	{
		this.combatChartDebounceTimer.Stop();
	}

	public void Reset()
	{
		this.HealLog.Clear();
		this.CombatLog.Clear();
		this.HealsByHealerStats.Clear();
		this.HealsByTargetStats.Clear();
		this.DamageTakenByAttackerStats.Clear();
		this.AttackTypeStats.Clear();
		this.DashboardTopTargets.Clear();
		this.DashboardTopSpells.Clear();
		this.DashboardTopHealers.Clear();
		this.DashboardTopDamageTaken.Clear();
		this.DashboardTopHealsDone.Clear();
	}

	public void Dispose()
	{
		this.combatProcessor.DamageLogged -= this.OnDamageLogged;
		this.combatProcessor.HealLogged -= this.OnHealLogged;
		this.combatProcessor.MultiHitDetected -= this.OnMultiHitDetected;
		this.combatSummary.PropertyChanged -= this.OnCombatSummaryPropertyChanged;
		this.combatChartDebounceTimer.Stop();
		this.combatChartDebounceTimer.Dispose();
		GC.SuppressFinalize(this);
	}
}
