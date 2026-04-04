using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.Core.Parsing;

namespace DAoCLogWatcher.Core;

public sealed partial class LogWatcher: IDisposable, IAsyncDisposable
{
	private const int BUFFER_SIZE = 4096;
	private const int POLL_DELAY_MILLISECONDS = 500;
	private const int LOG_REOPEN_THRESHOLD_SECONDS = 30;
	private const int KILL_RP_CORRELATION_WINDOW_SECONDS = 30;
	private const string DATE_FORMAT = "ddd MMM d HH:mm:ss yyyy";

	private static readonly Regex ChatLogOpenedRegex = GenerateChatLogOpenedRegex();
	private static readonly Regex ChatLogClosedRegex = GenerateChatLogClosedRegex();
	private static readonly Regex StatsCharacterRegex = GenerateStatsCharacterRegex();
	private static readonly Regex KillLineRegex = GenerateKillLineRegex();
	private static readonly Regex SendLineRegex = GenerateSendLineRegex();

	private readonly string logFilePath;
	private readonly double maxHistoryHours;
	private readonly byte[] readBuffer;
	private FileStream? fileStream;
	private readonly StringBuilder incompleteLineBuffer;
	private DateTime? lastLogClosed;
	private readonly bool skipOldEntries;
	private readonly Dictionary<string, int> characterNameCounts = new();
	private bool lastTimestampedLineWasInWindow = true;
	private readonly long endPosition;

	// Session-scoped state — initialised at the start of each WatchAsync call.
	private RealmPointParser? sessionParser;
	private CombatParser? sessionCombatParser;
	private KillEvent? lastKillEvent;

	public LogWatcher(string logFilePath, long startPosition = 0, bool enableTimeFiltering = false, double filterHours = 24, long endPosition = -1)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(logFilePath);

		this.logFilePath = logFilePath;
		this.LastPosition = startPosition;
		this.readBuffer = new byte[BUFFER_SIZE];
		this.incompleteLineBuffer = new StringBuilder();
		this.skipOldEntries = enableTimeFiltering&&startPosition == 0;
		this.maxHistoryHours = filterHours;
		this.endPosition = endPosition;
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

		this.sessionParser = new RealmPointParser();
		this.sessionCombatParser = new CombatParser();
		this.lastKillEvent = null;

		this.fileStream = new FileStream(this.logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 0, true);

		this.SeekToLastPosition(this.fileStream);

		try
		{
			while(!cancellationToken.IsCancellationRequested)
			{
				if(this.endPosition >= 0&&this.fileStream.Position >= this.endPosition)
				{
					yield break;
				}

				int bytesRead;
				try
				{
					var maxRead = this.endPosition >= 0?(int)Math.Min(this.readBuffer.Length, this.endPosition - this.fileStream.Position):this.readBuffer.Length;
					if(maxRead <= 0) yield break;
					bytesRead = await this.fileStream.ReadAsync(this.readBuffer.AsMemory(0, maxRead), cancellationToken);
				}
				catch(Exception ex) when(ex is OperationCanceledException or ObjectDisposedException)
				{
					yield break;
				}

				if(bytesRead == 0)
				{
					try
					{
						await Task.Delay(POLL_DELAY_MILLISECONDS, cancellationToken);
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
					var parsed = this.ProcessOneLine(line);
					if(!parsed.ShouldYield||string.IsNullOrWhiteSpace(line))
					{
						continue;
					}

					var entry = this.ApplyTimeFilter(parsed.Entry, e => e.Timestamp);
					var damage = this.ApplyTimeFilter(parsed.DamageEvent, e => e.Timestamp);
					var heal = this.ApplyTimeFilter(parsed.HealEvent, e => e.Timestamp);
					var miss = this.ApplyTimeFilter(parsed.MissEvent, e => e.Timestamp);

					var logLine = CreateLogLine(line, CorrelateKillWithRp(entry, this.lastKillEvent), damage, heal, miss, parsed.KillEvent, parsed.SendEvent);
					logLine = logLine with
					          {
							          DetectedCharacterName = parsed.CharacterName
					          };
					yield return logLine;

					// TryParse may flush a stale pending event AND return a new event on the same line.
					// The chain above picks the flushed event first -- emit the secondary event now.
					if(damage != null&&heal != null)
					{
						yield return new HealLogLine(line, heal)
						             {
								             DetectedCharacterName = parsed.CharacterName
						             };
					}
					else if(damage != null&&miss != null)
					{
						yield return new MissLogLine(line, miss)
						             {
								             DetectedCharacterName = parsed.CharacterName
						             };
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

	// ── Extracted helpers ─────────────────────────────────────────────────

	/// <summary>
	/// Nulls out an event whose timestamp falls outside the time filter window.
	/// Multi-line sequences (e.g. RP line → XP Guild Bonus → XP line) may resolve on
	/// a timestamp-less line that bypasses ShouldProcessLine; this re-checks the
	/// resolved entry's own timestamp.
	/// </summary>
	private T? ApplyTimeFilter<T>(T? evt, Func<T, TimeOnly> getTimestamp)
			where T: class
	{
		if(!this.skipOldEntries||evt == null)
			return evt;
		return this.ShouldProcessTimestamp(getTimestamp(evt))?evt:null;
	}

	private static RealmPointEntry? CorrelateKillWithRp(RealmPointEntry? entry, KillEvent? lastKillEvent)
	{
		if(entry == null||entry.Source != RealmPointSource.PlayerKill||lastKillEvent == null)
			return entry;
		var diffSeconds = TimeHelper.ShortestArcSeconds(entry.Timestamp, lastKillEvent.Timestamp);
		if(diffSeconds <= KILL_RP_CORRELATION_WINDOW_SECONDS)
		{
			return entry with
			       {
					       Victim = lastKillEvent.Victim
			       };
		}

		return entry;
	}

	private static LogLine CreateLogLine(string line, RealmPointEntry? entry, DamageEvent? damageEvent, HealEvent? healEvent, MissEvent? missEvent, KillEvent? killEvent, SendEvent? sendEvent)
	{
		if(entry != null) return new RealmPointLogLine(line, entry);
		if(damageEvent != null) return new DamageLogLine(line, damageEvent);
		if(healEvent != null) return new HealLogLine(line, healEvent);
		if(missEvent != null) return new MissLogLine(line, missEvent);
		if(killEvent != null) return new KillLogLine(line, killEvent);
		if(sendEvent != null) return new SendLogLine(line, sendEvent);
		return new UnknownLogLine(line);
	}

	// ── Line buffer and I/O ──────────────────────────────────────────────

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
			return [];
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

	// ── Per-line processing ──────────────────────────────────────────────

	private readonly record struct ParsedLine(bool ShouldYield, RealmPointEntry? Entry, DamageEvent? DamageEvent, HealEvent? HealEvent, MissEvent? MissEvent, KillEvent? KillEvent, SendEvent? SendEvent, string? CharacterName);

	/// <summary>
	/// Feeds one line through all parsers and detection helpers.
	/// Updates session state (lastKillEvent, lastTimestampedLineWasInWindow, CurrentCharacterName).
	/// The caller decides whether to yield — only lines where <see cref="ParsedLine.ShouldYield"/>
	/// is true and the line is non-empty should be emitted.
	/// </summary>
	private ParsedLine ProcessOneLine(string line)
	{
		this.ProcessLogClosedMarker(line);
		this.ProcessLogOpenedMarker(line);

		// Compute shouldYield once and reuse to gate character detection on
		// timestampless lines like "Statistics for X this Session:".
		var shouldYield = !this.skipOldEntries||this.ShouldProcessLine(line);
		if(this.skipOldEntries&&line.StartsWith('['))
		{
			this.lastTimestampedLineWasInWindow = shouldYield;
		}

		string? detectedName;
		if(!this.skipOldEntries||this.lastTimestampedLineWasInWindow)
		{
			detectedName = this.TryDetectCharacterName(line);
		}
		else
		{
			detectedName = null;
		}

		// Always feed both parsers to keep state machines consistent.
		this.sessionParser!.TryParse(line, out var entry);
		this.sessionCombatParser!.TryParse(line, out var damageEvent, out var healEvent, out var missEvent);

		// Detect kill events unconditionally — the kill line may be outside the time
		// filter window while the correlated RP entry is inside it.
		var killEvent = TryDetectKillEvent(line);
		if(killEvent != null)
		{
			this.lastKillEvent = killEvent;
		}

		var sendEvent = TryDetectSendEvent(line);

		var characterName = detectedName != null?this.CurrentCharacterName:null;
		return new ParsedLine(shouldYield, entry, damageEvent, healEvent, missEvent, killEvent, sendEvent, characterName);
	}

	// ── Session markers ──────────────────────────────────────────────────

	[GeneratedRegex(@"^\*\*\* Chat Log Closed: (?<date>.+)$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex GenerateChatLogClosedRegex();

	[GeneratedRegex(@"^\*\*\* Chat Log Opened: (?<date>.+)$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex GenerateChatLogOpenedRegex();

	private void ProcessLogClosedMarker(string line)
	{
		var match = ChatLogClosedRegex.Match(line);
		if(match.Success)
		{
			if(DateTime.TryParseExact(match.Groups["date"].Value, DATE_FORMAT, CultureInfo.InvariantCulture, DateTimeStyles.None, out var closedAt))
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
			if(DateTime.TryParseExact(match.Groups["date"].Value, DATE_FORMAT, CultureInfo.InvariantCulture, DateTimeStyles.None, out var sessionDate))
			{
				var isQuickReopen = this.lastLogClosed.HasValue&&(sessionDate - this.lastLogClosed.Value).TotalSeconds <= LOG_REOPEN_THRESHOLD_SECONDS;

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

	// ── Position and filtering ───────────────────────────────────────────

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
			return true;
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

		// If the resolved time is in the future, the session opened just after midnight
		// and this timestamp is actually from the previous day.
		if(lineDateTime > DateTime.Now)
		{
			lineDateTime = lineDateTime.AddDays(-1);
		}
		// If the resolved time predates the session start by more than 1 hour, the log
		// has crossed midnight — this timestamp belongs to the following day.
		else if(this.CurrentSessionStart.HasValue&&lineDateTime < this.CurrentSessionStart.Value.AddHours(-1))
		{
			lineDateTime = lineDateTime.AddDays(1);
		}

		var cutoffTime = DateTime.Now.AddHours(-this.maxHistoryHours);
		return lineDateTime >= cutoffTime;
	}

	// ── Detection helpers ────────────────────────────────────────────────

	private string? TryDetectCharacterName(string line)
	{
		var match = StatsCharacterRegex.Match(line);
		if(!match.Success)
		{
			return null;
		}

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
		{
			return null;
		}

		if(!TimeOnly.TryParseExact(match.Groups["timestamp"].Value, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
		{
			return null;
		}

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

	private static SendEvent? TryDetectSendEvent(string line)
	{
		var match = SendLineRegex.Match(line);
		if(!match.Success)
		{
			return null;
		}

		if(!TimeOnly.TryParseExact(match.Groups["timestamp"].Value, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
		{
			return null;
		}

		return new SendEvent
		       {
				       Timestamp = timestamp,
				       Sender = match.Groups["sender"].Value,
				       Message = match.Groups["message"].Value
		       };
	}

	[GeneratedRegex(@"^\[(?<timestamp>\d{2}:\d{2}:\d{2})\] @@(?<sender>\w+) sends, ""(?<message>.+)""$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex GenerateSendLineRegex();
}
