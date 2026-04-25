using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DAoCLogWatcher.UI.Models;

public partial class CombatSummary: ObservableObject
{
	[ObservableProperty] private int totalDamageDealt;

	[ObservableProperty] private int totalDamageTaken;

	[ObservableProperty] private int totalHealsReceived;

	[ObservableProperty] private int totalHealingDone;

	[ObservableProperty] private int hitCount;

	[ObservableProperty] private int critCount;

	[ObservableProperty] private int totalAbsorbed;

	[ObservableProperty] private int meleeHitCount;

	[ObservableProperty] private int spellHitCount;

	[ObservableProperty] private int meleeMissCount;

	[ObservableProperty] private int spellResistCount;

	public double CritRate => this.HitCount == 0?0.0:(double)this.CritCount / this.HitCount * 100.0;

	public int AvgDamagePerHit => this.HitCount == 0?0:this.TotalDamageDealt / this.HitCount;

	public double MeleeMissRate => ComputeRate(this.MeleeHitCount, this.MeleeMissCount);

	public double SpellResistRate => ComputeRate(this.SpellHitCount, this.SpellResistCount);

	private static double ComputeRate(int hits, int misses)
	{
		return hits + misses == 0?0.0:(double)misses / (hits + misses) * 100.0;
	}

	partial void OnHitCountChanged(int value)
	{
		this.OnPropertyChanged(nameof(this.CritRate));
		this.OnPropertyChanged(nameof(this.AvgDamagePerHit));
	}

	partial void OnCritCountChanged(int value)
	{
		this.OnPropertyChanged(nameof(this.CritRate));
	}

	partial void OnTotalDamageDealtChanged(int value)
	{
		this.OnPropertyChanged(nameof(this.AvgDamagePerHit));
	}

	partial void OnMeleeHitCountChanged(int value)
	{
		this.OnPropertyChanged(nameof(this.MeleeMissRate));
	}

	partial void OnMeleeMissCountChanged(int value)
	{
		this.OnPropertyChanged(nameof(this.MeleeMissRate));
	}

	partial void OnSpellHitCountChanged(int value)
	{
		this.OnPropertyChanged(nameof(this.SpellResistRate));
	}

	partial void OnSpellResistCountChanged(int value)
	{
		this.OnPropertyChanged(nameof(this.SpellResistRate));
	}

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
		this.TotalDamageDealt = 0;
		this.TotalDamageTaken = 0;
		this.TotalHealsReceived = 0;
		this.TotalHealingDone = 0;
		this.HitCount = 0;
		this.CritCount = 0;
		this.TotalAbsorbed = 0;
		this.MeleeHitCount = 0;
		this.SpellHitCount = 0;
		this.MeleeMissCount = 0;
		this.SpellResistCount = 0;
		this.DamageByTarget.Clear();
		this.HealsByHealer.Clear();
		this.HealsByTarget.Clear();
		this.DamageBySpell.Clear();
		this.DamageTakenByAttacker.Clear();
		this.ResetRequested?.Invoke(this, EventArgs.Empty);
	}
}
