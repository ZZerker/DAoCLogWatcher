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
	private static readonly Regex StatsCharacterRegex = GenerateStatsCharacterRegex();
	private static readonly Regex KillLineRegex = GenerateKillLineRegex();

	private readonly string logFilePath;
	private readonly double maxHistoryHours;
	private readonly byte[] readBuffer;
	private FileStream? fileStream;
	private readonly StringBuilder incompleteLineBuffer;
	private DateTime? lastLogClosed;
	private readonly bool skipOldEntries;
	private readonly Dictionary<string, int> characterNameCounts = new();
	private bool lastTimestampedLineWasInWindow = true;

	public LogWatcher(string logFilePath, long startPosition = 0, bool enableTimeFiltering = false, double filterHours = 24)
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

	public string? CurrentCharacterName { get; private set; }

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
		var combatParser = new CombatParser();

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

					// Compute shouldYield once and reuse it to gate character detection on
					// timestampless lines like "Statistics for X this Session:".
					var shouldYield = !this.skipOldEntries||this.ShouldProcessLine(line);

					if(this.skipOldEntries && line.StartsWith('['))
						this.lastTimestampedLineWasInWindow = shouldYield;

					var detectedName = (!this.skipOldEntries || this.lastTimestampedLineWasInWindow)
						? this.TryDetectCharacterName(line)
						: null;

					// Always feed both parsers to keep state machines consistent.
					parser.TryParse(line, out var entry);
					combatParser.TryParse(line, out var damageEvent, out var healEvent);

					if(shouldYield&&!string.IsNullOrWhiteSpace(line))
					{
						// Multi-line sequences (e.g. RP line → XP Guild Bonus → XP line) may
						// resolve on a timestamp-less line that bypasses ShouldProcessLine.
						// Re-check the resolved entry's own timestamp against the filter window.
						if(this.skipOldEntries&&entry != null&&!this.ShouldProcessTimestamp(entry.Timestamp))
							entry = null;

						if(this.skipOldEntries&&damageEvent != null&&!this.ShouldProcessTimestamp(damageEvent.Timestamp))
							damageEvent = null;
						if(this.skipOldEntries&&healEvent != null&&!this.ShouldProcessTimestamp(healEvent.Timestamp))
							healEvent = null;

						var characterNameForLine = detectedName != null ? this.CurrentCharacterName : null;
						var killEvent = TryDetectKillEvent(line);

						LogLine logLine = entry != null
							? new RealmPointLogLine(line, entry) { DetectedCharacterName = characterNameForLine }
							: damageEvent != null
								? new DamageLogLine(line, damageEvent) { DetectedCharacterName = characterNameForLine }
								: healEvent != null
									? new HealLogLine(line, healEvent) { DetectedCharacterName = characterNameForLine }
									: killEvent != null
										? new KillLogLine(line, killEvent) { DetectedCharacterName = characterNameForLine }
										: new UnknownLogLine(line) { DetectedCharacterName = characterNameForLine };

						yield return logLine;
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
					this.CurrentCharacterName = null;
					this.characterNameCounts.Clear();
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

	private string? TryDetectCharacterName(string line)
	{
		var match = StatsCharacterRegex.Match(line);
		if(!match.Success)
			return null;

		var name = match.Groups["name"].Value;
		this.characterNameCounts.TryGetValue(name, out var count);
		this.characterNameCounts[name] = count + 1;
		this.CurrentCharacterName = this.characterNameCounts.MaxBy(kv => kv.Value).Key;
		return name;
	}

	private void UpdatePosition(FileStream stream)
	{
		this.LastPosition = stream.Position;
	}

	[GeneratedRegex(@"^Statistics for (?<name>\w+) this Session:$", RegexOptions.CultureInvariant)]
	private static partial Regex GenerateStatsCharacterRegex();

	private static KillEvent? TryDetectKillEvent(string line)
	{
		var match = KillLineRegex.Match(line);
		if(!match.Success)
			return null;

		if(!TimeOnly.TryParseExact(match.Groups["timestamp"].Value, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
			return null;

		return new KillEvent
			{
				Timestamp = timestamp,
				Victim = match.Groups["victim"].Value,
				Killer = match.Groups["killer"].Value,
				Zone = match.Groups["zone"].Value
			};
	}

	[GeneratedRegex(@"^\[(?<timestamp>\d{2}:\d{2}:\d{2})\] (?<victim>\w+) was just killed by (?<killer>\w+) in (?<zone>.+)\.$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex GenerateKillLineRegex();
}
