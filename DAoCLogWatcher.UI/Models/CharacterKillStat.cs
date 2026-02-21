using CommunityToolkit.Mvvm.ComponentModel;

namespace DAoCLogWatcher.UI.Models;

public partial class CharacterKillStat : ObservableObject
{
    [ObservableProperty]
    private int killCount;

    public required string Name { get; init; }
}
