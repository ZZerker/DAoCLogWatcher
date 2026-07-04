namespace DAoCLogWatcher.UI.Helpers;

public static class StatMath
{
	public static double KdRatio(int kills, int deaths)
	{
		return deaths > 0?(double)kills / deaths:kills;
	}
}
