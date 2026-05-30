using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Services;
using FluentAssertions;

namespace DAoCLogWatcher.Tests.UI.Services;

public sealed class KillStatTrackerTests
{
	private readonly KillStatTracker tracker = new();
	private static readonly TimeOnly T0 = new(20, 0, 0);

	private static KillEvent Kill(string killer, string victim)
	{
		return new KillEvent
		       {
				       Timestamp = T0,
				       Killer = killer,
				       Victim = victim,
				       Zone = "Emain Macha"
		       };
	}

	// ── OnKillEvent ───────────────────────────────────────────────────────────

	[Fact]
	public void OnKillEvent_NullCharacter_BuffersEventWithoutChangingStats()
	{
		var changed = false;
		this.tracker.OnKillEvent(Kill("Player", "Enemy"), null, ref changed);

		this.tracker.Kills.Should().Be(0);
		this.tracker.Deaths.Should().Be(0);
		changed.Should().BeFalse();
	}

	[Fact]
	public void OnKillEvent_KillerMatchesCharacter_IncrementsKills()
	{
		var changed = false;
		this.tracker.OnKillEvent(Kill("MyChar", "Enemy"), "MyChar", ref changed);

		this.tracker.Kills.Should().Be(1);
		this.tracker.Deaths.Should().Be(0);
		changed.Should().BeTrue();
	}

	[Fact]
	public void OnKillEvent_VictimMatchesCharacter_IncrementsDeaths()
	{
		var changed = false;
		this.tracker.OnKillEvent(Kill("Enemy", "MyChar"), "MyChar", ref changed);

		this.tracker.Kills.Should().Be(0);
		this.tracker.Deaths.Should().Be(1);
		changed.Should().BeTrue();
	}

	[Fact]
	public void OnKillEvent_NeitherKillerNorVictim_NoChange()
	{
		var changed = false;
		this.tracker.OnKillEvent(Kill("SomeoneElse", "AnotherGuy"), "MyChar", ref changed);

		this.tracker.Kills.Should().Be(0);
		this.tracker.Deaths.Should().Be(0);
		changed.Should().BeFalse();
	}

	[Fact]
	public void OnKillEvent_MultipleEvents_AccumulatesCorrectly()
	{
		var changed = false;
		this.tracker.OnKillEvent(Kill("MyChar", "Enemy1"), "MyChar", ref changed);
		this.tracker.OnKillEvent(Kill("MyChar", "Enemy2"), "MyChar", ref changed);
		this.tracker.OnKillEvent(Kill("Enemy3", "MyChar"), "MyChar", ref changed);

		this.tracker.Kills.Should().Be(2);
		this.tracker.Deaths.Should().Be(1);
		changed.Should().BeTrue();
	}

	// ── OnCharacterChanged ────────────────────────────────────────────────────

	[Fact]
	public void OnCharacterChanged_RecomputesFromBuffer()
	{
		var changed = false;

		// Events arrive before character is known
		this.tracker.OnKillEvent(Kill("MyChar", "Enemy1"), null, ref changed);
		this.tracker.OnKillEvent(Kill("Enemy2", "MyChar"), null, ref changed);
		this.tracker.OnKillEvent(Kill("MyChar", "Enemy3"), null, ref changed);

		changed = false;
		this.tracker.OnCharacterChanged("MyChar", ref changed);

		this.tracker.Kills.Should().Be(2);
		this.tracker.Deaths.Should().Be(1);
		changed.Should().BeTrue();
	}

	[Fact]
	public void OnCharacterChanged_ToNull_ClearsCountsAndSetsChanged()
	{
		var changed = false;
		this.tracker.OnKillEvent(Kill("MyChar", "Enemy"), "MyChar", ref changed);

		changed = false;
		this.tracker.OnCharacterChanged(null, ref changed);

		this.tracker.Kills.Should().Be(0);
		this.tracker.Deaths.Should().Be(0);
		changed.Should().BeTrue();
	}

	[Fact]
	public void OnCharacterChanged_ToNull_WhenAlreadyZero_DoesNotSetChanged()
	{
		var changed = false;
		this.tracker.OnCharacterChanged(null, ref changed);

		changed.Should().BeFalse("counts were already zero, no change occurred");
	}

	[Fact]
	public void OnCharacterChanged_SameCountsAsCurrentStats_DoesNotSetChanged()
	{
		var changed = false;
		this.tracker.OnKillEvent(Kill("MyChar", "Enemy"), "MyChar", ref changed);

		// Kills=1, Deaths=0

		changed = false;

		// Same character name; recompute produces the same counts, so changed stays false
		this.tracker.OnCharacterChanged("MyChar", ref changed);

		changed.Should().BeFalse("counts did not change");
	}

	[Fact]
	public void OnCharacterChanged_ToDifferentCharacter_RecomputesFromBuffer()
	{
		var changed = false;

		// Buffer 3 events: Alice has 2 kills, Bob has 1
		this.tracker.OnKillEvent(Kill("Alice", "Enemy1"), "Alice", ref changed);
		this.tracker.OnKillEvent(Kill("Alice", "Enemy2"), "Alice", ref changed);
		this.tracker.OnKillEvent(Kill("Bob", "Enemy3"), "Alice", ref changed);

		// Kills=2, Deaths=0 (tracked for Alice)

		changed = false;
		this.tracker.OnCharacterChanged("Bob", ref changed);

		this.tracker.Kills.Should().Be(1, "Bob killed only Enemy3");
		this.tracker.Deaths.Should().Be(0);
		changed.Should().BeTrue("count changed from 2 to 1");
	}

	// ── Reset ─────────────────────────────────────────────────────────────────

	[Fact]
	public void Reset_ClearsCountsAndBuffer()
	{
		var changed = false;
		this.tracker.OnKillEvent(Kill("MyChar", "Enemy"), "MyChar", ref changed);
		this.tracker.Reset();

		this.tracker.Kills.Should().Be(0);
		this.tracker.Deaths.Should().Be(0);

		changed = false;
		this.tracker.OnCharacterChanged("MyChar", ref changed);
		changed.Should().BeFalse("buffer was cleared by Reset");
	}
}
