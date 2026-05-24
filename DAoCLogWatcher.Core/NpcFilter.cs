namespace DAoCLogWatcher.Core;

public static class NpcFilter
{
	private static readonly HashSet<string> KnownNpcNames = new(StringComparer.OrdinalIgnoreCase)
	{
		"Guard", "Guardian", "Keep Door", "Keep Gate", "Supply Commander",
        "Armsman", "Druid", "Eldritch", "Hunter", "Huscarl", "Nightshade", "Ranger", "Huscarl"
    };

	public static bool IsNpc(string name) => KnownNpcNames.Contains(name);
}
