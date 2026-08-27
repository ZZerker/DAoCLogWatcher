namespace DAoCLogWatcher.Core.Models;

/// <summary>Why a trim cannot run. None means the plan is executable.</summary>
public enum TrimBlocker
{
	None,
	NothingToTrim,
	WouldEmptyLog
}

public sealed record TrimPlan(
		string LogFilePath,
		string ArchiveFilePath,
		long CutOffset,
		int ArchivedSessionCount,
		long ArchivedLineCount,
		long ArchivedByteCount,
		int KeptSessionCount,
		long KeptLineCount,
		long KeptByteCount,
		DateTime? ArchivedFrom,
		DateTime? ArchivedTo,
		DateTime? KeptFrom,
		DateTime? KeptTo,
		TrimBlocker Blocker)
{
	public bool CanExecute => this.Blocker == TrimBlocker.None&&this.CutOffset > 0;
}

/// <summary><paramref name="ArchiveSizeOnDisk"/> is the compressed size of the archive after the trim, 0 when it failed.</summary>
public sealed record TrimResult(bool Success, string? ErrorMessage, long BytesRemoved, string ArchiveFilePath, long ArchiveSizeOnDisk);
