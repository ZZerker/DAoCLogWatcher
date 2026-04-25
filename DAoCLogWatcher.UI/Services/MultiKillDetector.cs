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
	private Action? markFirstEntryAsMultiKill;

	public event EventHandler<MultiKillResult>? MultiKillDetected;

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
	/// Call once a PlayerKill RP entry has been built. Opens or extends the current window.
	/// <paramref name="markAsMultiKill"/> is invoked on the first entry when the window closes.
	/// </summary>
	public void OnPlayerKillRp(TimeOnly entryTimestamp, int points, Action markAsMultiKill)
	{
		if(this.windowRpCount == 0)
		{
			this.windowStart = entryTimestamp;
			this.markFirstEntryAsMultiKill = markAsMultiKill;
		}

		this.windowRpCount++;
		this.windowRps += points;
	}

	public void Reset()
	{
		this.windowRpCount = 0;
		this.windowRps = 0;
		this.markFirstEntryAsMultiKill = null;
	}

	private void FinalizeWindow()
	{
		if(this.windowRpCount >= 5)
		{
			this.markFirstEntryAsMultiKill?.Invoke();

			this.MultiKillDetected?.Invoke(this, new MultiKillResult(this.windowStart, this.windowRps, this.windowRpCount));
		}

		this.windowRpCount = 0;
		this.windowRps = 0;
		this.markFirstEntryAsMultiKill = null;
	}
}
