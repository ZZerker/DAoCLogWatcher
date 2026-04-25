using System.Globalization;
using System.Text;
using DAoCLogWatcher.Core;
using DAoCLogWatcher.Core.Models;
using FluentAssertions;

namespace DAoCLogWatcher.Tests.Core;

public sealed class LogWatcherTests: IDisposable
{
	private readonly string testLogFilePath;
	private readonly List<string> createdFiles = new();

	public LogWatcherTests()
	{
		this.testLogFilePath = Path.Combine(Path.GetTempPath(), $"test_log_{Guid.NewGuid()}.log");
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
					// Ignore cleanup errors
				}
			}
		}
	}

	[Fact]
	public async Task WatchAsync_NonExistentFile_ThrowsFileNotFoundException()
	{
		// Arrange
		var nonExistentPath = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid()}.log");
		var watcher = new LogWatcher(nonExistentPath);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

		// Act
		var act = async () =>
		          {
			          await foreach(var line in watcher.WatchAsync(cts.Token))
			          {
			          }
		          };

		// Assert
		await act.Should().ThrowAsync<FileNotFoundException>();
	}

	[Fact]
	public async Task WatchAsync_EmptyFile_WaitsForContent()
	{
		// Arrange
		await File.WriteAllTextAsync(this.testLogFilePath, string.Empty);
		var watcher = new LogWatcher(this.testLogFilePath);
		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

		var lines = new List<string>();

		// Act
		await foreach(var line in watcher.WatchAsync(cts.Token))
		{
			lines.Add(line.Text);
		}

		// Assert
		lines.Should().BeEmpty();
	}

	[Fact]
	public async Task WatchAsync_FileWithInitialContent_ReadsFromStart()
	{
		// Arrange
		var initialContent = "[12:00:00] You get 1000 realm points for Campaign Quest!\n";
		await File.WriteAllTextAsync(this.testLogFilePath, initialContent);

		var watcher = new LogWatcher(this.testLogFilePath, 0, false);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

		var lines = new List<LogLine>();

		// Act
		var readTask = Task.Run(async () =>
		                        {
			                        await foreach(var line in watcher.WatchAsync(cts.Token))
			                        {
				                        lines.Add(line);
				                        if(lines.Count >= 1)
				                        {
					                        cts.Cancel();
				                        }
			                        }
		                        });

		await readTask;

		// Assert
		lines.Should().HaveCount(1);
		lines[0].Text.Should().Contain("Campaign Quest");
		lines[0].Should().BeOfType<RealmPointLogLine>();
		((RealmPointLogLine)lines[0]).Entry.Points.Should().Be(1000);
	}

	[Fact]
	public async Task WatchAsync_FileWithStartPosition_SkipsEarlierContent()
	{
		// Arrange
		var content = "[12:00:00] First line\n[12:00:01] Second line\n";
		await File.WriteAllTextAsync(this.testLogFilePath, content);

		var startPosition = Encoding.UTF8.GetByteCount("[12:00:00] First line\n");
		var watcher = new LogWatcher(this.testLogFilePath, startPosition, false);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

		var lines = new List<LogLine>();

		// Act
		var readTask = Task.Run(async () =>
		                        {
			                        await foreach(var line in watcher.WatchAsync(cts.Token))
			                        {
				                        lines.Add(line);
				                        if(lines.Count >= 1)
				                        {
					                        cts.Cancel();
				                        }
			                        }
		                        });

		await readTask;

		// Assert
		lines.Should().HaveCount(1);
		lines[0].Text.Should().Contain("Second line");
	}

	[Fact]
	public async Task WatchAsync_NewContentAppended_DetectsChanges()
	{
		// Arrange
		await File.WriteAllTextAsync(this.testLogFilePath, string.Empty);

		var watcher = new LogWatcher(this.testLogFilePath, enableTimeFiltering: false);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

		var lines = new List<LogLine>();

		// Act
		var watchTask = Task.Run(async () =>
		                         {
			                         await foreach(var line in watcher.WatchAsync(cts.Token))
			                         {
				                         lines.Add(line);
				                         if(lines.Count >= 2)
				                         {
					                         cts.Cancel();
				                         }
			                         }
		                         });

		// Give watcher time to start
		await Task.Delay(100);

		await using(var stream = new FileStream(this.testLogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
		{
			await using(var writer = new StreamWriter(stream))
			{
				await writer.WriteLineAsync("[12:00:00] You get 1000 realm points for Campaign Quest!");
				await writer.FlushAsync();
				await Task.Delay(100);
				await writer.WriteLineAsync("[12:00:01] You get 500 realm points for Tower Capture!");
				await writer.FlushAsync();
			}
		}

		await watchTask;

		// Assert
		lines.Should().HaveCountGreaterThanOrEqualTo(2);
		lines[0].Text.Should().Contain("Campaign Quest");
		lines[1].Text.Should().Contain("Tower Capture");
	}

	[Fact]
	public async Task WatchAsync_CancellationToken_StopsWatching()
	{
		// Arrange
		await File.WriteAllTextAsync(this.testLogFilePath, string.Empty);
		var watcher = new LogWatcher(this.testLogFilePath);
		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

		// Act
		var startTime = DateTime.UtcNow;
		await foreach(var line in watcher.WatchAsync(cts.Token))
		{
		}

		var elapsed = DateTime.UtcNow - startTime;

		// Assert
		elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
	}

	[Fact]
	public async Task WatchAsync_ProcessesChatLogOpenedMarker()
	{
		// Arrange
		var content = "*** Chat Log Opened: Mon Jan 15 10:30:00 2024\n[10:30:01] You get 1000 realm points!\n";
		await File.WriteAllTextAsync(this.testLogFilePath, content);

		var watcher = new LogWatcher(this.testLogFilePath, enableTimeFiltering: false);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

		// Act
		await foreach(var line in watcher.WatchAsync(cts.Token))
		{
			if(line.Text.Contains("realm points"))
			{
				cts.Cancel();
				break;
			}
		}

		// Assert
		watcher.CurrentSessionStart.Should().NotBeNull();
		watcher.CurrentSessionStart!.Value.Year.Should().Be(2024);
		watcher.CurrentSessionStart.Value.Month.Should().Be(1);
		watcher.CurrentSessionStart.Value.Day.Should().Be(15);
	}

	[Fact]
	public async Task WatchAsync_LastPosition_UpdatesAfterReading()
	{
		// Arrange
		var content = "[12:00:00] Test line\n";
		await File.WriteAllTextAsync(this.testLogFilePath, content);

		var watcher = new LogWatcher(this.testLogFilePath, enableTimeFiltering: false);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

		var initialPosition = watcher.LastPosition;

		// Act
		await foreach(var line in watcher.WatchAsync(cts.Token))
		{
			if(line.Text.Contains("Test line"))
			{
				cts.Cancel();
				break;
			}
		}

		// Assert
		watcher.LastPosition.Should().BeGreaterThan(initialPosition);
	}

	[Fact]
	public async Task WatchAsync_IncompleteLines_AreBufferedUntilComplete()
	{
		// Arrange
		await File.WriteAllTextAsync(this.testLogFilePath, string.Empty);

		var watcher = new LogWatcher(this.testLogFilePath, enableTimeFiltering: false);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

		var lines = new List<LogLine>();

		// Act
		var watchTask = Task.Run(async () =>
		                         {
			                         await foreach(var line in watcher.WatchAsync(cts.Token))
			                         {
				                         lines.Add(line);
				                         if(lines.Count >= 1)
				                         {
					                         cts.Cancel();
				                         }
			                         }
		                         });

		await Task.Delay(100);

		await using(var stream = new FileStream(this.testLogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
		{
			await using(var writer = new StreamWriter(stream))
			{
				await writer.WriteAsync("[12:00:00] Incomplete");
				await writer.FlushAsync();
				await Task.Delay(100);
				await writer.WriteLineAsync(" line completed!");
				await writer.FlushAsync();
			}
		}

		await watchTask;

		// Assert
		lines.Should().HaveCount(1);
		lines[0].Text.Should().Be("[12:00:00] Incomplete line completed!");
	}

	[Fact]
	public async Task WatchAsync_QuickLogReopen_PreservesSessionStart()
	{
		// Arrange - log closed and reopened within 30 seconds
		var content = "*** Chat Log Opened: Sat Feb 21 11:49:00 2026\n" + "[11:49:01] You get 500 realm points for Tower Capture!\n" + "*** Chat Log Closed: Sat Feb 21 11:50:35 2026\n" + "\n" +
		              "*** Chat Log Opened: Sat Feb 21 11:50:37 2026\n" + "[11:50:37] You get 100 realm points for Battle Tick!\n";

		await File.WriteAllTextAsync(this.testLogFilePath, content);
		var watcher = new LogWatcher(this.testLogFilePath, enableTimeFiltering: false);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

		// Act
		await foreach(var line in watcher.WatchAsync(cts.Token))
		{
			if(line.Text.Contains("Battle Tick"))
			{
				cts.Cancel();
				break;
			}
		}

		// Assert - session start should still be the original open time (11:49:00), not 11:50:37
		watcher.CurrentSessionStart.Should().NotBeNull();
		watcher.CurrentSessionStart!.Value.Hour.Should().Be(11);
		watcher.CurrentSessionStart.Value.Minute.Should().Be(49);
		watcher.CurrentSessionStart.Value.Second.Should().Be(0);
	}

	[Fact]
	public async Task WatchAsync_SlowLogReopen_UpdatesSessionStart()
	{
		// Arrange - log closed and reopened after more than 30 seconds
		var content = "*** Chat Log Opened: Sat Feb 21 11:00:00 2026\n" + "[11:00:01] You get 500 realm points for Tower Capture!\n" + "*** Chat Log Closed: Sat Feb 21 11:00:10 2026\n" + "\n" +
		              "*** Chat Log Opened: Sat Feb 21 11:05:00 2026\n" + "[11:05:01] You get 100 realm points for Battle Tick!\n";

		await File.WriteAllTextAsync(this.testLogFilePath, content);
		var watcher = new LogWatcher(this.testLogFilePath, enableTimeFiltering: false);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

		// Act
		await foreach(var line in watcher.WatchAsync(cts.Token))
		{
			if(line.Text.Contains("Battle Tick"))
			{
				cts.Cancel();
				break;
			}
		}

		// Assert - session start should be updated to the new open time (11:05:00)
		watcher.CurrentSessionStart.Should().NotBeNull();
		watcher.CurrentSessionStart!.Value.Hour.Should().Be(11);
		watcher.CurrentSessionStart.Value.Minute.Should().Be(5);
		watcher.CurrentSessionStart.Value.Second.Should().Be(0);
	}

	[Fact]
	public async Task WatchAsync_StatsLine_DetectsCharacterName()
	{
		// Arrange
		var content = "*** Chat Log Opened: Mon Feb 16 20:51:29 2026\n" + "[20:54:35] Options: /stats [ rp | kills | deathblows | solo | irs | heal | rez | player <name|target>  ]\n" + "Statistics for Kobil this Session:\n" +
		              "Total RP: 4717\n" + "[20:55:00] You get 1000 realm points for Campaign Quest!\n";

		await File.WriteAllTextAsync(this.testLogFilePath, content);
		var watcher = new LogWatcher(this.testLogFilePath, enableTimeFiltering: false);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

		LogLine? statsLine = null;

		// Act
		await foreach(var line in watcher.WatchAsync(cts.Token))
		{
			if(line.DetectedCharacterName != null)
			{
				statsLine = line;
			}

			if(line.Text.Contains("Campaign Quest"))
			{
				cts.Cancel();
				break;
			}
		}

		// Assert
		statsLine.Should().NotBeNull();
		statsLine!.DetectedCharacterName.Should().Be("Kobil");
		watcher.CurrentCharacterName.Should().Be("Kobil");
	}

	[Fact]
	public async Task WatchAsync_StatsLine_MostFrequentNameWins()
	{
		// Arrange – "Kobil" appears twice, "OtherPlayer" once; Kobil should win
		var content = "*** Chat Log Opened: Mon Feb 16 20:51:29 2026\n" + "Statistics for Kobil this Session:\n" + "Statistics for OtherPlayer this Session:\n" + "Statistics for Kobil this Session:\n" +
		              "[20:55:00] You get 1000 realm points for Campaign Quest!\n";

		await File.WriteAllTextAsync(this.testLogFilePath, content);
		var watcher = new LogWatcher(this.testLogFilePath, enableTimeFiltering: false);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

		await foreach(var line in watcher.WatchAsync(cts.Token))
		{
			if(line.Text.Contains("Campaign Quest"))
			{
				cts.Cancel();
				break;
			}
		}

		// Assert
		watcher.CurrentCharacterName.Should().Be("Kobil");
	}

	[Fact]
	public async Task WatchAsync_StatsLine_OutsideTimeWindow_NotDetected()
	{
		// Arrange – stats line appears before the filter cutoff (old context), should be ignored
		var oldTime = DateTime.Now.AddHours(-8).ToString("HH:mm:ss");
		var recentTime = DateTime.Now.AddHours(-1).ToString("HH:mm:ss");
		var sessionDateStr = DateTime.Now.AddHours(-10).ToString("ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture);

		var content = $"*** Chat Log Opened: {sessionDateStr}\n" + $"[{oldTime}] Options: /stats [ rp | kills | deathblows | solo | irs | heal | rez | player <name|target>  ]\n" + "Statistics for OldContextChar this Session:\n" +
		              $"[{recentTime}] You get 500 realm points for Battle Tick!\n";

		await File.WriteAllTextAsync(this.testLogFilePath, content);
		var watcher = new LogWatcher(this.testLogFilePath, enableTimeFiltering: true, filterHours: 6);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

		await foreach(var line in watcher.WatchAsync(cts.Token))
		{
			if(line.Text.Contains("Battle Tick"))
			{
				cts.Cancel();
				break;
			}
		}

		// Assert – stats line was outside the 6h window so character should not be detected
		watcher.CurrentCharacterName.Should().BeNull();
	}

	[Fact]
	public async Task WatchAsync_StatsLine_InsideTimeWindow_IsDetected()
	{
		// Arrange – stats line appears within the filter window, should be detected
		var recentTime = DateTime.Now.AddHours(-1).ToString("HH:mm:ss");
		var sessionDateStr = DateTime.Now.AddHours(-2).ToString("ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture);

		var content = $"*** Chat Log Opened: {sessionDateStr}\n" + $"[{recentTime}] Options: /stats [ rp | kills | deathblows | solo | irs | heal | rez | player <name|target>  ]\n" + "Statistics for RecentChar this Session:\n" +
		              $"[{recentTime}] You get 500 realm points for Battle Tick!\n";

		await File.WriteAllTextAsync(this.testLogFilePath, content);
		var watcher = new LogWatcher(this.testLogFilePath, enableTimeFiltering: true, filterHours: 6);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

		await foreach(var line in watcher.WatchAsync(cts.Token))
		{
			if(line.Text.Contains("Battle Tick"))
			{
				cts.Cancel();
				break;
			}
		}

		// Assert – stats line was within the 6h window so character should be detected
		watcher.CurrentCharacterName.Should().Be("RecentChar");
	}

	[Fact]
	public async Task WatchAsync_StatsLine_ResetsOnNewSession()
	{
		// Arrange – two sessions with different characters
		var content = "*** Chat Log Opened: Sat Feb 21 10:00:00 2026\n" + "Statistics for Kobil this Session:\n" + "[10:00:01] You get 500 realm points for Battle Tick!\n" + "*** Chat Log Closed: Sat Feb 21 10:05:00 2026\n" +
		              "*** Chat Log Opened: Sat Feb 21 11:00:00 2026\n" + "Statistics for AltChar this Session:\n" + "[11:00:01] You get 200 realm points for Battle Tick!\n";

		await File.WriteAllTextAsync(this.testLogFilePath, content);
		var watcher = new LogWatcher(this.testLogFilePath, enableTimeFiltering: false);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

		await foreach(var line in watcher.WatchAsync(cts.Token))
		{
			if(line.Text.Contains("11:00:01"))
			{
				cts.Cancel();
				break;
			}
		}

		// Assert – name from second session wins, not first
		watcher.CurrentCharacterName.Should().Be("AltChar");
	}

	[Fact]
	public async Task WatchAsync_KillLine_SetsKillEvent()
	{
		// Arrange
		var content = "*** Chat Log Opened: Mon Feb 16 20:00:00 2026\n" + "[20:34:52] Dfensze was just killed by Linkx in Emain Macha.\n" + "[20:35:00] You get 500 realm points for Battle Tick!\n";

		await File.WriteAllTextAsync(this.testLogFilePath, content);
		var watcher = new LogWatcher(this.testLogFilePath, enableTimeFiltering: false);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

		KillLogLine? killLine = null;

		// Act
		await foreach(var line in watcher.WatchAsync(cts.Token))
		{
			if(line is KillLogLine kl)
			{
				killLine = kl;
			}

			if(line.Text.Contains("Battle Tick"))
			{
				cts.Cancel();
				break;
			}
		}

		// Assert
		killLine.Should().NotBeNull();
		killLine!.Event.Victim.Should().Be("Dfensze");
		killLine.Event.Killer.Should().Be("Linkx");
		killLine.Event.Zone.Should().Be("Emain Macha");
		killLine.Event.Timestamp.Should().Be(new TimeOnly(20, 34, 52));
	}

	[Fact]
	public async Task WatchAsync_KillLine_OutsideTimeWindow_KillEventNotEmitted()
	{
		// Arrange – kill line is outside the filter window, should not be yielded
		var oldTime = DateTime.Now.AddHours(-8).ToString("HH:mm:ss");
		var recentTime = DateTime.Now.AddHours(-1).ToString("HH:mm:ss");
		var sessionDateStr = DateTime.Now.AddHours(-10).ToString("ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture);

		var content = $"*** Chat Log Opened: {sessionDateStr}\n" + $"[{oldTime}] Dfensze was just killed by Linkx in Emain Macha.\n" + $"[{recentTime}] You get 500 realm points for Battle Tick!\n";

		await File.WriteAllTextAsync(this.testLogFilePath, content);
		var watcher = new LogWatcher(this.testLogFilePath, enableTimeFiltering: true, filterHours: 6);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

		var killLines = new List<KillLogLine>();

		await foreach(var line in watcher.WatchAsync(cts.Token))
		{
			if(line is KillLogLine kl)
			{
				killLines.Add(kl);
			}

			if(line.Text.Contains("Battle Tick"))
			{
				cts.Cancel();
				break;
			}
		}

		// Assert – old kill line was outside 6h window, not yielded
		killLines.Should().BeEmpty();
	}

	[Fact]
	public void Constructor_NullOrEmptyPath_ThrowsArgumentException()
	{
		// Act & Assert
		var actNull = () => new LogWatcher(null!);
		var actEmpty = () => new LogWatcher(string.Empty);
		var actWhitespace = () => new LogWatcher("   ");

		actNull.Should().Throw<ArgumentException>();
		actEmpty.Should().Throw<ArgumentException>();
		actWhitespace.Should().Throw<ArgumentException>();
	}

	[Fact]
	public async Task DisposeAsync_CanBeCalledMultipleTimes()
	{
		// Arrange
		await File.WriteAllTextAsync(this.testLogFilePath, string.Empty);
		var watcher = new LogWatcher(this.testLogFilePath);

		// Act & Assert
		await watcher.DisposeAsync();
		await watcher.DisposeAsync(); // Should not throw
	}

	[Fact]
	public async Task WatchAsync_TimeFilter_BlocksOldEntriesResolvedOnTimestamplessLine()
	{
		// Regression: RP line (old) → XP Guild Bonus (filtered) → XP line (no timestamp, always passes
		// ShouldProcessLine) used to let the resolved PlayerKill entry slip through the time filter.
		// The entry's own timestamp must also be checked against the window.
		var oldTime = DateTime.Now.AddHours(-48).ToString("HH:mm:ss");
		var sessionDate = DateTime.Now.AddDays(-2).ToString("ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture);

		var content = $"*** Chat Log Opened: {sessionDate}\n" + $"[{oldTime}] You get 51 realm points!\n" + $"[{oldTime}] XP Guild Bonus: 160,671\n" + "You gain a total of 3,374,108 experience points.\n";

		await File.WriteAllTextAsync(this.testLogFilePath, content);

		var entries = new List<RealmPointLogLine>();
		var watcher = new LogWatcher(this.testLogFilePath, enableTimeFiltering: true, filterHours: 24);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

		await foreach(var line in watcher.WatchAsync(cts.Token))
		{
			if(line is RealmPointLogLine rp)
			{
				entries.Add(rp);
			}
		}

		entries.Should().BeEmpty("entries from 48 hours ago must be filtered out even when resolved on a timestamp-less line");
	}

	[Theory]
	[InlineData(1, 24, true, "1h old entry is within the 24h window")]
	[InlineData(25, 24, false, "25h old entry is outside the 24h window")]
	[InlineData(5, 6, true, "5h old entry is within the 6h window")]
	[InlineData(7, 6, false, "7h old entry is outside the 6h window")]
	public async Task WatchAsync_TimeFilter_RespectsWindowBoundary(int entryAgeHours, int filterHours, bool shouldBeIncluded, string reason)
	{
		// Arrange - build a log with a session marker and one RP entry at the given age
		var entryTime = DateTime.Now.AddHours(-entryAgeHours);
		var sessionOpened = entryTime.AddHours(-1);
		var sessionDateStr = sessionOpened.ToString("ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture);
		var entryTimeStr = entryTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

		var content = $"*** Chat Log Opened: {sessionDateStr}\n" + $"[{entryTimeStr}] You get 500 realm points for Battle Tick!\n";

		await File.WriteAllTextAsync(this.testLogFilePath, content);

		var entries = new List<RealmPointLogLine>();
		var watcher = new LogWatcher(this.testLogFilePath, enableTimeFiltering: true, filterHours: filterHours);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

		await foreach(var line in watcher.WatchAsync(cts.Token))
		{
			if(line is RealmPointLogLine rp)
			{
				entries.Add(rp);
			}
		}

		// Assert
		if(shouldBeIncluded)
		{
			entries.Should().HaveCount(1, reason);
		}
		else
		{
			entries.Should().BeEmpty(reason);
		}
	}

	[Fact]
	public async Task WatchAsync_TimeFilter_MidnightCrossing_IncludesLinesAfterMidnight()
	{
		// Session opened just before midnight yesterday; a log line arrives a few minutes after midnight.
		// The line's timestamp (e.g. 00:03) must not be resolved to yesterday's date and incorrectly
		// rejected as being ~24 hours old.
		var sessionOpenedAt = DateTime.Now.Date.AddDays(-1).AddHours(23).AddMinutes(55); // yesterday 23:55
		var lineTime = DateTime.Now.Date.AddMinutes(3); // today 00:03

		var sessionDateStr = sessionOpenedAt.ToString("ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture);
		var lineTimeStr = lineTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

		var content = $"*** Chat Log Opened: {sessionDateStr}\n" + $"[{lineTimeStr}] You get 500 realm points for Battle Tick!\n";

		await File.WriteAllTextAsync(this.testLogFilePath, content);

		var entries = new List<RealmPointLogLine>();
		var watcher = new LogWatcher(this.testLogFilePath, enableTimeFiltering: true, filterHours: 24);
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

		await foreach(var line in watcher.WatchAsync(cts.Token))
		{
			if(line is RealmPointLogLine rp)
			{
				entries.Add(rp);
			}
		}

		entries.Should().HaveCount(1, "a line logged a few minutes past midnight must not be filtered out");
	}
}
