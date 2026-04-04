using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Models;
using DAoCLogWatcher.UI.Services;
using FluentAssertions;

namespace DAoCLogWatcher.Tests.UI.Services;

/// <summary>
/// Tests for RealmPointProcessor's multi-kill detection (sliding RP window).
/// Multi-kill fires when ≥5 PlayerKill RP entries accumulate before the window expires.
/// Window expires when the next timestamped entry is > (5 + count × 0.2) seconds after windowStart.
/// </summary>
public sealed class RealmPointProcessorMultiKillTests
{
	private readonly RealmPointProcessor processor = new(new RealmPointSummary(), new RpsChartData());
	private readonly List<RealmPointLogEntry> multiKills = [];

	public RealmPointProcessorMultiKillTests()
	{
		this.processor.MultiKillDetected += (_, e) => this.multiKills.Add(e);
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	private static RealmPointLogLine PkRpLine(TimeOnly ts, int points = 200) =>
			new("",
			    new RealmPointEntry
			    {
					    Timestamp = ts,
					    Points = points,
					    Source = RealmPointSource.PlayerKill,
					    RawLine = ""
			    });

	private static DamageLogLine FlushLine(TimeOnly ts) =>
			new("",
			    new DamageEvent
			    {
					    Timestamp = ts,
					    Target = "flush",
					    BaseDamage = 1,
					    Absorbed = 0,
					    IsDealt = false,
			    });

	private static readonly TimeOnly T0 = new(20, 0, 0);
	private static readonly DateTime Session = new(2024, 1, 1);

	private void Feed(RealmPointLogLine line) => this.processor.Process(line, Session, out _, out _);

	private void Feed(DamageLogLine line) => this.processor.Process(line, Session, out _, out _);

	// ── Tests ──────────────────────────────────────────────────────────────────

	[Fact]
	public void MultiKill_FivePkRps_WithinWindow_FiresEvent()
	{
		// Feed 5 kills within 1 second of each other
		for(var i = 0; i < 5; i++)
			Feed(PkRpLine(T0.Add(TimeSpan.FromSeconds(i))));

		// Flush: threshold after 5 kills = 5 + 5×0.2 = 6.0s; send something 7s after T0
		Feed(FlushLine(T0.Add(TimeSpan.FromSeconds(7))));

		multiKills.Should().HaveCount(1);
		multiKills[0].Source.Should().Be("Multi-Kill");
	}

	[Fact]
	public void MultiKill_FourPkRps_DoesNotFire()
	{
		for(var i = 0; i < 4; i++)
			Feed(PkRpLine(T0.Add(TimeSpan.FromSeconds(i))));

		Feed(FlushLine(T0.Add(TimeSpan.FromSeconds(10))));

		multiKills.Should().BeEmpty("4 kills is below the threshold of 5");
	}

	[Fact]
	public void MultiKill_KillsSpreadBeyondWindow_DoesNotFire()
	{
		// First kill starts the window; second kill arrives 10s later — window expires
		Feed(PkRpLine(T0));

		// 10s > threshold for 1 kill (5.2s) → window finalized with count=1 on the second kill
		Feed(PkRpLine(T0.Add(TimeSpan.FromSeconds(10))));

		multiKills.Should().BeEmpty("kills were too spread apart to accumulate 5");
	}

	[Fact]
	public void MultiKill_SixKills_FiresOnceWithCorrectCount()
	{
		for(var i = 0; i < 6; i++)
			Feed(PkRpLine(T0.Add(TimeSpan.FromSeconds(i))));

		Feed(FlushLine(T0.Add(TimeSpan.FromSeconds(15))));

		multiKills.Should().HaveCount(1);
		multiKills[0].Details.Should().Contain("6");
	}

	[Fact]
	public void MultiKill_FirstEntryIsMarkedIsMultiKill()
	{
		RealmPointLogEntry? firstEntry = null;
		this.processor.EntryProcessed += (_, e) => firstEntry ??= e;

		for(var i = 0; i < 5; i++)
			Feed(PkRpLine(T0.Add(TimeSpan.FromSeconds(i))));

		Feed(FlushLine(T0.Add(TimeSpan.FromSeconds(7))));

		firstEntry.Should().NotBeNull();
		firstEntry!.IsMultiKill.Should().BeTrue("the first kill entry in a multi-kill window is flagged");
	}

	[Fact]
	public void MultiKill_Reset_ClearsInFlightWindow()
	{
		for(var i = 0; i < 4; i++)
			Feed(PkRpLine(T0.Add(TimeSpan.FromSeconds(i))));

		this.processor.Reset();

		// Add one more kill after reset — should not combine with pre-reset kills
		Feed(PkRpLine(T0.Add(TimeSpan.FromSeconds(5))));
		Feed(FlushLine(T0.Add(TimeSpan.FromSeconds(10))));

		multiKills.Should().BeEmpty("reset must discard the in-flight window");
	}
}
