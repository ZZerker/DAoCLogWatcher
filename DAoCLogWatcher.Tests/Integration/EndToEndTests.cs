using System.Text;
using DAoCLogWatcher.Core;
using DAoCLogWatcher.Core.Models;
using FluentAssertions;

namespace DAoCLogWatcher.Tests.Integration;

public sealed class EndToEndTests: IDisposable
{
	private readonly string testLogFilePath;
	private readonly List<string> createdFiles = new();

	public EndToEndTests()
	{
		this.testLogFilePath = Path.Combine(Path.GetTempPath(), $"integration_test_{Guid.NewGuid()}.log");
		this.createdFiles.Add(this.testLogFilePath);
	}

	public void Dispose()
	{
		foreach(var file in this.createdFiles)
		{
			if(File.Exists(file))
			{
				try
				{
					File.Delete(file);
				}
				catch
				{
					/* Ignore cleanup errors */
				}
			}
		}
	}

	[Fact]
	public async Task FullFlow_WithMixedRPSources_PercentagesSumTo100()
	{
		// Arrange - Create realistic log file with various RP sources
		var logContent = CreateRealisticLogFile();
		await File.WriteAllTextAsync(this.testLogFilePath, logContent);

		var summary = new TestSummary();
		var watcher = new LogWatcher(this.testLogFilePath, enableTimeFiltering: false);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

		// Act - Process all log entries
		await foreach(var logLine in watcher.WatchAsync(cts.Token))
		{
			if(logLine is RealmPointLogLine { Entry: var rpEntry })
			{
				summary.ProcessEntry(rpEntry);
			}
		}

		// Assert - Verify data consistency
		summary.IndividualRPsSum.Should().Be(summary.TotalRealmPoints, "Individual category RPs must sum exactly to TotalRealmPoints");

		summary.TotalPercentage.Should().BeApproximately(100.0, 0.01, "All percentages should sum to 100% (within rounding)");

		summary.TotalRealmPoints.Should().BeGreaterThan(0, "Should have processed some realm points");

		Console.WriteLine($"✓ Processed {summary.EntryCount} entries, {summary.TotalRealmPoints} RPs");
		Console.WriteLine($"✓ Percentages sum to {summary.TotalPercentage:F2}%");
		Console.WriteLine($"✓ Individual RPs: {summary.IndividualRPsSum} = Total: {summary.TotalRealmPoints}");
	}

	[Fact]
	public async Task FullFlow_WithOnlyPlayerKills_Percentage100()
	{
		// Arrange - Log with player kills (need extra lines to trigger parser state machine)
		var logContent = @"*** Chat Log Opened: Mon Jan 15 10:00:00 2024
[10:00:01] You get 1000 realm points!
[10:00:02] You gain a total of 10,000 experience points.
[10:00:05] You get 500 realm points!
[10:00:06] You gain a total of 5,000 experience points.
[10:00:10] You get 750 realm points!
[10:00:11] You gain a total of 7,500 experience points.
";
		await File.WriteAllTextAsync(this.testLogFilePath, logContent);

		var summary = new TestSummary();
		var watcher = new LogWatcher(this.testLogFilePath, enableTimeFiltering: false);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

		// Act
		await foreach(var logLine in watcher.WatchAsync(cts.Token))
		{
			if(logLine is RealmPointLogLine { Entry: var rpEntry })
			{
				summary.ProcessEntry(rpEntry);
			}
		}

		// Assert
		summary.TotalRealmPoints.Should().Be(2250); // 1000 + 500 + 750
		summary.PlayerKillsRp.Should().Be(2250);
		summary.PlayerKillsPercentage.Should().BeApproximately(100.0, 0.01);
		summary.TotalPercentage.Should().BeApproximately(100.0, 0.01);
	}

	[Fact]
	public async Task FullFlow_WithBonusLines_ExcludesFromTotal()
	{
		// Arrange - Log with base RP and bonuses
		var logContent = @"*** Chat Log Opened: Mon Jan 15 10:00:00 2024
[10:00:00] You get 100 realm points for Campaign Quest!
[10:00:00] You get an additional 20 realm points due to your realm rank!
[10:00:00] You get an additional 10 realm points due to your guild's buff!
[10:00:05] You get 200 realm points for Battle Tick!
";
		await File.WriteAllTextAsync(this.testLogFilePath, logContent);

		var summary = new TestSummary();
		var watcher = new LogWatcher(this.testLogFilePath, enableTimeFiltering: false);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

		// Act
		await foreach(var logLine in watcher.WatchAsync(cts.Token))
		{
			if(logLine is RealmPointLogLine { Entry: var rpEntry })
			{
				summary.ProcessEntry(rpEntry);
			}
		}

		// Assert - Only base RPs counted, bonuses skipped
		summary.TotalRealmPoints.Should().Be(300); // 100 + 200 (bonuses excluded)
		summary.CampaignQuestsRp.Should().Be(100);
		summary.TicksRp.Should().Be(200);
	}

	[Fact]
	public async Task FullFlow_WithAllRPSources_ValidDistribution()
	{
		// Arrange - At least one entry from each source
		var logContent = @"*** Chat Log Opened: Mon Jan 15 10:00:00 2024
[10:00:00] You get 1000 realm points!
[10:00:01] You gain a total of 10,000 experience points.
[10:00:05] You get 500 realm points for Campaign Quest!
[10:00:10] You get 2000 realm points for Battle Tick!
[10:00:15] You get 300 realm points for Tower Capture!
[10:00:20] You get 100 realm points for Assault Order!
[10:00:25] You get 50 realm points for support activity in battle!
[10:00:30] Albion has stored the Strength of Hibernia
[10:00:31] You get 5000 realm points!
";
		await File.WriteAllTextAsync(this.testLogFilePath, logContent);

		var summary = new TestSummary();
		var watcher = new LogWatcher(this.testLogFilePath, enableTimeFiltering: false);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

		// Act
		await foreach(var logLine in watcher.WatchAsync(cts.Token))
		{
			if(logLine is RealmPointLogLine { Entry: var rpEntry })
			{
				summary.ProcessEntry(rpEntry);
			}
		}

		// Assert
		var expectedTotal = 1000 + 500 + 2000 + 300 + 100 + 50 + 5000; // 8950
		summary.TotalRealmPoints.Should().Be(expectedTotal);

		// Each source should have entries
		summary.PlayerKillsRp.Should().BeGreaterThan(0);
		summary.CampaignQuestsRp.Should().Be(500);
		summary.TicksRp.Should().Be(2000);
		summary.SiegeRp.Should().Be(300);
		summary.AssaultOrderRp.Should().Be(100);
		summary.SupportActivityRp.Should().Be(50);
		summary.RelicCaptureRp.Should().Be(5000);

		summary.TotalPercentage.Should().BeApproximately(100.0, 0.01);
	}

	[Fact]
	public async Task FullFlow_WithRealLogSample_ProcessesCorrectly()
	{
		// Arrange - Use a subset of actual log patterns
		var logContent = @"*** Chat Log Opened: Mon Jan 15 20:58:00 2024
[20:58:00] You get an additional 7 realm points for your support activity in battle!
[20:58:21] You get 6 realm points!
[21:01:30] You get an additional 55 realm points for your support activity in battle!
[21:08:59] You get 16 realm points for your efforts in the Tower Capture!
[21:17:51] You get 5 realm points for your efforts in the Tower Capture!
[21:20:53] You get 17 realm points for your efforts in the Tower Capture!
[21:23:42] You get an additional 36 realm points due to your realm rank!
[21:23:42] You get 109 realm points!
[21:23:47] You get an additional 53 realm points due to your realm rank!
[21:23:47] You get 160 realm points!
";
		await File.WriteAllTextAsync(this.testLogFilePath, logContent);

		var summary = new TestSummary();
		var watcher = new LogWatcher(this.testLogFilePath, enableTimeFiltering: false);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

		// Act
		await foreach(var logLine in watcher.WatchAsync(cts.Token))
		{
			if(logLine is RealmPointLogLine { Entry: var rpEntry })
			{
				summary.ProcessEntry(rpEntry);
			}
		}

		// Assert
		summary.TotalRealmPoints.Should().BeGreaterThan(0);
		summary.IndividualRPsSum.Should().Be(summary.TotalRealmPoints);
		summary.TotalPercentage.Should().BeApproximately(100.0, 0.01);
	}

	[Fact]
	public async Task FullFlow_EmptyLog_HandlesGracefully()
	{
		// Arrange
		await File.WriteAllTextAsync(this.testLogFilePath, string.Empty);

		var summary = new TestSummary();
		var watcher = new LogWatcher(this.testLogFilePath, enableTimeFiltering: false);
		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

		// Act
		await foreach(var logLine in watcher.WatchAsync(cts.Token))
		{
			if(logLine is RealmPointLogLine { Entry: var rpEntry })
			{
				summary.ProcessEntry(rpEntry);
			}
		}

		// Assert
		summary.TotalRealmPoints.Should().Be(0);
		summary.TotalPercentage.Should().Be(0.0);
	}

	private static string CreateRealisticLogFile()
	{
		var sb = new StringBuilder();
		sb.AppendLine("*** Chat Log Opened: Mon Jan 15 10:00:00 2024");

		// Player kills (need trigger lines)
		sb.AppendLine("[10:00:01] You get 1200 realm points!");
		sb.AppendLine("[10:00:02] Random log line");
		sb.AppendLine("[10:00:05] You get 800 realm points!");
		sb.AppendLine("[10:00:06] Random log line");

		// Campaign quest
		sb.AppendLine("[10:01:00] You get 500 realm points for Campaign Quest!");

		// Battle ticks
		sb.AppendLine("[10:05:00] You get 2500 realm points for Battle Tick!");
		sb.AppendLine("[10:10:00] You get 2200 realm points for Battle Tick!");

		// Siege
		sb.AppendLine("[10:15:00] You get 400 realm points for Tower Capture!");
		sb.AppendLine("[10:20:00] You get 600 realm points for Keep Capture!");

		// Assault order
		sb.AppendLine("[10:25:00] You get 150 realm points for Assault Order!");

		// Support activity
		sb.AppendLine("[10:30:00] You get 75 realm points for support activity in battle!");
		sb.AppendLine("[10:35:00] You get 50 realm points for support activity in battle!");

		// Relic capture
		sb.AppendLine("[10:40:00] Albion has stored the Strength of Hibernia");
		sb.AppendLine("[10:40:01] You get 5000 realm points!");

		// More player kills (need trigger lines)
		sb.AppendLine("[10:45:00] You get 925 realm points!");
		sb.AppendLine("[10:45:01] Random log line");
		sb.AppendLine("[10:50:00] You get 700 realm points!");
		sb.AppendLine("[10:50:01] Random log line");

		// Total should be: 1200+800+500+2500+2200+400+600+150+75+50+5000+925+700 = 15100

		return sb.ToString();
	}

	/// <summary>
	/// Test helper that mimics UI Summary behavior
	/// </summary>
	private class TestSummary
	{
		public int TotalRealmPoints { get; private set; }

		public int PlayerKillsRp { get; private set; }

		public int CampaignQuestsRp { get; private set; }

		public int TicksRp { get; private set; }

		public int SiegeRp { get; private set; }

		public int AssaultOrderRp { get; private set; }

		public int SupportActivityRp { get; private set; }

		public int RelicCaptureRp { get; private set; }

		public int UnknownRp { get; private set; }

		public int EntryCount { get; private set; }

		public double PlayerKillsPercentage => this.TotalRealmPoints > 0?this.PlayerKillsRp * 100.0 / this.TotalRealmPoints:0;

		public double CampaignQuestsPercentage => this.TotalRealmPoints > 0?this.CampaignQuestsRp * 100.0 / this.TotalRealmPoints:0;

		public double TicksPercentage => this.TotalRealmPoints > 0?this.TicksRp * 100.0 / this.TotalRealmPoints:0;

		public double SiegePercentage => this.TotalRealmPoints > 0?this.SiegeRp * 100.0 / this.TotalRealmPoints:0;

		public double AssaultOrderPercentage => this.TotalRealmPoints > 0?this.AssaultOrderRp * 100.0 / this.TotalRealmPoints:0;

		public double SupportActivityPercentage => this.TotalRealmPoints > 0?this.SupportActivityRp * 100.0 / this.TotalRealmPoints:0;

		public double RelicCapturePercentage => this.TotalRealmPoints > 0?this.RelicCaptureRp * 100.0 / this.TotalRealmPoints:0;

		public double UnknownPercentage => this.TotalRealmPoints > 0?this.UnknownRp * 100.0 / this.TotalRealmPoints:0;

		public int IndividualRPsSum => this.PlayerKillsRp + this.CampaignQuestsRp + this.TicksRp + this.SiegeRp + this.AssaultOrderRp + this.SupportActivityRp + this.RelicCaptureRp + this.UnknownRp;

		public double TotalPercentage =>
				this.PlayerKillsPercentage + this.CampaignQuestsPercentage + this.TicksPercentage + this.SiegePercentage + this.AssaultOrderPercentage + this.SupportActivityPercentage + this.RelicCapturePercentage + this.UnknownPercentage;

		public void ProcessEntry(RealmPointEntry entry)
		{
			this.EntryCount++;
			this.TotalRealmPoints += entry.Points;

			switch(entry.Source)
			{
				case RealmPointSource.PlayerKill:
					this.PlayerKillsRp += entry.Points;
					break;
				case RealmPointSource.CampaignQuest:
					this.CampaignQuestsRp += entry.Points;
					break;
				case RealmPointSource.Tick:
					this.TicksRp += entry.Points;
					break;
				case RealmPointSource.Siege:
					this.SiegeRp += entry.Points;
					break;
				case RealmPointSource.AssaultOrder:
					this.AssaultOrderRp += entry.Points;
					break;
				case RealmPointSource.SupportActivity:
					this.SupportActivityRp += entry.Points;
					break;
				case RealmPointSource.RelicCapture:
					this.RelicCaptureRp += entry.Points;
					break;
				case RealmPointSource.Misc:
					this.UnknownRp += entry.Points;
					break;
			}
		}
	}
}
