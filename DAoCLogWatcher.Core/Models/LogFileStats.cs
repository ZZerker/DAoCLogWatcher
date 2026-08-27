namespace DAoCLogWatcher.Core.Models;

/// <summary>Sessions older than a retention cutoff, and what removing them would free.</summary>
public sealed record LogAgeBucket(string Label, int Months, int SessionCount, long LineCount, long ByteCount);

public sealed record LogFileStats(
		string FilePath,
		long ByteCount,
		long LineCount,
		int SessionCount,
		DateTime? OldestSessionStart,
		DateTime? NewestSessionEnd,
		IReadOnlyList<LogSession> Sessions,
		IReadOnlyList<LogAgeBucket> AgeBuckets)
{
	public bool IsEmpty => this.SessionCount == 0;
}
