using System;
using System.Collections.Generic;
using DAoCLogWatcher.Core;
using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Models;

namespace DAoCLogWatcher.UI.Services;

public sealed class CombatProcessor(CombatSummary summary): ICombatProcessor
{
	public event EventHandler<CombatLogEntry>? DamageLogged;

	public event EventHandler<HealLogEntry>? HealLogged;

	public event EventHandler<CombatLogEntry>? MultiHitDetected;

	private string? multiHitSpell;
	private TimeOnly multiHitWindowStart;
	private TimeOnly multiHitWindowLastHit;
	private readonly HashSet<string> multiHitTargets = new();
	private int multiHitTotalDamage;

	private string? dotStackSpell;
	private TimeOnly dotStackWindowLastTick;
	private readonly Dictionary<string, CombatLogEntry> dotStackEntries = new();

	// Sliding window: closes when no new hit of the same spell arrives within 4 seconds
	// of the previous hit. Using the last hit (not the window start) avoids premature
	// closure when an older cast's straggler arrives shortly before the current cast's hits,
	// and handles out-of-order timestamps (game writes player hits before mob hits for the
	// same cast, producing timestamps 1–2s apart in reverse order).
	private const double MULTI_HIT_WINDOW_SECONDS = 4.0;
	private const int MULTI_HIT_THRESHOLD = 5;
	private const double DOT_STACK_WINDOW_SECONDS = 15.0;

	public void Process(LogLine logLine)
	{
		// Flush time-based windows (AoE multi-hit and DoT stacks) when a new timestamped
		// event arrives far enough after the last window hit.
		var currentTs = logLine switch
		{
				DamageLogLine d => (TimeOnly?)d.Event.Timestamp,
				HealLogLine h => h.Event.Timestamp,
				MissLogLine m => m.Event.Timestamp,
				_ => null
		};
		if(currentTs.HasValue)
		{
			if(this.multiHitSpell != null)
			{
				var diffSec = TimeHelper.ShortestArcSeconds(currentTs.Value, this.multiHitWindowLastHit);
				if(diffSec >= MULTI_HIT_WINDOW_SECONDS)
				{
					this.FinalizeHitWindow();
				}
			}

			if(this.dotStackSpell != null)
			{
				var diffSec = TimeHelper.ShortestArcSeconds(currentTs.Value, this.dotStackWindowLastTick);
				if(diffSec >= DOT_STACK_WINDOW_SECONDS)
				{
					this.CloseDotStack();
				}
			}
		}

		if(logLine is DamageLogLine { Event: var damage })
		{
			this.ProcessDamage(damage);
		}
		else if(logLine is HealLogLine { Event: var heal })
		{
			this.ProcessHeal(heal);
		}
		else if(logLine is MissLogLine { Event: var miss })
		{
			this.ProcessMiss(miss);
		}
	}

	public void Reset()
	{
		summary.Reset();
		this.multiHitSpell = null;
		this.multiHitWindowStart = default;
		this.multiHitWindowLastHit = default;
		this.multiHitTargets.Clear();
		this.multiHitTotalDamage = 0;
		this.dotStackSpell = null;
		this.dotStackWindowLastTick = default;
		this.dotStackEntries.Clear();
	}

	private void ProcessDamage(DamageEvent damage)
	{
		if(damage.IsDealt)
		{
			summary.TotalDamageDealt += damage.TotalDamage;
			summary.HitCount++;
			summary.TotalAbsorbed += damage.Absorbed;
			if(damage.CritDamage > 0)
			{
				summary.CritCount++;
			}

			summary.DamageByTarget.Accumulate(damage.Opponent, damage.TotalDamage);

			if(damage.IsWeaponAttack)
			{
				summary.MeleeHitCount++;
			}
			else if(damage.SpellName != null)
			{
				summary.SpellHitCount++;
			}

			var spellKey = damage.IsWeaponAttack?damage.StyleName ?? "Melee":damage.SpellName ?? "Other";
			summary.DamageBySpell.TryGetValue(spellKey, out var existingSpell);
			summary.DamageBySpell[spellKey] = (existingSpell.TotalDamage + damage.TotalDamage, existingSpell.HitCount + 1);
		}
		else
		{
			summary.TotalDamageTaken += damage.TotalDamage;
			summary.DamageTakenByAttacker.Accumulate(damage.Opponent, damage.TotalDamage);
		}

		// Finalize any previous AoE window before logging the new hit, so the aggregate
		// entry appears immediately after the last AoE hit rather than after this one.
		// Weapon attacks carry the weapon name as SpellName but are single-target by nature;
		// including them would interrupt or pollute spell AoE windows mid-sequence.
		if(damage.IsDealt&&damage.SpellName != null&&!damage.IsWeaponAttack&&!damage.IsDotTick)
		{
			this.TrackMultiHit(damage);
		}

		if(damage.IsDotTick&&damage.IsDealt)
		{
			this.TrackDotStack(damage);
		}
		else
		{
			this.DamageLogged?.Invoke(this,
			                          new CombatLogEntry
			                          {
					                          Timestamp = damage.Timestamp.ToString("HH:mm:ss"),
					                          Opponent = damage.Opponent,
					                          TotalDamage = damage.TotalDamage,
					                          IsDealt = damage.IsDealt,
					                          IsCrit = damage.CritDamage > 0,
					                          SpellName = damage.SpellName,
					                          StyleName = damage.StyleName,
					                          IsWeaponAttack = damage.IsWeaponAttack,
					                          IsDotTick = damage.IsDotTick
			                          });
		}
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

		this.HealLogged?.Invoke(this,
		                        new HealLogEntry
		                        {
				                        Timestamp = heal.Timestamp.ToString("HH:mm:ss"),
				                        HitPoints = heal.HitPoints,
				                        IsOutgoing = heal.IsOutgoing,
				                        Who = heal.IsOutgoing?heal.Target:heal.Healer
		                        });
	}

	private void TrackMultiHit(DamageEvent damage)
	{
		var spell = damage.SpellName!;

		if(this.multiHitSpell != null)
		{
			var diffSec = TimeHelper.ShortestArcSeconds(damage.Timestamp, this.multiHitWindowLastHit);
			if(this.multiHitSpell != spell||diffSec >= MULTI_HIT_WINDOW_SECONDS)
			{
				this.FinalizeHitWindow();
			}
		}

		if(this.multiHitSpell == null)
		{
			this.multiHitSpell = spell;
			this.multiHitWindowStart = damage.Timestamp;
		}

		this.multiHitWindowLastHit = damage.Timestamp;
		this.multiHitTargets.Add(damage.Opponent);
		this.multiHitTotalDamage += damage.TotalDamage;
	}

	private void FinalizeHitWindow()
	{
		if(this.multiHitTargets.Count >= MULTI_HIT_THRESHOLD&&this.multiHitSpell != null)
		{
			this.MultiHitDetected?.Invoke(this,
			                              new CombatLogEntry
			                              {
					                              Timestamp = this.multiHitWindowStart.ToString("HH:mm:ss"),
					                              Opponent = $"{this.multiHitTargets.Count} targets",
					                              TotalDamage = this.multiHitTotalDamage,
					                              IsDealt = true,
					                              IsCrit = false,
					                              SpellName = this.multiHitSpell,
					                              IsWeaponAttack = false,
					                              IsMultiHit = true,
					                              HitCount = this.multiHitTargets.Count
			                              });
		}

		this.multiHitSpell = null;
		this.multiHitTargets.Clear();
		this.multiHitTotalDamage = 0;
	}

	private void TrackDotStack(DamageEvent damage)
	{
		var spell = damage.SpellName!;
		var target = damage.Opponent;

		if(this.dotStackSpell != null&&this.dotStackSpell != spell)
		{
			this.CloseDotStack();
		}

		if(this.dotStackSpell == null)
		{
			this.dotStackSpell = spell;
		}

		if(!this.dotStackEntries.TryGetValue(target, out var entry))
		{
			entry = new CombatLogEntry
			{
				Timestamp = damage.Timestamp.ToString("HH:mm:ss"),
				Opponent = target,
				TotalDamage = damage.TotalDamage,
				IsDealt = true,
				IsCrit = damage.CritDamage > 0,
				SpellName = spell,
				IsWeaponAttack = false,
				IsDotTick = true,
				HitCount = 1
			};
			this.dotStackEntries[target] = entry;
			this.DamageLogged?.Invoke(this, entry);
		}
		else
		{
			entry.TotalDamage += damage.TotalDamage;
			entry.HitCount++;
			if(damage.CritDamage > 0)
			{
				entry.IsCrit = true;
			}
		}

		this.dotStackWindowLastTick = damage.Timestamp;
	}

	private void CloseDotStack()
	{
		this.dotStackSpell = null;
		this.dotStackEntries.Clear();
	}

	private void ProcessMiss(MissEvent miss)
	{
		if(miss.IsSpell)
		{
			summary.SpellResistCount++;
		}
		else
		{
			summary.MeleeMissCount++;
		}
	}
}

file static class DictionaryExtensions
{
	public static void Accumulate<TKEY>(this Dictionary<TKEY, int> dict, TKEY key, int value)
			where TKEY: notnull
	{
		dict.TryGetValue(key, out var existing);
		dict[key] = existing + value;
	}
}
