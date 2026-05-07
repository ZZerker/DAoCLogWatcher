using CommunityToolkit.Mvvm.ComponentModel;

namespace DAoCLogWatcher.UI.Models;

public partial class CombatLogEntry: ObservableObject
{
	public required string Timestamp { get; init; }

	[ObservableProperty] private string opponent = string.Empty;

	public required bool IsDealt { get; init; }

	public string? SpellName { get; init; }

	public string? StyleName { get; init; }

	public required bool IsWeaponAttack { get; init; }

	public bool IsDotTick { get; init; }

	public bool IsAoe { get; init; }

	public bool IsMultiHit { get; init; }

	/// <summary>Number of targets hit for AoE; number of ticks for DoT stacks; 0 for individual hits.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(DotLabel))]
	private int hitCount;

	[ObservableProperty] private int totalDamage;

	[ObservableProperty] private bool isCrit;

	public string Source => this.IsWeaponAttack?this.StyleName ?? "Melee":this.SpellName ?? "Other";

	public string DotLabel => (this.IsAoe ? "AoE DoT" : "DoT") + (this.HitCount > 1 ? $" ×{this.HitCount}" : "");

	/// <summary>True for regular (non-AoE, non-DoT) dealt hits — used to show "Dealt" label without overlap.</summary>
	public bool ShowDealtLabel => this.IsDealt&&!this.IsMultiHit&&!this.IsDotTick;
}
