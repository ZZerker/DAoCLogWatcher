using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Models;
using DAoCLogWatcher.UI.Services;
using FluentAssertions;

namespace DAoCLogWatcher.Tests.UI.Services;

/// <summary>
/// Tests for the death counting mechanic in RealmPointProcessor.
/// Deaths are counted from kill lines (X was just killed by Y) where the character is the victim.
/// </summary>
public sealed class RealmPointProcessorDeathTests
{
	private const string CHARACTER_NAME = "Zordrak";

	private static RealmPointProcessor BuildProcessor()
	{
		var summary = new RealmPointSummary();
		var chartData = new RpsChartData();
		return new RealmPointProcessor(summary, chartData);
	}

	private static void DetectCharacter(RealmPointProcessor processor)
	{
		processor.Process(new UnknownLogLine("Statistics for Zordrak this Session:")
		                  {
				                  DetectedCharacterName = CHARACTER_NAME
		                  },
		                  null,
		                  out _,
		                  out _);
	}

	private static void FeedKillDeath(RealmPointProcessor processor, string killer = "Xyrrath")
	{
		processor.Process(new KillLogLine($"[20:00:00] {CHARACTER_NAME} was just killed by {killer} in Emain Macha.",
		                                  new KillEvent
		                                  {
				                                  Timestamp = new TimeOnly(20, 0, 0),
				                                  Victim = CHARACTER_NAME,
				                                  Killer = killer,
				                                  Zone = "Emain Macha"
		                                  }),
		                  null,
		                  out _,
		                  out _);
	}

	[Fact]
	public void Deaths_CountedFromKillLines()
	{
		var processor = BuildProcessor();
		DetectCharacter(processor);
		FeedKillDeath(processor);
		FeedKillDeath(processor, "Quelris");

		processor.Deaths.Should().Be(2);
	}

	[Fact]
	public void Deaths_Reset_ClearsKillLines()
	{
		var processor = BuildProcessor();
		DetectCharacter(processor);
		FeedKillDeath(processor);

		processor.Reset();

		processor.Deaths.Should().Be(0);
	}

	[Fact]
	public void Kills_KillLineWhereCharacterIsKiller_CountedAsKill()
	{
		var processor = BuildProcessor();
		DetectCharacter(processor);

		processor.Process(new KillLogLine($"[20:00:00] Xyrrath was just killed by {CHARACTER_NAME} in Emain Macha.",
		                                  new KillEvent
		                                  {
				                                  Timestamp = new TimeOnly(20, 0, 0),
				                                  Victim = "Xyrrath",
				                                  Killer = CHARACTER_NAME,
				                                  Zone = "Emain Macha"
		                                  }),
		                  null,
		                  out _,
		                  out _);

		processor.Kills.Should().Be(1);
		processor.Deaths.Should().Be(0, "character was the killer, not the victim");
	}
}
