using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DAoCLogWatcher.Core.Models;

namespace DAoCLogWatcher.Core;

/// <summary>
/// Scans a DAoC chat.log file and extracts all sessions (delimited by "Chat Log Opened/Closed" markers).
/// Returns sessions sorted newest-first.
/// </summary>
public static partial class LogSessionScanner
{
	private static readonly Regex OpenRegex = GenerateOpenRegex();
	private static readonly Regex CloseRegex = GenerateCloseRegex();
	private static readonly Regex CharacterRegex = GenerateCharacterRegex();

	private const string DATE_FORMAT = "ddd MMM d HH:mm:ss yyyy";

	private static readonly (string Label, int Months)[] AgeBucketDefs =
	[
			("1 month", 1),
			("3 months", 3),
			("6 months", 6),
			("1 year", 12)
	];

	public static List<LogSession> Scan(string logFilePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(logFilePath);
		if(!File.Exists(logFilePath))
		{
			return [];
		}

		return ScanCore(logFilePath).Sessions;
	}

	/// <summary>Same streaming pass as <see cref="Scan"/>, plus line/byte totals and age-bucket breakdowns for the maintenance UI.</summary>
	public static LogFileStats Analyze(string logFilePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(logFilePath);
		if(!File.Exists(logFilePath))
		{
			return new LogFileStats(logFilePath, 0, 0, 0, null, null, [], []);
		}

		var result = ScanCore(logFilePath);
		var sessions = result.Sessions;

		// Newest-first: the oldest session is the last one.
		var oldestStart = sessions.Count > 0?sessions[^1].StartTime:(DateTime?)null;
		var newestEnd = sessions.Count > 0?sessions[0].EndTime ?? sessions[0].StartTime:(DateTime?)null;

		var now = DateTime.Now;
		var buckets = new List<LogAgeBucket>(AgeBucketDefs.Length);
		foreach(var def in AgeBucketDefs)
		{
			var cutoff = now.AddMonths(-def.Months);
			var older = sessions.Where(s => s.StartTime < cutoff).ToList();
			var lineSum = older.Sum(s => s.LineCount);
			var byteSum = older.Sum(s => s.ByteLength);
			buckets.Add(new LogAgeBucket(def.Label, def.Months, older.Count, lineSum, byteSum));
		}

		return new LogFileStats(logFilePath, result.FileLength, result.TotalLineCount, sessions.Count, oldestStart, newestEnd, sessions, buckets);
	}

	private readonly record struct ScanCoreResult(List<LogSession> Sessions, long TotalLineCount, long FileLength);

	private static ScanCoreResult ScanCore(string logFilePath)
	{
		var sessions = new List<LogSession>();
		LogSession? current = null;
		long sessionLineCount = 0;
		long totalLineCount = 0;

		// Read in chunks to avoid allocating the entire file on the LOH.
		// We track byte offsets manually because StreamReader buffers internally
		// so stream.Position is unreliable for line offsets.
		using var fs = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		var readBuffer = new byte[65536];
		var lineBuffer = new byte[4096];
		var lineLen = 0;
		long lineStart = 0;
		long position = 0;

		int bytesRead;
		while((bytesRead = fs.Read(readBuffer, 0, readBuffer.Length)) > 0)
		{
			for(var i = 0; i < bytesRead; i++)
			{
				if(readBuffer[i] == (byte)'\n')
				{
					var end = lineLen > 0&&lineBuffer[lineLen - 1] == (byte)'\r'?lineLen - 1:lineLen;
					var line = Encoding.UTF8.GetString(lineBuffer, 0, end);
					totalLineCount++;
					ProcessScanLine(line, lineStart, sessions, ref current, ref sessionLineCount);
					lineLen = 0;
					lineStart = position + i + 1;
				}
				else
				{
					if(lineLen >= lineBuffer.Length)
					{
						Array.Resize(ref lineBuffer, lineBuffer.Length * 2);
					}

					lineBuffer[lineLen++] = readBuffer[i];
				}
			}

			position += bytesRead;
		}

		if(lineLen > 0)
		{
			var line = Encoding.UTF8.GetString(lineBuffer, 0, lineLen);
			totalLineCount++;
			ProcessScanLine(line, lineStart, sessions, ref current, ref sessionLineCount);
		}

		for(var i = 0; i < sessions.Count - 1; i++)
		{
			sessions[i].EndFilePosition = sessions[i + 1].FilePosition;
		}

		if(sessions.Count > 0)
		{
			sessions[^1].EndFilePosition = fs.Length;
			sessions[^1].LineCount = sessionLineCount;
		}

		sessions.Reverse();
		return new ScanCoreResult(sessions, totalLineCount, fs.Length);
	}

	private static void ProcessScanLine(string line, long lineStart, List<LogSession> sessions, ref LogSession? current, ref long sessionLineCount)
	{
		var openMatch = OpenRegex.Match(line);
		if(openMatch.Success)
		{
			if(!DateTime.TryParseExact(openMatch.Groups["date"].Value, DATE_FORMAT, CultureInfo.InvariantCulture, DateTimeStyles.None, out var openedAt))
			{
				Debug.WriteLine($"[SessionScanner] Unrecognized open date: '{openMatch.Groups["date"].Value}'");
				sessionLineCount++;
				return;
			}

			if(current is { EndTime: null })
			{
				current.EndTime = openedAt;
			}

			if(current != null)
			{
				current.LineCount = sessionLineCount;
			}

			current = new LogSession
			          {
					          StartTime = openedAt,
					          FilePosition = lineStart
			          };
			sessions.Add(current);
			sessionLineCount = 1;

			return;
		}

		sessionLineCount++;

		var closeMatch = CloseRegex.Match(line);
		if(closeMatch.Success&&current != null)
		{
			if(DateTime.TryParseExact(closeMatch.Groups["date"].Value, DATE_FORMAT, CultureInfo.InvariantCulture, DateTimeStyles.None, out var closedAt))
			{
				current.EndTime = closedAt;
			}
			else
			{
				Debug.WriteLine($"[SessionScanner] Unrecognized close date: '{closeMatch.Groups["date"].Value}'");
			}

			return;
		}

		if(current is { CharacterName: null })
		{
			var charMatch = CharacterRegex.Match(line);
			if(charMatch.Success)
			{
				current.CharacterName = charMatch.Groups["name"].Value;
			}
		}
	}

	[GeneratedRegex(@"^\*\*\* Chat Log Opened: (?<date>.+)$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex GenerateOpenRegex();

	[GeneratedRegex(@"^\*\*\* Chat Log Closed: (?<date>.+)$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex GenerateCloseRegex();

	[GeneratedRegex(@"^Statistics for (?<name>\w+) this Session:$", RegexOptions.CultureInvariant)]
	private static partial Regex GenerateCharacterRegex();
}
