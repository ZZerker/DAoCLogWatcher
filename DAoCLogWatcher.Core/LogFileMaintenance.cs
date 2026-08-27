using System.IO.Compression;
using System.Text;
using DAoCLogWatcher.Core.Models;

namespace DAoCLogWatcher.Core;

/// <summary>Plans and executes trimming whole sessions out of the head of chat.log into a growing archive file.</summary>
public static class LogFileMaintenance
{
	/// <summary>Name of the single growing archive, kept beside chat.log. A zip holding one plain
	/// chat log, so any zip tool opens it and the session markers inside survive untouched.</summary>
	public const string ARCHIVE_FILE_NAME = "chat-archive.zip";

	/// <summary>Name of the log inside <see cref="ARCHIVE_FILE_NAME"/>.</summary>
	public const string ARCHIVE_ENTRY_NAME = "chat-archive.log";

	private const int COPY_BUFFER_SIZE = 1024 * 1024;
	private const int MAX_TAIL_CATCHUP_PASSES = 3;

	private static readonly byte[] SessionMarkerBytes = Encoding.UTF8.GetBytes("*** Chat Log Opened");

	public static TrimPlan PlanTrim(LogFileStats stats, DateTime cutoffLocal)
	{
		var archiveFilePath = Path.Combine(Path.GetDirectoryName(stats.FilePath)!, ARCHIVE_FILE_NAME);

		var archivedSessions = stats.Sessions.Where(s => s.StartTime < cutoffLocal).ToList();
		var keptSessions = stats.Sessions.Where(s => s.StartTime >= cutoffLocal).ToList();

		if(archivedSessions.Count == 0)
		{
			return new TrimPlan(stats.FilePath, archiveFilePath, 0, 0, 0, 0, keptSessions.Count, 0, 0, null, null, null, null, TrimBlocker.NothingToTrim);
		}

		if(keptSessions.Count == 0)
		{
			return new TrimPlan(stats.FilePath, archiveFilePath, 0, archivedSessions.Count, 0, 0, 0, 0, 0, null, null, null, null, TrimBlocker.WouldEmptyLog);
		}

		// Sessions are newest-first, so the last kept session is the oldest one that survives the cut.
		var oldestKept = keptSessions[^1];
		var cutOffset = oldestKept.FilePosition;

		var archivedLineCount = archivedSessions.Sum(s => s.LineCount);
		var archivedByteCount = cutOffset;
		var keptLineCount = stats.LineCount - archivedLineCount;
		var keptByteCount = stats.ByteCount - cutOffset;

		var archivedFrom = archivedSessions.Min(s => s.StartTime);
		var archivedTo = archivedSessions.Max(s => s.EndTime ?? s.StartTime);
		var keptFrom = keptSessions.Min(s => s.StartTime);
		var keptTo = keptSessions.Max(s => s.EndTime ?? s.StartTime);

		return new TrimPlan(
				stats.FilePath,
				archiveFilePath,
				cutOffset,
				archivedSessions.Count,
				archivedLineCount,
				archivedByteCount,
				keptSessions.Count,
				keptLineCount,
				keptByteCount,
				archivedFrom,
				archivedTo,
				keptFrom,
				keptTo,
				TrimBlocker.None);
	}

	/// <summary>
	/// A zip entry cannot be appended to, so every trim rewrites the archive: the existing entry is
	/// streamed into a fresh zip, the newly archived head follows it, and the result replaces the old
	/// archive only once it is complete. The old archive is kept as a backup until chat.log has been
	/// rewritten, so a failure at any point leaves both files as they were.
	/// </summary>
	public static TrimResult ExecuteTrim(TrimPlan plan, IProgress<double>? progress = null)
	{
		var tempLogPath = plan.LogFilePath + ".tmp";
		var tempArchivePath = plan.ArchiveFilePath + ".tmp";
		var backupArchivePath = plan.ArchiveFilePath + ".bak";
		var archiveReplaced = false;

		try
		{
			using(var logStream = new FileStream(plan.LogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				if(logStream.Length < plan.CutOffset||!StartsWithSessionMarker(logStream, plan.CutOffset))
				{
					return new TrimResult(false, "The log file changed since it was analysed. Re-analyse and try again.", 0, plan.ArchiveFilePath, 0);
				}

				var buffer = new byte[COPY_BUFFER_SIZE];
				var carriedBytes = ExistingArchiveLength(plan.ArchiveFilePath);
				var tailLength = logStream.Length - plan.CutOffset;
				var grandTotal = carriedBytes + plan.CutOffset + tailLength;

				var expectedEntryLength = WriteArchive(plan, tempArchivePath, logStream, buffer, carriedBytes, grandTotal, progress);
				if(!ArchiveEntryHasLength(tempArchivePath, expectedEntryLength))
				{
					return new TrimResult(false, "Archive write did not complete as expected. chat.log was not modified.", 0, plan.ArchiveFilePath, 0);
				}

				if(File.Exists(plan.ArchiveFilePath))
				{
					File.Replace(tempArchivePath, plan.ArchiveFilePath, backupArchivePath);
				}
				else
				{
					File.Move(tempArchivePath, plan.ArchiveFilePath);
				}

				archiveReplaced = true;

				using(var tempStream = new FileStream(tempLogPath, FileMode.Create, FileAccess.Write, FileShare.None))
				{
					var endOffset = logStream.Length;
					CopyRange(logStream, tempStream, plan.CutOffset, endOffset, buffer, carriedBytes + plan.CutOffset, grandTotal, progress);

					// The game may still be appending. Copying the growth costs nothing when idle and
					// keeps lines written during the trim from being dropped by the swap below.
					var pass = 0;
					while(pass < MAX_TAIL_CATCHUP_PASSES)
					{
						var newLength = logStream.Length;
						if(newLength <= endOffset)
						{
							break;
						}

						CopyRange(logStream, tempStream, endOffset, newLength, buffer, carriedBytes + endOffset, grandTotal, progress);
						endOffset = newLength;
						pass++;
					}

					tempStream.Flush(true);
				}
			}

			// Only once our own read handle is closed: on Windows File.Replace fails with a sharing
			// violation while the destination is still open, even by this process.
			File.Replace(tempLogPath, plan.LogFilePath, null);

			DeleteQuietly(backupArchivePath);
			return new TrimResult(true, null, plan.CutOffset, plan.ArchiveFilePath, FileLengthOrZero(plan.ArchiveFilePath));
		}
		catch(IOException ex)
		{
			RestoreArchive(plan.ArchiveFilePath, backupArchivePath, archiveReplaced);
			return new TrimResult(false, ex.Message, 0, plan.ArchiveFilePath, 0);
		}
		catch(UnauthorizedAccessException ex)
		{
			RestoreArchive(plan.ArchiveFilePath, backupArchivePath, archiveReplaced);
			return new TrimResult(false, ex.Message, 0, plan.ArchiveFilePath, 0);
		}
		catch(InvalidDataException ex)
		{
			RestoreArchive(plan.ArchiveFilePath, backupArchivePath, archiveReplaced);
			return new TrimResult(false, $"The existing archive could not be read: {ex.Message}", 0, plan.ArchiveFilePath, 0);
		}
		finally
		{
			DeleteQuietly(tempLogPath);
			DeleteQuietly(tempArchivePath);
		}
	}

	/// <summary>Writes the new archive and returns the uncompressed length its entry should have.</summary>
	private static long WriteArchive(TrimPlan plan, string tempArchivePath, FileStream logStream, byte[] buffer, long carriedBytes, long grandTotal, IProgress<double>? progress)
	{
		using var tempArchiveStream = new FileStream(tempArchivePath, FileMode.Create, FileAccess.Write, FileShare.None);
		using(var zip = new ZipArchive(tempArchiveStream, ZipArchiveMode.Create, true))
		{
			var entry = zip.CreateEntry(ARCHIVE_ENTRY_NAME, CompressionLevel.Optimal);
			using var entryStream = entry.Open();

			if(carriedBytes > 0)
			{
				using var oldZip = ZipFile.OpenRead(plan.ArchiveFilePath);
				var oldEntry = oldZip.GetEntry(ARCHIVE_ENTRY_NAME);
				if(oldEntry != null)
				{
					using var oldEntryStream = oldEntry.Open();
					CopyStream(oldEntryStream, entryStream, buffer, 0, grandTotal, progress);
				}
			}

			CopyRange(logStream, entryStream, 0, plan.CutOffset, buffer, carriedBytes, grandTotal, progress);
		}

		tempArchiveStream.Flush(true);
		return carriedBytes + plan.CutOffset;
	}

	/// <summary>Uncompressed length of the log already inside the archive, or 0 when there is none yet.</summary>
	private static long ExistingArchiveLength(string archiveFilePath)
	{
		if(!File.Exists(archiveFilePath))
		{
			return 0;
		}

		using var zip = ZipFile.OpenRead(archiveFilePath);
		return zip.GetEntry(ARCHIVE_ENTRY_NAME)?.Length ?? 0;
	}

	private static bool ArchiveEntryHasLength(string archiveFilePath, long expectedLength)
	{
		using var zip = ZipFile.OpenRead(archiveFilePath);
		return zip.GetEntry(ARCHIVE_ENTRY_NAME)?.Length == expectedLength;
	}

	/// <summary>Puts the previous archive back after a failed trim, so a retry does not archive the same sessions twice.</summary>
	private static void RestoreArchive(string archiveFilePath, string backupArchivePath, bool archiveReplaced)
	{
		if(!archiveReplaced||!File.Exists(backupArchivePath))
		{
			return;
		}

		try
		{
			File.Move(backupArchivePath, archiveFilePath, true);
		}
		catch(IOException)
		{
		}
		catch(UnauthorizedAccessException)
		{
		}
	}

	private static void DeleteQuietly(string path)
	{
		if(!File.Exists(path))
		{
			return;
		}

		try
		{
			File.Delete(path);
		}
		catch(IOException)
		{
		}
		catch(UnauthorizedAccessException)
		{
		}
	}

	private static long FileLengthOrZero(string path)
	{
		try
		{
			return File.Exists(path)?new FileInfo(path).Length:0;
		}
		catch(IOException)
		{
			return 0;
		}
	}

	private static bool StartsWithSessionMarker(FileStream logStream, long cutOffset)
	{
		var markerBuffer = new byte[SessionMarkerBytes.Length];
		logStream.Seek(cutOffset, SeekOrigin.Begin);
		var read = logStream.Read(markerBuffer, 0, markerBuffer.Length);
		if(read < markerBuffer.Length)
		{
			return false;
		}

		return markerBuffer.AsSpan().SequenceEqual(SessionMarkerBytes);
	}

	/// <summary>Streams <paramref name="source"/> bytes [<paramref name="startOffset"/>, <paramref name="endOffset"/>) into <paramref name="destination"/> using one reusable buffer.
	/// Progress is reported against the whole move (<paramref name="grandTotal"/> bytes), not just this range, so the bar ramps monotonically across phases.</summary>
	private static void CopyRange(FileStream source, Stream destination, long startOffset, long endOffset, byte[] buffer, long alreadyCopied, long grandTotal, IProgress<double>? progress)
	{
		source.Seek(startOffset, SeekOrigin.Begin);
		CopyExactly(source, destination, endOffset - startOffset, buffer, alreadyCopied, grandTotal, progress);
	}

	/// <summary>Copies <paramref name="source"/> to its end. Used for the zip entry, which is not seekable.</summary>
	private static void CopyStream(Stream source, Stream destination, byte[] buffer, long alreadyCopied, long grandTotal, IProgress<double>? progress)
	{
		CopyExactly(source, destination, -1, buffer, alreadyCopied, grandTotal, progress);
	}

	private static void CopyExactly(Stream source, Stream destination, long byteCount, byte[] buffer, long alreadyCopied, long grandTotal, IProgress<double>? progress)
	{
		var remaining = byteCount;
		var copied = 0L;

		while(remaining != 0)
		{
			var toRead = remaining < 0?buffer.Length:(int)Math.Min(buffer.Length, remaining);
			var read = source.Read(buffer, 0, toRead);
			if(read <= 0)
			{
				break;
			}

			destination.Write(buffer, 0, read);
			copied += read;

			if(remaining > 0)
			{
				remaining -= read;
			}

			if(grandTotal > 0)
			{
				progress?.Report(Math.Min(1.0, (alreadyCopied + copied) / (double)grandTotal));
			}
		}
	}
}
