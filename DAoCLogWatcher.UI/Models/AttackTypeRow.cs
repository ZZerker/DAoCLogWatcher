namespace DAoCLogWatcher.UI.Models;

public sealed class AttackTypeRow
{
	public required string Name { get; init; }

	public required int AvgDamage { get; init; }

	public required int HitCount { get; init; }

	public required int CritCount { get; init; }

	public required double Percentage { get; init; }

	public double CritRate => this.HitCount == 0?0.0:(double)this.CritCount / this.HitCount * 100.0;
}
