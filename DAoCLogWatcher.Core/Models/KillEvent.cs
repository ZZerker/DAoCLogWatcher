namespace DAoCLogWatcher.Core.Models;

public sealed record KillEvent
{
	public required TimeOnly Timestamp { get; init; }

	public required string Victim { get; init; }

	public required string Killer { get; init; }

	public required string Zone { get; init; }
}
