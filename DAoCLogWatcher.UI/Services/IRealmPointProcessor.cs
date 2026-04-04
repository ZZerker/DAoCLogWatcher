using System;
using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Models;

namespace DAoCLogWatcher.UI.Services;

public interface IRealmPointProcessor
{
	event EventHandler<RealmPointLogEntry>? EntryProcessed;

	event EventHandler<RealmPointLogEntry>? MultiKillDetected;

	string? DetectedCharacterName { get; }

	int Kills { get; }

	int Deaths { get; }

	void Process(LogLine logLine, DateTime? sessionStartTime, out bool characterChanged, out bool killStatsChanged);

	void Reset();
}
