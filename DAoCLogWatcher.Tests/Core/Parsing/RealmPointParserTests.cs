using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.Core.Parsing;
using FluentAssertions;

namespace DAoCLogWatcher.Tests.Core.Parsing;

public sealed class RealmPointParserTests
{
	// Feeds unrelated chatter lines until the pending entry flushes (the parser skips a
	// bounded number of non-confirmation lines before classifying the entry as Misc).
	private static RealmPointEntry? FlushWithChatter(RealmPointParser parser, string timestamp)
	{
		for(var i = 0; i < 8; i++)
		{
			if(parser.TryParse($"[{timestamp}] Unrelated chatter {i}", out var e))
			{
				return e;
			}
		}

		return null;
	}

	[Theory]
	[InlineData("[12:34:56] You get 1000 realm points for Campaign Quest!", 1000, RealmPointSource.CampaignQuest, null)]
	[InlineData("[23:59:59] You get 500 realm points for Tower Capture!", 500, RealmPointSource.Siege, "Tower Capture")]
	[InlineData("[00:00:01] You get 750 realm points for Keep Capture!", 750, RealmPointSource.Siege, "Keep Capture")]
	[InlineData("[14:22:33] You get 2500 realm points for Battle Tick!", 2500, RealmPointSource.Tick, "Battle Tick")]
	[InlineData("[08:15:42] You get 100 realm points for Assault Order!", 100, RealmPointSource.AssaultOrder, "Assault Order")]
	[InlineData("[19:45:12] You get 50 realm points for support activity in battle!", 50, RealmPointSource.SupportActivity, "Support Activity")]
	public void TryParse_ValidRealmPointLine_ParsesCorrectly(string line, int expectedPoints, RealmPointSource expectedSource, string? expectedSubSource)
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

		// Act - Unrelated chatter is skipped while awaiting a confirmation, up to the
		// bounded budget; once exhausted the pending entry flushes as Misc.
		RealmPointEntry? lastEntry = null;
		var emitted = false;
		for(var i = 0; i < 8; i++)
		{
			if(parser.TryParse($"[12:34:57] Some unrelated log line {i}.", out var e))
			{
				emitted = true;
				lastEntry = e;
				break;
			}
		}

		// Assert
		emitted.Should().BeTrue();
		lastEntry.Should().NotBeNull();
		lastEntry.Points.Should().Be(1234);
		lastEntry.Source.Should().Be(RealmPointSource.Misc);
	}

	[Fact]
	public void TryParse_CaptureSeparatedFromRpByChatter_ClassifiesAsSiege()
	{
		// Arrange
		var parser = new RealmPointParser();

		// Act - Bare RP line is buffered pending a confirmation
		parser.TryParse("[13:11:42] You get 1470 realm points!", out _).Should().BeFalse();

		// A spell-cast notice lands between the RP line and the capture broadcast
		parser.TryParse("[13:11:42] You are already casting a spell! You prepare this spell as a follow up!", out _).Should().BeFalse();

		var result = parser.TryParse("[13:11:42] The forces of Hibernia led by Pilzpower have captured Dun Crimthain!", out var entry);

		// Assert
		result.Should().BeTrue();
		entry.Should().NotBeNull();
		entry.Points.Should().Be(1470);
		entry.Source.Should().Be(RealmPointSource.Siege);
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

		// Unrelated chatter is skipped up to the bounded budget; feed enough to exhaust
		// it and flush the pending entry as Misc.
		RealmPointEntry? secondEntry = null;
		for(var i = 0; i < 8; i++)
		{
			if(parser.TryParse($"[12:34:57] Next line {i}", out var e))
			{
				secondEntry = e;
				break;
			}
		}

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
		var captureResult = parser.TryParse("[11:49:29] The forces of Midgard led by Wraelok have captured Hlidskialf Faste!", out var captureEntry);

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

		// Act - First sequence (unrelated chatter is skipped until the budget is exhausted)
		parser.TryParse("[10:00:00] You get 1000 realm points!", out _);
		var firstEntry = FlushWithChatter(parser, "10:00:01");

		// Second sequence should work independently
		parser.TryParse("[10:00:02] You get 2000 realm points!", out _);
		var secondEntry = FlushWithChatter(parser, "10:00:03");

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

	[Fact]
	public void TryParse_ArenaMatchLine_ClassifiedAsTimedMission()
	{
		var parser = new RealmPointParser();

		var result = parser.TryParse("[14:34:50] You get 2000 realm points for participating in an Arena Match!", out var entry);

		result.Should().BeTrue();
		entry.Should().NotBeNull();
		entry!.Points.Should().Be(2000);
		entry.Source.Should().Be(RealmPointSource.TimedMission);
		entry.SubSource.Should().Be("Arena Match");
	}

	[Fact]
	public void TryParse_ArenaMatchFollowedByWinStreak_BothCounted()
	{
		// From GitHub issue #1 — Arena sequence:
		//   [14:34:50] You get 2000 realm points for participating in an Arena Match!
		//   [14:34:50] You get an additional 2000 realm points due to your Win Streak!
		var parser = new RealmPointParser();

		parser.TryParse("[14:34:50] You get 2000 realm points for participating in an Arena Match!", out var arena);
		parser.TryParse("[14:34:50] You get an additional 2000 realm points due to your Win Streak!", out var streak);

		arena.Should().NotBeNull();
		arena!.Points.Should().Be(2000);
		arena.Source.Should().Be(RealmPointSource.TimedMission);
		arena.SubSource.Should().Be("Arena Match");

		streak.Should().NotBeNull();
		streak!.Points.Should().Be(2000);
		streak.Source.Should().Be(RealmPointSource.TimedMission);
		streak.SubSource.Should().Be("Win Streak");
	}

	[Fact]
	public void TryParse_SkirmishSequence_AllThreeCounted()
	{
		// From GitHub issue #1 — Skirmish chest event:
		//   [15:12:58] You get 250 realm points for completing your mission!
		//   [15:12:58] You get 250 realm points for reaching Tier 2 Participation!
		//   [15:12:58] You get an additional 250 realm points due to your Win Streak!
		var parser = new RealmPointParser();

		parser.TryParse("[15:12:58] You get 250 realm points for completing your mission!", out var mission);
		parser.TryParse("[15:12:58] You get 250 realm points for reaching Tier 2 Participation!", out var tier);
		parser.TryParse("[15:12:58] You get an additional 250 realm points due to your Win Streak!", out var streak);

		mission.Should().NotBeNull();
		mission!.Points.Should().Be(250);
		mission.SubSource.Should().Be("Mission Complete");

		tier.Should().NotBeNull();
		tier!.Points.Should().Be(250);
		tier.SubSource.Should().Be("Tier 2 Participation");

		streak.Should().NotBeNull();
		streak!.Points.Should().Be(250);
		streak.SubSource.Should().Be("Win Streak");
	}

	[Fact]
	public void TryParse_FairFightSequence_ClassifiedAsPlayerKill()
	{
		// Fair fight context line precedes the RP line
		var parser = new RealmPointParser();

		var contextResult = parser.TryParse("[21:42:08] You recently lost a fair fight, 8v8", out var contextEntry);
		contextResult.Should().BeFalse();
		contextEntry.Should().BeNull();

		var rpResult = parser.TryParse("[21:42:08] You get 800 realm points!", out var rpEntry);
		rpResult.Should().BeTrue();
		rpEntry.Should().NotBeNull();
		rpEntry!.Points.Should().Be(800);
		rpEntry.Source.Should().Be(RealmPointSource.CampaignQuest);
		rpEntry.SubSource.Should().Be("Fair Fight");
	}

	[Fact]
	public void TryParse_PlayerKillWithKillParticipation_ClassifiedAsPlayerKill()
	{
		// Kill participation line follows the RP line instead of XP line
		var parser = new RealmPointParser();

		parser.TryParse("[22:22:18] You get 2 realm points!", out _).Should().BeFalse();

		var result = parser.TryParse("[22:22:18] Kill participation: 100%", out var entry);
		result.Should().BeTrue();
		entry.Should().NotBeNull();
		entry!.Points.Should().Be(2);
		entry.Source.Should().Be(RealmPointSource.PlayerKill);
	}

	[Fact]
	public void TryParse_UnknownRpFollowedByAnotherRpLine_BothCounted()
	{
		// Defensive test: an unknown RP source goes pending, but the next line is
		// itself an RP line. Both must be counted (the pending as Misc, the next normally).
		var parser = new RealmPointParser();

		parser.TryParse("[10:00:00] You get 500 realm points!", out var first);
		first.Should().BeNull(); // pending

		parser.TryParse("[10:00:01] You get 300 realm points for completing your mission!", out var second);
		second.Should().NotBeNull();
		second!.Points.Should().Be(500);
		second.Source.Should().Be(RealmPointSource.Misc);

		// The mission line was buffered — emitted on next call
		parser.TryParse("[10:00:02] Some unrelated line", out var buffered);
		buffered.Should().NotBeNull();
		buffered!.Points.Should().Be(300);
		buffered.Source.Should().Be(RealmPointSource.TimedMission);
		buffered.SubSource.Should().Be("Mission Complete");
	}

	[Theory]
	[InlineData("[21:40:37] Patrol Hunt completed. Earned 175 RP and 20 Buff XP.", 175, "Patrol Hunt")]
	[InlineData("[08:44:48] Patrol Hunt completed. Earned 175 RP, 17 BP and 20 Buff XP.", 175, "Patrol Hunt")]
	[InlineData("[08:47:43] Treasure Hunt completed. Earned 500 RP, 50 BP and 20 Buff XP.", 500, "Treasure Hunt")]
	[InlineData("[09:06:00] Collection completed. Earned 210 RP, 17 BP and 20 Buff XP.", 210, "Collection")]
	[InlineData("[08:51:35] Capture the Flag won. Earned 385 RP, 35 BP and 40 Buff XP.", 385, "Capture the Flag")]
	[InlineData("[09:04:22] Vendetta won. Earned 385 RP, 35 BP and 40 Buff XP.", 385, "Vendetta")]
	[InlineData("[21:30:45] Vendetta lost. Participation reward: 175 RP and 40 Buff XP.", 175, "Vendetta")]
	public void TryParse_MissionPayoutLines_ClassifiedAsTimedMissionWithMode(string line, int expectedPoints, string expectedMode)
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
		entry.SubSource.Should().Be(expectedMode);
		entry.RawLine.Should().Be(line);
	}

	[Fact]
	public void TryParse_MissionPayoutLine_NotMatchedInsidePlayerChat()
	{
		// Arrange
		var parser = new RealmPointParser();

		// Act
		var result = parser.TryParse("[10:00:00] @@[Advice] Bob: Patrol Hunt completed. Earned 175 RP and 20 Buff XP.", out var entry);

		// Assert
		result.Should().BeFalse();
		entry.Should().BeNull();
	}

	[Fact]
	public void TryParse_MissionPayoutAfterPendingRp_BothCounted()
	{
		// A kill's RP line goes pending; the payout arrives before any confirmation.
		// The pending entry flushes as Misc and the payout must not be dropped.
		var parser = new RealmPointParser();

		parser.TryParse("[10:00:00] You get 500 realm points!", out var first);
		first.Should().BeNull();

		parser.TryParse("[10:00:01] Vendetta won. Earned 385 RP, 35 BP and 40 Buff XP.", out var flushed);
		flushed.Should().NotBeNull();
		flushed!.Points.Should().Be(500);
		flushed.Source.Should().Be(RealmPointSource.Misc);

		parser.TryParse("[10:00:02] Some unrelated line", out var payout);
		payout.Should().NotBeNull();
		payout!.Points.Should().Be(385);
		payout.Source.Should().Be(RealmPointSource.TimedMission);
		payout.SubSource.Should().Be("Vendetta");
	}

	[Fact]
	public void TryParse_VendettaLossSequence_FairFightAndParticipationRewardCountedOnce()
	{
		// Real sequence from chat.log (21:30:45): the fair-fight RP, then the mission participation reward.
		var parser = new RealmPointParser();

		parser.TryParse("[21:30:45] You recently lost a fair fight, 1v1", out var none).Should().BeFalse();
		none.Should().BeNull();

		parser.TryParse("[21:30:45] You get 100 realm points!", out var fairFight).Should().BeTrue();
		fairFight!.Points.Should().Be(100);
		fairFight.Source.Should().Be(RealmPointSource.CampaignQuest);
		fairFight.SubSource.Should().Be("Fair Fight");

		parser.TryParse("[21:30:45] Vendetta lost. Participation reward: 175 RP and 40 Buff XP.", out var reward).Should().BeTrue();
		reward!.Points.Should().Be(175);
		reward.Source.Should().Be(RealmPointSource.TimedMission);
		reward.SubSource.Should().Be("Vendetta");

		parser.TryParse("[21:30:46] Riantan was just killed by Loemiiee in Ellan Vannin.", out var extra).Should().BeFalse();
		extra.Should().BeNull();
	}

	[Theory]
	[InlineData("[12:33:18] You get an additional 15 realm points due to your winning streak!", 15, "Winning Streak")]
	[InlineData("[12:33:18] You get an additional 18 realm points due to your GroupFinder bonus!", 18, "GroupFinder Bonus")]
	public void TryParse_KillBonusLines_EmitDirectlyAsMiscWithSubSource(string line, int expectedPoints, string expectedSubSource)
	{
		// Arrange
		var parser = new RealmPointParser();

		// Act
		var result = parser.TryParse(line, out var entry);

		// Assert
		result.Should().BeTrue();
		entry.Should().NotBeNull();
		entry!.Points.Should().Be(expectedPoints);
		entry.Source.Should().Be(RealmPointSource.Misc);
		entry.SubSource.Should().Be(expectedSubSource);
	}

	[Fact]
	public void TryParse_KillWithBonusLines_BonusesDoNotDisturbKillClassification()
	{
		// Real sequence from chat.log (12:33:18): guild buff is folded into the kill, the other bonuses are separate entries.
		var parser = new RealmPointParser();

		parser.TryParse("[12:33:18] Sekk was just killed by Adusta in Hadrian's Wall.", out _);
		parser.TryParse("[12:33:18] You get an additional 3 realm points due to your guild's buff!", out var guild).Should().BeFalse();
		guild.Should().BeNull();

		parser.TryParse("[12:33:18] You get an additional 15 realm points due to your winning streak!", out var streak).Should().BeTrue();
		streak!.SubSource.Should().Be("Winning Streak");

		parser.TryParse("[12:33:18] You get an additional 18 realm points due to your GroupFinder bonus!", out var groupFinder).Should().BeTrue();
		groupFinder!.SubSource.Should().Be("GroupFinder Bonus");

		parser.TryParse("[12:33:18] You get 110 realm points!", out var kill).Should().BeFalse();
		kill.Should().BeNull();

		parser.TryParse("[12:33:18] You gain a total of 12,345 experience points.", out var confirmed).Should().BeTrue();
		confirmed!.Points.Should().Be(110);
		confirmed.Source.Should().Be(RealmPointSource.PlayerKill);
	}
}
