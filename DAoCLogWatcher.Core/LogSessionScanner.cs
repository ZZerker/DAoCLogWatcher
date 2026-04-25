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

	/// <summary>Scan the file and return all sessions, newest first.</summary>
	public static List<LogSession> Scan(string logFilePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(logFilePath);
		if(!File.Exists(logFilePath))
		{
			return [];
		}

		var sessions = new List<LogSession>();
		LogSession? current = null;

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
					ProcessScanLine(line, lineStart, sessions, ref current);
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
			ProcessScanLine(line, lineStart, sessions, ref current);
		}

		for(var i = 0; i < sessions.Count - 1; i++)
		{
			sessions[i].EndFilePosition = sessions[i + 1].FilePosition;
		}

		if(sessions.Count > 0)
		{
			sessions[^1].EndFilePosition = fs.Length;
		}

		sessions.Reverse();
		return sessions;
	}

	private static void ProcessScanLine(string line, long lineStart, List<LogSession> sessions, ref LogSession? current)
	{
		var openMatch = OpenRegex.Match(line);
		if(openMatch.Success)
		{
			if(!DateTime.TryParseExact(openMatch.Groups["date"].Value, DATE_FORMAT, CultureInfo.InvariantCulture, DateTimeStyles.None, out var openedAt))
			{
				return;
			}

			if(current is { EndTime: null })
			{
				current.EndTime = openedAt;
			}

			current = new LogSession
			          {
					          StartTime = openedAt,
					          FilePosition = lineStart
			          };
			sessions.Add(current);

			return;
		}

		var closeMatch = CloseRegex.Match(line);
		if(closeMatch.Success&&current != null)
		{
			if(DateTime.TryParseExact(closeMatch.Groups["date"].Value, DATE_FORMAT, CultureInfo.InvariantCulture, DateTimeStyles.None, out var closedAt))
			{
				current.EndTime = closedAt;
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
