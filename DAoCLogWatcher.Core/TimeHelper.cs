namespace DAoCLogWatcher.Core;

public static class TimeHelper
{
	/// <summary>
	/// Returns the shortest arc (in seconds) between two TimeOnly values,
	/// correctly handling midnight wraparound.
	/// </summary>
	public static double ShortestArcSeconds(TimeOnly a, TimeOnly b)
	{
		var diff = Math.Abs((a.ToTimeSpan() - b.ToTimeSpan()).TotalSeconds);
		if(diff > 43200)
		{
			diff = 86400 - diff;
		}

		return diff;
	}
}
