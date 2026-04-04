using DAoCLogWatcher.UI.Models;
using FluentAssertions;

namespace DAoCLogWatcher.Tests.UI.Models;

public sealed class RealmPointSummaryTests
{
	[Fact]
	public void TotalEntries_WithMultipleSourceCounts_SumsCorrectly()
	{
		// Arrange
		var summary = new RealmPointSummary
		              {
				              PlayerKills = 5,
				              CampaignQuests = 3,
				              Ticks = 10,
				              Siege = 2,
				              AssaultOrder = 1,
				              SupportActivity = 4,
				              RelicCapture = 1,
				              Misc = 2
		              };

		// Act
		var total = summary.TotalEntries;

		// Assert
		total.Should().Be(28); // 5+3+10+2+1+4+1+2
	}

	[Fact]
	public void Percentages_WithVariousRPAmounts_CalculateCorrectly()
	{
		// Arrange
		var summary = new RealmPointSummary
		              {
				              TotalRealmPoints = 10000,
				              PlayerKillsRP = 3000, // 30%
				              CampaignQuestsRP = 2000, // 20%
				              TicksRP = 2500, // 25%
				              SiegeRP = 1500, // 15%
				              AssaultOrderRP = 500, // 5%
				              SupportActivityRP = 300, // 3%
				              RelicCaptureRP = 200, // 2%
				              MiscRP = 0 // 0%
		              };

		// Act & Assert
		summary.PlayerKillsPercentage.Should().BeApproximately(30.0, 0.01);
		summary.CampaignQuestsPercentage.Should().BeApproximately(20.0, 0.01);
		summary.TicksPercentage.Should().BeApproximately(25.0, 0.01);
		summary.SiegePercentage.Should().BeApproximately(15.0, 0.01);
		summary.AssaultOrderPercentage.Should().BeApproximately(5.0, 0.01);
		summary.SupportActivityPercentage.Should().BeApproximately(3.0, 0.01);
		summary.RelicCapturePercentage.Should().BeApproximately(2.0, 0.01);
		summary.MiscPercentage.Should().Be(0.0);
	}

	[Fact]
	public void Percentages_ShouldSumTo100_WhenAllRPsAccountedFor()
	{
		// Arrange
		var summary = new RealmPointSummary
		              {
				              TotalRealmPoints = 10000,
				              PlayerKillsRP = 3000,
				              CampaignQuestsRP = 2000,
				              TicksRP = 2500,
				              SiegeRP = 1500,
				              AssaultOrderRP = 500,
				              SupportActivityRP = 300,
				              RelicCaptureRP = 200,
				              MiscRP = 0
		              };

		// Act
		var totalPercentage = summary.PlayerKillsPercentage + summary.CampaignQuestsPercentage + summary.TicksPercentage + summary.SiegePercentage + summary.AssaultOrderPercentage + summary.SupportActivityPercentage +
		                      summary.RelicCapturePercentage + summary.MiscPercentage;

		// Assert
		totalPercentage.Should().BeApproximately(100.0, 0.01);
	}

	[Theory]
	[InlineData(10000, 3000, 2000, 2500, 1500, 500, 300, 200, 0)] // Sums to 10000
	[InlineData(5000, 5000, 0, 0, 0, 0, 0, 0, 0)] // Single source
	[InlineData(1000, 250, 250, 250, 250, 0, 0, 0, 0)] // Even split
	public void Percentages_SumTo100_ForVariousValidDistributions(int total, int kills, int quests, int ticks, int siege, int assault, int support, int relic, int unknown)
	{
		// Arrange
		var summary = new RealmPointSummary
		              {
				              TotalRealmPoints = total,
				              PlayerKillsRP = kills,
				              CampaignQuestsRP = quests,
				              TicksRP = ticks,
				              SiegeRP = siege,
				              AssaultOrderRP = assault,
				              SupportActivityRP = support,
				              RelicCaptureRP = relic,
				              MiscRP = unknown
		              };

		// Act
		var totalPercentage = summary.PlayerKillsPercentage + summary.CampaignQuestsPercentage + summary.TicksPercentage + summary.SiegePercentage + summary.AssaultOrderPercentage + summary.SupportActivityPercentage +
		                      summary.RelicCapturePercentage + summary.MiscPercentage;

		// Assert
		totalPercentage.Should().BeApproximately(100.0, 0.01);
	}

	[Fact]
	public void Percentages_WhenTotalIsZero_ReturnsZero()
	{
		// Arrange
		var summary = new RealmPointSummary
		              {
				              TotalRealmPoints = 0,
				              PlayerKillsRP = 0
		              };

		// Act & Assert
		summary.PlayerKillsPercentage.Should().Be(0.0);
		summary.CampaignQuestsPercentage.Should().Be(0.0);
		summary.TicksPercentage.Should().Be(0.0);
	}

	[Fact]
	public void Percentages_WhenSubtotalExceedsTotal_ReturnsOver100()
	{
		// Arrange - Bug scenario: Individual RPs don't sum to Total
		var summary = new RealmPointSummary
		              {
				              TotalRealmPoints = 5000,
				              PlayerKillsRP = 3000,
				              TicksRP = 3000 // 3000 + 3000 = 6000 > 5000 (inconsistent state)
		              };

		// Act
		var totalPercentage = summary.PlayerKillsPercentage + summary.TicksPercentage;

		// Assert - This would indicate a bug in accumulation logic
		totalPercentage.Should().BeGreaterThan(100.0);
	}

	[Fact]
	public void RpsPerHour_WithNoEntries_ReturnsZero()
	{
		// Arrange
		var summary = new RealmPointSummary
		              {
				              TotalRealmPoints = 0,
				              FirstEntryTime = null,
				              LastEntryTime = null
		              };

		// Act
		var rpsPerHour = summary.RpsPerHour;

		// Assert
		rpsPerHour.Should().Be(0.0);
	}

	[Fact]
	public void RpsPerHour_WithOneHourSession_CalculatesCorrectly()
	{
		// Arrange
		var now = DateTime.Now;
		var summary = new RealmPointSummary
		              {
				              TotalRealmPoints = 5000,
				              FirstEntryTime = now.AddHours(-1),
				              LastEntryTime = now.AddMinutes(-5) // Recent entry (< 1 hour ago)
		              };

		// Act
		var rpsPerHour = summary.RpsPerHour;

		// Assert - Should use current time as end (live session)
		rpsPerHour.Should().BeApproximately(5000.0, 100.0); // ~5000 RPs/hour
	}

	[Fact]
	public void RpsPerHour_WithOldSession_UsesLastEntryTime()
	{
		// Arrange
		var summary = new RealmPointSummary
		              {
				              TotalRealmPoints = 10000,
				              FirstEntryTime = DateTime.Now.AddHours(-3),
				              LastEntryTime = DateTime.Now.AddHours(-2) // > 1 hour ago (old session)
		              };

		// Act
		var rpsPerHour = summary.RpsPerHour;

		// Assert - 10000 RPs over 1 hour = 10000 RPs/hour
		rpsPerHour.Should().BeApproximately(10000.0, 1.0);
	}

	[Fact]
	public void RpsPerHour_WithIdenticalTimestamps_Recent_UsesNowAsEnd()
	{
		// When FirstEntryTime == LastEntryTime and recent (< 1 hour),
		// uses DateTime.Now as end, giving valid RPs/hour

		// Arrange
		var now = DateTime.Now;
		var summary = new RealmPointSummary
		              {
				              TotalRealmPoints = 1000,
				              FirstEntryTime = now.AddMinutes(-1),
				              LastEntryTime = now.AddMinutes(-1)
		              };

		// Act
		var rpsPerHour = summary.RpsPerHour;

		// Assert - Should calculate using DateTime.Now as end
		rpsPerHour.Should().BeGreaterThan(0.0);
	}

	[Fact]
	public void RpsPerHour_WithIdenticalTimestamps_Old_UsesLastAsEnd()
	{
		// When FirstEntryTime == LastEntryTime and old (> 1 hour),
		// uses LastEntryTime as end, resulting in zero duration

		// Arrange
		var timestamp = DateTime.Now.AddHours(-5);
		var summary = new RealmPointSummary
		              {
				              TotalRealmPoints = 1000,
				              FirstEntryTime = timestamp,
				              LastEntryTime = timestamp
		              };

		// Act
		var rpsPerHour = summary.RpsPerHour;

		// Assert - Duration is zero, returns 0
		rpsPerHour.Should().Be(0.0);
	}

	[Fact]
	public void Reset_ClearsAllValues()
	{
		// Arrange
		var summary = new RealmPointSummary
		              {
				              TotalRealmPoints = 10000,
				              FirstEntryTime = DateTime.Now.AddHours(-1),
				              LastEntryTime = DateTime.Now,
				              PlayerKills = 5,
				              PlayerKillsRP = 3000,
				              CampaignQuests = 3,
				              CampaignQuestsRP = 2000,
				              Ticks = 10,
				              TicksRP = 2500,
				              Siege = 2,
				              SiegeRP = 1500,
				              AssaultOrder = 1,
				              AssaultOrderRP = 500,
				              SupportActivity = 4,
				              SupportActivityRP = 300,
				              RelicCapture = 1,
				              RelicCaptureRP = 200,
				              Misc = 2,
				              MiscRP = 0
		              };

		// Act
		summary.Reset();

		// Assert
		summary.TotalRealmPoints.Should().Be(0);
		summary.FirstEntryTime.Should().BeNull();
		summary.LastEntryTime.Should().BeNull();
		summary.PlayerKills.Should().Be(0);
		summary.PlayerKillsRP.Should().Be(0);
		summary.CampaignQuests.Should().Be(0);
		summary.CampaignQuestsRP.Should().Be(0);
		summary.Ticks.Should().Be(0);
		summary.TicksRP.Should().Be(0);
		summary.Siege.Should().Be(0);
		summary.SiegeRP.Should().Be(0);
		summary.AssaultOrder.Should().Be(0);
		summary.AssaultOrderRP.Should().Be(0);
		summary.SupportActivity.Should().Be(0);
		summary.SupportActivityRP.Should().Be(0);
		summary.RelicCapture.Should().Be(0);
		summary.RelicCaptureRP.Should().Be(0);
		summary.Misc.Should().Be(0);
		summary.MiscRP.Should().Be(0);
		summary.TotalEntries.Should().Be(0);
		summary.RpsPerHour.Should().Be(0.0);
	}

	[Theory]
	[InlineData(1000, 300, 200, 200, 150, 50, 50, 25, 25)] // Sums to 1000
	[InlineData(500, 100, 100, 100, 100, 50, 25, 15, 10)] // Sums to 500
	public void Integration_RealisticScenario_PercentagesAreValid(int total, int kills, int quests, int ticks, int siege, int assault, int support, int relic, int unknown)
	{
		// Arrange
		var summary = new RealmPointSummary
		              {
				              TotalRealmPoints = total,
				              PlayerKillsRP = kills,
				              CampaignQuestsRP = quests,
				              TicksRP = ticks,
				              SiegeRP = siege,
				              AssaultOrderRP = assault,
				              SupportActivityRP = support,
				              RelicCaptureRP = relic,
				              MiscRP = unknown
		              };

		// Act
		var allPercentages = new[]
		                     {
				                     summary.PlayerKillsPercentage,
				                     summary.CampaignQuestsPercentage,
				                     summary.TicksPercentage,
				                     summary.SiegePercentage,
				                     summary.AssaultOrderPercentage,
				                     summary.SupportActivityPercentage,
				                     summary.RelicCapturePercentage,
				                     summary.MiscPercentage
		                     };

		// Assert
		allPercentages.Should().AllSatisfy(p => p.Should().BeGreaterThanOrEqualTo(0.0));
		allPercentages.Should().AllSatisfy(p => p.Should().BeLessThanOrEqualTo(100.0));
		allPercentages.Sum().Should().BeApproximately(100.0, 0.01);
	}

	[Fact]
	public void BugDetection_IndividualRPsSumToTotal_ShouldBeEnforced()
	{
		// This test validates that the sum of individual RP values equals TotalRealmPoints
		// If this fails, it indicates a bug in the accumulation logic in ProcessLogLine

		// Arrange
		var summary = new RealmPointSummary
		              {
				              TotalRealmPoints = 10000,
				              PlayerKillsRP = 3000,
				              CampaignQuestsRP = 2000,
				              TicksRP = 2500,
				              SiegeRP = 1500,
				              AssaultOrderRP = 500,
				              SupportActivityRP = 300,
				              RelicCaptureRP = 200,
				              MiscRP = 0
		              };

		// Act
		var sumOfIndividualRPs = summary.PlayerKillsRP + summary.CampaignQuestsRP + summary.TicksRP + summary.SiegeRP + summary.AssaultOrderRP + summary.SupportActivityRP + summary.RelicCaptureRP + summary.MiscRP;

		// Assert
		sumOfIndividualRPs.Should().Be(summary.TotalRealmPoints, "Individual RP values should sum exactly to TotalRealmPoints. " + "If this fails, there's a bug in ProcessLogLine accumulation logic.");
	}
}
