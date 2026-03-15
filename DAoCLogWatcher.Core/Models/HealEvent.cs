namespace DAoCLogWatcher.Core.Models;

public sealed record HealEvent
{
	public required TimeOnly Timestamp { get; init; }
	public required int HitPoints { get; init; }

	/// <summary>True when you cast the heal; false when someone healed you.</summary>
	public bool IsOutgoing { get; init; }

	/// <summary>Who healed you (incoming heals only).</summary>
	public string? Healer { get; init; }

	/// <summary>Who you healed; "yourself" for self-heals (outgoing heals only).</summary>
	public string? Target { get; init; }
}
