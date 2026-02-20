using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Order;
using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.Core.Parsing;

namespace DAoCLogWatcher.Benchmarks;

/// <summary>
/// Performance benchmarks for RealmPointParser
/// Measures throughput (lines/second), memory allocations, and parsing speed
/// </summary>
[MemoryDiagnoser]  // Shows memory allocations
[Orderer(SummaryOrderPolicy.FastestToSlowest)]  // Consistent ordering
[RankColumn]  // Shows relative performance
public class ParserBenchmarks
{
    private RealmPointParser parser = null!;
    private string[] testLines = null!;

    // Pre-generated test data
    private string playerKillLine = null!;
    private string campaignQuestLine = null!;
    private string tickLine = null!;
    private string siegeLine = null!;
    private string bonusLine = null!;
    private string invalidLine = null!;

    [GlobalSetup]
    public void Setup()
    {
        parser = new RealmPointParser();

        // Real log patterns
        playerKillLine = "[12:34:56] You get 1234 realm points!";
        campaignQuestLine = "[12:34:56] You get 1000 realm points for Campaign Quest!";
        tickLine = "[12:34:56] You get 2500 realm points for Battle Tick!";
        siegeLine = "[12:34:56] You get 400 realm points for Tower Capture!";
        bonusLine = "[12:34:56] You get an additional 20 realm points due to your realm rank!";
        invalidLine = "[12:34:56] Some random log message";

        // Mixed workload: realistic distribution of line types
        testLines = GenerateMixedWorkload(1000);
    }

    [Benchmark(Description = "Parse 1000 mixed lines (realistic workload)")]
    public int ParseMixedWorkload()
    {
        var parser = new RealmPointParser();
        int count = 0;

        foreach (var line in testLines)
        {
            if (parser.TryParse(line, out var entry))
            {
                count++;
            }
        }

        return count;
    }

    [Benchmark(Description = "Parse player kill (state machine)")]
    public RealmPointEntry? ParsePlayerKill()
    {
        var parser = new RealmPointParser();
        parser.TryParse(playerKillLine, out _);  // Pending
        parser.TryParse(invalidLine, out var entry);  // Emit
        return entry;
    }

    [Benchmark(Description = "Parse campaign quest (immediate)")]
    public RealmPointEntry? ParseCampaignQuest()
    {
        var parser = new RealmPointParser();
        parser.TryParse(campaignQuestLine, out var entry);
        return entry;
    }

    [Benchmark(Description = "Parse battle tick (immediate)")]
    public RealmPointEntry? ParseBattleTick()
    {
        var parser = new RealmPointParser();
        parser.TryParse(tickLine, out var entry);
        return entry;
    }

    [Benchmark(Description = "Parse siege (immediate)")]
    public RealmPointEntry? ParseSiege()
    {
        var parser = new RealmPointParser();
        parser.TryParse(siegeLine, out var entry);
        return entry;
    }

    [Benchmark(Description = "Parse bonus line (filtered)")]
    public RealmPointEntry? ParseBonusLine()
    {
        var parser = new RealmPointParser();
        parser.TryParse(bonusLine, out var entry);
        return entry;
    }

    [Benchmark(Description = "Parse invalid line (fast reject)")]
    public RealmPointEntry? ParseInvalidLine()
    {
        var parser = new RealmPointParser();
        parser.TryParse(invalidLine, out var entry);
        return entry;
    }

    [Benchmark(Description = "Relic capture sequence (2 lines)")]
    public RealmPointEntry? ParseRelicCapture()
    {
        var parser = new RealmPointParser();
        parser.TryParse("[10:00:00] Albion has stored the Strength of Hibernia", out _);
        parser.TryParse("[10:00:01] You get 5000 realm points!", out var entry);
        return entry;
    }

    [Benchmark(Description = "Parser instantiation overhead")]
    public RealmPointParser InstantiateParser()
    {
        return new RealmPointParser();
    }

    [Benchmark(Description = "Regex match performance (valid line)")]
    public bool RegexMatch_Valid()
    {
        return campaignQuestLine.Contains("realm point");
    }

    [Benchmark(Description = "Regex match performance (invalid line)")]
    public bool RegexMatch_Invalid()
    {
        return invalidLine.Contains("realm point");
    }

    /// <summary>
    /// Generates realistic workload:
    /// - 60% player kills (requires 2 lines each)
    /// - 15% battle ticks
    /// - 10% invalid lines
    /// - 5% campaign quests
    /// - 5% siege
    /// - 5% bonus lines (filtered)
    /// </summary>
    private static string[] GenerateMixedWorkload(int lineCount)
    {
        var lines = new List<string>(lineCount);
        var random = new Random(42);  // Fixed seed for consistency

        for (int i = 0; i < lineCount; i++)
        {
            var roll = random.Next(100);

            if (roll < 60)  // 60% player kills
            {
                lines.Add($"[{10 + i % 14:D2}:{i % 60:D2}:{i % 60:D2}] You get {100 + random.Next(1000)} realm points!");
            }
            else if (roll < 75)  // 15% ticks
            {
                lines.Add($"[{10 + i % 14:D2}:{i % 60:D2}:{i % 60:D2}] You get {2000 + random.Next(1000)} realm points for Battle Tick!");
            }
            else if (roll < 85)  // 10% invalid
            {
                lines.Add($"[{10 + i % 14:D2}:{i % 60:D2}:{i % 60:D2}] Player{i} casts a spell!");
            }
            else if (roll < 90)  // 5% quests
            {
                lines.Add($"[{10 + i % 14:D2}:{i % 60:D2}:{i % 60:D2}] You get {500 + random.Next(500)} realm points for Campaign Quest!");
            }
            else if (roll < 95)  // 5% siege
            {
                lines.Add($"[{10 + i % 14:D2}:{i % 60:D2}:{i % 60:D2}] You get {300 + random.Next(300)} realm points for Tower Capture!");
            }
            else  // 5% bonus (filtered)
            {
                lines.Add($"[{10 + i % 14:D2}:{i % 60:D2}:{i % 60:D2}] You get an additional {10 + random.Next(50)} realm points due to your realm rank!");
            }
        }

        return lines.ToArray();
    }
}
