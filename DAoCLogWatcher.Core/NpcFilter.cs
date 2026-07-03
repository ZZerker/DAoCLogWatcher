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
			                                                        "Armswoman",
			                                                        "Cleric",
			                                                        "Druid",
			                                                        "Eldritch",
			                                                        "Hunter",
			                                                        "Huscarl",
			                                                        "Nightshade",
			                                                        "Ranger",
			                                                        "Realm Enhancer",
			                                                        "Scout",
			                                                        "Tower Captain",
			                                                        "Training Dummy"
	                                                        };

	// Keep lords and named commanders carry a realm title prefix followed by a unique
	// name (Albion "Lord …", Midgard "Jarl …", Hibernia "Chieftain …"). The trailing
	// space is required: player names are single tokens, so "Jarl Bleedmer" is always
	// an NPC, while a player named "Lordbob" (no space) must not match.
	private static readonly string[] NpcTitlePrefixes =
	{
		"Lord ",
		"Lady ",
		"Jarl ",
		"Chieftain "
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

		foreach(var title in NpcTitlePrefixes)
		{
			if(name.StartsWith(title, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
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
