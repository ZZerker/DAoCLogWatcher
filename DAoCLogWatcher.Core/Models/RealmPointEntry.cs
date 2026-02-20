namespace DAoCLogWatcher.Core.Models;

public sealed record RealmPointEntry
{
    public required TimeOnly Timestamp { get; init; }
    public required int Points { get; init; }
    public required RealmPointSource Source { get; init; }
    public string? PlayerName { get; init; }
    public required string RawLine { get; init; }
}
