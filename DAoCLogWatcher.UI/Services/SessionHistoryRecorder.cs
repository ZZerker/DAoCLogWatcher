using System;
using System.Linq;
using DAoCLogWatcher.UI.Models;

namespace DAoCLogWatcher.UI.Services;

public sealed class SessionHistoryRecorder
{
	private readonly IRealmPointProcessor processor;
	private readonly RealmPointSummary summary;
	private readonly CombatSummary combatSummary;

	public SessionHistoryRecorder(IRealmPointProcessor processor, RealmPointSummary summary, CombatSummary combatSummary)
	{
		this.processor = processor;
		this.summary = summary;
		this.combatSummary = combatSummary;
	}

	public SessionRecord BuildRecord(DateTime startTime, string? characterName, DateTime? endTime, int bestMultiKill)
	{
		var topZone = this.processor.CurrentZoneKills.Count == 0
				? null
				: this.processor.CurrentZoneKills.OrderByDescending(kv => kv.Value).First().Key;

		return new SessionRecord
		       {
				       StartTime = startTime,
				       EndTime = endTime,
				       CharacterName = characterName,
				       RealmPoints = this.summary.TotalRealmPoints,
				       RpPerHour = this.summary.RpsPerHour,
				       Kills = this.processor.Kills,
				       Deaths = this.processor.Deaths,
				       BestMultiKill = bestMultiKill,
				       TopZone = topZone,
				       DamageDone = this.combatSummary.TotalDamageDealt,
				       HealingDone = this.combatSummary.TotalHealingDone
		       };
	}

}
