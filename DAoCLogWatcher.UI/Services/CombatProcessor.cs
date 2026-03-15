using System;
using System.Collections.Generic;
using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Models;

namespace DAoCLogWatcher.UI.Services;

public sealed class CombatProcessor(CombatSummary summary)
{
	public event EventHandler<CombatLogEntry>? DamageLogged;
	public event EventHandler<HealLogEntry>? HealLogged;

	public void Process(LogLine logLine)
	{
		if(logLine is DamageLogLine { Event: var damage })
			this.ProcessDamage(damage);
		else if(logLine is HealLogLine { Event: var heal })
			this.ProcessHeal(heal);
		else if(logLine is MissLogLine { Event: var miss })
			this.ProcessMiss(miss);
	}

	public void Reset() => summary.Reset();

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
