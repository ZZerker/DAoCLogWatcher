using System;

namespace DAoCLogWatcher.UI.Models;

public sealed record SessionRecord
{
	public required DateTime StartTime { get; init; }
	public DateTime? EndTime { get; init; }
	public DateTime LastUpdated { get; init; }
	public string? CharacterName { get; init; }
	public long RealmPoints { get; init; }
	public double RpPerHour { get; init; }
	public int Kills { get; init; }
	public int Deaths { get; init; }
	public int BestMultiKill { get; init; }
	public string? TopZone { get; init; }
	public long DamageDone { get; init; }
	public long HealingDone { get; init; }
	public int SchemaVersion { get; init; } = 1;
}
