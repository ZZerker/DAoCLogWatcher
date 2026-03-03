namespace DAoCLogWatcher.Core.Models;

public sealed record HealEvent
{
	public required TimeOnly Timestamp { get; init; }
	public required string Healer { get; init; }
	public required int HitPoints { get; init; }
}
