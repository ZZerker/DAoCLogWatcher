using DAoCLogWatcher.Core.Models;

namespace DAoCLogWatcher.Core;

public sealed class MultiKillDetector: IMultiKillDetector
{
	private int windowRpCount;
	private int windowRps;
	private TimeOnly windowStart;
	private Action? markFirstEntryAsMultiKill;

	public event EventHandler<MultiKillResult>? MultiKillDetected;

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
		this.ClearWindow();
	}

	private void FinalizeWindow()
	{
		if(this.windowRpCount >= 5)
		{
			this.markFirstEntryAsMultiKill?.Invoke();
			this.MultiKillDetected?.Invoke(this, new MultiKillResult(this.windowStart, this.windowRps, this.windowRpCount));
		}

		this.ClearWindow();
	}

	private void ClearWindow()
	{
		this.windowRpCount = 0;
		this.windowRps = 0;
		this.markFirstEntryAsMultiKill = null;
	}
}
