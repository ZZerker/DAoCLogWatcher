using System.Windows.Input;

namespace DAoCLogWatcher.UI.Models;

public sealed class RecentSessionEntry
{
	public required string Label { get; init; }

	public required string Sublabel { get; init; }

	public required ICommand OpenCommand { get; init; }
}
