using DAoCLogWatcher.Core.Models;

namespace DAoCLogWatcher.Core;

public interface IMultiKillDetector
{
	event EventHandler<MultiKillResult>? MultiKillDetected;

	void AdvanceTimestamp(TimeOnly? currentTs);

	void OnPlayerKillRp(TimeOnly entryTimestamp, int points, Action markAsMultiKill);

	void Reset();
}
