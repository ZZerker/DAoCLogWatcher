using System.Globalization;
using System.Text.RegularExpressions;
using DAoCLogWatcher.Core.Models;

namespace DAoCLogWatcher.Core.Parsing;

/// <summary>
/// Parses combat lines: damage dealt/taken, heals, and misses/resists.
/// Handles multi-line crit sequences:
///   Dealt crits have a timestamp and come on the very next line after the hit.
///   Taken crits have NO timestamp and are a direct continuation of the hit line.
/// Correlates "You cast a X spell!" with the immediately following "You hit" to
/// populate DamageEvent.SpellName (within 4.5s window for spell travel time).
/// </summary>
public sealed partial class CombatParser
{
	private PendingDamage? pendingDealt;
	private PendingDamage? pendingTaken;
	private string? pendingSpellName;
	private TimeOnly pendingSpellTimestamp;
	private string? confirmedDamageSpellName;

	/// <summary>
	/// Attempts to parse a line. Returns true when an event is ready.
	/// A flushed pending event from a previous hit (no crit followed) takes priority.
	/// </summary>
	public bool TryParse(string line, out DamageEvent? damage, out HealEvent? heal, out MissEvent? miss)
	{
		damage = null;
		heal = null;
		miss = null;

		if(string.IsNullOrWhiteSpace(line))
			return false;

		// Taken-crit has NO timestamp — must check before timestamp-bearing patterns
		var takenCritMatch = TakenCritRegex().Match(line);
		if(takenCritMatch.Success && this.pendingTaken != null)
		{
			var crit = int.Parse(takenCritMatch.Groups["crit"].Value, CultureInfo.InvariantCulture);
			damage = this.pendingTaken.Value.ToDamageEvent(crit);
			this.pendingTaken = null;
			return true;
		}

		// Flush any stale pending-taken if this line is not a crit continuation
		DamageEvent? flushedTaken = null;
		if(this.pendingTaken != null)
		{
			flushedTaken = this.pendingTaken.Value.ToDamageEvent(0);
			this.pendingTaken = null;
		}

		// Spell interrupted — clear pending spell name; fall through to no-match handling
		if(line.Contains("your spell is interrupted!"))
			this.pendingSpellName = null;

		// Spell cast — update pending spell name
		var spellCastMatch = SpellCastRegex().Match(line);
		if(spellCastMatch.Success && ExtractTimestamp(spellCastMatch, out var spellTs))
		{
			this.pendingSpellName = spellCastMatch.Groups["spell"].Value;
			this.pendingSpellTimestamp = spellTs;
			// Spell cast is not a crit continuation — flush stale pending-dealt
			if(this.pendingDealt != null)
			{
				damage = this.pendingDealt.Value.ToDamageEvent(0);
				this.pendingDealt = null;
				return true;
			}
			damage = flushedTaken;
			return flushedTaken != null;
		}

		// Try incoming heal (you are healed by someone)
		var healMatch = HealRegex().Match(line);
		if(healMatch.Success && ExtractTimestamp(healMatch, out var healTs))
		{
			heal = new HealEvent
			{
				Timestamp = healTs,
				Healer = healMatch.Groups["healer"].Value,
				HitPoints = int.Parse(healMatch.Groups["hp"].Value, CultureInfo.InvariantCulture)
			};
			if(this.pendingDealt != null)
			{
				damage = this.pendingDealt.Value.ToDamageEvent(0);
				this.pendingDealt = null;
			}
			else
			{
				damage = flushedTaken;
			}
			return true;
		}

		// Try outgoing heal (you heal yourself or a group member)
		var outgoingHealMatch = OutgoingHealRegex().Match(line);
		if(outgoingHealMatch.Success && ExtractTimestamp(outgoingHealMatch, out var outgoingHealTs))
		{
			heal = new HealEvent
			{
				Timestamp = outgoingHealTs,
				Target = outgoingHealMatch.Groups["target"].Value,
				HitPoints = int.Parse(outgoingHealMatch.Groups["hp"].Value, CultureInfo.InvariantCulture),
				IsOutgoing = true
			};
			if(this.pendingDealt != null)
			{
				damage = this.pendingDealt.Value.ToDamageEvent(0);
				this.pendingDealt = null;
			}
			else
			{
				damage = flushedTaken;
			}
			return true;
		}

		// Try dealt hit — weapon attack format: "You attack {target} with your {weapon} and hit for N damage!"
		var weaponAttackMatch = WeaponAttackRegex().Match(line);
		if(weaponAttackMatch.Success && ExtractTimestamp(weaponAttackMatch, out var weaponTs))
		{
			this.pendingSpellName = null;
			var newPending = new PendingDamage(
				weaponTs,
				weaponAttackMatch.Groups["target"].Value,
				int.Parse(weaponAttackMatch.Groups["dmg"].Value, CultureInfo.InvariantCulture),
				ParseAbsorbed(weaponAttackMatch),
				true,
				weaponAttackMatch.Groups["weapon"].Value,
				IsWeaponAttack: true);

			if(this.pendingDealt != null)
			{
				damage = this.pendingDealt.Value.ToDamageEvent(0);
				this.pendingDealt = newPending;
				return true;
			}

			this.pendingDealt = newPending;
			damage = flushedTaken;
			return flushedTaken != null;
		}

		// Try dealt hit — spell/generic format: "You hit {target} for N damage!"
		var dealtHitMatch = DealtHitRegex().Match(line);
		if(dealtHitMatch.Success && ExtractTimestamp(dealtHitMatch, out var dealtTs))
		{
			// Two-tier spell attribution:
			// 1. Within 4.5s of last cast: attribute to that spell and confirm it deals damage.
			//    Keeps pendingSpellName alive for AE (multiple simultaneous hits from one cast).
			// 2. Outside window (or no pending cast): fall back to confirmedDamageSpellName.
			//    Covers DoT/rain ticks that arrive long after the cast.
			//    Buff casts never get confirmed (no hit within 4.5s), so they don't pollute.
			string? spellName;
			if(this.pendingSpellName != null)
			{
				var diffSeconds = Math.Abs((dealtTs.ToTimeSpan() - this.pendingSpellTimestamp.ToTimeSpan()).TotalSeconds);
				if(diffSeconds <= 4.5)
				{
					spellName = this.pendingSpellName;
					this.confirmedDamageSpellName = this.pendingSpellName;
				}
				else
				{
					spellName = this.confirmedDamageSpellName;
				}
			}
			else
			{
				spellName = this.confirmedDamageSpellName;
			}

			var newPending = new PendingDamage(
				dealtTs,
				dealtHitMatch.Groups["target"].Value,
				int.Parse(dealtHitMatch.Groups["dmg"].Value, CultureInfo.InvariantCulture),
				ParseAbsorbed(dealtHitMatch),
				true,
				spellName,
				IsWeaponAttack: false);

			if(this.pendingDealt != null)
			{
				// Flush previous dealt pending (no crit arrived) and start new pending
				damage = this.pendingDealt.Value.ToDamageEvent(0);
				this.pendingDealt = newPending;
				return true;
			}

			this.pendingDealt = newPending;
			damage = flushedTaken;
			return flushedTaken != null;
		}

		// Try dealt crit (timestamped continuation of a dealt hit)
		var dealtCritMatch = DealtCritRegex().Match(line);
		if(dealtCritMatch.Success && this.pendingDealt != null)
		{
			var crit = int.Parse(dealtCritMatch.Groups["crit"].Value, CultureInfo.InvariantCulture);
			damage = this.pendingDealt.Value.ToDamageEvent(crit);
			this.pendingDealt = null;
			return true;
		}

		// Try taken hit (timestamped)
		var takenHitMatch = TakenHitRegex().Match(line);
		if(takenHitMatch.Success && ExtractTimestamp(takenHitMatch, out var takenTs))
		{
			var newPending = new PendingDamage(
				takenTs,
				takenHitMatch.Groups["attacker"].Value,
				int.Parse(takenHitMatch.Groups["dmg"].Value, CultureInfo.InvariantCulture),
				ParseAbsorbed(takenHitMatch),
				false,
				null,
				IsWeaponAttack: false);

			if(this.pendingDealt != null)
			{
				// Flush stale pending-dealt, then start new taken-pending
				damage = this.pendingDealt.Value.ToDamageEvent(0);
				this.pendingDealt = null;
				this.pendingTaken = newPending;
				return true;
			}

			this.pendingTaken = newPending;
			damage = flushedTaken;
			return flushedTaken != null;
		}

		// Try melee miss: "You miss! (Miss Chance: X%)"
		var meleesMissMatch = MeleeMissRegex().Match(line);
		if(meleesMissMatch.Success && ExtractTimestamp(meleesMissMatch, out var missTs))
		{
			miss = new MissEvent { Timestamp = missTs, IsSpell = false, Target = null };
			damage = FlushForMiss(flushedTaken);
			return true;
		}

		// Try melee block: "{target} blocks your attack! (Block Chance: X%)"
		var blockMatch = BlockRegex().Match(line);
		if(blockMatch.Success && ExtractTimestamp(blockMatch, out var blockTs))
		{
			miss = new MissEvent { Timestamp = blockTs, IsSpell = false, Target = blockMatch.Groups["target"].Value };
			damage = FlushForMiss(flushedTaken);
			return true;
		}

		// Try spell resist: "{target} resists the effect! (Resist Chance: X%)"
		var resistMatch = SpellResistRegex().Match(line);
		if(resistMatch.Success && ExtractTimestamp(resistMatch, out var resistTs))
		{
			miss = new MissEvent { Timestamp = resistTs, IsSpell = true, Target = resistMatch.Groups["target"].Value };
			damage = FlushForMiss(flushedTaken);
			return true;
		}

		// No combat match — flush stale pending-dealt
		if(this.pendingDealt != null)
		{
			damage = this.pendingDealt.Value.ToDamageEvent(0);
			this.pendingDealt = null;
			return true;
		}

		if(flushedTaken != null)
		{
			damage = flushedTaken;
			return true;
		}

		return false;
	}

	/// <summary>Flush any pending events (call at end of session/reset).</summary>
	public DamageEvent? FlushPending()
	{
		if(this.pendingDealt != null)
		{
			return FlushPendingDealt();
		}
		if(this.pendingTaken != null)
		{
			var ev = this.pendingTaken.Value.ToDamageEvent(0);
			this.pendingTaken = null;
			return ev;
		}
		return null;
	}

	private DamageEvent FlushPendingDealt()
	{
		var ev = this.pendingDealt!.Value.ToDamageEvent(0);
		this.pendingDealt = null;
		return ev;
	}

	private DamageEvent? FlushForMiss(DamageEvent? flushedTaken)
		=> flushedTaken ?? (this.pendingDealt != null ? FlushPendingDealt() : null);

	private static bool ExtractTimestamp(Match match, out TimeOnly timestamp)
		=> TimeOnly.TryParseExact(match.Groups["ts"].Value, "HH:mm:ss",
			CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp);

	private static int ParseAbsorbed(Match match)
	{
		var group = match.Groups["abs"];
		return group.Success ? int.Parse(group.Value, CultureInfo.InvariantCulture) : 0;
	}

	// [HH:mm:ss] You attack {target} with your {weapon} and hit for {N} (-{abs}) damage! (Damage Modifier: X)
	[GeneratedRegex(@"^\[(?<ts>\d{2}:\d{2}:\d{2})\] You attack (?<target>.+?) with your (?<weapon>.+?) and hit for (?<dmg>\d+)(?: \(-(?<abs>\d+)\))? damage!",
		RegexOptions.Compiled | RegexOptions.CultureInvariant)]
	private static partial Regex WeaponAttackRegex();

	// [HH:mm:ss] You hit {target} for {N} (-{abs}) damage!
	[GeneratedRegex(@"^\[(?<ts>\d{2}:\d{2}:\d{2})\] You hit (?<target>.+?) for (?<dmg>\d+)(?: \(-(?<abs>\d+)\))? damage!$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant)]
	private static partial Regex DealtHitRegex();

	// [HH:mm:ss] You critically hit for an additional {N} damage! (Crit Chance: X%)
	[GeneratedRegex(@"^\[(?<ts>\d{2}:\d{2}:\d{2})\] You critically hit for an additional (?<crit>\d+) damage! \(Crit Chance: [\d.]+%\)$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant)]
	private static partial Regex DealtCritRegex();

	// [HH:mm:ss] {attacker} hits you for {N} (-{abs}) damage!
	[GeneratedRegex(@"^\[(?<ts>\d{2}:\d{2}:\d{2})\] (?<attacker>.+?) hits you for (?<dmg>\d+)(?: \(-(?<abs>\d+)\))? damage!$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant)]
	private static partial Regex TakenHitRegex();

	// {attacker} critically hits you for an additional {N} damage! (Crit Chance: X%)  — NO timestamp
	[GeneratedRegex(@"^(?<attacker>.+?) critically hits you for an additional (?<crit>\d+) damage! \(Crit Chance: [\d.]+%\)$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant)]
	private static partial Regex TakenCritRegex();

	// [HH:mm:ss] You are healed by {healer} for {N} hit points.
	[GeneratedRegex(@"^\[(?<ts>\d{2}:\d{2}:\d{2})\] You are healed by (?<healer>.+?) for (?<hp>\d+) hit points\.$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant)]
	private static partial Regex HealRegex();

	// [HH:mm:ss] You cast a {spell} spell!
	[GeneratedRegex(@"^\[(?<ts>\d{2}:\d{2}:\d{2})\] You cast a (?<spell>.+?) spell!$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant)]
	private static partial Regex SpellCastRegex();

	// [HH:mm:ss] You heal {target} for {N} hit points.   (yourself — ends with .)
	// [HH:mm:ss] You heal {target} for {N} hit points!   (others  — ends with !)
	[GeneratedRegex(@"^\[(?<ts>\d{2}:\d{2}:\d{2})\] You heal (?<target>.+?) for (?<hp>\d+) hit points[.!]$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant)]
	private static partial Regex OutgoingHealRegex();

	// [HH:mm:ss] You miss! (Miss Chance: X%)
	[GeneratedRegex(@"^\[(?<ts>\d{2}:\d{2}:\d{2})\] You miss! \(Miss Chance: [\d.]+%\)$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant)]
	private static partial Regex MeleeMissRegex();

	// [HH:mm:ss] {target} blocks your attack! (Block Chance: X%)
	[GeneratedRegex(@"^\[(?<ts>\d{2}:\d{2}:\d{2})\] (?<target>.+?) blocks your attack! \(Block Chance: [\d.]+%\)$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant)]
	private static partial Regex BlockRegex();

	// [HH:mm:ss] {target} resists the effect! (Resist Chance: X%)
	[GeneratedRegex(@"^\[(?<ts>\d{2}:\d{2}:\d{2})\] (?<target>.+?) resists the effect! \(Resist Chance: [\d.]+%\)$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant)]
	private static partial Regex SpellResistRegex();

	private readonly record struct PendingDamage(TimeOnly Timestamp, string Target, int BaseDamage, int Absorbed, bool IsDealt, string? SpellName, bool IsWeaponAttack)
	{
		public DamageEvent ToDamageEvent(int critDamage) => new()
		{
			Timestamp = Timestamp,
			Target = Target,
			BaseDamage = BaseDamage,
			Absorbed = Absorbed,
			IsDealt = IsDealt,
			CritDamage = critDamage,
			SpellName = SpellName,
			IsWeaponAttack = IsWeaponAttack,
		};
	}
}
