namespace DAoCLogWatcher.Core.Models;

public sealed record LogLine
{
    public required string Text { get; init; }
    public RealmPointEntry? RealmPointEntry { get; init; }
}
