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
		chart.Add(T0, 500, 500);

		chart.CumulativeDataPoints.Should().HaveCount(1);
		chart.CumulativeDataPoints[0].Rps.Should().Be(500);
		chart.HourlyDataPoints.Should().HaveCount(1);
	}

	[Fact]
	public void Add_MultipleEntries_CumulativeRpsIsPassedInValue()
	{
		chart.Add(T0, 100, 100);
		chart.Add(T0.AddMinutes(1), 250, 150);
		chart.Add(T0.AddMinutes(2), 400, 150);

		chart.CumulativeDataPoints.Should().HaveCount(3);
		chart.CumulativeDataPoints[2].Rps.Should().Be(400);
	}

	[Fact]
	public void Add_EntryOlderThanOneHour_IsEvictedFromRollingWindow()
	{
		// First entry is over 1 hour before the second — should be evicted
		var tOld = T0.AddHours(-1).AddMinutes(-1);
		chart.Add(tOld, 100, 100);
		chart.Add(T0, 200, 100);

		// After eviction the rolling window only contains the second entry
		// (100 points in a window that is effectively only the minimum window)
		chart.HourlyDataPoints[1].RpsPerHour.Should().BeGreaterThan(0);
		chart.HourlyDataPoints[1].RpsPerHour.Should().BeLessThan(chart.HourlyDataPoints[0].RpsPerHour + 1, "evicted entry no longer contributes to hourly rate");
	}

	[Fact]
	public void Add_FiresUpdateRequested()
	{
		var fired = 0;
		chart.UpdateRequested += (_, _) => fired++;

		chart.Add(T0, 100, 100);

		fired.Should().Be(1);
	}

	[Fact]
	public void Reset_ClearsAllDataAndFiresUpdateRequested()
	{
		chart.Add(T0, 100, 100);
		chart.Add(T0.AddMinutes(1), 200, 100);

		var fired = 0;
		chart.UpdateRequested += (_, _) => fired++;
		chart.Reset();

		chart.CumulativeDataPoints.Should().BeEmpty();
		chart.HourlyDataPoints.Should().BeEmpty();
		fired.Should().Be(1, "Reset fires UpdateRequested once");

		// Adding after reset should work normally
		chart.Add(T0.AddHours(1), 50, 50);
		chart.CumulativeDataPoints.Should().HaveCount(1);
	}
}
