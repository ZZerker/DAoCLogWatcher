using System.Text;
using DAoCLogWatcher.Core;
using FluentAssertions;

namespace DAoCLogWatcher.Tests.Core;

public sealed class LogSessionScannerTests: IDisposable
{
	private readonly List<string> tempFiles = [];

	public void Dispose()
	{
		foreach(var f in this.tempFiles)
		{
			if(File.Exists(f))
			{
				File.Delete(f);
			}
		}
	}

	// Creates a temp file with \n line endings (no CRLF ambiguity) and tracks it for cleanup.
	private string CreateTempFile(string content)
	{
		var path = Path.GetTempFileName();
		File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
		this.tempFiles.Add(path);
		return path;
	}

	// ── Helpers ──────────────────────────────────────────────────────────────

	private static string OpenLine(string date)
	{
		return $"*** Chat Log Opened: {date}\n";
	}

	private static string CloseLine(string date)
	{
		return $"*** Chat Log Closed: {date}\n";
	}

	private static string StatsLine(string name)
	{
		return $"Statistics for {name} this Session:\n";
	}

	private static string ChatLine(string msg)
	{
		return $"[12:00:00] {msg}\n";
	}

	private const string D1 = "Mon Jan 1 12:00:00 2024";
	private const string D2 = "Tue Jan 2 10:00:00 2024";
	private const string D3 = "Wed Jan 3 08:00:00 2024";

	// ── Tests ─────────────────────────────────────────────────────────────────

	[Fact]
	public void Scan_MissingFile_ReturnsEmptyList()
	{
		var sessions = LogSessionScanner.Scan(@"C:\does\not\exist\chat.log");
		sessions.Should().BeEmpty();
	}

	[Fact]
	public void Scan_EmptyFile_ReturnsEmptyList()
	{
		var path = this.CreateTempFile("");
		LogSessionScanner.Scan(path).Should().BeEmpty();
	}

	[Fact]
	public void Scan_SingleSessionNoClose_ReturnsSessionWithNullEndTime()
	{
		var path = this.CreateTempFile(OpenLine(D1));
		var sessions = LogSessionScanner.Scan(path);

		sessions.Should().HaveCount(1);
		sessions[0].StartTime.Should().Be(new DateTime(2024, 1, 1, 12, 0, 0));
		sessions[0].EndTime.Should().BeNull();
	}

	[Fact]
	public void Scan_SingleSessionWithClose_ReturnsSessionWithEndTime()
	{
		var path = this.CreateTempFile(OpenLine(D1) + CloseLine(D2));
		var sessions = LogSessionScanner.Scan(path);

		sessions.Should().HaveCount(1);
		sessions[0].StartTime.Should().Be(new DateTime(2024, 1, 1, 12, 0, 0));
		sessions[0].EndTime.Should().Be(new DateTime(2024, 1, 2, 10, 0, 0));
	}

	[Fact]
	public void Scan_MultipleSessionsReturnsNewestFirst()
	{
		var path = this.CreateTempFile(OpenLine(D1) + CloseLine(D2) + OpenLine(D2) + CloseLine(D3));
		var sessions = LogSessionScanner.Scan(path);

		sessions.Should().HaveCount(2);
		sessions[0].StartTime.Should().Be(new DateTime(2024, 1, 2, 10, 0, 0), "first result is newest");
		sessions[1].StartTime.Should().Be(new DateTime(2024, 1, 1, 12, 0, 0), "second result is oldest");
	}

	[Fact]
	public void Scan_ExtractsCharacterName()
	{
		var path = this.CreateTempFile(OpenLine(D1) + StatsLine("Zordrak"));
		var sessions = LogSessionScanner.Scan(path);

		sessions[0].CharacterName.Should().Be("Zordrak");
	}

	[Fact]
	public void Scan_CharacterNameOnlyTakenFromFirstStatsLine()
	{
		var path = this.CreateTempFile(OpenLine(D1) + StatsLine("Zordrak") + StatsLine("Altchar"));
		var sessions = LogSessionScanner.Scan(path);

		sessions[0].CharacterName.Should().Be("Zordrak", "only the first stats line per session is used");
	}

	[Fact]
	public void Scan_FilePositionPointsToOpenLine()
	{
		var line1 = OpenLine(D1);
		var line2 = CloseLine(D2);
		var line3 = OpenLine(D2);
		var path = this.CreateTempFile(line1 + line2 + line3);

		var sessions = LogSessionScanner.Scan(path);
		var expectedNewestPos = (long)Encoding.UTF8.GetByteCount(line1 + line2);

		sessions[0].FilePosition.Should().Be(expectedNewestPos, "newest session open line starts after lines 1-2");
		sessions[1].FilePosition.Should().Be(0, "oldest session open line is at the start of the file");
	}

	[Fact]
	public void Scan_EndFilePositionIsSetCorrectly()
	{
		var line1 = OpenLine(D1);
		var line2 = CloseLine(D1);
		var line3 = OpenLine(D2);
		var content = line1 + line2 + line3;
		var path = this.CreateTempFile(content);

		var sessions = LogSessionScanner.Scan(path);
		var totalBytes = (long)Encoding.UTF8.GetByteCount(content);
		var session2StartPos = (long)Encoding.UTF8.GetByteCount(line1 + line2);

		// Newest session (D2) ends at file end
		sessions[0].EndFilePosition.Should().Be(totalBytes);

		// Oldest session (D1) ends where newest session starts
		sessions[1].EndFilePosition.Should().Be(session2StartPos);
	}
}
