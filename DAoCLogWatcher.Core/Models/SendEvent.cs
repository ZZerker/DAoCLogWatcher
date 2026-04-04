namespace DAoCLogWatcher.Core.Models;

/// <summary>A chat send message: "@@Dosenklopper sends, \"lappen\""</summary>
public sealed record SendEvent
{
	public required TimeOnly Timestamp { get; init; }

	public required string Sender { get; init; }

	public required string Message { get; init; }
}
