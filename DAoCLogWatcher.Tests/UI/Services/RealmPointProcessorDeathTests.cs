using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Models;
using DAoCLogWatcher.UI.Services;
using FluentAssertions;

namespace DAoCLogWatcher.Tests.UI.Services;

/// <summary>
/// Tests for the death counting mechanic in RealmPointProcessor:
/// - Kill lines (X was just killed by Y) are the fallback source.
/// - A /stats Deaths line is authoritative and overrides the kill-line count when present.
/// </summary>
public sealed class RealmPointProcessorDeathTests
{
    private const string CharacterName = "Zordrak";

    private static RealmPointProcessor BuildProcessor()
    {
        var summary = new RealmPointSummary();
        var chartData = new RpsChartData();
        return new RealmPointProcessor(summary, chartData);
    }

    private static void DetectCharacter(RealmPointProcessor processor)
    {
        processor.Process(
            new UnknownLogLine("Statistics for Zordrak this Session:")
                { DetectedCharacterName = CharacterName },
            null, out _, out _);
    }

    private static void FeedKillDeath(RealmPointProcessor processor, string killer = "Xyrrath")
    {
        processor.Process(
            new KillLogLine(
                $"[20:00:00] {CharacterName} was just killed by {killer} in Emain Macha.",
                new KillEvent
                {
                    Timestamp = new TimeOnly(20, 0, 0),
                    Victim    = CharacterName,
                    Killer    = killer,
                    Zone      = "Emain Macha"
                }),
            null, out _, out _);
    }

    private static void FeedStatsDeaths(RealmPointProcessor processor, int count)
    {
        processor.Process(
            new StatsDeathsLogLine($"[20:54:35] Deaths:        {count}", count),
            null, out _, out _);
    }

    [Fact]
    public void Deaths_NoStatsLine_CountedFromKillLines()
    {
        // Arrange
        var processor = BuildProcessor();
        DetectCharacter(processor);
        FeedKillDeath(processor);
        FeedKillDeath(processor, "Quelris");

        // Assert
        processor.Deaths.Should().Be(2);
    }

    [Fact]
    public void Deaths_StatsLinePresent_OverridesKillLineCount()
    {
        // Stats Deaths is the authoritative source. Even if kill lines only found 1 death,
        // the /stats figure (which the game tracks authoritatively) should be used.
        var processor = BuildProcessor();
        DetectCharacter(processor);
        FeedKillDeath(processor);        // kill-line: 1 death
        FeedStatsDeaths(processor, 5);  // /stats says 5

        processor.Deaths.Should().Be(5);
    }

    [Fact]
    public void Deaths_StatsLineIsZero_KillLinesUsedAsFallback()
    {
        // A "Deaths: 0" line should NOT override kill-line deaths because statsDeaths == 0
        // means no deaths recorded yet in /stats, not a confirmed zero count.
        var processor = BuildProcessor();
        DetectCharacter(processor);
        FeedKillDeath(processor);
        FeedStatsDeaths(processor, 0); // /stats says 0 (no authoritative data yet)

        processor.Deaths.Should().Be(1, "kill-line deaths should be used when statsDeaths is 0");
    }

    [Fact]
    public void Deaths_MultipleStatsLines_HighestValueWins()
    {
        // /stats is cumulative — later /stats calls always have >= the previous count.
        // Processor should track the highest statsDeaths seen.
        var processor = BuildProcessor();
        DetectCharacter(processor);
        FeedStatsDeaths(processor, 3);
        FeedStatsDeaths(processor, 7); // higher later /stats

        processor.Deaths.Should().Be(7);
    }

    [Fact]
    public void Deaths_Reset_ClearsStatsDeathsAndKillLines()
    {
        var processor = BuildProcessor();
        DetectCharacter(processor);
        FeedKillDeath(processor);
        FeedStatsDeaths(processor, 4);

        processor.Reset();

        processor.Deaths.Should().Be(0);
    }

    [Fact]
    public void Kills_KillLineWhereCharacterIsKiller_CountedAsKill()
    {
        var processor = BuildProcessor();
        DetectCharacter(processor);

        processor.Process(
            new KillLogLine(
                $"[20:00:00] Xyrrath was just killed by {CharacterName} in Emain Macha.",
                new KillEvent
                {
                    Timestamp = new TimeOnly(20, 0, 0),
                    Victim    = "Xyrrath",
                    Killer    = CharacterName,
                    Zone      = "Emain Macha"
                }),
            null, out _, out _);

        processor.Kills.Should().Be(1);
        processor.Deaths.Should().Be(0, "character was the killer, not the victim");
    }
}
