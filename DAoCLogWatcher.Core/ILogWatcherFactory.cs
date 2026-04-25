namespace DAoCLogWatcher.Core;

public interface ILogWatcherFactory
{
	LogWatcher Create(string logFilePath, long startPosition = 0, bool enableTimeFiltering = false, double filterHours = 24, long endPosition = -1);
}
