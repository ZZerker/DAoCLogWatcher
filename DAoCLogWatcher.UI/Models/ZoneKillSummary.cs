namespace DAoCLogWatcher.UI.Models;

public sealed class ZoneKillSummary
{
	public required string Zone { get; init; }

	public int KillCount { get; init; }

	public double Percentage { get; init; }

	public required string HeatColor { get; init; }
}
