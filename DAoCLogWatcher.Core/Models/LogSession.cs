namespace DAoCLogWatcher.Core.Models;

/// <summary>Represents a single game session delimited by "Chat Log Opened/Closed" markers.</summary>
public sealed class LogSession
{
	public required DateTime StartTime { get; init; }

	public DateTime? EndTime { get; set; }

	public string? CharacterName { get; set; }

	/// <summary>Byte offset of the "Chat Log Opened" line in the file.</summary>
	public long FilePosition { get; init; }

	/// <summary>Byte offset of the end of this session (start of next session, or end of file). -1 = no limit.</summary>
	public long EndFilePosition { get; set; } = -1;

	public TimeSpan Duration => this.EndTime.HasValue?this.EndTime.Value - this.StartTime:DateTime.Now - this.StartTime;

	public string DurationFormatted
	{
		get
		{
			var d = this.Duration;
			return d.TotalHours >= 1?$"{(int)d.TotalHours}h {d.Minutes:D2}m":$"{d.Minutes}m {d.Seconds:D2}s";
		}
	}

	public string StartTimeFormatted => this.StartTime.ToString("yyyy-MM-dd  HH:mm:ss");
}
