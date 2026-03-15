using CommunityToolkit.Mvvm.ComponentModel;

namespace DAoCLogWatcher.UI.Models;

public partial class RealmPointLogEntry : ObservableObject
{
    public required string Timestamp { get; init; }
    public required int Points { get; init; }
    public required string Source { get; init; }
    public required string Details { get; init; }
    [ObservableProperty] private bool isMultiKill;
}
