using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.Core.Parsing;
using FluentAssertions;

namespace DAoCLogWatcher.Tests.Core.Parsing;

public sealed class RealmPointParserTests
{
    [Theory]
    [InlineData("[12:34:56] You get 1000 realm points for Campaign Quest!", 1000, RealmPointSource.CampaignQuest, null)]
    [InlineData("[23:59:59] You get 500 realm points for Tower Capture!", 500, RealmPointSource.Siege, "Tower Capture")]
    [InlineData("[00:00:01] You get 750 realm points for Keep Capture!", 750, RealmPointSource.Siege, "Keep Capture")]
    [InlineData("[14:22:33] You get 2500 realm points for Battle Tick!", 2500, RealmPointSource.Tick, "Battle Tick")]
    [InlineData("[08:15:42] You get 100 realm points for Assault Order!", 100, RealmPointSource.AssaultOrder, "Assault Order")]
    [InlineData("[19:45:12] You get 50 realm points for support activity in battle!", 50, RealmPointSource.SupportActivity, "Support Activity")]
    public void TryParse_ValidRealmPointLine_ParsesCorrectly(string line, int expectedPoints, RealmPointSource expectedSource, string expectedSubSource)
    {
        // Arrange
        var parser = new RealmPointParser();

        // Act
        var result = parser.TryParse(line, out var entry);

        // Assert
        result.Should().BeTrue();
        entry.Should().NotBeNull();
        entry!.Points.Should().Be(expectedPoints);
        entry.Source.Should().Be(expectedSource);
        entry.SubSource.Should().Be(expectedSubSource);
        entry.Timestamp.Should().NotBe(default);
        entry.RawLine.Should().Be(line);
    }

    [Theory]
    [InlineData("[12:34:56] You get 1234 realm points!")]
    public void TryParse_PlayerKillLine_RequiresTwoLineSequence(string line)
    {
        // Arrange
        var parser = new RealmPointParser();

        // Act - First line should be held pending
        var firstResult = parser.TryParse(line, out var firstEntry);

        // Assert - First parse returns false (waiting for participation line)
        firstResult.Should().BeFalse();
        firstEntry.Should().BeNull();

        // Act - XP gain line confirms this was a player kill
        var secondResult = parser.TryParse("[12:34:57] You gain a total of 9,599,247 experience points.", out var secondEntry);

        // Assert - Second parse emits the pending entry
        secondResult.Should().BeTrue();
        secondEntry.Should().NotBeNull();
        secondEntry!.Points.Should().Be(1234);
        secondEntry.Source.Should().Be(RealmPointSource.PlayerKill);
    }

    [Fact]
    public void TryParse_UnknownNextLine_ClassifiesAsMisc()
    {
        // Arrange
        var parser = new RealmPointParser();

        // Act - Ambiguous RP line is buffered
        var firstResult = parser.TryParse("[12:34:56] You get 1234 realm points!", out var firstEntry);
        firstResult.Should().BeFalse();
        firstEntry.Should().BeNull();

        // Act - Unrelated next line cannot confirm kill or capture
        var secondResult = parser.TryParse("[12:34:57] Some unrelated log line.", out var secondEntry);

        // Assert
        secondResult.Should().BeTrue();
        secondEntry.Should().NotBeNull();
        secondEntry!.Points.Should().Be(1234);
        secondEntry.Source.Should().Be(RealmPointSource.Misc);
    }

    [Fact]
    public void TryParse_RelicCaptureSequence_ParsesCorrectly()
    {
        // Arrange
        var parser = new RealmPointParser();

        // Act - First line announces relic capture
        var relicLineResult = parser.TryParse("[10:00:00] Albion has stored the Strength of Hibernia", out var relicEntry);

        // Assert - Relic line doesn't produce entry yet
        relicLineResult.Should().BeFalse();
        relicEntry.Should().BeNull();

        // Act - Next RP line should be marked as RelicCapture
        var rpLineResult = parser.TryParse("[10:00:01] You get 5000 realm points!", out var rpEntry);

        // Assert
        rpLineResult.Should().BeTrue();
        rpEntry.Should().NotBeNull();
        rpEntry!.Points.Should().Be(5000);
        rpEntry.Source.Should().Be(RealmPointSource.RelicCapture);
    }

    [Theory]
    [InlineData("[12:34:56] You get an additional 100 realm points for realm rank!")]
    [InlineData("[12:34:56] You get an additional 50 realm points for your guild's buff!")]
    public void TryParse_BonusLines_AreSkipped(string bonusLine)
    {
        // Arrange
        var parser = new RealmPointParser();

        // Act
        var result = parser.TryParse(bonusLine, out var entry);

        // Assert
        result.Should().BeFalse();
        entry.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Random log line without RP")]
    [InlineData("[12:34:56] You killed an enemy")]
    [InlineData("Not a valid log line")]
    public void TryParse_InvalidLines_ReturnsFalse(string line)
    {
        // Arrange
        var parser = new RealmPointParser();

        // Act
        var result = parser.TryParse(line, out var entry);

        // Assert
        result.Should().BeFalse();
        entry.Should().BeNull();
    }

    [Fact]
    public void TryParse_TimestampParsing_ExtractsCorrectTime()
    {
        // Arrange
        var parser = new RealmPointParser();
        var line = "[14:22:33] You get 1000 realm points for Campaign Quest!";

        // Act
        parser.TryParse(line, out var entry);

        // Assert
        entry.Should().NotBeNull();
        entry!.Timestamp.Hour.Should().Be(14);
        entry.Timestamp.Minute.Should().Be(22);
        entry.Timestamp.Second.Should().Be(33);
    }

    [Theory]
    [InlineData("[12:34:56] You get 0 realm points!", 0)]
    [InlineData("[12:34:56] You get 999999 realm points!", 999999)]
    public void TryParse_EdgeCasePoints_ParsesCorrectly(string line, int expectedPoints)
    {
        // Arrange
        var parser = new RealmPointParser();

        // Act
        var result = parser.TryParse(line, out var entry);

        // Assert - Even with 0 points, parsing should work
        result.Should().BeFalse(); // Returns false because it's waiting for participation check

        // Parse another line to trigger emission
        parser.TryParse("[12:34:57] Next line", out var secondEntry);
        secondEntry.Should().NotBeNull();
        secondEntry!.Points.Should().Be(expectedPoints);
    }

    [Fact]
    public void TryParse_MultipleParserInstances_AreIndependent()
    {
        // Arrange
        var parser1 = new RealmPointParser();
        var parser2 = new RealmPointParser();

        // Act - Parser 1 starts waiting for relic
        parser1.TryParse("[10:00:00] Albion has stored the Strength of Hibernia", out _);

        // Parser 2 should not be affected
        var result = parser2.TryParse("[10:00:01] You get 1000 realm points!", out var entry);

        // Assert - Parser 2 should be waiting for participation, not treating as relic
        result.Should().BeFalse();

        // Complete parser2's sequence
        parser2.TryParse("[10:00:02] You gain a total of 5,000 experience points.", out var entry2);
        entry2.Should().NotBeNull();
        entry2!.Source.Should().Be(RealmPointSource.PlayerKill);
    }

    [Fact]
    public void TryParse_PlayerKillWithXpGuildBonus_ClassifiesAsPlayerKill()
    {
        // Arrange - sequence: RP line → XP Guild Bonus → XP gain
        var parser = new RealmPointParser();

        parser.TryParse("[20:56:19] You get 51 realm points!", out _).Should().BeFalse();
        parser.TryParse("[20:56:19] XP Guild Bonus: 160,671", out var intermediate).Should().BeFalse();
        intermediate.Should().BeNull();

        var result = parser.TryParse("You gain a total of 3,374,108 experience points.", out var entry);

        result.Should().BeTrue();
        entry.Should().NotBeNull();
        entry!.Points.Should().Be(51);
        entry.Source.Should().Be(RealmPointSource.PlayerKill);
    }

    [Fact]
    public void TryParse_PlayerKillWithoutXpGuildBonus_ClassifiesAsPlayerKill()
    {
        // Arrange - sequence: RP line → XP gain (no Guild Bonus line)
        var parser = new RealmPointParser();

        parser.TryParse("[20:56:19] You get 51 realm points!", out _).Should().BeFalse();

        var result = parser.TryParse("You gain a total of 3,374,108 experience points.", out var entry);

        result.Should().BeTrue();
        entry.Should().NotBeNull();
        entry!.Points.Should().Be(51);
        entry.Source.Should().Be(RealmPointSource.PlayerKill);
    }

    [Fact]
    public void TryParse_SiegeCaptureSequence_ParsesCorrectly()
    {
        // Arrange
        var parser = new RealmPointParser();

        // Act - RP line comes first (no reason → pending)
        var rpResult = parser.TryParse("[11:49:29] You get 630 realm points!", out var rpEntry);

        // Assert - Held pending
        rpResult.Should().BeFalse();
        rpEntry.Should().BeNull();

        // Act - Capture announcement follows
        var captureResult = parser.TryParse("[11:49:29] The forces of Midgard led by Boomalaka have captured Hlidskialf Faste!", out var captureEntry);

        // Assert
        captureResult.Should().BeTrue();
        captureEntry.Should().NotBeNull();
        captureEntry!.Points.Should().Be(630);
        captureEntry.Source.Should().Be(RealmPointSource.Siege);
    }

    [Fact]
    public void TryParse_StateMachine_ResetsAfterEmission()
    {
        // Arrange
        var parser = new RealmPointParser();

        // Act - First sequence
        parser.TryParse("[10:00:00] You get 1000 realm points!", out _);
        parser.TryParse("[10:00:01] Trigger line", out var firstEntry);

        // Second sequence should work independently
        parser.TryParse("[10:00:02] You get 2000 realm points!", out _);
        parser.TryParse("[10:00:03] Trigger line", out var secondEntry);

        // Assert
        firstEntry.Should().NotBeNull();
        firstEntry!.Points.Should().Be(1000);

        secondEntry.Should().NotBeNull();
        secondEntry!.Points.Should().Be(2000);
    }

    [Theory]
    [InlineData("[11:24:03] You get 250 realm points for completing your mission!", 250, "Mission Complete")]
    [InlineData("[11:24:03] You get 250 realm points for reaching Tier 2 Participation!", 250, "Tier 2 Participation")]
    [InlineData("[11:39:49] You get 3500 realm points for reaching Tier 3 Participation!", 3500, "Tier 3 Participation")]
    [InlineData("[11:39:49] You get 11 realm points for reaching Tier 1 Participation!", 11, "Tier 1 Participation")]
    [InlineData("[08:00:00] You get 500 realm points for War Supplies!", 500, "War Supplies")]
    public void TryParse_TimedMissionLines_ClassifiedAsTimedMission(string line, int expectedPoints, string expectedSubSource)
    {
        // Arrange
        var parser = new RealmPointParser();

        // Act
        var result = parser.TryParse(line, out var entry);

        // Assert
        result.Should().BeTrue();
        entry.Should().NotBeNull();
        entry!.Points.Should().Be(expectedPoints);
        entry.Source.Should().Be(RealmPointSource.TimedMission);
        entry.SubSource.Should().Be(expectedSubSource);
    }

    [Fact]
    public void TryParse_WinStreakLine_ClassifiedAsTimedMission()
    {
        // Arrange
        var parser = new RealmPointParser();

        // Act
        var result = parser.TryParse("[12:01:28] You get an additional 150 realm points due to your Win Streak!", out var entry);

        // Assert
        result.Should().BeTrue();
        entry.Should().NotBeNull();
        entry!.Points.Should().Be(150);
        entry.Source.Should().Be(RealmPointSource.TimedMission);
        entry.SubSource.Should().Be("Win Streak");
    }

    [Fact]
    public void TryParse_WinStreakLine_NotSkippedLikeRealmRankBonus()
    {
        // Win Streak uses "an additional" phrasing but must NOT be filtered like realm rank / guild bonus
        var parser = new RealmPointParser();

        var result = parser.TryParse("[12:01:28] You get an additional 150 realm points due to your Win Streak!", out var entry);

        result.Should().BeTrue();
        entry.Should().NotBeNull();
    }

    [Fact]
    public void TryParse_NonTimedMissionSource_HasNullSubSource()
    {
        // Arrange
        var parser = new RealmPointParser();

        // Act
        parser.TryParse("[12:34:56] You get 1000 realm points for Campaign Quest!", out var entry);

        // Assert
        entry.Should().NotBeNull();
        entry!.SubSource.Should().BeNull();
    }

    [Fact]
    public void TryParse_RepairLine_EmitsDirectlyWithRepairSubSource()
    {
        // Repair has an explicit reason so it must bypass the pending state and emit immediately
        var parser = new RealmPointParser();

        var result = parser.TryParse("[10:00:00] You get 75 realm points for repairing!", out var entry);

        result.Should().BeTrue();
        entry.Should().NotBeNull();
        entry!.Source.Should().Be(RealmPointSource.Misc);
        entry.SubSource.Should().Be("Repair");
    }
}

