using System.Globalization;
using System.Text;
using DAoCLogWatcher.Core;
using DAoCLogWatcher.Core.Models;
using FluentAssertions;

namespace DAoCLogWatcher.Tests.Core;

public sealed class LogWatcherTests : IDisposable
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
        foreach (var file in this.createdFiles)
        {
            if (File.Exists(file))
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
            await foreach (var line in watcher.WatchAsync(cts.Token))
            {
                // Should not reach here
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
        await foreach (var line in watcher.WatchAsync(cts.Token))
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

        var watcher = new LogWatcher(this.testLogFilePath, startPosition: 0, enableTimeFiltering: false);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        var lines = new List<LogLine>();

        // Act
        var readTask = Task.Run(async () =>
        {
            await foreach (var line in watcher.WatchAsync(cts.Token))
            {
                lines.Add(line);
                if (lines.Count >= 1)
                    cts.Cancel();
            }
        });

        await readTask;

        // Assert
        lines.Should().HaveCount(1);
        lines[0].Text.Should().Contain("Campaign Quest");
        lines[0].RealmPointEntry.Should().NotBeNull();
        lines[0].RealmPointEntry!.Points.Should().Be(1000);
    }

    [Fact]
    public async Task WatchAsync_FileWithStartPosition_SkipsEarlierContent()
    {
        // Arrange
        var content = "[12:00:00] First line\n[12:00:01] Second line\n";
        await File.WriteAllTextAsync(this.testLogFilePath, content);

        var startPosition = Encoding.UTF8.GetByteCount("[12:00:00] First line\n");
        var watcher = new LogWatcher(this.testLogFilePath, startPosition: startPosition, enableTimeFiltering: false);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        var lines = new List<LogLine>();

        // Act
        var readTask = Task.Run(async () =>
        {
            await foreach (var line in watcher.WatchAsync(cts.Token))
            {
                lines.Add(line);
                if (lines.Count >= 1)
                    cts.Cancel();
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
            await foreach (var line in watcher.WatchAsync(cts.Token))
            {
                lines.Add(line);
                if (lines.Count >= 2)
                    cts.Cancel();
            }
        });

        // Give watcher time to start
        await Task.Delay(100);

        // Append content while watching
        await using (var stream = new FileStream(this.testLogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        await using (var writer = new StreamWriter(stream))
        {
            await writer.WriteLineAsync("[12:00:00] You get 1000 realm points for Campaign Quest!");
            await writer.FlushAsync();
            await Task.Delay(100);
            await writer.WriteLineAsync("[12:00:01] You get 500 realm points for Tower Capture!");
            await writer.FlushAsync();
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
        await foreach (var line in watcher.WatchAsync(cts.Token))
        {
            // Should not get any lines
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
        await foreach (var line in watcher.WatchAsync(cts.Token))
        {
            if (line.Text.Contains("realm points"))
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
        await foreach (var line in watcher.WatchAsync(cts.Token))
        {
            if (line.Text.Contains("Test line"))
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
            await foreach (var line in watcher.WatchAsync(cts.Token))
            {
                lines.Add(line);
                if (lines.Count >= 1)
                    cts.Cancel();
            }
        });

        await Task.Delay(100);

        // Write incomplete line, then complete it
        await using (var stream = new FileStream(this.testLogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        await using (var writer = new StreamWriter(stream))
        {
            await writer.WriteAsync("[12:00:00] Incomplete");
            await writer.FlushAsync();
            await Task.Delay(100);
            await writer.WriteLineAsync(" line completed!");
            await writer.FlushAsync();
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
        var content =
            "*** Chat Log Opened: Sat Feb 21 11:49:00 2026\n" +
            "[11:49:01] You get 500 realm points for Tower Capture!\n" +
            "*** Chat Log Closed: Sat Feb 21 11:50:35 2026\n" +
            "\n" +
            "*** Chat Log Opened: Sat Feb 21 11:50:37 2026\n" +
            "[11:50:37] You get 100 realm points for Battle Tick!\n";

        await File.WriteAllTextAsync(this.testLogFilePath, content);
        var watcher = new LogWatcher(this.testLogFilePath, enableTimeFiltering: false);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act
        await foreach (var line in watcher.WatchAsync(cts.Token))
        {
            if (line.Text.Contains("Battle Tick"))
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
        var content =
            "*** Chat Log Opened: Sat Feb 21 11:00:00 2026\n" +
            "[11:00:01] You get 500 realm points for Tower Capture!\n" +
            "*** Chat Log Closed: Sat Feb 21 11:00:10 2026\n" +
            "\n" +
            "*** Chat Log Opened: Sat Feb 21 11:05:00 2026\n" +
            "[11:05:01] You get 100 realm points for Battle Tick!\n";

        await File.WriteAllTextAsync(this.testLogFilePath, content);
        var watcher = new LogWatcher(this.testLogFilePath, enableTimeFiltering: false);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act
        await foreach (var line in watcher.WatchAsync(cts.Token))
        {
            if (line.Text.Contains("Battle Tick"))
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
        var sessionDate = DateTime.Now.AddDays(-2).ToString("ddd MMM d HH:mm:ss yyyy", System.Globalization.CultureInfo.InvariantCulture);

        var content =
            $"*** Chat Log Opened: {sessionDate}\n" +
            $"[{oldTime}] You get 51 realm points!\n" +
            $"[{oldTime}] XP Guild Bonus: 160,671\n" +
            "You gain a total of 3,374,108 experience points.\n";

        await File.WriteAllTextAsync(this.testLogFilePath, content);

        var entries = new List<LogLine>();
        var watcher = new LogWatcher(this.testLogFilePath, enableTimeFiltering: true, filterHours: 24);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await foreach (var line in watcher.WatchAsync(cts.Token))
        {
            if (line.RealmPointEntry != null)
                entries.Add(line);
        }

        entries.Should().BeEmpty("entries from 48 hours ago must be filtered out even when resolved on a timestamp-less line");
    }

    [Theory]
    [InlineData(1,  24, true,  "1h old entry is within the 24h window")]
    [InlineData(25, 24, false, "25h old entry is outside the 24h window")]
    [InlineData(5,  6,  true,  "5h old entry is within the 6h window")]
    [InlineData(7,  6,  false, "7h old entry is outside the 6h window")]
    public async Task WatchAsync_TimeFilter_RespectsWindowBoundary(
        int entryAgeHours, int filterHours, bool shouldBeIncluded, string reason)
    {
        // Arrange - build a log with a session marker and one RP entry at the given age
        var entryTime      = DateTime.Now.AddHours(-entryAgeHours);
        var sessionOpened  = entryTime.AddHours(-1);
        var sessionDateStr = sessionOpened.ToString("ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture);
        var entryTimeStr   = entryTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        var content =
            $"*** Chat Log Opened: {sessionDateStr}\n" +
            $"[{entryTimeStr}] You get 500 realm points for Battle Tick!\n";

        await File.WriteAllTextAsync(this.testLogFilePath, content);

        var entries = new List<LogLine>();
        var watcher = new LogWatcher(this.testLogFilePath, enableTimeFiltering: true, filterHours: filterHours);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await foreach (var line in watcher.WatchAsync(cts.Token))
        {
            if (line.RealmPointEntry != null)
                entries.Add(line);
        }

        // Assert
        if (shouldBeIncluded)
            entries.Should().HaveCount(1, reason);
        else
            entries.Should().BeEmpty(reason);
    }
}
