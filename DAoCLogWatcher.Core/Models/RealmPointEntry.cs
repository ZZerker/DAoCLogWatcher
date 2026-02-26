namespace DAoCLogWatcher.Core.Models;

public sealed record RealmPointEntry
{
    public required TimeOnly Timestamp { get; init; }
    public required int Points { get; init; }
    public required RealmPointSource Source { get; init; }
    public required string RawLine { get; init; }
    /// <summary>The specific sub-reason extracted from the log line, e.g. "Tier 2 Participation" or "Win Streak".</summary>
    public string? SubSource { get; init; }
}
