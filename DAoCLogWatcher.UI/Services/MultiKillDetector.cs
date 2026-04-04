using System;
using DAoCLogWatcher.Core;
using DAoCLogWatcher.UI.Models;

namespace DAoCLogWatcher.UI.Services;

/// <summary>
/// Tracks a sliding window of PlayerKill RP entries and fires
/// <see cref="MultiKillDetected"/> when five or more accumulate before
/// the window expires.
/// </summary>
internal sealed class MultiKillDetector
{
	private int windowRpCount;
	private int windowRps;
	private TimeOnly windowStart;
	private RealmPointLogEntry? windowFirstEntry;

	public event EventHandler<RealmPointLogEntry>? MultiKillDetected;

	/// <summary>
	/// Call on every timestamped log line. If the window is open and the
	/// timestamp has moved past the expiry threshold, the window is finalized.
	/// </summary>
	public void AdvanceTimestamp(TimeOnly? currentTs)
	{
		if(this.windowRpCount <= 0||!currentTs.HasValue)
		{
			return;
		}

		var diffSec = TimeHelper.ShortestArcSeconds(currentTs.Value, this.windowStart);
		if(diffSec > 5 + this.windowRpCount * 0.2)
		{
			this.FinalizeWindow();
		}
	}

	/// <summary>
	/// Call once a PlayerKill RP entry has been built into a <see cref="RealmPointLogEntry"/>.
	/// Opens or extends the current window.
	/// </summary>
	public void OnPlayerKillRp(TimeOnly entryTimestamp, int points, RealmPointLogEntry logEntry)
	{
		if(this.windowRpCount == 0)
		{
			this.windowStart = entryTimestamp;
			this.windowFirstEntry = logEntry;
		}

		this.windowRpCount++;
		this.windowRps += points;
	}

	public void Reset()
	{
		this.windowRpCount = 0;
		this.windowRps = 0;
		this.windowFirstEntry = null;
	}

	private void FinalizeWindow()
	{
		if(this.windowRpCount >= 5)
		{
			if(this.windowFirstEntry != null)
			{
				this.windowFirstEntry.IsMultiKill = true;
			}

			this.MultiKillDetected?.Invoke(this,
			                               new RealmPointLogEntry
			                               {
					                               Timestamp = this.windowStart.ToString("HH:mm:ss"),
					                               Points = this.windowRps,
					                               Source = "Multi-Kill",
					                               Details = $"{this.windowRpCount}x player kills",
					                               IsMultiKill = true,
					                               KillCount = this.windowRpCount
			                               });
		}

		this.windowRpCount = 0;
		this.windowRps = 0;
		this.windowFirstEntry = null;
	}
}
