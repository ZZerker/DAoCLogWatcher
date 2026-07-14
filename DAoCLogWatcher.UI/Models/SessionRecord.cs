using System;
using System.Text.Json.Serialization;

namespace DAoCLogWatcher.UI.Models;

public sealed record SessionRecord
{
	public required DateTime StartTime { get; init; }
	public DateTime? EndTime { get; init; }
	public DateTime LastUpdated { get; init; }
	public double DurationSeconds { get; init; }
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

	[JsonIgnore]
	public TimeSpan Duration
	{
		get
		{
			if(this.DurationSeconds > 0)
			{
				return TimeSpan.FromSeconds(this.DurationSeconds);
			}

			// Pre-DurationSeconds records: wall-clock span is unusable (log sessions stay open for
			// days, and a missing LastUpdated defaults to year 1). RpPerHour was RP / hours, so
			// dividing back recovers the real duration.
			if(this.RpPerHour > 0&&this.RealmPoints > 0)
			{
				return TimeSpan.FromHours(this.RealmPoints / this.RpPerHour);
			}

			var span = (this.EndTime ?? this.LastUpdated) - this.StartTime;

			return span > TimeSpan.Zero?span:TimeSpan.Zero;
		}
	}
}
