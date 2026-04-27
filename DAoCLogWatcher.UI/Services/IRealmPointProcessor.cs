using System;
using System.Collections.Generic;
using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Models;

namespace DAoCLogWatcher.UI.Services;

public interface IRealmPointProcessor
{
	event EventHandler<RealmPointLogEntry>? EntryProcessed;

	event EventHandler<RealmPointLogEntry>? MultiKillDetected;

	event EventHandler? ZoneKillsUpdated;

	IReadOnlyDictionary<string, int> CurrentZoneKills { get; }

	string? DetectedCharacterName { get; }

	int Kills { get; }

	int Deaths { get; }

	void Process(LogLine logLine, DateTime? sessionStartTime, out bool characterChanged, out bool killStatsChanged);

	void SetZoneKillWindow(TimeSpan window);

	void RefreshZoneKills();

	IReadOnlyList<KillActivityPoint> KillActivityPoints { get; }

	IReadOnlyList<KillActivityPoint> GetSessionActivityPoints(DateTime sessionStart, DateTime now);

	void Reset();
}
