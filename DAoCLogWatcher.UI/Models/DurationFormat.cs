using System;

namespace DAoCLogWatcher.UI.Models;

public static class DurationFormat
{
	public static string Short(TimeSpan duration)
	{
		return duration.TotalHours >= 1?$"{(int)duration.TotalHours}h {duration.Minutes}m":$"{(int)duration.TotalMinutes}m";
	}
}
