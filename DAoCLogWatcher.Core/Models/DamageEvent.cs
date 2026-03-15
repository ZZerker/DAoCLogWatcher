namespace DAoCLogWatcher.Core.Models;

public sealed record DamageEvent
{
	public required TimeOnly Timestamp { get; init; }
	/// <summary>The enemy's name — target when IsDealt, attacker when not IsDealt.</summary>
	public required string Target { get; init; }
	public required int BaseDamage { get; init; }
	/// <summary>Damage absorbed/mitigated by the target (the (-N) value in the log).</summary>
	public required int Absorbed { get; init; }
	/// <summary>True when the player dealt the damage; false when the player received it.</summary>
	public required bool IsDealt { get; init; }
	/// <summary>Additional crit damage, 0 when not a critical hit.</summary>
	public int CritDamage { get; init; }
	/// <summary>Spell name when the hit originated from a cast; weapon name when IsWeaponAttack; null for unknown.</summary>
	public string? SpellName { get; init; }
	/// <summary>True when the hit came from a weapon swing ("You attack X with your Y and hit"); false for spells/generic.</summary>
	public bool IsWeaponAttack { get; init; }

	public int TotalDamage => BaseDamage + CritDamage;
}
