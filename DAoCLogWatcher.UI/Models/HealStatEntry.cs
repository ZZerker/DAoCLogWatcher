namespace DAoCLogWatcher.UI.Models;

public sealed class HealStatEntry
{
    public required string Name { get; init; }
    public required int Total { get; init; }
    public required double Percentage { get; init; }
}
