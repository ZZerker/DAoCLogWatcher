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
/// populate DamageEvent.SpellName (within a window for spell travel time).
/// </summary>
public sealed partial class CombatParser
{
	private const double SPELL_ATTRIBUTION_WINDOW_SECONDS = 4.5;

	private PendingDamage? pendingDealt;
	private PendingDamage? pendingTaken;
	private string? pendingSpellName;
	private TimeOnly pendingSpellTimestamp;
	private string? confirmedDamageSpellName;
	private string? pendingBowShotName;
	private TimeOnly pendingBowShotTimestamp;
	private string? pendingStyleName;
	private TimeOnly pendingStyleTimestamp;

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
		{
			return false;
		}

		// Taken-crit has NO timestamp — must check before timestamp-bearing patterns
		if(this.TryTakenCritContinuation(line, out damage))
		{
			return true;
		}

		// Flush any stale pending-taken if this line is not a crit continuation
		var flushedTaken = this.FlushPendingTaken();

		// Spell interrupted — clear pending spell name; fall through to no-match handling
		if(line.Contains("your spell is interrupted!"))
		{
			this.pendingSpellName = null;
		}

		if(this.TryMatchSpellCast(line))
		{
			damage = this.FlushPendingDealtOrFallback(flushedTaken);
			return damage != null;
		}

		if(this.TryMatchBowShot(line))
		{
			damage = this.FlushPendingDealtOrFallback(flushedTaken);
			return damage != null;
		}

		if(this.TryMatchStylePerformed(line))
		{
			damage = this.FlushPendingDealtOrFallback(flushedTaken);
			return damage != null;
		}

		if(this.TryMatchStylePrepare(line))
		{
			damage = this.FlushPendingDealtOrFallback(flushedTaken);
			return damage != null;
		}

		if(this.TryMatchStyleChainFallback(line))
		{
			damage = this.FlushPendingDealtOrFallback(flushedTaken);
			return damage != null;
		}

		if(this.TryMatchStyleFailed(line))
		{
			damage = this.FlushPendingDealtOrFallback(flushedTaken);
			return damage != null;
		}

		if(this.TryMatchIncomingHeal(line, out heal))
		{
			damage = this.FlushPendingDealtOrFallback(flushedTaken);
			return true;
		}

		if(this.TryMatchOutgoingHeal(line, out heal))
		{
			damage = this.FlushPendingDealtOrFallback(flushedTaken);
			return true;
		}

		if(this.TryMatchWeaponAttack(line, out var weaponPending))
		{
			damage = this.SwapPendingDealt(weaponPending, flushedTaken);
			return damage != null;
		}

		if(this.TryMatchDealtSpellHit(line, out var spellPending))
		{
			damage = this.SwapPendingDealt(spellPending, flushedTaken);
			return damage != null;
		}

		if(this.TryMatchDealtDotHit(line, out var dotPending))
		{
			damage = this.SwapPendingDealt(dotPending, flushedTaken);
			return damage != null;
		}

		if(this.TryMatchDealtCrit(line, out damage))
		{
			return true;
		}

		if(this.TryMatchTakenHit(line, out var takenPending))
		{
			damage = this.SetTakenPending(takenPending, flushedTaken);
			return damage != null;
		}

		if(this.TryMatchMeleeMiss(line, out miss)||this.TryMatchBlock(line, out miss)||this.TryMatchSpellResist(line, out miss))
		{
			damage = this.FlushForMiss(flushedTaken);
			return true;
		}

		// No combat match — flush any stale pending
		damage = this.FlushPendingDealtOrFallback(flushedTaken);
		return damage != null;
	}

	/// <summary>Flush any pending events (call at end of session/reset).</summary>
	public DamageEvent? FlushPending()
	{
		if(this.pendingDealt != null)
		{
			return this.FlushPendingDealt();
		}

		if(this.pendingTaken != null)
		{
			var ev = this.pendingTaken.Value.ToDamageEvent(0);
			this.pendingTaken = null;
			return ev;
		}

		return null;
	}

	// ── Match methods ────────────────────────────────────────────────────

	private bool TryTakenCritContinuation(string line, out DamageEvent? damage)
	{
		damage = null;
		var match = TakenCritRegex().Match(line);
		if(!match.Success||this.pendingTaken == null)
		{
			return false;
		}

		var crit = int.Parse(match.Groups["crit"].Value, CultureInfo.InvariantCulture);
		damage = this.pendingTaken.Value.ToDamageEvent(crit);
		this.pendingTaken = null;
		return true;
	}

	private bool TryMatchSpellCast(string line)
	{
		var match = SpellCastRegex().Match(line);
		if(!match.Success||!ExtractTimestamp(match, out var ts))
		{
			return false;
		}

		this.pendingSpellName = match.Groups["spell"].Value;
		this.pendingSpellTimestamp = ts;
		return true;
	}

	private bool TryMatchBowShot(string line)
	{
		var match = BowShotRegex().Match(line);
		if(!match.Success||!ExtractTimestamp(match, out var ts))
		{
			return false;
		}

		this.pendingBowShotName = match.Groups["shot"].Value;
		this.pendingBowShotTimestamp = ts;
		return true;
	}

	private bool TryMatchStylePerformed(string line)
	{
		var match = StylePerformedRegex().Match(line);
		if(!match.Success||!ExtractTimestamp(match, out var ts))
		{
			return false;
		}

		this.pendingStyleName = match.Groups["style"].Value;
		this.pendingStyleTimestamp = ts;
		return true;
	}

	private bool TryMatchStylePrepare(string line)
	{
		var match = StylePrepareRegex().Match(line);
		if(!match.Success||!ExtractTimestamp(match, out var ts))
		{
			return false;
		}

		this.pendingStyleName = match.Groups["style"].Value;
		this.pendingStyleTimestamp = ts;
		return true;
	}

	private bool TryMatchStyleChainFallback(string line)
	{
		var match = StyleChainFallbackRegex().Match(line);
		if(!match.Success||!ExtractTimestamp(match, out var ts))
		{
			return false;
		}

		this.pendingStyleName = match.Groups["style"].Value;
		this.pendingStyleTimestamp = ts;
		return true;
	}

	private bool TryMatchStyleFailed(string line)
	{
		var match = StyleFailedRegex().Match(line);
		if(!match.Success)
		{
			return false;
		}

		this.pendingStyleName = null;
		return true;
	}

	private string? ResolveStyleName(TimeOnly hitTimestamp)
	{
		if(this.pendingStyleName == null)
		{
			return null;
		}

		var diff = TimeHelper.ShortestArcSeconds(hitTimestamp, this.pendingStyleTimestamp);
		return diff <= 6.0?this.pendingStyleName:null;
	}

	private bool TryMatchIncomingHeal(string line, out HealEvent? heal)
	{
		heal = null;
		var match = HealRegex().Match(line);
		if(!match.Success||!ExtractTimestamp(match, out var ts))
		{
			return false;
		}

		heal = new HealEvent
		       {
				       Timestamp = ts,
				       Healer = match.Groups["healer"].Value,
				       HitPoints = int.Parse(match.Groups["hp"].Value, CultureInfo.InvariantCulture)
		       };
		return true;
	}

	private bool TryMatchOutgoingHeal(string line, out HealEvent? heal)
	{
		heal = null;
		var match = OutgoingHealRegex().Match(line);
		if(!match.Success||!ExtractTimestamp(match, out var ts))
		{
			return false;
		}

		heal = new HealEvent
		       {
				       Timestamp = ts,
				       Target = match.Groups["target"].Value,
				       HitPoints = int.Parse(match.Groups["hp"].Value, CultureInfo.InvariantCulture),
				       IsOutgoing = true
		       };
		return true;
	}

	private bool TryMatchWeaponAttack(string line, out PendingDamage pending)
	{
		pending = default;
		var match = WeaponAttackRegex().Match(line);
		if(!match.Success||!ExtractTimestamp(match, out var ts))
		{
			return false;
		}

		this.pendingSpellName = null;
		pending = new PendingDamage(ts, match.Groups["target"].Value, int.Parse(match.Groups["dmg"].Value, CultureInfo.InvariantCulture), ParseAbsorbed(match), true, match.Groups["weapon"].Value, true, this.ResolveStyleName(ts));
		return true;
	}

	private bool TryMatchDealtSpellHit(string line, out PendingDamage pending)
	{
		pending = default;
		var match = DealtHitRegex().Match(line);
		if(!match.Success||!ExtractTimestamp(match, out var ts))
		{
			return false;
		}

		var spellName = this.ResolveSpellName(ts);
		var isWeaponAttack = spellName == null;
		var styleName = isWeaponAttack?this.ResolveStyleName(ts):null;
		pending = new PendingDamage(ts, match.Groups["target"].Value, int.Parse(match.Groups["dmg"].Value, CultureInfo.InvariantCulture), ParseAbsorbed(match), true, spellName, isWeaponAttack, styleName);
		return true;
	}

	private bool TryMatchDealtDotHit(string line, out PendingDamage pending)
	{
		pending = default;
		var match = DealtDotHitRegex().Match(line);
		if(!match.Success||!ExtractTimestamp(match, out var ts))
		{
			return false;
		}

		pending = new PendingDamage(ts, match.Groups["target"].Value, int.Parse(match.Groups["dmg"].Value, CultureInfo.InvariantCulture), ParseAbsorbed(match), true, match.Groups["spell"].Value, false);
		return true;
	}

	private bool TryMatchDealtCrit(string line, out DamageEvent? damage)
	{
		damage = null;
		if(this.pendingDealt == null)
		{
			return false;
		}

		var match = DealtCritRegex().Match(line);
		if(!match.Success)
		{
			match = DealtMeleeCritRegex().Match(line);
		}

		if(!match.Success)
		{
			match = DealtDotCritRegex().Match(line);
		}

		if(!match.Success)
		{
			return false;
		}

		var crit = int.Parse(match.Groups["crit"].Value, CultureInfo.InvariantCulture);
		damage = this.pendingDealt.Value.ToDamageEvent(crit);
		this.pendingDealt = null;
		return true;
	}

	private bool TryMatchTakenHit(string line, out PendingDamage pending)
	{
		pending = default;
		var match = TakenHitRegex().Match(line);
		if(!match.Success||!ExtractTimestamp(match, out var ts))
		{
			return false;
		}

		pending = new PendingDamage(ts, match.Groups["attacker"].Value, int.Parse(match.Groups["dmg"].Value, CultureInfo.InvariantCulture), ParseAbsorbed(match), false, null, false);
		return true;
	}

	private bool TryMatchMeleeMiss(string line, out MissEvent? miss)
	{
		miss = null;
		var match = MeleeMissRegex().Match(line);
		if(!match.Success||!ExtractTimestamp(match, out var ts))
		{
			return false;
		}

		miss = new MissEvent
		       {
				       Timestamp = ts,
				       IsSpell = false
		       };
		return true;
	}

	private bool TryMatchBlock(string line, out MissEvent? miss)
	{
		miss = null;
		var match = BlockRegex().Match(line);
		if(!match.Success||!ExtractTimestamp(match, out var ts))
		{
			return false;
		}

		miss = new MissEvent
		       {
				       Timestamp = ts,
				       IsSpell = false,
				       Target = match.Groups["target"].Value
		       };
		return true;
	}

	private bool TryMatchSpellResist(string line, out MissEvent? miss)
	{
		miss = null;
		var match = SpellResistRegex().Match(line);
		if(!match.Success||!ExtractTimestamp(match, out var ts))
		{
			return false;
		}

		miss = new MissEvent
		       {
				       Timestamp = ts,
				       IsSpell = true,
				       Target = match.Groups["target"].Value
		       };
		return true;
	}

	// ── State management helpers ─────────────────────────────────────────

	/// <summary>
	/// Two-tier spell attribution:
	/// 1. Within window of last cast → attribute to that spell and confirm it deals damage.
	///    Keeps pendingSpellName alive for AE (multiple simultaneous hits from one cast).
	/// 2. Outside window (or no pending cast) → fall back to confirmedDamageSpellName.
	///    Covers DoT/rain ticks that arrive long after the cast.
	///    Buff casts never get confirmed (no hit within window), so they don't pollute.
	/// </summary>
	private string? ResolveSpellName(TimeOnly hitTimestamp)
	{
		// Bow shots have priority — check before spell cast (pet summons etc. cast between fire and hit must not win)
		if(this.pendingBowShotName != null)
		{
			var bowDiff = TimeHelper.ShortestArcSeconds(hitTimestamp, this.pendingBowShotTimestamp);
			if(bowDiff <= SPELL_ATTRIBUTION_WINDOW_SECONDS)
			{
				this.confirmedDamageSpellName = this.pendingBowShotName;
				return this.pendingBowShotName;
			}
		}

		if(this.pendingSpellName != null)
		{
			var diffSeconds = TimeHelper.ShortestArcSeconds(hitTimestamp, this.pendingSpellTimestamp);
			if(diffSeconds <= SPELL_ATTRIBUTION_WINDOW_SECONDS)
			{
				this.confirmedDamageSpellName = this.pendingSpellName;
				return this.pendingSpellName;
			}
		}

		return this.confirmedDamageSpellName;
	}

	private DamageEvent? FlushPendingTaken()
	{
		if(this.pendingTaken == null)
		{
			return null;
		}

		var ev = this.pendingTaken.Value.ToDamageEvent(0);
		this.pendingTaken = null;
		return ev;
	}

	private DamageEvent FlushPendingDealt()
	{
		var ev = this.pendingDealt!.Value.ToDamageEvent(0);
		this.pendingDealt = null;
		return ev;
	}

	private DamageEvent? FlushPendingDealtOrFallback(DamageEvent? fallback)
	{
		if(this.pendingDealt != null)
		{
			return this.FlushPendingDealt();
		}

		return fallback;
	}

	/// <summary>
	/// Replace current pending-dealt with a new one. Returns the flushed old dealt event,
	/// or the fallback if there was no previous pending.
	/// </summary>
	private DamageEvent? SwapPendingDealt(PendingDamage newPending, DamageEvent? fallback)
	{
		DamageEvent? flushed = null;
		if(this.pendingDealt != null)
		{
			flushed = this.pendingDealt.Value.ToDamageEvent(0);
		}

		this.pendingDealt = newPending;
		return flushed ?? fallback;
	}

	/// <summary>
	/// Set a new taken-pending, flushing any stale dealt-pending first.
	/// Returns the flushed dealt event, or the fallback if no dealt was pending.
	/// </summary>
	private DamageEvent? SetTakenPending(PendingDamage takenPending, DamageEvent? fallback)
	{
		DamageEvent? flushed = null;
		if(this.pendingDealt != null)
		{
			flushed = this.pendingDealt.Value.ToDamageEvent(0);
			this.pendingDealt = null;
		}

		this.pendingTaken = takenPending;
		return flushed ?? fallback;
	}

	private DamageEvent? FlushForMiss(DamageEvent? flushedTaken)
	{
		return flushedTaken ?? (this.pendingDealt != null?FlushPendingDealt():null);
	}

	// ── Parsing utilities ────────────────────────────────────────────────

	private static bool ExtractTimestamp(Match match, out TimeOnly timestamp)
	{
		return TimeOnly.TryParseExact(match.Groups["ts"].Value, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp);
	}

	private static int ParseAbsorbed(Match match)
	{
		var group = match.Groups["abs"];
		return group.Success?int.Parse(group.Value, CultureInfo.InvariantCulture):0;
	}

	// ── Regex patterns ───────────────────────────────────────────────────

	// [HH:mm:ss] You attack {target} with your {weapon} and hit for {N} (-{abs}) damage! (Damage Modifier: X)
	[GeneratedRegex(@"^\[(?<ts>\d{2}:\d{2}:\d{2})\] You attack (?<target>.+?) with your (?<weapon>.+?) and hit for (?<dmg>\d+)(?: \(-(?<abs>\d+)\))? damage!", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex WeaponAttackRegex();

	// [HH:mm:ss] You hit {target} for {N} (-{abs}) damage!
	[GeneratedRegex(@"^\[(?<ts>\d{2}:\d{2}:\d{2})\] You hit (?<target>.+?) for (?<dmg>\d+)(?: \(-(?<abs>\d+)\))? damage!$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex DealtHitRegex();

	// [HH:mm:ss] You critically hit for an additional {N} damage! (Crit Chance: X%)  — spell/range crit (with timestamp)
	[GeneratedRegex(@"^\[(?<ts>\d{2}:\d{2}:\d{2})\] You critically hit for an additional (?<crit>\d+) damage! \(Crit Chance: [\d.]+%\)$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex DealtCritRegex();

	// You critically hit {target} for an additional {N} damage! (Crit Chance: X%)  — melee/weapon crit (NO timestamp)
	[GeneratedRegex(@"^You critically hit .+? for an additional (?<crit>\d+) damage! \(Crit Chance: [\d.]+%\)$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex DealtMeleeCritRegex();

	// [HH:mm:ss] Your {spell} hits {target} for {N} damage!  — DoT/named-spell tick
	[GeneratedRegex(@"^\[(?<ts>\d{2}:\d{2}:\d{2})\] Your (?<spell>.+?) hits (?<target>.+?) for (?<dmg>\d+)(?: \(-(?<abs>\d+)\))? damage!$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex DealtDotHitRegex();

	// [HH:mm:ss] Your {spell} critically hits {target} for an additional {N} damage!  — DoT/named-spell crit (no Crit Chance suffix)
	[GeneratedRegex(@"^\[(?<ts>\d{2}:\d{2}:\d{2})\] Your (?<spell>.+?) critically hits (?<target>.+?) for an additional (?<crit>\d+) damage!$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex DealtDotCritRegex();

	// [HH:mm:ss] {attacker} hits you for {N} (-{abs}) damage!
	[GeneratedRegex(@"^\[(?<ts>\d{2}:\d{2}:\d{2})\] (?<attacker>.+?) hits you for (?<dmg>\d+)(?: \(-(?<abs>\d+)\))? damage!$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex TakenHitRegex();

	// {attacker} critically hits you for an additional {N} damage! (Crit Chance: X%)  — NO timestamp
	[GeneratedRegex(@"^(?<attacker>.+?) critically hits you for an additional (?<crit>\d+) damage! \(Crit Chance: [\d.]+%\)$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex TakenCritRegex();

	// [HH:mm:ss] You are healed by {healer} for {N} hit points.
	[GeneratedRegex(@"^\[(?<ts>\d{2}:\d{2}:\d{2})\] You are healed by (?<healer>.+?) for (?<hp>\d+) hit points\.$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex HealRegex();

	// [HH:mm:ss] You cast a {spell} spell!
	[GeneratedRegex(@"^\[(?<ts>\d{2}:\d{2}:\d{2})\] You cast a (?<spell>.+?) spell!$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex SpellCastRegex();

	// [HH:mm:ss] You fire a {shot}!
	[GeneratedRegex(@"^\[(?<ts>\d{2}:\d{2}:\d{2})\] You fire a (?<shot>.+?)!$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex BowShotRegex();

	// [HH:mm:ss] You heal {target} for {N} hit points.   (yourself — ends with .)
	// [HH:mm:ss] You heal {target} for {N} hit points!   (others  — ends with !)
	[GeneratedRegex(@"^\[(?<ts>\d{2}:\d{2}:\d{2})\] You heal (?<target>.+?) for (?<hp>\d+) hit points[.!]$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex OutgoingHealRegex();

	// [HH:mm:ss] You miss! (Miss Chance: X%)
	[GeneratedRegex(@"^\[(?<ts>\d{2}:\d{2}:\d{2})\] You miss! \(Miss Chance: [\d.]+%\)$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex MeleeMissRegex();

	// [HH:mm:ss] {target} blocks your attack! (Block Chance: X%)
	[GeneratedRegex(@"^\[(?<ts>\d{2}:\d{2}:\d{2})\] (?<target>.+?) blocks your attack! \(Block Chance: [\d.]+%\)$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex BlockRegex();

	// [HH:mm:ss] {target} resists the effect! (Resist Chance: X%)
	[GeneratedRegex(@"^\[(?<ts>\d{2}:\d{2}:\d{2})\] (?<target>.+?) resists the effect! \(Resist Chance: [\d.]+%\)$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex SpellResistRegex();

	// [HH:mm:ss] You perform your {style} perfectly! (+N, Growth Rate: X.XX)
	[GeneratedRegex(@"^\[(?<ts>\d{2}:\d{2}:\d{2})\] You perform your (?<style>.+?) perfectly!", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex StylePerformedRegex();

	// [HH:mm:ss] You prepare to perform a {style}!  — NPC/monster combat (style queued before hit)
	[GeneratedRegex(@"^\[(?<ts>\d{2}:\d{2}:\d{2})\] You prepare to perform (?:a |an )(?<style>.+?)!$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex StylePrepareRegex();

	// [HH:mm:ss] You must perform the {style} style before this one!  — chain style fallback; opening style executes
	[GeneratedRegex(@"^\[(?<ts>\d{2}:\d{2}:\d{2})\] You must perform the (?<style>.+?) style before this one!$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex StyleChainFallbackRegex();

	// [HH:mm:ss] You fail to execute your {style} perfectly!
	[GeneratedRegex(@"^\[\d{2}:\d{2}:\d{2}\] You fail to execute your .+? perfectly!", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex StyleFailedRegex();

	private readonly record struct PendingDamage(TimeOnly Timestamp, string Opponent, int BaseDamage, int Absorbed, bool IsDealt, string? SpellName, bool IsWeaponAttack, string? StyleName = null)
	{
		public DamageEvent ToDamageEvent(int critDamage)
		{
			return new DamageEvent
			       {
					       Timestamp = Timestamp,
					       Opponent = Opponent,
					       BaseDamage = BaseDamage,
					       Absorbed = Absorbed,
					       IsDealt = IsDealt,
					       CritDamage = critDamage,
					       SpellName = SpellName,
					       IsWeaponAttack = IsWeaponAttack,
					       StyleName = StyleName
			       };
		}
	}
}
