using System;
using System.Collections.Generic;
using Avalonia.Threading;
using DAoCLogWatcher.Core;
using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Models;

namespace DAoCLogWatcher.UI.Services;

public sealed class CombatProcessor(CombatSummary summary, SpellRegistry? dotRegistry = null): ICombatProcessor
{
	private readonly SpellRegistry registry = dotRegistry ?? SpellRegistry.Empty;
	public event EventHandler<CombatLogEntry>? DamageLogged;

	public event EventHandler<HealLogEntry>? HealLogged;

	public event EventHandler<CombatLogEntry>? MultiHitDetected;

	private string? multiHitSpell;
	private bool multiHitIsKnownAoeNuke;
	private TimeOnly multiHitWindowStart;
	private TimeOnly multiHitWindowLastHit;
	private readonly HashSet<string> multiHitTargets = new();
	private int multiHitTotalDamage;

	private string? dotStackSpell;
	private double dotStackWindowSeconds;
	private TimeOnly dotStackWindowLastTick;
	private readonly Dictionary<string, CombatLogEntry> dotStackEntries = new();
	private readonly Dictionary<string, CombatLogEntry> lastDealtByOpponent = new();

	// Sliding window: closes when no new hit of the same spell arrives within 4 seconds
	// of the previous hit. Using the last hit (not the window start) avoids premature
	// closure when an older cast's straggler arrives shortly before the current cast's hits,
	// and handles out-of-order timestamps (game writes player hits before mob hits for the
	// same cast, producing timestamps 1–2s apart in reverse order).
	private const double MULTI_HIT_WINDOW_SECONDS = 4.0;
	private const int MULTI_HIT_THRESHOLD = 5;
	private const double DOT_STACK_FALLBACK_SECONDS = 15.0;
	private const double DOT_STACK_SLACK_SECONDS = 1.0;

	public void Process(LogLine logLine)
	{
		// Flush time-based windows (AoE multi-hit and DoT stacks) when a new timestamped
		// event arrives far enough after the last window hit.
		var currentTs = logLine.Timestamp;
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
				if(diffSec >= this.dotStackWindowSeconds)
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
		else if(logLine is KillLogLine { Event: var kill })
		{
			if(this.lastDealtByOpponent.Remove(kill.Victim, out var entry))
			{
				entry.IsKillingBlow = true;
			}
		}
	}

	public void Reset()
	{
		summary.Reset();
		this.multiHitSpell = null;
		this.multiHitIsKnownAoeNuke = false;
		this.multiHitWindowStart = default;
		this.multiHitWindowLastHit = default;
		this.multiHitTargets.Clear();
		this.multiHitTotalDamage = 0;
		this.dotStackSpell = null;
		this.dotStackWindowSeconds = 0;
		this.dotStackWindowLastTick = default;
		this.dotStackEntries.Clear();
		this.lastDealtByOpponent.Clear();
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
			summary.DamageBySpell[spellKey] = (existingSpell.TotalDamage + damage.TotalDamage, existingSpell.HitCount + 1, existingSpell.CritCount + (damage.CritDamage > 0 ? 1 : 0));
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
			var entry = new CombatLogEntry
			            {
					            Timestamp = damage.Timestamp.ToString("HH:mm:ss"),
					            Opponent = damage.Opponent,
					            TotalDamage = damage.TotalDamage,
					            IsDealt = damage.IsDealt,
					            IsCrit = damage.CritDamage > 0,
					            SpellName = damage.SpellName,
					            StyleName = damage.StyleName,
					            IsWeaponAttack = damage.IsWeaponAttack,
					            IsDotTick = damage.IsDotTick,
					            IsPlayer = !NpcFilter.IsNpc(damage.Opponent)
			            };
			if(damage.IsDealt)
			{
				this.lastDealtByOpponent[damage.Opponent] = entry;
			}

			this.DamageLogged?.Invoke(this, entry);
		}
	}

	private void ProcessHeal(HealEvent heal)
	{
		if(heal.IsOutgoing)
		{
			summary.TotalHealingDone += heal.TotalHitPoints;
			summary.HealDoneCount++;
			summary.HealsByTarget.Accumulate(heal.Target ?? "Unknown", heal.TotalHitPoints);
			summary.UniqueHealTargetCount = summary.HealsByTarget.Count;
		}
		else
		{
			summary.TotalHealsReceived += heal.TotalHitPoints;
			summary.HealsByHealer.Accumulate(heal.Healer ?? "Unknown", heal.TotalHitPoints);
		}

		if(heal.CritHitPoints > 0)
		{
			summary.HealCritCount++;
		}

		this.HealLogged?.Invoke(this,
		                        new HealLogEntry
		                        {
				                        Timestamp = heal.Timestamp.ToString("HH:mm:ss"),
				                        HitPoints = heal.HitPoints,
				                        CritHitPoints = heal.CritHitPoints,
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
			this.multiHitIsKnownAoeNuke = this.registry.IsKnownAoeNuke(spell);
			this.multiHitWindowStart = damage.Timestamp;
		}

		this.multiHitWindowLastHit = damage.Timestamp;
		this.multiHitTargets.Add(damage.Opponent);
		this.multiHitTotalDamage += damage.TotalDamage;
	}

	private void FinalizeHitWindow()
	{
		var threshold = this.multiHitIsKnownAoeNuke ? 2 : MULTI_HIT_THRESHOLD;
		if(this.multiHitTargets.Count >= threshold)
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
			this.dotStackWindowSeconds = this.DotWindowFor(spell);
		}

		if(!this.dotStackEntries.TryGetValue(target, out var entry))
		{
			var isAoe = this.registry.TryGet(spell, out var info) && info.IsAoe;
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
				IsAoe = isAoe,
				HitCount = 1
			};
			this.dotStackEntries[target] = entry;
			this.lastDealtByOpponent[target] = entry;
			this.DamageLogged?.Invoke(this, entry);
		}
		else
		{
			var totalDamage = damage.TotalDamage;
			var isCrit = damage.CritDamage > 0;
			Dispatcher.UIThread.Post(() =>
			{
				entry.TotalDamage += totalDamage;
				entry.HitCount++;
				if(isCrit)
				{
					entry.IsCrit = true;
				}
			}, DispatcherPriority.Background);
		}

		this.dotStackWindowLastTick = damage.Timestamp;
	}

	private void CloseDotStack()
	{
		foreach(var key in this.dotStackEntries.Keys)
		{
			this.lastDealtByOpponent.Remove(key);
		}

		this.dotStackSpell = null;
		this.dotStackEntries.Clear();
	}

	private double DotWindowFor(string spell)
	{
		if(!this.registry.TryGet(spell, out var info) || info.DurationSeconds <= 0)
		{
			return DOT_STACK_FALLBACK_SECONDS;
		}

		var tick = info.FrequencySeconds > 0 ? info.FrequencySeconds : 2;
		return info.DurationSeconds + tick + DOT_STACK_SLACK_SECONDS;
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
