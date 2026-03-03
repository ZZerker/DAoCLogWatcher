using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Models;

namespace DAoCLogWatcher.UI.Services;

public sealed class CombatProcessor(CombatSummary summary)
{
	public void Process(LogLine logLine)
	{
		if(logLine is DamageLogLine { Event: var damage })
			this.ProcessDamage(damage);
		else if(logLine is HealLogLine { Event: var heal })
			this.ProcessHeal(heal);
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

			summary.DamageByTarget.TryGetValue(damage.Target, out var existing);
			summary.DamageByTarget[damage.Target] = existing + damage.TotalDamage;

			var spellKey = damage.SpellName ?? "Melee";
			summary.DamageBySpell.TryGetValue(spellKey, out var existingSpell);
			summary.DamageBySpell[spellKey] = (existingSpell.TotalDamage + damage.TotalDamage, existingSpell.HitCount + 1);
		}
		else
		{
			summary.TotalDamageTaken += damage.TotalDamage;

			summary.DamageTakenByAttacker.TryGetValue(damage.Target, out var existingAttacker);
			summary.DamageTakenByAttacker[damage.Target] = existingAttacker + damage.TotalDamage;
		}
	}

	private void ProcessHeal(HealEvent heal)
	{
		summary.TotalHealsReceived += heal.HitPoints;

		summary.HealsByHealer.TryGetValue(heal.Healer, out var existing);
		summary.HealsByHealer[heal.Healer] = existing + heal.HitPoints;
	}
}
