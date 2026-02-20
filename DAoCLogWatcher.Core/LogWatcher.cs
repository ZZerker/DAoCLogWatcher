using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.Core.Parsing;

namespace DAoCLogWatcher.Core;

public sealed partial class LogWatcher : IDisposable, IAsyncDisposable
{
    private const int BufferSize = 4096;
    private const int PollDelayMilliseconds = 500;

    private readonly string logFilePath;
    private readonly byte[] readBuffer;
    private readonly int maxHistoryHours;
    private FileStream? fileStream;
    private long lastPosition;
    private string incompleteLineBuffer;
    private bool skipOldEntries;
    private DateTime? currentSessionStart;

    public LogWatcher(string logFilePath, long startPosition = 0, bool enableTimeFiltering = false, int filterHours = 24)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logFilePath);

        this.logFilePath = logFilePath;
        this.lastPosition = startPosition;
        this.readBuffer = new byte[BufferSize];
        this.incompleteLineBuffer = string.Empty;
        this.skipOldEntries = enableTimeFiltering && startPosition == 0;
        this.maxHistoryHours = filterHours;
    }

    public long LastPosition => this.lastPosition;

    public DateTime? CurrentSessionStart => this.currentSessionStart;

    public async IAsyncEnumerable<LogLine> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!File.Exists(this.logFilePath))
            throw new FileNotFoundException($"Log file not found: {this.logFilePath}");

        var parser = new RealmPointParser();

        this.fileStream = new FileStream(this.logFilePath,
                                         FileMode.Open,
                                         FileAccess.Read,
                                         FileShare.ReadWrite,
                                         bufferSize: 0,
                                         useAsync: true);

        this.SeekToLastPosition(this.fileStream);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int bytesRead;
                try
                {
                    bytesRead = await this.fileStream.ReadAsync(this.readBuffer, cancellationToken);
                }
                catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
                {
                    yield break;
                }

                if (bytesRead == 0)
                {
                    try
                    {
                        await Task.Delay(PollDelayMilliseconds, cancellationToken);
                    }
                    catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
                    {
                        yield break;
                    }
                    continue;
                }

                this.UpdatePosition(this.fileStream);
                var newText = this.DecodeBytes(bytesRead);
                this.AppendToLineBuffer(newText);

                var completeLines = this.ExtractCompleteLines();

                foreach (var line in completeLines)
                {
	                this.ProcessLogOpenedMarker(line);

                    var shouldYield = !this.skipOldEntries ||this.ShouldProcessLine(line);

                    if (shouldYield && !string.IsNullOrWhiteSpace(line))
                    {
                        parser.TryParse(line, out var entry);
                        yield return new LogLine
                        {
                            Text = line,
                            RealmPointEntry = entry
                        };
                    }
                    else
                    {
                        parser.TryParse(line, out _);
                    }
                }
            }
        }
        finally
        {
            if (this.fileStream != null)
            {
                await this.fileStream.DisposeAsync();
                this.fileStream = null;
            }
        }
    }

    private static readonly Regex ChatLogOpenedRegex = GenerateChatLogOpenedRegex();

    private void ProcessLogOpenedMarker(string line)
    {
        var match = ChatLogOpenedRegex.Match(line);
        if (match.Success)
        {
            if (DateTime.TryParseExact(
                match.Groups["date"].Value,
                "ddd MMM d HH:mm:ss yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var sessionDate))
            {
	            this.currentSessionStart = sessionDate;
            }
        }
    }

    private bool ShouldProcessLine(string line)
    {
        // Extract timestamp from line and check if it's within the filter window
        if (!line.StartsWith('[') || line.Length < 11)
            return true; // No timestamp, include by default

        var endIndex = line.IndexOf(']');
        if (endIndex <= 0 || endIndex > 10)
            return true;

        var timestampStr = line.Substring(1, endIndex - 1);
        if (!TimeOnly.TryParseExact(timestampStr, "HH:mm:ss", 
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var lineTime))
            return true;

        // Get current session date or use today
        var sessionDate = this.currentSessionStart?.Date ?? DateTime.Now.Date;
        var lineDateTime = sessionDate.Add(lineTime.ToTimeSpan());

        // Handle day wraparound - if line time is in the future, it's from yesterday
        if (lineDateTime > DateTime.Now)
            lineDateTime = lineDateTime.AddDays(-1);

        var cutoffTime = DateTime.Now.AddHours(-this.maxHistoryHours);
        return lineDateTime >= cutoffTime;
    }

    [GeneratedRegex(@"^\*\*\* Chat Log Opened: (?<date>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex GenerateChatLogOpenedRegex();

    private void SeekToLastPosition(FileStream stream)
    {
        if (this.lastPosition > 0 &&this.lastPosition < stream.Length)
        {
            stream.Seek(this.lastPosition, SeekOrigin.Begin);
        }
    }

    private void UpdatePosition(FileStream stream)
    {
	    this.lastPosition = stream.Position;
    }

    private string DecodeBytes(int byteCount)
    {
        return Encoding.UTF8.GetString(this.readBuffer, 0, byteCount);
    }

    private void AppendToLineBuffer(string text)
    {
	    this.incompleteLineBuffer += text;
    }

    private string[] ExtractCompleteLines()
    {
        var allLines = this.incompleteLineBuffer.Split('\n');
        var completeLineCount = allLines.Length - 1;

        if (completeLineCount <= 0)
            return Array.Empty<string>();

        var completeLines = new string[completeLineCount];

        for (int i = 0; i < completeLineCount; i++)
        {
            completeLines[i] = allLines[i].TrimEnd('\r');
        }

        this.incompleteLineBuffer = allLines[^1];

        return completeLines;
    }

    public void Dispose()
    {
	    this.fileStream?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (this.fileStream != null)
        {
            await this.fileStream.DisposeAsync();
        }
    }
}
