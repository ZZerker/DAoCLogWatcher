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
	private int hitCount;

	[ObservableProperty]
	private int critCount;

	[ObservableProperty]
	private int totalAbsorbed;

	public double CritRate => HitCount == 0 ? 0.0 : (double)CritCount / HitCount * 100.0;

	public int AvgDamagePerHit => HitCount == 0 ? 0 : TotalDamageDealt / HitCount;

	partial void OnHitCountChanged(int value) { OnPropertyChanged(nameof(CritRate)); OnPropertyChanged(nameof(AvgDamagePerHit)); }
	partial void OnCritCountChanged(int value) => OnPropertyChanged(nameof(CritRate));
	partial void OnTotalDamageDealtChanged(int value) => OnPropertyChanged(nameof(AvgDamagePerHit));

	/// <summary>Total damage dealt per enemy (key = target name).</summary>
	public Dictionary<string, int> DamageByTarget { get; } = new();

	/// <summary>Total HP healed per healer (key = healer name).</summary>
	public Dictionary<string, int> HealsByHealer { get; } = new();

	/// <summary>
	/// Per attack type: key = spell name or "Melee", value = (TotalDamage, HitCount).
	/// </summary>
	public Dictionary<string, (int TotalDamage, int HitCount)> DamageBySpell { get; } = new();

	/// <summary>Total damage received per attacker (key = attacker name).</summary>
	public Dictionary<string, int> DamageTakenByAttacker { get; } = new();

	public void Reset()
	{
		TotalDamageDealt = 0;
		TotalDamageTaken = 0;
		TotalHealsReceived = 0;
		HitCount = 0;
		CritCount = 0;
		TotalAbsorbed = 0;
		DamageByTarget.Clear();
		HealsByHealer.Clear();
		DamageBySpell.Clear();
		DamageTakenByAttacker.Clear();
	}
}
