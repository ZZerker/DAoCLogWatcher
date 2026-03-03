using System.Globalization;
using System.Text.RegularExpressions;
using DAoCLogWatcher.Core.Models;

namespace DAoCLogWatcher.Core.Parsing;

/// <summary>
/// Parses combat lines: damage dealt/taken and heals received.
/// Handles multi-line crit sequences:
///   Dealt crits have a timestamp and come on the very next line after the hit.
///   Taken crits have NO timestamp and are a direct continuation of the hit line.
/// Correlates "You cast a X spell!" with the immediately following "You hit" to
/// populate DamageEvent.SpellName (same or adjacent second required).
/// </summary>
public sealed partial class CombatParser
{
	private PendingDamage? pendingDealt;
	private PendingDamage? pendingTaken;
	private string? pendingSpellName;
	private TimeOnly pendingSpellTimestamp;

	/// <summary>
	/// Attempts to parse a line. Returns true when an event is ready.
	/// A flushed pending event from a previous hit (no crit followed) takes priority:
	/// it is emitted via <paramref name="damage"/> and the current line may start a new pending.
	/// </summary>
	public bool TryParse(string line, out DamageEvent? damage, out HealEvent? heal)
	{
		damage = null;
		heal = null;

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

		// Try heal — flush any stale pending-dealt as well
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

		// Try dealt hit
		var dealtHitMatch = DealtHitRegex().Match(line);
		if(dealtHitMatch.Success && ExtractTimestamp(dealtHitMatch, out var dealtTs))
		{
			// Associate the pending spell if it was cast within the same or adjacent second
			var diffSeconds = Math.Abs((dealtTs.ToTimeSpan() - this.pendingSpellTimestamp.ToTimeSpan()).TotalSeconds);
			var spellName = (this.pendingSpellName != null && diffSeconds <= 1) ? this.pendingSpellName : null;
			this.pendingSpellName = null;

			var newPending = new PendingDamage(
				dealtTs,
				dealtHitMatch.Groups["target"].Value,
				int.Parse(dealtHitMatch.Groups["dmg"].Value, CultureInfo.InvariantCulture),
				ParseAbsorbed(dealtHitMatch),
				true,
				spellName);

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
				null);

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
			var ev = this.pendingDealt.Value.ToDamageEvent(0);
			this.pendingDealt = null;
			return ev;
		}
		if(this.pendingTaken != null)
		{
			var ev = this.pendingTaken.Value.ToDamageEvent(0);
			this.pendingTaken = null;
			return ev;
		}
		return null;
	}

	private static bool ExtractTimestamp(Match match, out TimeOnly timestamp)
		=> TimeOnly.TryParseExact(match.Groups["ts"].Value, "HH:mm:ss",
			CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp);

	private static int ParseAbsorbed(Match match)
	{
		var group = match.Groups["abs"];
		return group.Success ? int.Parse(group.Value, CultureInfo.InvariantCulture) : 0;
	}

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

	private readonly record struct PendingDamage(TimeOnly Timestamp, string Target, int BaseDamage, int Absorbed, bool IsDealt, string? SpellName)
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
		};
	}
}
