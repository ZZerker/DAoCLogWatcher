using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using DAoCLogWatcher.Core;
using DAoCLogWatcher.Core.Models;
using FluentAssertions;
using Xunit;

namespace DAoCLogWatcher.Tests.Core;

/// <summary>
/// These run against real files in a temp directory: the trim rewrites chat.log in place, so the
/// only meaningful verification is that the bytes actually land where they should.
/// </summary>
public sealed class LogFileMaintenanceTests: IDisposable
{
	private readonly string directory;
	private readonly string logPath;

	public LogFileMaintenanceTests()
	{
		this.directory = Path.Combine(Path.GetTempPath(), "dlw-trim-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(this.directory);
		this.logPath = Path.Combine(this.directory, "chat.log");
	}

	public void Dispose()
	{
		try
		{
			Directory.Delete(this.directory, true);
		}
		catch(IOException)
		{
		}
	}

	private static string Session(DateTime start, string character, int bodyLines)
	{
		var sb = new StringBuilder();
		sb.Append($"*** Chat Log Opened: {start.ToString("ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture)}\r\n");
		sb.Append($"Statistics for {character} this Session:\r\n");
		for(var i = 0; i < bodyLines; i++)
		{
			sb.Append($"You hit the training dummy for {i} damage.\r\n");
		}

		sb.Append($"*** Chat Log Closed: {start.AddHours(1).ToString("ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture)}\r\n");
		return sb.ToString();
	}

	private void WriteLog(params string[] sessions)
	{
		File.WriteAllText(this.logPath, string.Concat(sessions), new UTF8Encoding(false));
	}

	private string ArchiveText()
	{
		using var zip = ZipFile.OpenRead(Path.Combine(this.directory, LogFileMaintenance.ARCHIVE_FILE_NAME));
		using var entry = zip.GetEntry(LogFileMaintenance.ARCHIVE_ENTRY_NAME)!.Open();
		using var reader = new StreamReader(entry);
		return reader.ReadToEnd();
	}

	[Fact]
	public void Analyze_CountsSessionsAndLines()
	{
		var old = Session(DateTime.Now.AddYears(-2), "Oldchar", 3);
		var recent = Session(DateTime.Now.AddDays(-1), "Newchar", 5);
		this.WriteLog(old, recent);

		var stats = LogSessionScanner.Analyze(this.logPath);

		stats.SessionCount.Should().Be(2);
		stats.LineCount.Should().Be(6 + 8);
		stats.ByteCount.Should().Be(new FileInfo(this.logPath).Length);
		stats.Sessions[0].CharacterName.Should().Be("Newchar");
	}

	[Fact]
	public void PlanTrim_CutsOnTheSessionBoundary()
	{
		var old = Session(DateTime.Now.AddYears(-2), "Oldchar", 3);
		var recent = Session(DateTime.Now.AddDays(-1), "Newchar", 5);
		this.WriteLog(old, recent);

		var stats = LogSessionScanner.Analyze(this.logPath);
		var plan = LogFileMaintenance.PlanTrim(stats, DateTime.Now.AddMonths(-1));

		plan.Blocker.Should().Be(TrimBlocker.None);
		plan.CutOffset.Should().Be(Encoding.UTF8.GetByteCount(old));
		plan.ArchivedSessionCount.Should().Be(1);
		plan.KeptSessionCount.Should().Be(1);
		plan.ArchivedByteCount.Should().Be(plan.CutOffset);
		(plan.ArchivedByteCount + plan.KeptByteCount).Should().Be(stats.ByteCount);
		(plan.ArchivedLineCount + plan.KeptLineCount).Should().Be(stats.LineCount);
	}

	[Fact]
	public void PlanTrim_RefusesToEmptyTheLog()
	{
		this.WriteLog(Session(DateTime.Now.AddYears(-2), "Oldchar", 3));

		var stats = LogSessionScanner.Analyze(this.logPath);
		var plan = LogFileMaintenance.PlanTrim(stats, DateTime.Now.AddMonths(-1));

		plan.Blocker.Should().Be(TrimBlocker.WouldEmptyLog);
		plan.CanExecute.Should().BeFalse();
	}

	[Fact]
	public void PlanTrim_NothingOldEnough()
	{
		this.WriteLog(Session(DateTime.Now.AddDays(-2), "Newchar", 3));

		var stats = LogSessionScanner.Analyze(this.logPath);
		var plan = LogFileMaintenance.PlanTrim(stats, DateTime.Now.AddMonths(-1));

		plan.Blocker.Should().Be(TrimBlocker.NothingToTrim);
	}

	[Fact]
	public void ExecuteTrim_MovesOldSessionsIntoTheArchiveAndKeepsTheRest()
	{
		var old = Session(DateTime.Now.AddYears(-2), "Oldchar", 3);
		var recent = Session(DateTime.Now.AddDays(-1), "Newchar", 5);
		this.WriteLog(old, recent);

		var stats = LogSessionScanner.Analyze(this.logPath);
		var plan = LogFileMaintenance.PlanTrim(stats, DateTime.Now.AddMonths(-1));
		var result = LogFileMaintenance.ExecuteTrim(plan);

		result.ErrorMessage.Should().BeNull();
		result.Success.Should().BeTrue();
		File.ReadAllText(this.logPath).Should().Be(recent);
		this.ArchiveText().Should().Be(old);
		result.ArchiveSizeOnDisk.Should().BeGreaterThan(0);
	}

	[Fact]
	public void ExecuteTrim_SecondTrimAppendsToTheSameArchive()
	{
		var first = Session(DateTime.Now.AddYears(-3), "First", 3);
		var second = Session(DateTime.Now.AddYears(-2), "Second", 4);
		var recent = Session(DateTime.Now.AddDays(-1), "Newchar", 5);
		this.WriteLog(first, second, recent);

		var stats = LogSessionScanner.Analyze(this.logPath);
		LogFileMaintenance.ExecuteTrim(LogFileMaintenance.PlanTrim(stats, DateTime.Now.AddMonths(-30))).Success.Should().BeTrue();
		this.ArchiveText().Should().Be(first);

		var stats2 = LogSessionScanner.Analyze(this.logPath);
		LogFileMaintenance.ExecuteTrim(LogFileMaintenance.PlanTrim(stats2, DateTime.Now.AddMonths(-1))).Success.Should().BeTrue();

		this.ArchiveText().Should().Be(first + second);
		File.ReadAllText(this.logPath).Should().Be(recent);
	}

	[Fact]
	public void ExecuteTrim_TrimmedLogStillScansAsASession()
	{
		var old = Session(DateTime.Now.AddYears(-2), "Oldchar", 3);
		var recent = Session(DateTime.Now.AddDays(-1), "Newchar", 5);
		this.WriteLog(old, recent);

		var plan = LogFileMaintenance.PlanTrim(LogSessionScanner.Analyze(this.logPath), DateTime.Now.AddMonths(-1));
		LogFileMaintenance.ExecuteTrim(plan);

		var sessions = LogSessionScanner.Scan(this.logPath);
		sessions.Should().HaveCount(1);
		sessions[0].CharacterName.Should().Be("Newchar");
		sessions[0].FilePosition.Should().Be(0);
	}

	[Fact]
	public void ExecuteTrim_RefusesWhenTheLogChangedSinceAnalysis()
	{
		var old = Session(DateTime.Now.AddYears(-2), "Oldchar", 3);
		var recent = Session(DateTime.Now.AddDays(-1), "Newchar", 5);
		this.WriteLog(old, recent);

		var plan = LogFileMaintenance.PlanTrim(LogSessionScanner.Analyze(this.logPath), DateTime.Now.AddMonths(-1));

		// Rewritten with a different first session, so the cut offset no longer lands on a marker.
		this.WriteLog(Session(DateTime.Now.AddYears(-2), "Different", 9), recent);
		var before = File.ReadAllText(this.logPath);

		var result = LogFileMaintenance.ExecuteTrim(plan);

		result.Success.Should().BeFalse();
		File.ReadAllText(this.logPath).Should().Be(before);
		File.Exists(Path.Combine(this.directory, LogFileMaintenance.ARCHIVE_FILE_NAME)).Should().BeFalse();
	}

	[Fact]
	public void ExecuteTrim_ReportsMonotonicProgress()
	{
		var old = Session(DateTime.Now.AddYears(-2), "Oldchar", 200);
		var recent = Session(DateTime.Now.AddDays(-1), "Newchar", 200);
		this.WriteLog(old, recent);

		var reported = new System.Collections.Generic.List<double>();
		var plan = LogFileMaintenance.PlanTrim(LogSessionScanner.Analyze(this.logPath), DateTime.Now.AddMonths(-1));
		LogFileMaintenance.ExecuteTrim(plan, new Progress<double>(reported.Add));

		// Progress<T> posts asynchronously, so only ordering of what arrived can be asserted.
		reported.Should().BeInAscendingOrder();
		reported.Should().OnlyContain(p => p >= 0.0&&p <= 1.0);
	}
}
