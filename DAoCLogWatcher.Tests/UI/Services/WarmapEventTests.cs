using System;
using DAoCLogWatcher.UI.Services;
using FluentAssertions;
using Xunit;

namespace DAoCLogWatcher.Tests.UI.Services;

public sealed class WarmapEventTests
{
	private static WarmapEvent MakeEvent(string type = "SupplyDrop", string size = "Small", string state = "active", string source = "ws", long endsAtMs = 0)
	{
		return new WarmapEvent("id-1", type, size, state, source, 5, 100, 100, endsAtMs);
	}

	[Theory]
	[InlineData("GeneratedMissionStaticTreasureHunt", "Treasure Hunt")]
	[InlineData("GeneratedMissionStaticPvpTeleporter", "Pvp Teleporter")]
	[InlineData("SupplyDrop", "Supply Drop")]
	[InlineData("Behemoth", "Behemoth")]
	[InlineData("CampExtinction", "Camp Extinction")]
	public void DisplayName_FormatsTypeString_Expected(string type, string expected)
	{
		var ev = MakeEvent(type: type);

		ev.DisplayName.Should().Be(expected);
	}

	[Fact]
	public void TimeRemaining_NoDeadline_ReturnsNull()
	{
		var ev = MakeEvent(endsAtMs: 0);

		ev.TimeRemaining.Should().BeNull();
	}

	[Fact]
	public void TimeRemaining_DeadlineInPast_ReturnsZero()
	{
		var ev = MakeEvent(endsAtMs: DateTimeOffset.UtcNow.AddSeconds(-30).ToUnixTimeMilliseconds());

		ev.TimeRemaining.Should().Be(TimeSpan.Zero);
	}

	[Fact]
	public void TimeRemaining_DeadlineInFuture_ReturnsApproximateDuration()
	{
		var ev = MakeEvent(endsAtMs: DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeMilliseconds());

		ev.TimeRemaining.Should().NotBeNull();
		ev.TimeRemaining!.Value.TotalSeconds.Should().BeInRange(50, 61);
	}

	[Fact]
	public void IsCampaignSpawn_SourceWs_IsTrue()
	{
		var ev = MakeEvent(source: "ws");

		ev.IsCampaignSpawn.Should().BeTrue();
	}

	[Fact]
	public void IsCampaignSpawn_SourceGm_IsFalse()
	{
		var ev = MakeEvent(source: "gm");

		ev.IsCampaignSpawn.Should().BeFalse();
	}

	[Fact]
	public void HasDeadline_EndsAtMsSet_IsTrue()
	{
		var ev = MakeEvent(endsAtMs: 12345);

		ev.HasDeadline.Should().BeTrue();
	}

	[Fact]
	public void HasDeadline_EndsAtMsZero_IsFalse()
	{
		var ev = MakeEvent(endsAtMs: 0);

		ev.HasDeadline.Should().BeFalse();
	}

	[Fact]
	public void IsPending_StatePending_IsTrue()
	{
		var ev = MakeEvent(state: "pending");

		ev.IsPending.Should().BeTrue();
	}

	[Fact]
	public void IsPending_StateActive_IsFalse()
	{
		var ev = MakeEvent(state: "active");

		ev.IsPending.Should().BeFalse();
	}

	[Fact]
	public void TimeToNext_NextAtMsZero_ReturnsNull()
	{
		var rotation = new WarmapRotation(1, 3, "next", 0, "Koth");

		rotation.TimeToNext.Should().BeNull();
	}

	[Fact]
	public void TimeToNext_NextAtMsInPast_ReturnsZero()
	{
		var rotation = new WarmapRotation(1, 3, "next", DateTimeOffset.UtcNow.AddSeconds(-30).ToUnixTimeMilliseconds(), "Koth");

		rotation.TimeToNext.Should().Be(TimeSpan.Zero);
	}

	[Fact]
	public void TimeToNext_NextAtMsInFuture_ReturnsPositive()
	{
		var rotation = new WarmapRotation(1, 3, "next", DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeMilliseconds(), "Koth");

		rotation.TimeToNext.Should().NotBeNull();
		rotation.TimeToNext!.Value.TotalSeconds.Should().BeInRange(50, 61);
	}
}
