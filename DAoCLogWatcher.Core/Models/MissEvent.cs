namespace DAoCLogWatcher.Core.Models;

public sealed record MissEvent
{
	public required TimeOnly Timestamp { get; init; }

	/// <summary>True = spell resisted by target; false = melee miss or block.</summary>
	public required bool IsSpell { get; init; }

	/// <summary>Target name when available (block/resist lines); null for plain miss.</summary>
	public string? Target { private get; init; }
}
