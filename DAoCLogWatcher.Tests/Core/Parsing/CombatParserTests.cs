using DAoCLogWatcher.Core.Parsing;
using FluentAssertions;

namespace DAoCLogWatcher.Tests.Core.Parsing;

public sealed class CombatParserTests
{
	private readonly CombatParser parser = new();

	// ── Damage Dealt ─────────────────────────────────────────────────────────

	[Fact]
	public void TryParse_DealtHitWithAbsorbed_ReturnsPendingThenFlushed()
	{
		// The hit is stored as pending; it resolves when the next unrelated line arrives
		var result = parser.TryParse("[21:06:19] You hit Vareth for 39 (-7) damage!", out var damage, out var heal, out _);

		result.Should().BeFalse("hit is pending until we know if a crit follows");
		damage.Should().BeNull();
		heal.Should().BeNull();

		// Next unrelated line flushes it
		var result2 = parser.TryParse("[21:06:25] Some other line", out var damage2, out var heal2, out _);
		result2.Should().BeTrue();
		damage2.Should().NotBeNull();
		damage2!.Target.Should().Be("Vareth");
		damage2.BaseDamage.Should().Be(39);
		damage2.Absorbed.Should().Be(7);
		damage2.CritDamage.Should().Be(0);
		damage2.IsDealt.Should().BeTrue();
		damage2.Timestamp.Should().Be(new TimeOnly(21, 6, 19));
	}

	[Fact]
	public void TryParse_DealtHitFollowedByCrit_EmitsMergedEvent()
	{
		parser.TryParse("[21:06:20] You hit Vareth for 39 (-7) damage!", out _, out _, out _);

		var result = parser.TryParse("[21:06:20] You critically hit for an additional 16 damage! (Crit Chance: 10%)", out var damage, out _, out _);

		result.Should().BeTrue();
		damage.Should().NotBeNull();
		damage!.BaseDamage.Should().Be(39);
		damage.CritDamage.Should().Be(16);
		damage.TotalDamage.Should().Be(55);
		damage.IsDealt.Should().BeTrue();
	}

	[Fact]
	public void TryParse_DealtHitNoAbsorbed_ParsesCorrectly()
	{
		parser.TryParse("[21:10:43] You hit the elder tangler for 532 damage!", out _, out _, out _);

		var result = parser.TryParse("[21:10:44] unrelated", out var damage, out _, out _);
		result.Should().BeTrue();
		damage!.BaseDamage.Should().Be(532);
		damage.Absorbed.Should().Be(0);
		damage.Target.Should().Be("the elder tangler");
	}

	[Fact]
	public void TryParse_ConsecutiveDealtHits_FlushesFirstOnSecond()
	{
		parser.TryParse("[21:06:40] You hit Drakthos for 250 (-193) damage!", out _, out _, out _);

		// Second hit flushes first (no crit for first)
		var result = parser.TryParse("[21:06:41] You hit Drakthos for 250 (-193) damage!", out var damage, out _, out _);
		result.Should().BeTrue();
		damage!.Target.Should().Be("Drakthos");
		damage.BaseDamage.Should().Be(250);
		damage.CritDamage.Should().Be(0);
	}

	// ── Damage Taken ─────────────────────────────────────────────────────────

	[Fact]
	public void TryParse_TakenHit_ReturnsPendingThenFlushed()
	{
		var result = parser.TryParse("[20:56:16] Grimskar hits you for 121 (-42) damage!", out var damage, out _, out _);

		result.Should().BeFalse("hit is pending — crit may follow");
		damage.Should().BeNull();

		var result2 = parser.TryParse("[20:56:17] unrelated", out var damage2, out _, out _);
		result2.Should().BeTrue();
		damage2!.Target.Should().Be("Grimskar");
		damage2.BaseDamage.Should().Be(121);
		damage2.Absorbed.Should().Be(42);
		damage2.IsDealt.Should().BeFalse();
	}

	[Fact]
	public void TryParse_TakenHitFollowedByNoTimestampCrit_EmitsMergedEvent()
	{
		parser.TryParse("[20:56:16] Grimskar hits you for 121 (-42) damage!", out _, out _, out _);

		// Taken crit has NO timestamp
		var result = parser.TryParse("Grimskar critically hits you for an additional 46 damage! (Crit Chance: 41%)", out var damage, out _, out _);

		result.Should().BeTrue();
		damage!.Target.Should().Be("Grimskar");
		damage.BaseDamage.Should().Be(121);
		damage.CritDamage.Should().Be(46);
		damage.TotalDamage.Should().Be(167);
		damage.IsDealt.Should().BeFalse();
	}

	[Fact]
	public void TryParse_TakenCritWithNoMatchingPending_IsIgnored()
	{
		// Orphan crit line with no preceding hit — should not crash or emit
		var result = parser.TryParse("Grimskar critically hits you for an additional 46 damage! (Crit Chance: 41%)", out var damage, out _, out _);

		result.Should().BeFalse();
		damage.Should().BeNull();
	}

	// ── Heals ─────────────────────────────────────────────────────────────────

	[Fact]
	public void TryParse_HealLine_EmitsHealEvent()
	{
		var result = parser.TryParse("[20:56:44] You are healed by Thendria for 582 hit points.", out _, out var heal, out _);

		result.Should().BeTrue();
		heal.Should().NotBeNull();
		heal!.Healer.Should().Be("Thendria");
		heal.HitPoints.Should().Be(582);
		heal.Timestamp.Should().Be(new TimeOnly(20, 56, 44));
	}

	[Fact]
	public void TryParse_HealLine_FlushesStaleDealtPending()
	{
		// A hit line followed by a heal (no crit) — pending hit should be flushed alongside the heal
		parser.TryParse("[21:00:00] You hit Enemy for 300 (-50) damage!", out _, out _, out _);

		var result = parser.TryParse("[21:00:01] You are healed by Ulfdan for 248 hit points.", out var damage, out var heal, out _);

		result.Should().BeTrue();
		heal.Should().NotBeNull();

		// The flushed damage is returned together with the heal
		damage.Should().NotBeNull();
		damage!.BaseDamage.Should().Be(300);
	}

	// ── Unrelated lines ───────────────────────────────────────────────────────

	[Fact]
	public void TryParse_UnrelatedLine_ReturnsFalse()
	{
		var result = parser.TryParse("[20:51:32] Quelris was just killed by Torven in Hadrian's Wall.", out var damage, out var heal, out _);

		result.Should().BeFalse();
		damage.Should().BeNull();
		heal.Should().BeNull();
	}

	[Fact]
	public void TryParse_EmptyLine_ReturnsFalse()
	{
		var result = parser.TryParse("   ", out var damage, out var heal, out _);

		result.Should().BeFalse();
		damage.Should().BeNull();
		heal.Should().BeNull();
	}

	// ── Spell name correlation ────────────────────────────────────────────────

	[Fact]
	public void TryParse_CastThenHitSameSecond_HitCarriesSpellName()
	{
		parser.TryParse("[21:06:19] You cast a Extinguish Lifeforce spell!", out _, out _, out _);
		parser.TryParse("[21:06:19] You hit Vareth for 39 (-7) damage!", out _, out _, out _);

		// Flush to resolve the pending hit
		var ev = parser.FlushPending();
		ev.Should().NotBeNull();
		ev!.SpellName.Should().Be("Extinguish Lifeforce");
	}

	[Fact]
	public void TryParse_MultipleBuffCastsThenNukeCastThenHit_UsesLastCastAsSpellName()
	{
		parser.TryParse("[21:06:21] You cast a Annihilate Strength spell!", out _, out _, out _);
		parser.TryParse("[21:06:21] You cast a Scatter Zeal spell!", out _, out _, out _);
		parser.TryParse("[21:06:21] You cast a Extinguish Lifeforce spell!", out _, out _, out _);
		parser.TryParse("[21:06:21] You hit Vareth for 39 (-7) damage!", out _, out _, out _);

		var ev = parser.FlushPending();
		ev!.SpellName.Should().Be("Extinguish Lifeforce");
	}

	[Fact]
	public void TryParse_HitWithNoPrecedingCast_SpellNameIsNull()
	{
		parser.TryParse("[21:06:19] You hit Vareth for 39 (-7) damage!", out _, out _, out _);

		var ev = parser.FlushPending();
		ev!.SpellName.Should().BeNull("no cast line preceded this hit");
	}

	[Fact]
	public void TryParse_CastInterruptedThenHit_SpellNameIsNull()
	{
		parser.TryParse("[21:06:19] You cast a Extinguish Lifeforce spell!", out _, out _, out _);
		parser.TryParse("[21:06:19] Vareth attacks you and your spell is interrupted!", out _, out _, out _);
		parser.TryParse("[21:06:20] You hit Vareth for 39 (-7) damage!", out _, out _, out _);

		var ev = parser.FlushPending();
		ev!.SpellName.Should().BeNull("cast was interrupted before the hit");
	}

	[Fact]
	public void TryParse_AeSpell_AllHitsCarrySpellName()
	{
		// AE spell: one cast, multiple simultaneous hits — all should get the spell name
		parser.TryParse("[21:16:01] You cast a Resonant Concussion spell!", out _, out _, out _);
		parser.TryParse("[21:16:01] You hit Draekon for 93 (-17) damage!", out _, out _, out _);

		// Flush first hit, start second
		parser.TryParse("[21:16:01] You hit Draekon for 93 (-17) damage!", out var first, out _, out _);
		first!.SpellName.Should().Be("Resonant Concussion", "first AE hit should carry spell name");

		// Flush second, start third
		parser.TryParse("[21:16:01] You hit Draekon for 93 (-17) damage!", out var second, out _, out _);
		second!.SpellName.Should().Be("Resonant Concussion", "second AE hit should also carry spell name");

		var third = parser.FlushPending();
		third!.SpellName.Should().Be("Resonant Concussion", "third AE hit should also carry spell name");
	}

	[Fact]
	public void TryParse_HitWithNoConfirmedSpell_OutsideWindow_SpellNameIsNull()
	{
		parser.TryParse("[21:06:19] You cast a Extinguish Lifeforce spell!", out _, out _, out _);

		// 5 seconds later — no earlier hit confirmed this as a damage spell (could be a buff)
		parser.TryParse("[21:06:24] You hit Vareth for 39 (-7) damage!", out _, out _, out _);

		var ev = parser.FlushPending();
		ev!.SpellName.Should().BeNull("no hit within 4.5s window confirmed this as a damage spell");
	}

	[Fact]
	public void TryParse_DotTicks_SubsequentTicksUseConfirmedSpellName()
	{
		// First tick within window — confirms the spell
		parser.TryParse("[21:06:19] You cast a Extinguish Lifeforce spell!", out _, out _, out _);
		parser.TryParse("[21:06:21] You hit Vareth for 39 (-7) damage!", out _, out _, out _);

		// Second tick 5s after cast — outside window, but spell is confirmed
		var result = parser.TryParse("[21:06:24] You hit Vareth for 39 (-7) damage!", out var flushed, out _, out _);
		result.Should().BeTrue("first pending should be flushed");
		flushed!.SpellName.Should().Be("Extinguish Lifeforce", "first tick confirmed the spell");

		var ev = parser.FlushPending();
		ev!.SpellName.Should().Be("Extinguish Lifeforce", "second tick uses confirmedDamageSpellName");
	}

	[Fact]
	public void TryParse_SpellCastFlushesStaleDealtPending()
	{
		// Hit is pending
		parser.TryParse("[21:06:19] You hit Vareth for 39 (-7) damage!", out _, out _, out _);

		// A new spell cast should flush the pending hit (no crit arrived)
		var result = parser.TryParse("[21:06:20] You cast a Extinguish Lifeforce spell!", out var flushed, out _, out _);
		result.Should().BeTrue("pending hit should have been flushed");
		flushed!.CritDamage.Should().Be(0);
		flushed.SpellName.Should().BeNull("the first hit had no spell associated");
	}

	// ── Bow shot attribution ──────────────────────────────────────────────────

	[Fact]
	public void TryParse_BowFireThenHit_HitCarriesShotName()
	{
		parser.TryParse("[00:24:49] You fire a Critical Shot 9!", out _, out _, out _);
		parser.TryParse("[00:24:50] You hit Kingleo for 73 (-32) damage!", out _, out _, out _);

		var ev = parser.FlushPending();
		ev.Should().NotBeNull();
		ev!.SpellName.Should().Be("Critical Shot 9");
		ev.IsWeaponAttack.Should().BeFalse();
	}

	[Fact]
	public void TryParse_BowFireWithPetCastBetweenFireAndHit_HitCarriesShotNameNotPetSpell()
	{
		// Real Hunter scenario: fire bow, game simultaneously logs pet summon + hit
		parser.TryParse("[00:31:30] You fire a Critical Shot 9!", out _, out _, out _);
		parser.TryParse("[00:31:31] You cast a Hunter's Companion spell!", out _, out _, out _);
		parser.TryParse("[00:31:31] You hit Hormei for 489 (-219) damage!", out _, out _, out _);

		var ev = parser.FlushPending();
		ev.Should().NotBeNull();
		ev!.SpellName.Should().Be("Critical Shot 9", "bow shot pre-announcement has priority over simultaneous pet spell cast");
	}

	[Fact]
	public void TryParse_TwoConsecutiveBowShots_EachHitCarriesItsOwnShotName()
	{
		// First shot
		parser.TryParse("[00:24:49] You fire a Critical Shot 9!", out _, out _, out _);
		parser.TryParse("[00:24:50] You hit Kingleo for 73 (-32) damage!", out _, out _, out _);
		parser.TryParse("[00:24:51] You fire a Tempestuous Shot 9!", out var first, out _, out _);
		first!.SpellName.Should().Be("Critical Shot 9");

		// Second shot
		parser.TryParse("[00:24:52] You hit Kingleo for 255 (-85) damage!", out _, out _, out _);
		var second = parser.FlushPending();
		second!.SpellName.Should().Be("Tempestuous Shot 9");
	}

	[Fact]
	public void TryParse_BowShotConfirmedThenHitOutsideWindow_UsesConfirmedBowShotName()
	{
		// First shot confirms bow shot name as the "confirmed" source
		parser.TryParse("[00:24:49] You fire a Critical Shot 9!", out _, out _, out _);
		parser.TryParse("[00:24:50] You hit Kingleo for 73 (-32) damage!", out _, out _, out _);

		// Hit well outside 4.5s — no pending bow shot, falls through to confirmed
		parser.TryParse("[00:25:20] You hit Kingleo for 35 damage!", out var first, out _, out _);
		first!.SpellName.Should().Be("Critical Shot 9");

		var second = parser.FlushPending();
		second!.SpellName.Should().Be("Critical Shot 9", "confirmed bow shot name persists for subsequent hits");
	}

	// ── FlushPending ──────────────────────────────────────────────────────────

	[Fact]
	public void FlushPending_WithDealtPending_ReturnsEventAndClearsPending()
	{
		parser.TryParse("[21:06:19] You hit Vareth for 39 (-7) damage!", out _, out _, out _);

		var ev = parser.FlushPending();
		ev.Should().NotBeNull();
		ev!.BaseDamage.Should().Be(39);

		parser.FlushPending().Should().BeNull("should be cleared after first flush");
	}

	[Fact]
	public void FlushPending_NoPending_ReturnsNull()
	{
		parser.FlushPending().Should().BeNull();
	}

	// ── Outgoing Heals ────────────────────────────────────────────────────────

	[Fact]
	public void TryParse_SelfHeal_ReturnsOutgoingHealWithTargetYourself()
	{
		parser.TryParse("[21:55:49] You heal yourself for 1060 hit points.", out _, out var heal, out _);

		heal.Should().NotBeNull();
		heal!.IsOutgoing.Should().BeTrue();
		heal.Target.Should().Be("yourself");
		heal.HitPoints.Should().Be(1060);
		heal.Timestamp.Should().Be(new TimeOnly(21, 55, 49));
	}

	[Fact]
	public void TryParse_HealOther_ReturnsOutgoingHealWithTargetName()
	{
		parser.TryParse("[21:55:49] You heal Pelgath for 1060 hit points!", out _, out var heal, out _);

		heal.Should().NotBeNull();
		heal!.IsOutgoing.Should().BeTrue();
		heal.Target.Should().Be("Pelgath");
		heal.HitPoints.Should().Be(1060);
	}

	[Fact]
	public void TryParse_OutgoingHeal_WithPendingTaken_ReturnsBothDamageAndHeal()
	{
		// Enemy hits player — pendingTaken is set
		parser.TryParse("[21:55:48] Pelgath hits you for 200 damage!", out _, out _, out _);

		// Player heals a group member — pendingTaken is flushed as damage AND heal is returned
		var result = parser.TryParse("[21:55:49] You heal Rendis for 500 hit points!", out var damage, out var heal, out _);

		result.Should().BeTrue();
		damage.Should().NotBeNull("flushed taken hit must be returned");
		damage!.IsDealt.Should().BeFalse();
		damage.BaseDamage.Should().Be(200);
		heal.Should().NotBeNull("outgoing heal must not be dropped when there is a pending taken hit");
		heal!.IsOutgoing.Should().BeTrue();
		heal.Target.Should().Be("Rendis");
		heal.HitPoints.Should().Be(500);
	}

	[Fact]
	public void TryParse_IncomingHeal_IsNotOutgoing()
	{
		parser.TryParse("[21:55:49] You are healed by Pelgath for 800 hit points.", out _, out var heal, out _);

		heal.Should().NotBeNull();
		heal!.IsOutgoing.Should().BeFalse();
		heal.Healer.Should().Be("Pelgath");
		heal.HitPoints.Should().Be(800);
	}

	[Fact]
	public void TryParse_IsFullyHealedLine_NoHealEvent()
	{
		var result = parser.TryParse("[21:55:49] Sylvorn is fully healed.", out _, out var heal, out _);

		heal.Should().BeNull();
	}

	// ── Style attribution ─────────────────────────────────────────────────────

	[Fact]
	public void TryParse_StylePerformedThenWeaponAttack_HitCarriesStyleName()
	{
		parser.TryParse("[13:00:40] You perform your Clan's Call perfectly! (+90, Growth Rate: 1.21)", out _, out _, out _);
		parser.TryParse("[13:00:40] You attack Templar with your Fang of Everlasting Twilight and hit for 235 damage! (Damage Modifier: 1772)", out _, out _, out _);

		var ev = parser.FlushPending();
		ev.Should().NotBeNull();
		ev!.StyleName.Should().Be("Clan's Call");
		ev.IsWeaponAttack.Should().BeTrue();
	}

	[Fact]
	public void TryParse_StylePerformed_MultiHit_AllHitsCarryStyleName()
	{
		// Savage/Berserker triple hit — all 3 swings share the same style
		parser.TryParse("[13:00:40] You perform your Clan's Call perfectly! (+90, Growth Rate: 1.21)", out _, out _, out _);
		parser.TryParse("[13:00:40] You attack Templar with your Fang of Everlasting Twilight and hit for 235 damage! (Damage Modifier: 1772)", out _, out _, out _);
		parser.TryParse("[13:00:40] You attack Templar with your Astral Mephitic Fang and hit for 157 damage! (Damage Modifier: 2094)", out var first, out _, out _);
		first.Should().NotBeNull();
		first!.StyleName.Should().Be("Clan's Call", "first hit in multi-hit should carry style name");

		parser.TryParse("[13:00:40] You attack Templar with your Fang of Everlasting Twilight and hit for 151 damage! (Damage Modifier: 1850)", out var second, out _, out _);
		second.Should().NotBeNull();
		second!.StyleName.Should().Be("Clan's Call", "second hit in multi-hit should carry style name");

		var third = parser.FlushPending();
		third.Should().NotBeNull();
		third!.StyleName.Should().Be("Clan's Call", "third hit in multi-hit should carry style name");
	}

	[Fact]
	public void TryParse_StyleFailed_HitHasNoStyleName()
	{
		parser.TryParse("[13:00:38] You fail to execute your Clan's Call perfectly!", out _, out _, out _);
		parser.TryParse("[13:00:38] You attack Templar with your Fang of Everlasting Twilight and hit for 127 damage! (Damage Modifier: 1553)", out _, out _, out _);

		var ev = parser.FlushPending();
		ev.Should().NotBeNull();
		ev!.StyleName.Should().BeNull("style failed so no style name should be attributed");
	}

	[Fact]
	public void TryParse_NoStyleLine_WeaponAttack_StyleNameIsNull()
	{
		parser.TryParse("[13:00:40] You attack Templar with your Fang of Everlasting Twilight and hit for 235 damage! (Damage Modifier: 1772)", out _, out _, out _);

		var ev = parser.FlushPending();
		ev.Should().NotBeNull();
		ev!.StyleName.Should().BeNull("plain swing with no style performed");
	}

	[Fact]
	public void TryParse_StylePerformed_ThenNextStyleOverridesPrevious()
	{
		// First style, first hit
		parser.TryParse("[13:00:38] You perform your Kelgor's Fist perfectly! (+50, Growth Rate: 1.10)", out _, out _, out _);
		parser.TryParse("[13:00:38] You attack Templar with your Fang of Everlasting Twilight and hit for 127 damage! (Damage Modifier: 1553)", out _, out _, out _);

		// Second style immediately after
		parser.TryParse("[13:00:40] You perform your Clan's Call perfectly! (+90, Growth Rate: 1.21)", out var first, out _, out _);
		first.Should().NotBeNull();
		first!.StyleName.Should().Be("Kelgor's Fist", "first hit should have first style");

		parser.TryParse("[13:00:40] You attack Templar with your Fang of Everlasting Twilight and hit for 235 damage! (Damage Modifier: 1772)", out _, out _, out _);
		var second = parser.FlushPending();
		second!.StyleName.Should().Be("Clan's Call", "second hit should have second style");
	}

	[Fact]
	public void TryParse_StylePerformed_OutsideOneSecondWindow_StyleNameIsNull()
	{
		// Style at 13:00:38 but hit at 13:00:45 (7 seconds later) — outside 6s attribution window
		parser.TryParse("[13:00:38] You perform your Clan's Call perfectly! (+90, Growth Rate: 1.21)", out _, out _, out _);
		parser.TryParse("[13:00:45] You attack Templar with your Fang of Everlasting Twilight and hit for 235 damage! (Damage Modifier: 1772)", out _, out _, out _);

		var ev = parser.FlushPending();
		ev.Should().NotBeNull();
		ev!.StyleName.Should().BeNull("hit is 7 seconds after style — outside 6s attribution window");
	}

	[Fact]
	public void TryParse_WeaponAttackWithMeleeCrit_CritDamageIncluded()
	{
		// Real game format: weapon hit followed by no-timestamp crit with target name
		parser.TryParse("[13:00:40] You attack Templar with your Astral Mephitic Fang and hit for 157 damage! (Damage Modifier: 2094)", out _, out _, out _);
		var result = parser.TryParse("You critically hit Templar for an additional 50 damage! (Crit Chance: 23%)", out var damage, out _, out _);

		result.Should().BeTrue();
		damage.Should().NotBeNull();
		damage!.BaseDamage.Should().Be(157);
		damage.CritDamage.Should().Be(50);
		damage.TotalDamage.Should().Be(207);
		damage.IsDealt.Should().BeTrue();
	}

	[Fact]
	public void TryParse_StyledWeaponAttackWithMeleeCrit_BothStyleAndCritPreserved()
	{
		parser.TryParse("[13:00:40] You perform your Clan's Call perfectly! (+90, Growth Rate: 1.21)", out _, out _, out _);
		parser.TryParse("[13:00:40] You attack Templar with your Fang of Everlasting Twilight and hit for 235 damage! (Damage Modifier: 1772)", out _, out _, out _);
		var result = parser.TryParse("You critically hit Templar for an additional 80 damage! (Crit Chance: 23%)", out var damage, out _, out _);

		result.Should().BeTrue();
		damage.Should().NotBeNull();
		damage!.StyleName.Should().Be("Clan's Call");
		damage.CritDamage.Should().Be(80);
		damage.TotalDamage.Should().Be(315);
	}

	[Fact]
	public void TryParse_YouHitWithNoSpellAttribution_TreatedAsWeaponAttack()
	{
		// "You hit X for N damage!" with no preceding spell cast → melee hit (e.g. off-hand swing)
		parser.TryParse("[13:00:40] You hit Templar for 150 damage!", out _, out _, out _);

		var ev = parser.FlushPending();
		ev.Should().NotBeNull();
		ev!.IsWeaponAttack.Should().BeTrue("no spell attribution means this is a melee hit");
		ev.SpellName.Should().BeNull();
	}

	[Fact]
	public void TryParse_YouHitWithNoSpellAttribution_WithActivestyle_CarriesStyleName()
	{
		// Off-hand hit right after a style performed
		parser.TryParse("[13:00:40] You perform your Kelgor's Fist perfectly! (+50, Growth Rate: 1.10)", out _, out _, out _);
		parser.TryParse("[13:00:40] You hit Templar for 150 damage!", out _, out _, out _);

		var ev = parser.FlushPending();
		ev.Should().NotBeNull();
		ev!.IsWeaponAttack.Should().BeTrue();
		ev.StyleName.Should().Be("Kelgor's Fist");
	}

	[Fact]
	public void TryParse_YouHitWithSpellAttribution_TreatedAsSpell()
	{
		// "You hit X for N damage!" WITH spell attribution → spell hit, not melee
		parser.TryParse("[13:00:38] You cast a Extinguish Lifeforce spell!", out _, out _, out _);
		parser.TryParse("[13:00:38] You hit Templar for 150 damage!", out _, out _, out _);

		var ev = parser.FlushPending();
		ev.Should().NotBeNull();
		ev!.IsWeaponAttack.Should().BeFalse("spell attribution means this is a spell hit");
		ev.SpellName.Should().Be("Extinguish Lifeforce");
	}

	// ── Style prepare (NPC/monster combat) ───────────────────────────────────

	[Fact]
	public void TryParse_StylePrepare_NpcHit_CarriesStyleName()
	{
		// "You prepare to perform a {style}!" fires before the weapon hit
		parser.TryParse("[14:42:56] You prepare to perform a Totemic Fear!", out _, out _, out _);
		parser.TryParse("[14:42:57] You attack Guardian with your Fang of Everlasting Twilight and hit for 163 (-48) damage! (Damage Modifier: 1997)", out _, out _, out _);

		var ev = parser.FlushPending();
		ev.Should().NotBeNull();
		ev!.StyleName.Should().Be("Totemic Fear");
		ev.IsWeaponAttack.Should().BeTrue();
	}

	[Fact]
	public void TryParse_StylePrepare_ThenFail_NpcHit_NoStyleName()
	{
		// When the style fails, the following hit should have no style
		parser.TryParse("[14:42:56] You prepare to perform a Totemic Fear!", out _, out _, out _);
		parser.TryParse("[14:42:58] You fail to execute your Totemic Fear perfectly!", out _, out _, out _);
		parser.TryParse("[14:42:58] You attack Guardian with your Fang of Everlasting Twilight and hit for 149 (-44) damage! (Damage Modifier: 1822)", out var damage, out _, out _);

		var ev = damage ?? parser.FlushPending();
		ev.Should().NotBeNull();
		ev!.StyleName.Should().BeNull("style failed so no attribution");
	}

	[Fact]
	public void TryParse_StylePrepare_AnPrefix_CarriesStyleName()
	{
		// Handles "an" article: "You prepare to perform an Uppercut!"
		parser.TryParse("[14:43:10] You prepare to perform an Uppercut!", out _, out _, out _);
		parser.TryParse("[14:43:11] You attack Mob with your Sword and hit for 100 damage! (Damage Modifier: 1000)", out _, out _, out _);

		var ev = parser.FlushPending();
		ev.Should().NotBeNull();
		ev!.StyleName.Should().Be("Uppercut");
	}

	// ── Style chain fallback ─────────────────────────────────────────────────

	[Fact]
	public void TryParse_StyleChainFallback_HitAttributedToOpeningStyle()
	{
		// "You must perform the Totemic Fear style before this one!" means
		// the chain was rejected and the opening style (Totemic Fear) executes instead.
		parser.TryParse("[14:56:28] You must perform the Totemic Fear style before this one!", out _, out _, out _);
		parser.TryParse("[14:56:28] You attack Druid with your Fang of Everlasting Twilight and hit for 180 (-31) damage! (Damage Modifier: 2204)", out _, out _, out _);

		var ev = parser.FlushPending();
		ev.Should().NotBeNull();
		ev!.StyleName.Should().Be("Totemic Fear");
		ev.IsWeaponAttack.Should().BeTrue();
	}

	[Fact]
	public void TryParse_StyleChainFallback_MultiHit_AllSwingsAttributed()
	{
		// Triple hit following a chain fallback — all swings get the style
		parser.TryParse("[14:56:30] You must perform the Totemic Fear style before this one!", out _, out _, out _);
		parser.TryParse("[14:56:30] You attack Druid with your Astral Mephitic Fang and hit for 174 (-30) damage! (Damage Modifier: 2311)", out _, out _, out _);
		parser.TryParse("[14:56:30] You attack Druid with your Fang of Everlasting Twilight and hit for 182 (-32) damage! (Damage Modifier: 2234)", out var hit2, out _, out _);

		hit2.Should().NotBeNull("second weapon attack flushes the first");
		hit2!.StyleName.Should().Be("Totemic Fear");

		var hit3 = parser.FlushPending();
		hit3.Should().NotBeNull();
		hit3!.StyleName.Should().Be("Totemic Fear");
	}
}
