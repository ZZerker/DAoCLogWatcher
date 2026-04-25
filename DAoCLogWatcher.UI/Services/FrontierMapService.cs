using System;
using System.Text.Json;
using Avalonia.Platform;
using DAoCLogWatcher.UI.Models;

namespace DAoCLogWatcher.UI.Services;

public class FrontierMapService
{
	private static readonly JsonSerializerOptions Options = new()
	                                                        {
			                                                        PropertyNameCaseInsensitive = true
	                                                        };

	public FrontierMapData Load()
	{
		using var stream = AssetLoader.Open(new Uri("avares://DAoCLogWatcher.UI/Assets/frontier_zones.json"));
		return JsonSerializer.Deserialize<FrontierMapData>(stream, Options)!;
	}
}
