using DAoCLogWatcher.Core.Models;
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
}
