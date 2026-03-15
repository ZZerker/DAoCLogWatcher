using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DAoCLogWatcher.UI.Models;

public partial class CombatSummary : ObservableObject
{
	[ObservableProperty]
	private int totalDamageDealt;

	[ObservableProperty]
	private int totalDamageTaken;

	[ObservableProperty]
	private int totalHealsReceived;

	[ObservableProperty]
	private int totalHealingDone;

	[ObservableProperty]
	private int hitCount;

	[ObservableProperty]
	private int critCount;

	[ObservableProperty]
	private int totalAbsorbed;

	[ObservableProperty]
	private int meleeHitCount;

	[ObservableProperty]
	private int spellHitCount;

	[ObservableProperty]
	private int meleeMissCount;

	[ObservableProperty]
	private int spellResistCount;

	public double CritRate => HitCount == 0 ? 0.0 : (double)CritCount / HitCount * 100.0;

	public int AvgDamagePerHit => HitCount == 0 ? 0 : TotalDamageDealt / HitCount;

	public double MeleeMissRate => ComputeRate(MeleeHitCount, MeleeMissCount);

	public double SpellResistRate => ComputeRate(SpellHitCount, SpellResistCount);

	private static double ComputeRate(int hits, int misses)
		=> (hits + misses) == 0 ? 0.0 : (double)misses / (hits + misses) * 100.0;

	partial void OnHitCountChanged(int value) { OnPropertyChanged(nameof(CritRate)); OnPropertyChanged(nameof(AvgDamagePerHit)); }
	partial void OnCritCountChanged(int value) => OnPropertyChanged(nameof(CritRate));
	partial void OnTotalDamageDealtChanged(int value) => OnPropertyChanged(nameof(AvgDamagePerHit));
	partial void OnMeleeHitCountChanged(int value) => OnPropertyChanged(nameof(MeleeMissRate));
	partial void OnMeleeMissCountChanged(int value) => OnPropertyChanged(nameof(MeleeMissRate));
	partial void OnSpellHitCountChanged(int value) => OnPropertyChanged(nameof(SpellResistRate));
	partial void OnSpellResistCountChanged(int value) => OnPropertyChanged(nameof(SpellResistRate));

	/// <summary>Total damage dealt per enemy (key = target name).</summary>
	public Dictionary<string, int> DamageByTarget { get; } = new();

	/// <summary>Total HP healed per healer (key = healer name).</summary>
	public Dictionary<string, int> HealsByHealer { get; } = new();

	/// <summary>Total HP you healed per target (key = target name; "yourself" for self-heals).</summary>
	public Dictionary<string, int> HealsByTarget { get; } = new();

	/// <summary>
	/// Per attack type: key = spell name or "Melee", value = (TotalDamage, HitCount).
	/// </summary>
	public Dictionary<string, (int TotalDamage, int HitCount)> DamageBySpell { get; } = new();

	/// <summary>Total damage received per attacker (key = attacker name).</summary>
	public Dictionary<string, int> DamageTakenByAttacker { get; } = new();

	public event EventHandler? ResetRequested;

	public void Reset()
	{
		TotalDamageDealt = 0;
		TotalDamageTaken = 0;
		TotalHealsReceived = 0;
		TotalHealingDone = 0;
		HitCount = 0;
		CritCount = 0;
		TotalAbsorbed = 0;
		MeleeHitCount = 0;
		SpellHitCount = 0;
		MeleeMissCount = 0;
		SpellResistCount = 0;
		DamageByTarget.Clear();
		HealsByHealer.Clear();
		HealsByTarget.Clear();
		DamageBySpell.Clear();
		DamageTakenByAttacker.Clear();
		ResetRequested?.Invoke(this, EventArgs.Empty);
	}
}
