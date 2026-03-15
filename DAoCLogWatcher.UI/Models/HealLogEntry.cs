namespace DAoCLogWatcher.UI.Models;

public class HealLogEntry
{
	public required string Timestamp { get; init; }
	public required int HitPoints { get; init; }
	public required bool IsOutgoing { get; init; }

	/// <summary>Healer name (incoming) or target name (outgoing).</summary>
	public string? Who { get; init; }

	public string DirectionLabel => IsOutgoing ? "Done" : "Received";
}
