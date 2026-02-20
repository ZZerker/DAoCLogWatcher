namespace DAoCLogWatcher.UI.Models;

public class RealmPointLogEntry
{
    public required string Timestamp { get; init; }
    public required int Points { get; init; }
    public required string Source { get; init; }
    public required string Details { get; init; }
}
