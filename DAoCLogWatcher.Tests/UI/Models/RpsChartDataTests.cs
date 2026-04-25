using DAoCLogWatcher.UI.Models;
using FluentAssertions;

namespace DAoCLogWatcher.Tests.UI.Models;

public sealed class RpsChartDataTests
{
	private readonly RpsChartData chart = new();

	private static readonly DateTime T0 = new(2024, 1, 1, 12, 0, 0);

	[Fact]
	public void Add_SingleEntry_AppearsInBothCollections()
	{
		this.chart.Add(T0, 500, 500);

		this.chart.CumulativeDataPoints.Should().HaveCount(1);
		this.chart.CumulativeDataPoints[0].Rps.Should().Be(500);
		this.chart.HourlyDataPoints.Should().HaveCount(1);
	}

	[Fact]
	public void Add_MultipleEntries_CumulativeRpsIsPassedInValue()
	{
		this.chart.Add(T0, 100, 100);
		this.chart.Add(T0.AddMinutes(1), 250, 150);
		this.chart.Add(T0.AddMinutes(2), 400, 150);

		this.chart.CumulativeDataPoints.Should().HaveCount(3);
		this.chart.CumulativeDataPoints[2].Rps.Should().Be(400);
	}

	[Fact]
	public void Add_EntryOlderThanOneHour_IsEvictedFromRollingWindow()
	{
		// First entry is over 1 hour before the second — should be evicted
		var tOld = T0.AddHours(-1).AddMinutes(-1);
		this.chart.Add(tOld, 100, 100);
		this.chart.Add(T0, 200, 100);

		// After eviction the rolling window only contains the second entry
		// (100 points in a window that is effectively only the minimum window)
		this.chart.HourlyDataPoints[1].RpsPerHour.Should().BeGreaterThan(0);
		this.chart.HourlyDataPoints[1].RpsPerHour.Should().BeLessThan(this.chart.HourlyDataPoints[0].RpsPerHour + 1, "evicted entry no longer contributes to hourly rate");
	}

	[Fact]
	public void Add_FiresUpdateRequested()
	{
		var fired = 0;
		this.chart.UpdateRequested += (_, _) => fired++;

		this.chart.Add(T0, 100, 100);

		fired.Should().Be(1);
	}

	[Fact]
	public void Reset_ClearsAllDataAndFiresUpdateRequested()
	{
		this.chart.Add(T0, 100, 100);
		this.chart.Add(T0.AddMinutes(1), 200, 100);

		var fired = 0;
		this.chart.UpdateRequested += (_, _) => fired++;
		this.chart.Reset();

		this.chart.CumulativeDataPoints.Should().BeEmpty();
		this.chart.HourlyDataPoints.Should().BeEmpty();
		fired.Should().Be(1, "Reset fires UpdateRequested once");

		// Adding after reset should work normally
		this.chart.Add(T0.AddHours(1), 50, 50);
		this.chart.CumulativeDataPoints.Should().HaveCount(1);
	}
}
