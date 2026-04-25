using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Models;
using DAoCLogWatcher.UI.Services;
using FluentAssertions;

namespace DAoCLogWatcher.Tests.UI.Services;

/// <summary>
/// Tests for CombatProcessor's AoE multi-hit detection (sliding-window grouping).
/// Multi-hit fires when ≥5 unique targets are hit by the same spell within the window.
/// </summary>
public sealed class CombatProcessorTests
{
	private readonly CombatProcessor processor = new(new CombatSummary());

	private readonly List<CombatLogEntry> multiHits = [];

	public CombatProcessorTests()
	{
		this.processor.MultiHitDetected += (_, e) => this.multiHits.Add(e);
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	private static DamageLogLine SpellHit(string target, string spell, TimeOnly ts, int dmg = 100)
	{
		return new DamageLogLine("",
		                         new DamageEvent
		                         {
				                         Timestamp = ts,
				                         Opponent = target,
				                         BaseDamage = dmg,
				                         Absorbed = 0,
				                         IsDealt = true,
				                         SpellName = spell,
				                         IsWeaponAttack = false
		                         });
	}

	private static DamageLogLine WeaponHit(string target, TimeOnly ts)
	{
		return new DamageLogLine("",
		                         new DamageEvent
		                         {
				                         Timestamp = ts,
				                         Opponent = target,
				                         BaseDamage = 100,
				                         Absorbed = 0,
				                         IsDealt = true,
				                         SpellName = "sword",
				                         IsWeaponAttack = true
		                         });
	}

	private static HealLogLine IncomingHeal(TimeOnly ts)
	{
		return new HealLogLine("",
		                       new HealEvent
		                       {
				                       Timestamp = ts,
				                       HitPoints = 100,
				                       IsOutgoing = false,
				                       Healer = "Healer"
		                       });
	}

	private static MissLogLine MissLine(TimeOnly ts)
	{
		return new MissLogLine("",
		                       new MissEvent
		                       {
				                       Timestamp = ts,
				                       IsSpell = true
		                       });
	}

	private static readonly string[] Targets = ["T1", "T2", "T3", "T4", "T5", "T6"];
	private static readonly TimeOnly T0 = new(20, 0, 0);

	// ── Tests ──────────────────────────────────────────────────────────────────

	[Fact]
	public void MultiHit_SingleSpellHit_NeverFires()
	{
		this.processor.Process(SpellHit("T1", "Fireball", T0));

		// Flush with a far-future line
		this.processor.Process(SpellHit("T2", "Fireball", T0.Add(TimeSpan.FromSeconds(10))));

		this.multiHits.Should().BeEmpty("only 2 unique targets hit");
	}

	[Fact]
	public void MultiHit_FiveUniqueTargets_FiresWhenNextSpellClosesWindow()
	{
		for(var i = 0; i < 5; i++)
		{
			this.processor.Process(SpellHit(Targets[i], "Fireball", T0));
		}

		// Next spell hit of a different spell closes the Fireball window
		this.processor.Process(SpellHit("T6", "Lightning", T0.Add(TimeSpan.FromSeconds(1))));

		this.multiHits.Should().HaveCount(1);
		this.multiHits[0].HitCount.Should().Be(5);
		this.multiHits[0].SpellName.Should().Be("Fireball");
	}

	[Fact]
	public void MultiHit_FourUniqueTargets_DoesNotFire()
	{
		for(var i = 0; i < 4; i++)
		{
			this.processor.Process(SpellHit(Targets[i], "Fireball", T0));
		}

		this.processor.Process(SpellHit("T5", "Lightning", T0.Add(TimeSpan.FromSeconds(1))));

		this.multiHits.Should().BeEmpty("4 targets is below the threshold of 5");
	}

	[Fact]
	public void MultiHit_WindowClosedByIncomingHeal_Fires()
	{
		// Bug scenario: no subsequent outgoing spell damage arrives after the AoE
		for(var i = 0; i < 5; i++)
		{
			this.processor.Process(SpellHit(Targets[i], "Fireball", T0));
		}

		// A heal 5 seconds later should close the expired window
		this.processor.Process(IncomingHeal(T0.Add(TimeSpan.FromSeconds(5))));

		this.multiHits.Should().HaveCount(1, "window expired and should fire on any subsequent timestamped line");
	}

	[Fact]
	public void MultiHit_WindowClosedByMiss_Fires()
	{
		for(var i = 0; i < 5; i++)
		{
			this.processor.Process(SpellHit(Targets[i], "Fireball", T0));
		}

		this.processor.Process(MissLine(T0.Add(TimeSpan.FromSeconds(5))));

		this.multiHits.Should().HaveCount(1);
	}

	[Fact]
	public void MultiHit_SameTargetHitMultipleTimes_CountedOnce()
	{
		for(var i = 0; i < 5; i++)
		{
			this.processor.Process(SpellHit("SameTarget", "Fireball", T0.Add(TimeSpan.FromSeconds(i))));
		}

		this.processor.Process(SpellHit("Other", "Lightning", T0.Add(TimeSpan.FromSeconds(10))));

		this.multiHits.Should().BeEmpty("5 hits on the same target counts as 1 unique target");
	}

	[Fact]
	public void MultiHit_WeaponHitBetweenAoeHits_DoesNotInterruptWindow()
	{
		// Bug scenario: melee swing mid-AoE sequence used to reset the spell window
		this.processor.Process(SpellHit("T1", "Fireball", T0));
		this.processor.Process(SpellHit("T2", "Fireball", T0));
		this.processor.Process(SpellHit("T3", "Fireball", T0));

		// Sword swing between AoE hits — must NOT interrupt the Fireball window
		this.processor.Process(WeaponHit("T4", T0));

		this.processor.Process(SpellHit("T4", "Fireball", T0));
		this.processor.Process(SpellHit("T5", "Fireball", T0));

		// Close window with a different spell
		this.processor.Process(SpellHit("T6", "Lightning", T0.Add(TimeSpan.FromSeconds(1))));

		this.multiHits.Should().HaveCount(1, "weapon hit should not have split the Fireball window");
		this.multiHits[0].HitCount.Should().Be(5);
	}

	[Fact]
	public void MultiHit_DifferentSpellInterruptsWindowAndFinalizes()
	{
		for(var i = 0; i < 5; i++)
		{
			this.processor.Process(SpellHit(Targets[i], "Fireball", T0));
		}

		// Lightning immediately starts its own window
		for(var i = 0; i < 5; i++)
		{
			this.processor.Process(SpellHit(Targets[i], "Lightning", T0.Add(TimeSpan.FromSeconds(1))));
		}

		// Close Lightning window
		this.processor.Process(SpellHit("T6", "Fireball", T0.Add(TimeSpan.FromSeconds(6))));

		this.multiHits.Should().HaveCount(2, "each spell's AoE should produce its own multi-hit event");
	}

	[Fact]
	public void MultiHit_Reset_ClearsWindowWithoutFiring()
	{
		for(var i = 0; i < 5; i++)
		{
			this.processor.Process(SpellHit(Targets[i], "Fireball", T0));
		}

		this.processor.Reset();

		// Even after reset, feeding more lines should not produce a stale event
		this.processor.Process(SpellHit("T6", "Lightning", T0.Add(TimeSpan.FromSeconds(1))));

		this.multiHits.Should().BeEmpty("reset must clear the in-flight window");
	}
}
