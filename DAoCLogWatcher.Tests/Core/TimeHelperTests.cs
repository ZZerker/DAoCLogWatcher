using DAoCLogWatcher.Core;
using FluentAssertions;

namespace DAoCLogWatcher.Tests.Core;

public sealed class TimeHelperTests
{
	[Fact]
	public void ShortestArcSeconds_SameTime_ReturnsZero()
	{
		var t = new TimeOnly(12, 0, 0);
		TimeHelper.ShortestArcSeconds(t, t).Should().Be(0);
	}

	[Fact]
	public void ShortestArcSeconds_ForwardDifference_ReturnsAbsSeconds()
	{
		var a = new TimeOnly(12, 0, 30);
		var b = new TimeOnly(12, 0, 0);
		TimeHelper.ShortestArcSeconds(a, b).Should().Be(30);
	}

	[Fact]
	public void ShortestArcSeconds_BackwardDifference_ReturnsAbsSeconds()
	{
		var a = new TimeOnly(12, 0, 0);
		var b = new TimeOnly(12, 0, 30);
		TimeHelper.ShortestArcSeconds(a, b).Should().Be(30);
	}

	[Fact]
	public void ShortestArcSeconds_MidnightCrossing_ReturnsShortArc()
	{
		// 23:59:59 → 00:00:01 is only 2 seconds, not 86 398
		var before = new TimeOnly(23, 59, 59);
		var after = new TimeOnly(0, 0, 1);
		TimeHelper.ShortestArcSeconds(before, after).Should().Be(2);
		TimeHelper.ShortestArcSeconds(after, before).Should().Be(2);
	}

	[Fact]
	public void ShortestArcSeconds_ExactlyHalfDay_ReturnsBoundary()
	{
		var noon = new TimeOnly(12, 0, 0);
		var midnight = new TimeOnly(0, 0, 0);
		TimeHelper.ShortestArcSeconds(noon, midnight).Should().Be(43200);
	}

	[Fact]
	public void ShortestArcSeconds_JustOverHalfDay_WrapsToShortArc()
	{
		// 43 201 s raw diff → wraps to 86 400 - 43 201 = 43 199
		var a = new TimeOnly(12, 0, 1);
		var b = new TimeOnly(0, 0, 0);
		TimeHelper.ShortestArcSeconds(a, b).Should().Be(43199);
	}
}
