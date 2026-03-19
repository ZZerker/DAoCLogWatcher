using System;
using System.Collections.Generic;
using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Models;

namespace DAoCLogWatcher.UI.Services;

public sealed class CombatProcessor(CombatSummary summary)
{
	public event EventHandler<CombatLogEntry>? DamageLogged;
	public event EventHandler<HealLogEntry>? HealLogged;
	public event EventHandler<CombatLogEntry>? MultiHitDetected;

	private string? multiHitSpell;
	private TimeOnly multiHitWindowStart;
	private TimeOnly multiHitWindowLastHit;
	private readonly HashSet<string> multiHitTargets = new();
	private int multiHitTotalDamage;
	// Sliding window: closes when no new hit of the same spell arrives within 4 seconds
	// of the previous hit. Using the last hit (not the window start) avoids premature
	// closure when an older cast's straggler arrives shortly before the current cast's hits,
	// and handles out-of-order timestamps (game writes player hits before mob hits for the
	// same cast, producing timestamps 1–2s apart in reverse order).
	private const double MULTI_HIT_WINDOW_SECONDS = 4.0;
	private const int MULTI_HIT_THRESHOLD = 5;

	public void Process(LogLine logLine)
	{
		if(logLine is DamageLogLine { Event: var damage })
			this.ProcessDamage(damage);
		else if(logLine is HealLogLine { Event: var heal })
			this.ProcessHeal(heal);
		else if(logLine is MissLogLine { Event: var miss })
			this.ProcessMiss(miss);
	}

	public void Reset()
	{
		summary.Reset();
		this.multiHitSpell = null;
		this.multiHitTargets.Clear();
		this.multiHitTotalDamage = 0;
	}

	private void ProcessDamage(DamageEvent damage)
	{
		if(damage.IsDealt)
		{
			summary.TotalDamageDealt += damage.TotalDamage;
			summary.HitCount++;
			summary.TotalAbsorbed += damage.Absorbed;
			if(damage.CritDamage > 0)
				summary.CritCount++;

			summary.DamageByTarget.Accumulate(damage.Target, damage.TotalDamage);

			if(damage.IsWeaponAttack)
				summary.MeleeHitCount++;
			else if(damage.SpellName != null)
				summary.SpellHitCount++;

			var spellKey = damage.SpellName ?? (damage.IsWeaponAttack ? "Melee" : "Other");
			summary.DamageBySpell.TryGetValue(spellKey, out var existingSpell);
			summary.DamageBySpell[spellKey] = (existingSpell.TotalDamage + damage.TotalDamage, existingSpell.HitCount + 1);
		}
		else
		{
			summary.TotalDamageTaken += damage.TotalDamage;
			summary.DamageTakenByAttacker.Accumulate(damage.Target, damage.TotalDamage);
		}

		// Finalize any previous AoE window before logging the new hit, so the aggregate
		// entry appears immediately after the last AoE hit rather than after this one.
		if(damage.IsDealt && damage.SpellName != null)
			this.TrackMultiHit(damage);

		this.DamageLogged?.Invoke(this, new CombatLogEntry
		{
			Timestamp     = damage.Timestamp.ToString("HH:mm:ss"),
			Target        = damage.Target,
			TotalDamage   = damage.TotalDamage,
			IsDealt       = damage.IsDealt,
			IsCrit        = damage.CritDamage > 0,
			SpellName     = damage.SpellName,
			IsWeaponAttack = damage.IsWeaponAttack,
		});
	}

	private void ProcessHeal(HealEvent heal)
	{
		if(heal.IsOutgoing)
		{
			summary.TotalHealingDone += heal.HitPoints;
			summary.HealsByTarget.Accumulate(heal.Target ?? "Unknown", heal.HitPoints);
		}
		else
		{
			summary.TotalHealsReceived += heal.HitPoints;
			summary.HealsByHealer.Accumulate(heal.Healer ?? "Unknown", heal.HitPoints);
		}

		this.HealLogged?.Invoke(this, new HealLogEntry
		{
			Timestamp  = heal.Timestamp.ToString("HH:mm:ss"),
			HitPoints  = heal.HitPoints,
			IsOutgoing = heal.IsOutgoing,
			Who        = heal.IsOutgoing ? heal.Target : heal.Healer,
		});
	}

	private void TrackMultiHit(DamageEvent damage)
	{
		var spell = damage.SpellName!;

		if(this.multiHitSpell != null)
		{
			var diffSec = Math.Abs((damage.Timestamp.ToTimeSpan() - this.multiHitWindowLastHit.ToTimeSpan()).TotalSeconds);
			if(diffSec > 43200) diffSec = 86400 - diffSec;
			if(this.multiHitSpell != spell || diffSec >= MULTI_HIT_WINDOW_SECONDS)
				this.FinalizeHitWindow();
		}

		if(this.multiHitSpell == null)
		{
			this.multiHitSpell = spell;
			this.multiHitWindowStart = damage.Timestamp;
		}

		this.multiHitWindowLastHit = damage.Timestamp;
		this.multiHitTargets.Add(damage.Target);
		this.multiHitTotalDamage += damage.TotalDamage;
	}

	private void FinalizeHitWindow()
	{
		if(this.multiHitTargets.Count >= MULTI_HIT_THRESHOLD && this.multiHitSpell != null)
		{
			this.MultiHitDetected?.Invoke(this, new CombatLogEntry
			{
				Timestamp      = this.multiHitWindowStart.ToString("HH:mm:ss"),
				Target         = $"{this.multiHitTargets.Count} targets",
				TotalDamage    = this.multiHitTotalDamage,
				IsDealt        = true,
				IsCrit         = false,
				SpellName      = this.multiHitSpell,
				IsWeaponAttack = false,
				IsMultiHit     = true,
				HitCount       = this.multiHitTargets.Count,
			});
		}

		this.multiHitSpell = null;
		this.multiHitTargets.Clear();
		this.multiHitTotalDamage = 0;
	}

	private void ProcessMiss(MissEvent miss)
	{
		if(miss.IsSpell)
			summary.SpellResistCount++;
		else
			summary.MeleeMissCount++;
	}
}

file static class DictionaryExtensions
{
	public static void Accumulate<TKey>(this Dictionary<TKey, int> dict, TKey key, int value) where TKey : notnull
	{
		dict.TryGetValue(key, out var existing);
		dict[key] = existing + value;
	}
}
