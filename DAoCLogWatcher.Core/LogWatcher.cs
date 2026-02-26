using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.Core.Parsing;

namespace DAoCLogWatcher.Core;

public sealed partial class LogWatcher: IDisposable, IAsyncDisposable
{
	private const int BufferSize = 4096;
	private const int PollDelayMilliseconds = 500;
	private const int LogReopenThresholdSeconds = 30;

	private static readonly Regex ChatLogOpenedRegex = GenerateChatLogOpenedRegex();
	private static readonly Regex ChatLogClosedRegex = GenerateChatLogClosedRegex();

	private readonly string logFilePath;
	private readonly int maxHistoryHours;
	private readonly byte[] readBuffer;
	private FileStream? fileStream;
	private readonly StringBuilder incompleteLineBuffer;
	private DateTime? lastLogClosed;
	private readonly bool skipOldEntries;

	public LogWatcher(string logFilePath, long startPosition = 0, bool enableTimeFiltering = false, int filterHours = 24)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(logFilePath);

		this.logFilePath = logFilePath;
		this.LastPosition = startPosition;
		this.readBuffer = new byte[BufferSize];
		this.incompleteLineBuffer = new StringBuilder();
		this.skipOldEntries = enableTimeFiltering&&startPosition == 0;
		this.maxHistoryHours = filterHours;
	}

	public long LastPosition { get; private set; }

	public DateTime? CurrentSessionStart { get; private set; }

	public async ValueTask DisposeAsync()
	{
		if(this.fileStream != null)
		{
			await this.fileStream.DisposeAsync();
		}
	}

	public void Dispose()
	{
		this.fileStream?.Dispose();
	}

	public async IAsyncEnumerable<LogLine> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		if(!File.Exists(this.logFilePath))
		{
			throw new FileNotFoundException($"Log file not found: {this.logFilePath}");
		}

		var parser = new RealmPointParser();

		this.fileStream = new FileStream(this.logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 0, true);

		this.SeekToLastPosition(this.fileStream);

		try
		{
			while(!cancellationToken.IsCancellationRequested)
			{
				int bytesRead;
				try
				{
					bytesRead = await this.fileStream.ReadAsync(this.readBuffer, cancellationToken);
				}
				catch(Exception ex) when(ex is OperationCanceledException or ObjectDisposedException)
				{
					yield break;
				}

				if(bytesRead == 0)
				{
					try
					{
						await Task.Delay(PollDelayMilliseconds, cancellationToken);
					}
					catch(Exception ex) when(ex is OperationCanceledException or ObjectDisposedException)
					{
						yield break;
					}

					continue;
				}

				this.UpdatePosition(this.fileStream);
				var newText = this.DecodeBytes(bytesRead);
				this.AppendToLineBuffer(newText);

				var completeLines = this.ExtractCompleteLines();

				foreach(var line in completeLines)
				{
					this.ProcessLogClosedMarker(line);
					this.ProcessLogOpenedMarker(line);

					var shouldYield = !this.skipOldEntries||this.ShouldProcessLine(line);

					if(shouldYield&&!string.IsNullOrWhiteSpace(line))
						{
							parser.TryParse(line, out var entry);

							// Multi-line sequences (e.g. RP line → XP Guild Bonus → XP line) may
							// resolve on a timestamp-less line that bypasses ShouldProcessLine.
							// Re-check the resolved entry's own timestamp against the filter window.
							if(this.skipOldEntries&&entry != null&&!this.ShouldProcessTimestamp(entry.Timestamp))
								entry = null;

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
			if(this.fileStream != null)
			{
				await this.fileStream.DisposeAsync();
				this.fileStream = null;
			}
		}
	}

	private void AppendToLineBuffer(string text)
	{
		this.incompleteLineBuffer.Append(text);
	}

	private string DecodeBytes(int byteCount)
	{
		return Encoding.UTF8.GetString(this.readBuffer, 0, byteCount);
	}

	private string[] ExtractCompleteLines()
	{
		var allLines = this.incompleteLineBuffer.ToString().Split('\n');
		var completeLineCount = allLines.Length - 1;

		if(completeLineCount <= 0)
		{
			return Array.Empty<string>();
		}

		var completeLines = new string[completeLineCount];

		for(var i = 0; i < completeLineCount; i++)
		{
			completeLines[i] = allLines[i].TrimEnd('\r');
		}

		this.incompleteLineBuffer.Clear();
		this.incompleteLineBuffer.Append(allLines[^1]);

		return completeLines;
	}

	[GeneratedRegex(@"^\*\*\* Chat Log Closed: (?<date>.+)$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex GenerateChatLogClosedRegex();

	[GeneratedRegex(@"^\*\*\* Chat Log Opened: (?<date>.+)$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex GenerateChatLogOpenedRegex();

	private void ProcessLogClosedMarker(string line)
	{
		var match = ChatLogClosedRegex.Match(line);
		if(match.Success)
		{
			if(DateTime.TryParseExact(match.Groups["date"].Value, "ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var closedAt))
			{
				this.lastLogClosed = closedAt;
			}
		}
	}

	private void ProcessLogOpenedMarker(string line)
	{
		var match = ChatLogOpenedRegex.Match(line);
		if(match.Success)
		{
			if(DateTime.TryParseExact(match.Groups["date"].Value, "ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sessionDate))
			{
				var isQuickReopen = this.lastLogClosed.HasValue&&(sessionDate - this.lastLogClosed.Value).TotalSeconds <= LogReopenThresholdSeconds;

				if(!isQuickReopen)
				{
					this.CurrentSessionStart = sessionDate;
				}

				this.lastLogClosed = null;
			}
		}
	}

	private void SeekToLastPosition(FileStream stream)
	{
		if(this.LastPosition > 0&&this.LastPosition < stream.Length)
		{
			stream.Seek(this.LastPosition, SeekOrigin.Begin);
		}
	}

	private bool ShouldProcessLine(string line)
	{
		if(!line.StartsWith('[')||line.Length < 11)
		{
			return true; // No timestamp, include by default
		}

		var endIndex = line.IndexOf(']');
		if(endIndex <= 0||endIndex > 10)
		{
			return true;
		}

		var timestampStr = line.Substring(1, endIndex - 1);
		if(!TimeOnly.TryParseExact(timestampStr, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var lineTime))
		{
			return true;
		}

		return this.ShouldProcessTimestamp(lineTime);
	}

	private bool ShouldProcessTimestamp(TimeOnly time)
	{
		var sessionDate = this.CurrentSessionStart?.Date ?? DateTime.Now.Date;
		var lineDateTime = sessionDate.Add(time.ToTimeSpan());

		// Handle day wraparound - if line time is in the future, it's from yesterday
		if(lineDateTime > DateTime.Now)
		{
			lineDateTime = lineDateTime.AddDays(-1);
		}

		var cutoffTime = DateTime.Now.AddHours(-this.maxHistoryHours);
		return lineDateTime >= cutoffTime;
	}

	private void UpdatePosition(FileStream stream)
	{
		this.LastPosition = stream.Position;
	}
}
