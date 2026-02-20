using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.Core.Parsing;
using FluentAssertions;

namespace DAoCLogWatcher.Tests.Core.Parsing;

public sealed class RealmPointParserTests
{
    [Theory]
    [InlineData("[12:34:56] You get 1000 realm points for Campaign Quest!", 1000, RealmPointSource.CampaignQuest)]
    [InlineData("[23:59:59] You get 500 realm points for Tower Capture!", 500, RealmPointSource.Siege)]
    [InlineData("[00:00:01] You get 750 realm points for Keep Capture!", 750, RealmPointSource.Siege)]
    [InlineData("[14:22:33] You get 2500 realm points for Battle Tick!", 2500, RealmPointSource.Tick)]
    [InlineData("[08:15:42] You get 100 realm points for Assault Order!", 100, RealmPointSource.AssaultOrder)]
    [InlineData("[19:45:12] You get 50 realm points for support activity in battle!", 50, RealmPointSource.SupportActivity)]
    public void TryParse_ValidRealmPointLine_ParsesCorrectly(string line, int expectedPoints, RealmPointSource expectedSource)
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

        // Act - Second line triggers emission
        var secondResult = parser.TryParse("[12:34:57] Some other line", out var secondEntry);

        // Assert - Second parse emits the pending entry
        secondResult.Should().BeTrue();
        secondEntry.Should().NotBeNull();
        secondEntry!.Points.Should().Be(1234);
        secondEntry.Source.Should().Be(RealmPointSource.PlayerKill);
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
        parser2.TryParse("[10:00:02] Other line", out var entry2);
        entry2.Should().NotBeNull();
        entry2!.Source.Should().Be(RealmPointSource.PlayerKill);
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
}
