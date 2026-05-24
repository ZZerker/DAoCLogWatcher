namespace DAoCLogWatcher.Core.Models;

public sealed record KillEvent
{
	public required TimeOnly Timestamp { get; init; }

	public required string Victim { get; init; }

	public required string Killer { get; init; }

	public required string Zone { get; init; }

	/// <summary>True when the victim is a known keep/frontier NPC rather than a player.</summary>
	public bool IsNpc { get; init; }
}
