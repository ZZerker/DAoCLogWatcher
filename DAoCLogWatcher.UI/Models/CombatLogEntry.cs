namespace DAoCLogWatcher.UI.Models;

public class CombatLogEntry
{
	public required string Timestamp { get; init; }
	public required string Target { get; init; }
	public required int TotalDamage { get; init; }
	public required bool IsDealt { get; init; }
	public required bool IsCrit { get; init; }
	public string? SpellName { get; init; }
	public required bool IsWeaponAttack { get; init; }

	public string DirectionLabel => IsDealt ? "Dealt" : "Taken";
	public string Source => IsWeaponAttack ? "Melee" : (SpellName ?? "Other");
}
