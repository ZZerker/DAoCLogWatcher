using System.Text.Json;
using DAoCLogWatcher.Core.Models;

namespace DAoCLogWatcher.Core;

public sealed class SpellRegistry
{
	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

	private readonly Dictionary<string, SpellInfo> spells;

	private SpellRegistry(Dictionary<string, SpellInfo> spells) => this.spells = spells;

	public bool TryGet(string name, out SpellInfo info) => this.spells.TryGetValue(name, out info!);

	public bool IsKnownAoeNuke(string name) => this.spells.TryGetValue(name, out var info) && info.IsAoeNuke;

	public static SpellRegistry Empty { get; } = new(new Dictionary<string, SpellInfo>());

	public static SpellRegistry LoadFromEmbedded()
	{
		using var stream = typeof(SpellRegistry).Assembly
			.GetManifestResourceStream("DAoCLogWatcher.Core.Resources.spells.json");
		if(stream == null)
		{
			return Empty;
		}

		var loaded = JsonSerializer.Deserialize<SpellInfo[]>(stream, JsonOptions) ?? [];
		var dict = new Dictionary<string, SpellInfo>(loaded.Length, StringComparer.OrdinalIgnoreCase);
		foreach(var spell in loaded)
		{
			dict.TryAdd(spell.Name, spell);
		}
		return new SpellRegistry(dict);
	}
}
