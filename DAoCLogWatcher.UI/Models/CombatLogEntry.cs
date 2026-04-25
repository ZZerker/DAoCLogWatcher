namespace DAoCLogWatcher.UI.Models;

public class CombatLogEntry
{
	public required string Timestamp { get; init; }

	public required string Opponent { get; init; }

	public required int TotalDamage { get; init; }

	public required bool IsDealt { get; init; }

	public required bool IsCrit { get; init; }

	public string? SpellName { get; init; }

	public string? StyleName { get; init; }

	public required bool IsWeaponAttack { get; init; }

	public bool IsMultiHit { get; init; }

	/// <summary>Number of targets hit; 0 for individual hit entries, N for multi-hit aggregate entries.</summary>
	public int HitCount { get; init; }

	public string DirectionLabel => this.IsDealt?"Dealt":"Taken";

	public string Source => this.IsWeaponAttack?this.StyleName ?? "Melee":this.SpellName ?? "Other";

	/// <summary>True for regular (non-AoE) dealt hits — used to show "Dealt" label without overlap.</summary>
	public bool ShowDealtLabel => this.IsDealt&&!this.IsMultiHit;
}
