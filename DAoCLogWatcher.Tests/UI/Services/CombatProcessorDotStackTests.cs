using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Models;
using DAoCLogWatcher.UI.Services;
using FluentAssertions;

namespace DAoCLogWatcher.Tests.UI.Services;

/// <summary>
/// Tests for CombatProcessor's DoT stacking: per-target live-updating entries,
/// 15-second window, and spell-change closure.
/// </summary>
public sealed class CombatProcessorDotStackTests
{
	private readonly CombatSummary summary = new();
	private readonly CombatProcessor processor;
	private readonly List<CombatLogEntry> logged = [];

	public CombatProcessorDotStackTests()
	{
		this.processor = new CombatProcessor(this.summary);
		this.processor.DamageLogged += (_, e) => this.logged.Add(e);
	}

	private static readonly TimeOnly T0 = new(20, 0, 0);

	private static DamageLogLine DotTick(string target, string spell, TimeOnly ts, int baseDmg = 30, int crit = 0)
	{
		return new DamageLogLine("",
		                         new DamageEvent
		                         {
				                         Timestamp = ts,
				                         Opponent = target,
				                         BaseDamage = baseDmg,
				                         CritDamage = crit,
				                         Absorbed = 0,
				                         IsDealt = true,
				                         SpellName = spell,
				                         IsWeaponAttack = false,
				                         IsDotTick = true
		                         });
	}

	private static DamageLogLine SpellHit(string target, string spell, TimeOnly ts)
	{
		return new DamageLogLine("",
		                         new DamageEvent
		                         {
				                         Timestamp = ts,
				                         Opponent = target,
				                         BaseDamage = 100,
				                         Absorbed = 0,
				                         IsDealt = true,
				                         SpellName = spell,
				                         IsWeaponAttack = false
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
				                       Healer = "Cleric"
		                       });
	}

	// ── First tick ────────────────────────────────────────────────────────────

	[Fact]
	public void FirstDotTick_FiresDamageLoggedAndCreatesEntry()
	{
		this.processor.Process(DotTick("Mob", "Plague", T0));

		this.logged.Should().HaveCount(1);
		this.logged[0].IsDotTick.Should().BeTrue();
		this.logged[0].Opponent.Should().Be("Mob");
		this.logged[0].TotalDamage.Should().Be(30);
		this.logged[0].HitCount.Should().Be(1);
		this.logged[0].SpellName.Should().Be("Plague");
	}

	// ── Subsequent ticks on same target ──────────────────────────────────────

	[Fact]
	public void SecondTickSameTarget_UpdatesEntryInPlaceWithoutNewLoggedEvent()
	{
		this.processor.Process(DotTick("Mob", "Plague", T0, 30));
		this.processor.Process(DotTick("Mob", "Plague", T0.Add(TimeSpan.FromSeconds(3)), 30));

		this.logged.Should().HaveCount(1, "second tick mutates the existing entry");
		this.logged[0].TotalDamage.Should().Be(60);
		this.logged[0].HitCount.Should().Be(2);
	}

	[Fact]
	public void MultipleTicks_AccumulateDamageAndHitCount()
	{
		for(var i = 0; i < 4; i++)
		{
			this.processor.Process(DotTick("Mob", "Plague", T0.Add(TimeSpan.FromSeconds(i * 3)), 25));
		}

		this.logged.Should().HaveCount(1);
		this.logged[0].TotalDamage.Should().Be(100);
		this.logged[0].HitCount.Should().Be(4);
	}

	// ── Per-target independence ────────────────────────────────────────────────

	[Fact]
	public void AoEDot_DifferentTargets_EachGetOwnEntry()
	{
		this.processor.Process(DotTick("MobA", "Rain", T0));
		this.processor.Process(DotTick("MobB", "Rain", T0.Add(TimeSpan.FromSeconds(1))));
		this.processor.Process(DotTick("MobC", "Rain", T0.Add(TimeSpan.FromSeconds(1))));

		this.logged.Should().HaveCount(3, "each target gets its own live entry");
		this.logged.Select(e => e.Opponent).Should().BeEquivalentTo("MobA", "MobB", "MobC");
	}

	[Fact]
	public void AoEDot_SecondRoundOfTicks_UpdatesExistingEntries()
	{
		this.processor.Process(DotTick("MobA", "Rain", T0, 20));
		this.processor.Process(DotTick("MobB", "Rain", T0, 20));
		this.processor.Process(DotTick("MobA", "Rain", T0.Add(TimeSpan.FromSeconds(3)), 20));
		this.processor.Process(DotTick("MobB", "Rain", T0.Add(TimeSpan.FromSeconds(3)), 20));

		this.logged.Should().HaveCount(2, "second round updates entries in place");
		this.logged.Should().AllSatisfy(e =>
		                                {
			                                e.TotalDamage.Should().Be(40);
			                                e.HitCount.Should().Be(2);
		                                });
	}

	// ── Crit propagation ─────────────────────────────────────────────────────

	[Fact]
	public void CritTick_SetsIsCritOnExistingEntry()
	{
		this.processor.Process(DotTick("Mob", "Plague", T0, crit: 0));
		this.processor.Process(DotTick("Mob", "Plague", T0.Add(TimeSpan.FromSeconds(3)), 30, 15));

		this.logged[0].IsCrit.Should().BeTrue("a crit on any tick marks the entry as crit");
	}

	[Fact]
	public void NoCrit_IsCritRemainsFalse()
	{
		this.processor.Process(DotTick("Mob", "Plague", T0));
		this.processor.Process(DotTick("Mob", "Plague", T0.Add(TimeSpan.FromSeconds(3))));

		this.logged[0].IsCrit.Should().BeFalse();
	}

	// ── Spell change closes the stack ─────────────────────────────────────────

	[Fact]
	public void DifferentSpellArrives_ClosesCurrentStackAndStartsNew()
	{
		this.processor.Process(DotTick("Mob", "Plague", T0, 30));
		this.processor.Process(DotTick("Mob", "Plague", T0.Add(TimeSpan.FromSeconds(3)), 30));

		this.processor.Process(DotTick("Mob", "Disease", T0.Add(TimeSpan.FromSeconds(4)), 20));

		this.logged.Should().HaveCount(2, "Disease creates a new entry after Plague stack is closed");
		this.logged[0].SpellName.Should().Be("Plague");
		this.logged[0].TotalDamage.Should().Be(60);
		this.logged[1].SpellName.Should().Be("Disease");
	}

	// ── Window timeout ────────────────────────────────────────────────────────

	[Fact]
	public void AfterWindowExpires_NewTickOfSameSpellStartsFreshEntry()
	{
		this.processor.Process(DotTick("Mob", "Plague", T0, 30));

		// 16s > DOT_STACK_WINDOW_SECONDS (15s) — window expires on next timestamped event
		this.processor.Process(IncomingHeal(T0.Add(TimeSpan.FromSeconds(16))));

		// New tick of same spell after expiry should create a fresh entry
		this.processor.Process(DotTick("Mob", "Plague", T0.Add(TimeSpan.FromSeconds(17)), 30));

		this.logged.Should().HaveCount(2, "stack expired; new entry created for the second cast");
		this.logged[1].HitCount.Should().Be(1);
		this.logged[1].TotalDamage.Should().Be(30);
	}

	[Fact]
	public void WithinWindow_TicksAccumulateAcrossGaps()
	{
		// 14s gap is within the 15s window — should still accumulate
		this.processor.Process(DotTick("Mob", "Plague", T0, 30));
		this.processor.Process(DotTick("Mob", "Plague", T0.Add(TimeSpan.FromSeconds(14)), 30));

		this.logged.Should().HaveCount(1);
		this.logged[0].TotalDamage.Should().Be(60);
	}

	// ── Reset ─────────────────────────────────────────────────────────────────

	[Fact]
	public void Reset_ClearsDotStackState_WithoutFiring()
	{
		this.processor.Process(DotTick("Mob", "Plague", T0));
		this.logged.Clear();

		this.processor.Reset();

		this.logged.Should().BeEmpty("Reset must not fire DamageLogged");

		this.processor.Process(DotTick("Mob", "Plague", T0.Add(TimeSpan.FromSeconds(1))));

		this.logged.Should().HaveCount(1, "reset discards the in-flight stack");
		this.logged[0].HitCount.Should().Be(1);
	}

	// ── Non-DoT hits are not routed through DoT stack ─────────────────────────

	[Fact]
	public void RegularSpellHit_DoesNotCreateDotEntry()
	{
		this.processor.Process(SpellHit("Mob", "Fireball", T0));

		this.logged.Should().HaveCount(1);
		this.logged[0].IsDotTick.Should().BeFalse();
	}
}
