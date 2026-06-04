namespace DAoCLogWatcher.Core;

/// <summary>
/// Classifies a combat participant name as a non-player NPC (guards, keep doors,
/// training dummies, …) so they can be excluded from player combat stats.
/// </summary>
public static class NpcFilter
{
	private static readonly HashSet<string> KnownNpcNames = new(StringComparer.OrdinalIgnoreCase)
	                                                        {
			                                                        "Guard",
			                                                        "Guardian",
			                                                        "Supply Commander",
			                                                        "Armsman",
			                                                        "Druid",
			                                                        "Eldritch",
			                                                        "Hunter",
			                                                        "Huscarl",
			                                                        "Nightshade",
			                                                        "Ranger",
			                                                        "Training Dummy"
	                                                        };

	public static bool IsNpc(string? name)
	{
		if(string.IsNullOrEmpty(name))
		{
			return false;
		}

		if(KnownNpcNames.Contains(name))
		{
			return true;
		}

		// Keep/fortress structures appear under varied names (e.g. "Keep Door",
		// "Postern Door", "<Keep> Gate") — match by suffix rather than enumerate them.
		if(name.EndsWith("Door", StringComparison.OrdinalIgnoreCase)
		|| name.EndsWith("Gate", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		// Behemoth-family mobs/pets show up with a prefix/suffix (e.g. "Greater Behemoth").
		return name.Contains("Behemoth", StringComparison.OrdinalIgnoreCase);
	}
}
