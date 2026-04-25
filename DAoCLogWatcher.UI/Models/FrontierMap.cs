using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DAoCLogWatcher.UI.Models;

public record FrontierMapData([property: JsonPropertyName("zones")] List<FrontierZone> Zones, [property: JsonPropertyName("keeps")] List<FrontierKeep> Keeps);

public record FrontierZone(
		[property: JsonPropertyName("zoneId")] int ZoneId,
		[property: JsonPropertyName("name")] string Name,
		[property: JsonPropertyName("realm")] string? Realm,
		[property: JsonPropertyName("pixelBounds")] PixelBounds? PixelBounds);

public record FrontierKeep(
		[property: JsonPropertyName("name")] string Name,
		[property: JsonPropertyName("type")] string Type,
		[property: JsonPropertyName("defaultRealm")] string? DefaultRealm,
		[property: JsonPropertyName("pixel")] PixelCoord? Pixel);

public record PixelBounds([property: JsonPropertyName("x")] int X, [property: JsonPropertyName("y")] int Y, [property: JsonPropertyName("width")] int Width, [property: JsonPropertyName("height")] int Height);

public record PixelCoord([property: JsonPropertyName("x")] int X, [property: JsonPropertyName("y")] int Y);
