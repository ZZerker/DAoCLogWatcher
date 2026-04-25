namespace DAoCLogWatcher.Core;

public sealed class LogWatcherFactory: ILogWatcherFactory
{
	public LogWatcher Create(string logFilePath, long startPosition = 0, bool enableTimeFiltering = false, double filterHours = 24, long endPosition = -1)
	{
		return new LogWatcher(logFilePath, startPosition, enableTimeFiltering, filterHours, endPosition);
	}
}
