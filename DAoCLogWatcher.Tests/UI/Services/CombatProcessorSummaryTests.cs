using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Models;
using DAoCLogWatcher.UI.Services;
using FluentAssertions;

namespace DAoCLogWatcher.Tests.UI.Services;

/// <summary>
/// Tests for CombatProcessor's accumulation into CombatSummary (damage, heals, misses).
/// </summary>
public sealed class CombatProcessorSummaryTests
{
	private readonly CombatSummary summary = new();
	private readonly CombatProcessor processor;

	public CombatProcessorSummaryTests()
	{
		this.processor = new CombatProcessor(this.summary);
	}

	private static readonly TimeOnly T0 = new(12, 0, 0);

	private static DamageLogLine SpellDealt(string target, string spell, int baseDmg = 100, int crit = 0, int absorbed = 0)
	{
		return new DamageLogLine("",
		                         new DamageEvent
		                         {
				                         Timestamp = T0,
				                         Opponent = target,
				                         BaseDamage = baseDmg,
				                         CritDamage = crit,
				                         Absorbed = absorbed,
				                         IsDealt = true,
				                         SpellName = spell,
				                         IsWeaponAttack = false
		                         });
	}

	private static DamageLogLine WeaponDealt(string target, string style, int baseDmg = 50)
	{
		return new DamageLogLine("",
		                         new DamageEvent
		                         {
				                         Timestamp = T0,
				                         Opponent = target,
				                         BaseDamage = baseDmg,
				                         Absorbed = 0,
				                         IsDealt = true,
				                         SpellName = style,
				                         StyleName = style,
				                         IsWeaponAttack = true
		                         });
	}

	private static DamageLogLine DotDealt(string target, string spell, int baseDmg = 30)
	{
		return new DamageLogLine("",
		                         new DamageEvent
		                         {
				                         Timestamp = T0,
				                         Opponent = target,
				                         BaseDamage = baseDmg,
				                         Absorbed = 0,
				                         IsDealt = true,
				                         SpellName = spell,
				                         IsWeaponAttack = false,
				                         IsDotTick = true
		                         });
	}

	private static DamageLogLine DamageTaken(string attacker, int baseDmg = 80)
	{
		return new DamageLogLine("",
		                         new DamageEvent
		                         {
				                         Timestamp = T0,
				                         Opponent = attacker,
				                         BaseDamage = baseDmg,
				                         Absorbed = 0,
				                         IsDealt = false,
				                         IsWeaponAttack = false
		                         });
	}

	private static HealLogLine OutgoingHeal(string target, int hp)
	{
		return new HealLogLine("",
		                       new HealEvent
		                       {
				                       Timestamp = T0,
				                       HitPoints = hp,
				                       IsOutgoing = true,
				                       Target = target
		                       });
	}

	private static HealLogLine IncomingHeal(string healer, int hp)
	{
		return new HealLogLine("",
		                       new HealEvent
		                       {
				                       Timestamp = T0,
				                       HitPoints = hp,
				                       IsOutgoing = false,
				                       Healer = healer
		                       });
	}

	private static MissLogLine SpellResist()
	{
		return new MissLogLine("",
		                       new MissEvent
		                       {
				                       Timestamp = T0,
				                       IsSpell = true
		                       });
	}

	private static MissLogLine MeleeMiss()
	{
		return new MissLogLine("",
		                       new MissEvent
		                       {
				                       Timestamp = T0,
				                       IsSpell = false
		                       });
	}

	// ── Damage dealt ──────────────────────────────────────────────────────────

	[Fact]
	public void SpellHit_AccumulatesTotalDamageAndHitCount()
	{
		this.processor.Process(SpellDealt("Mob", "Fireball", 200));

		this.summary.TotalDamageDealt.Should().Be(200);
		this.summary.HitCount.Should().Be(1);
		this.summary.SpellHitCount.Should().Be(1);
		this.summary.MeleeHitCount.Should().Be(0);
	}

	[Fact]
	public void WeaponHit_AccumulatesAndCountsAsMelee()
	{
		this.processor.Process(WeaponDealt("Mob", "Slash", 75));

		this.summary.TotalDamageDealt.Should().Be(75);
		this.summary.HitCount.Should().Be(1);
		this.summary.MeleeHitCount.Should().Be(1);
		this.summary.SpellHitCount.Should().Be(0);
	}

	[Fact]
	public void CritHit_IncrementsCritCountAndTotalDamage()
	{
		this.processor.Process(SpellDealt("Mob", "Fireball", 150, 50));

		this.summary.TotalDamageDealt.Should().Be(200);
		this.summary.CritCount.Should().Be(1);
		this.summary.HitCount.Should().Be(1);
	}

	[Fact]
	public void AbsorbedDamage_AccumulatesTotalAbsorbed()
	{
		this.processor.Process(SpellDealt("Mob", "Fireball", 100, absorbed: 40));

		this.summary.TotalAbsorbed.Should().Be(40);
	}

	[Fact]
	public void DamageByTarget_AccumulatesPerOpponent()
	{
		this.processor.Process(SpellDealt("OrcA", "Fireball", 100));
		this.processor.Process(SpellDealt("OrcA", "Fireball", 50));
		this.processor.Process(SpellDealt("OrcB", "Fireball", 200));

		this.summary.DamageByTarget["OrcA"].Should().Be(150);
		this.summary.DamageByTarget["OrcB"].Should().Be(200);
	}

	[Fact]
	public void DamageBySpell_AccumulatesPerSpell()
	{
		this.processor.Process(SpellDealt("Mob", "Fireball", 100));
		this.processor.Process(SpellDealt("Mob", "Fireball", 80));
		this.processor.Process(SpellDealt("Mob", "Lightning", 150));

		this.summary.DamageBySpell["Fireball"].TotalDamage.Should().Be(180);
		this.summary.DamageBySpell["Fireball"].HitCount.Should().Be(2);
		this.summary.DamageBySpell["Lightning"].TotalDamage.Should().Be(150);
	}

	[Fact]
	public void WeaponDamageBySpell_UsesStyleNameAsKey()
	{
		this.processor.Process(WeaponDealt("Mob", "Slash", 60));

		this.summary.DamageBySpell.Should().ContainKey("Slash");
		this.summary.DamageBySpell["Slash"].TotalDamage.Should().Be(60);
	}

	// ── Damage taken ──────────────────────────────────────────────────────────

	[Fact]
	public void IncomingDamage_AccumulatesTotalDamageTaken()
	{
		this.processor.Process(DamageTaken("Orc", 80));
		this.processor.Process(DamageTaken("Orc", 20));

		this.summary.TotalDamageTaken.Should().Be(100);
		this.summary.HitCount.Should().Be(0, "incoming damage does not count as outgoing hits");
	}

	[Fact]
	public void IncomingDamage_AccumulatesDamageTakenByAttacker()
	{
		this.processor.Process(DamageTaken("OrcA", 50));
		this.processor.Process(DamageTaken("OrcB", 70));
		this.processor.Process(DamageTaken("OrcA", 30));

		this.summary.DamageTakenByAttacker["OrcA"].Should().Be(80);
		this.summary.DamageTakenByAttacker["OrcB"].Should().Be(70);
	}

	// ── DoT ticks ─────────────────────────────────────────────────────────────

	[Fact]
	public void DotTick_AccumulatesTotalDamageDealt()
	{
		this.processor.Process(DotDealt("Mob", "Plague", 30));
		this.processor.Process(DotDealt("Mob", "Plague", 30));

		this.summary.TotalDamageDealt.Should().Be(60);
		this.summary.HitCount.Should().Be(2);
	}

	// ── Heals ─────────────────────────────────────────────────────────────────

	[Fact]
	public void OutgoingHeal_AccumulatesTotalHealingDoneAndByTarget()
	{
		this.processor.Process(OutgoingHeal("Ally", 200));
		this.processor.Process(OutgoingHeal("Ally", 100));
		this.processor.Process(OutgoingHeal("Self", 50));

		this.summary.TotalHealingDone.Should().Be(350);
		this.summary.HealsByTarget["Ally"].Should().Be(300);
		this.summary.HealsByTarget["Self"].Should().Be(50);
	}

	[Fact]
	public void IncomingHeal_AccumulatesTotalHealsReceivedAndByHealer()
	{
		this.processor.Process(IncomingHeal("Cleric", 180));
		this.processor.Process(IncomingHeal("Cleric", 120));
		this.processor.Process(IncomingHeal("Friar", 90));

		this.summary.TotalHealsReceived.Should().Be(390);
		this.summary.HealsByHealer["Cleric"].Should().Be(300);
		this.summary.HealsByHealer["Friar"].Should().Be(90);
	}

	// ── Misses and resists ────────────────────────────────────────────────────

	[Fact]
	public void SpellResist_IncrementsSpellResistCount()
	{
		this.processor.Process(SpellResist());
		this.processor.Process(SpellResist());

		this.summary.SpellResistCount.Should().Be(2);
		this.summary.MeleeMissCount.Should().Be(0);
	}

	[Fact]
	public void MeleeMiss_IncrementsMeleeMissCount()
	{
		this.processor.Process(MeleeMiss());

		this.summary.MeleeMissCount.Should().Be(1);
		this.summary.SpellResistCount.Should().Be(0);
	}

	// ── Reset ─────────────────────────────────────────────────────────────────

	[Fact]
	public void Reset_ClearsAllSummaryStats()
	{
		this.processor.Process(SpellDealt("Mob", "Fireball", 100, 20, 10));
		this.processor.Process(WeaponDealt("Mob", "Slash"));
		this.processor.Process(DamageTaken("Orc", 50));
		this.processor.Process(OutgoingHeal("Ally", 200));
		this.processor.Process(IncomingHeal("Cleric", 100));
		this.processor.Process(SpellResist());
		this.processor.Process(MeleeMiss());

		this.processor.Reset();

		this.summary.TotalDamageDealt.Should().Be(0);
		this.summary.TotalDamageTaken.Should().Be(0);
		this.summary.TotalHealingDone.Should().Be(0);
		this.summary.TotalHealsReceived.Should().Be(0);
		this.summary.HitCount.Should().Be(0);
		this.summary.CritCount.Should().Be(0);
		this.summary.TotalAbsorbed.Should().Be(0);
		this.summary.SpellHitCount.Should().Be(0);
		this.summary.MeleeHitCount.Should().Be(0);
		this.summary.SpellResistCount.Should().Be(0);
		this.summary.MeleeMissCount.Should().Be(0);
		this.summary.DamageByTarget.Should().BeEmpty();
		this.summary.DamageBySpell.Should().BeEmpty();
		this.summary.HealsByTarget.Should().BeEmpty();
		this.summary.HealsByHealer.Should().BeEmpty();
		this.summary.DamageTakenByAttacker.Should().BeEmpty();
	}
}
