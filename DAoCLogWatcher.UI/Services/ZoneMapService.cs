using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Platform;
using DAoCLogWatcher.UI.Models;
using ScottPlot;
using SkiaSharp;

namespace DAoCLogWatcher.UI.Services;

public class ZoneMapService
{
	private enum LocationKind
	{
		Zone,
		Keep,
		Tower,
		Dock
	}

	private sealed record LocationEntry(LocationKind Kind, double PixelX, double PixelY, double Width, double Height, string? Realm);

	private const int GRID_W = 224;
	private const int GRID_H = 244;
	private const double CELL_W = 1408.0 / GRID_W;
	private const double CELL_H = 1536.0 / GRID_H;

	private SKBitmap? terrain;
#pragma warning disable CS0169
	private SKBitmap? legendBitmap; // used in commented-out legacy rendering block
#pragma warning restore CS0169
	private Dictionary<string, LocationEntry>? locationIndex;
	private Dictionary<int, PixelBounds>? zonePixelIndex;
	private SKBitmap? keepR1, keepR2, keepR3;
	private SKBitmap? towerR1, towerR2, towerR3;

	private SKBitmap? flameBitmap;

	// [realm 1-3, size 1-3] — 1-based indexing, slot [0,*] and [*,0] unused
	private readonly SKBitmap?[,] fightBitmaps = new SKBitmap?[4, 4];
	private readonly SKBitmap?[,] groupBitmaps = new SKBitmap?[4, 4];
	private List<(string Name, double Px, double Py, bool IsKeep)> burningKeeps = [];
	private List<(WarmapRelicPlacement Relic, double Px, double Py)> relicHits = [];

	// Mirrors the App.axaml tokens AppEventCampaign / AppEventMission.
	private static readonly Color EventCampaignColor = Color.FromHex("#C77DFF");
	private static readonly Color EventMissionColor = Color.FromHex("#22D3EE");

	// The feed sends only the end time, never a start or a total, so the ring is scaled against
	// the observed standard mission length.
	private const double NominalMissionSeconds = 21 * 60;

	public sealed record MinimapViewSpec(PixelBounds ZoneBounds, int? ActiveZoneId, double? PlayerPixelX, double? PlayerPixelY, string ZoneName);

	public MinimapViewSpec? GetMinimapViewSpec(string location, FrontierMapData map)
	{
		var normalized = NormalizeName(location);

		var matchedZone = map.Zones.FirstOrDefault(z => z.PixelBounds != null&&string.Equals(NormalizeName(z.Name), normalized, StringComparison.OrdinalIgnoreCase));
		if(matchedZone != null)
		{
			return new MinimapViewSpec(matchedZone.PixelBounds!, matchedZone.ZoneId, null, null, matchedZone.Name);
		}

		var matchedKeep = map.Keeps.FirstOrDefault(k => k.Pixel != null&&string.Equals(NormalizeName(k.Name), normalized, StringComparison.OrdinalIgnoreCase));
		if(matchedKeep == null)
		{
			return null;
		}

		var kx = matchedKeep.Pixel!.X;
		var ky = matchedKeep.Pixel.Y;
		var containingZone = map.Zones.FirstOrDefault(z => z.PixelBounds != null&&kx >= z.PixelBounds.X&&kx <= z.PixelBounds.X + z.PixelBounds.Width&&ky >= z.PixelBounds.Y&&ky <= z.PixelBounds.Y + z.PixelBounds.Height);

		return containingZone == null?null:new MinimapViewSpec(containingZone.PixelBounds!, containingZone.ZoneId, kx, ky, containingZone.Name);
	}

	public void InitializeMinimapPlot(Plot plot)
	{
		plot.FigureBackground.Color = Color.FromHex("#252525");
		plot.DataBackground.Color = Color.FromHex("#1E1E1E");
		plot.Grid.IsVisible = false;
		plot.Axes.SetLimits(0, 1408, -1536, 0);
	}

	public void ApplyMinimapOverlay(Plot plot,
	                                FrontierMapData map,
	                                MinimapViewSpec spec,
	                                IReadOnlyDictionary<string, WarmapKeepState>? liveKeeps = null,
	                                IReadOnlyList<WarmapActivityEntry>? fights = null,
	                                IReadOnlyList<WarmapActivityEntry>? groups = null,
	                                IReadOnlyList<WarmapEvent>? events = null,
	                                bool showEvents = true,
	                                IReadOnlyList<WarmapRelicPlacement>? relics = null,
	                                bool showRelics = true)
	{
		this.EnsureIconsLoaded();
		plot.Clear();

		if(this.terrain != null)
		{
			plot.Add.ImageRect(new Image(this.terrain), new CoordinateRect(0, 1408, -1536, 0));
		}

		var b = spec.ZoneBounds;

		var zoneRect = plot.Add.Rectangle(b.X, b.X + b.Width, -(b.Y + b.Height), -b.Y);
		zoneRect.FillColor = Color.FromHex("#000000").WithAlpha(0);
		zoneRect.LineColor = Color.FromHex("#FFCC00");
		zoneRect.LineWidth = 2.5f;

		var zoneLbl = plot.Add.Text(spec.ZoneName, b.X + b.Width / 2.0, -(b.Y + b.Height / 2.0));
		zoneLbl.LabelFontSize = 11;
		zoneLbl.LabelFontColor = Color.FromHex("#FFFFFF");
		zoneLbl.LabelBackgroundColor = Color.FromHex("#000000").WithAlpha(0.55);
		zoneLbl.LabelAlignment = Alignment.MiddleCenter;

		this.DrawKeepsAndTowers(plot, map.Keeps, liveKeeps, null, b);

		if(showRelics)
		{
			this.DrawRelics(plot, relics, false, b);
		}

		if(spec.PlayerPixelX.HasValue&&spec.PlayerPixelY.HasValue)
		{
			var pm = plot.Add.Marker(spec.PlayerPixelX.Value, -spec.PlayerPixelY.Value, MarkerShape.FilledCircle, 14);
			pm.Color = Color.FromHex("#FFFF00").WithAlpha(0.9);
			var plbl = plot.Add.Text("YOU", spec.PlayerPixelX.Value, -spec.PlayerPixelY.Value + 16);
			plbl.LabelFontSize = 9;
			plbl.LabelFontColor = Color.FromHex("#FFFF00");
			plbl.LabelBackgroundColor = Color.FromHex("#000000").WithAlpha(0.6);
			plbl.LabelAlignment = Alignment.LowerCenter;
		}

		if(spec.ActiveZoneId.HasValue)
		{
			var zoneIdx = this.GetOrBuildZoneIndex(map);
			if(groups != null)
			{
				this.DrawActivityMarkers(plot, groups.Where(g => g.Zone == spec.ActiveZoneId.Value).ToList(), false, zoneIdx);
			}

			if(fights != null)
			{
				this.DrawActivityMarkers(plot, fights.Where(f => f.Zone == spec.ActiveZoneId.Value).ToList(), true, zoneIdx);
			}

			if(showEvents&&events != null)
			{
				this.DrawEventMarkers(plot, events.Where(e => e.Zone == spec.ActiveZoneId.Value).ToList(), zoneIdx);
			}
		}
	}

	public void InitializePlot(Plot plot, FrontierMapData map)
	{
		this.EnsureIconsLoaded();
		plot.FigureBackground.Color = Color.FromHex("#252525");
		plot.DataBackground.Color = Color.FromHex("#1E1E1E");
		plot.Grid.IsVisible = false;

		this.terrain ??= BuildTerrainBitmap(map);

		plot.Add.ImageRect(new Image(this.terrain), new CoordinateRect(0, 1408, -1536, 0));

		foreach(var z in map.Zones.Where(z => z.PixelBounds != null))
		{
			var b = z.PixelBounds!;
			var rect = plot.Add.Rectangle(b.X, b.X + b.Width, -(b.Y + b.Height), -b.Y);
			rect.FillColor = Color.FromHex("#000000").WithAlpha(0);
			rect.LineColor = RealmColor(z.Realm);
			rect.LineWidth = 2f;
			var lbl = plot.Add.Text(z.Name, b.X + b.Width / 2.0, -(b.Y + b.Height / 2.0));
			lbl.LabelFontSize = 10;
			lbl.LabelFontColor = Color.FromHex("#FFFFFF");
			lbl.LabelBackgroundColor = Color.FromHex("#000000").WithAlpha(0.55);
			lbl.LabelAlignment = Alignment.MiddleCenter;
		}

		foreach(var k in map.Keeps.Where(k => k.Pixel != null&&k.Type == "keep"))
		{
			this.DrawKeepIcon(plot, k.Pixel!.X, -k.Pixel.Y, k.DefaultRealm);
			var lbl = plot.Add.Text(ShortKeepName(k.Name), k.Pixel.X, -k.Pixel.Y + 12);
			lbl.LabelFontSize = 9;
			lbl.LabelFontColor = Color.FromHex("#FFFFFF");
			lbl.LabelBackgroundColor = Color.FromHex("#000000").WithAlpha(0.6);
			lbl.LabelAlignment = Alignment.LowerCenter;
		}

		foreach(var k in map.Keeps.Where(k => k.Pixel != null&&k.Type == "tower"))
		{
			this.DrawTowerIcon(plot, k.Pixel!.X, -k.Pixel.Y, k.DefaultRealm);
		}

		foreach(var k in map.Keeps.Where(k => k.Pixel != null&&k.Type == "dock"))
		{
			var m = plot.Add.Marker(k.Pixel!.X, -k.Pixel.Y, MarkerShape.FilledTriangleUp, 8);
			m.Color = Color.FromHex("#FFFF00");
		}

		plot.Axes.SetLimits(-20, 1440, -1580, 20);
	}

	public void ApplyHeatmapOverlay(Plot plot,
	                                FrontierMapData map,
	                                IReadOnlyDictionary<string, int> zoneCounts,
	                                IReadOnlyDictionary<string, WarmapKeepState>? liveKeeps = null,
	                                IReadOnlyList<WarmapActivityEntry>? fights = null,
	                                IReadOnlyList<WarmapActivityEntry>? groups = null,
	                                bool showFights = true,
	                                IReadOnlyList<WarmapEvent>? events = null,
	                                bool showEvents = true,
	                                bool showHeatmap = true,
	                                IReadOnlyList<WarmapRelicPlacement>? relics = null,
	                                bool showRelics = true)
	{
		this.EnsureIconsLoaded();
		plot.Clear();

		if(this.terrain != null)
		{
			plot.Add.ImageRect(new Image(this.terrain), new CoordinateRect(0, 1408, -1536, 0));
		}

		var idx = this.GetOrBuildIndex(map);

		if(showHeatmap)
		{
			// ── NEW: 2D ScottPlot heatmap overlay ─────────────────────────────────
			var grid = new double[GRID_H, GRID_W];

			foreach(var kv in zoneCounts)
			{
				if(kv.Value == 0)
				{
					continue;
				}

				if(!idx.TryGetValue(NormalizeName(kv.Key), out var entry))
				{
					continue;
				}

				var px = entry.Kind == LocationKind.Zone?entry.PixelX + entry.Width / 2.0:entry.PixelX;
				var py = entry.Kind == LocationKind.Zone?entry.PixelY + entry.Height / 2.0:entry.PixelY;

				var sigma = entry.Kind switch
				{
						LocationKind.Zone => 6.0,
						LocationKind.Keep => 4.0,
						LocationKind.Tower => 2.8,
						_ => 2.0
				};
				ApplyGaussian(grid, px, py, kv.Value, sigma);
			}

			var hasData = false;
			var heatData = new double[GRID_H, GRID_W];
			for(var r = 0; r < GRID_H; r++)
			{
				for(var c = 0; c < GRID_W; c++)
				{
					if(grid[r, c] > 0)
					{
						heatData[r, c] = grid[r, c];
						hasData = true;
					}
					else
					{
						heatData[r, c] = double.NaN;
					}
				}
			}

			if(hasData)
			{
				var hm = plot.Add.Heatmap(heatData);
				hm.Colormap = new AlphaScaledTurbo();
				hm.Rectangle = new CoordinateRect(0, 1408, -1536, 0);
			}
		}

		// ──────────────────────────────────────────────────────────────────────

		// ── OLD: discrete thermal rendering (kept for reference) ──────────────
		/*
		var maxCount = zoneCounts.Count > 0 ? zoneCounts.Values.Max() : 0;

		// zone fill overlays (thermal, active only — drawn before borders)
		foreach(var kv in zoneCounts)
		{
			if(kv.Value == 0) continue;
			if(!idx.TryGetValue(NormalizeName(kv.Key), out var entry) || entry.Kind != LocationKind.Zone) continue;
			var ratio = maxCount > 0 ? (double)kv.Value / maxCount : 0.0;
			var rect = plot.Add.Rectangle(entry.PixelX, entry.PixelX + entry.Width,
			                              -(entry.PixelY + entry.Height), -entry.PixelY);
			rect.FillColor = HeatmapFillColor(ratio);
			rect.LineWidth = 0;
		}

		// thermal circles for active keeps / towers / docks (below base markers)
		foreach(var kv in zoneCounts)
		{
			if(kv.Value == 0) continue;
			if(!idx.TryGetValue(NormalizeName(kv.Key), out var entry) || entry.Kind == LocationKind.Zone) continue;
			var ratio = maxCount > 0 ? (double)kv.Value / maxCount : 0.0;
			var size = (float)(entry.Kind switch
			{
				LocationKind.Keep  => 14 + ratio * 22,
				LocationKind.Tower => 8  + ratio * 14,
				_                  => 6  + ratio * 10,
			});
			var m = plot.Add.Marker(entry.PixelX, -entry.PixelY, MarkerShape.FilledCircle, size);
			m.Color = HeatmapFillColor(ratio).WithAlpha(0.85);
		}

		// kill count labels for all active locations (topmost layer)
		foreach(var kv in zoneCounts)
		{
			if(kv.Value == 0) continue;
			if(!idx.TryGetValue(NormalizeName(kv.Key), out var entry)) continue;

			if(entry.Kind == LocationKind.Zone)
			{
				var cx = entry.PixelX + entry.Width / 2.0;
				var cy = -(entry.PixelY + entry.Height / 2.0);

				var countLbl = plot.Add.Text(kv.Value.ToString(), cx, cy);
				countLbl.LabelFontSize = 30;
				countLbl.LabelFontColor = Color.FromHex("#FFFFFF");
				countLbl.LabelBackgroundColor = Color.FromHex("#000000").WithAlpha(0.45);
				countLbl.LabelAlignment = Alignment.MiddleCenter;

				var nameLbl = plot.Add.Text(kv.Key, cx, cy - 26);
				nameLbl.LabelFontSize = 9;
				nameLbl.LabelFontColor = Color.FromHex("#DDDDDD");
				nameLbl.LabelBackgroundColor = Color.FromHex("#000000").WithAlpha(0.5);
				nameLbl.LabelAlignment = Alignment.MiddleCenter;
			}
			else
			{
				var cx = entry.PixelX;
				var cy = -entry.PixelY;
				var countLbl = plot.Add.Text(kv.Value.ToString(), cx, cy);
				countLbl.LabelFontSize = 14;
				countLbl.LabelFontColor = Color.FromHex("#FFFFFF");
				countLbl.LabelBackgroundColor = Color.FromHex("#000000").WithAlpha(0.55);
				countLbl.LabelAlignment = Alignment.MiddleCenter;
			}
		}

		// gradient legend bar at bottom-right
		this.legendBitmap?.Dispose();
		this.legendBitmap = BuildLegendBitmap();
		plot.Add.ImageRect(new ScottPlot.Image(this.legendBitmap),
		                   new CoordinateRect(left: 1010, right: 1400, bottom: -1535, top: -1503));

		var legendTitle = plot.Add.Text("kill density", 1205, -1499);
		legendTitle.LabelFontSize = 9;
		legendTitle.LabelFontColor = Color.FromHex("#AAAAAA");
		legendTitle.LabelBackgroundColor = Color.FromHex("#000000").WithAlpha(0.0);
		legendTitle.LabelAlignment = Alignment.LowerCenter;
		*/
		// ──────────────────────────────────────────────────────────────────────

		// zone borders (all zones, realm-colored, transparent fill)
		foreach(var z in map.Zones.Where(z => z.PixelBounds != null))
		{
			var b = z.PixelBounds!;
			var rect = plot.Add.Rectangle(b.X, b.X + b.Width, -(b.Y + b.Height), -b.Y);
			rect.FillColor = Color.FromHex("#000000").WithAlpha(0);
			rect.LineColor = RealmColor(z.Realm);
			rect.LineWidth = 2f;
		}

		// base keeps + name labels (on top of heatmap)
		var newBurningKeeps = new List<(string Name, double Px, double Py, bool IsKeep)>();
		this.DrawKeepsAndTowers(plot, map.Keeps, liveKeeps, newBurningKeeps);
		this.burningKeeps = newBurningKeeps;

		this.relicHits = showRelics?this.DrawRelics(plot, relics, true, null):[];

		foreach(var k in map.Keeps.Where(k => k.Pixel != null&&k.Type == "dock"))
		{
			var m = plot.Add.Marker(k.Pixel!.X, -k.Pixel.Y, MarkerShape.FilledTriangleUp, 8);
			m.Color = Color.FromHex("#FFFF00");
		}

		// kill count labels for all active locations (topmost layer)
		if(showHeatmap)
		{
			foreach(var kv in zoneCounts)
			{
				if(kv.Value == 0)
				{
					continue;
				}

				if(!idx.TryGetValue(NormalizeName(kv.Key), out var entry))
				{
					continue;
				}

				if(entry.Kind == LocationKind.Zone)
				{
					var cx = entry.PixelX + entry.Width / 2.0;
					var cy = -(entry.PixelY + entry.Height / 2.0);

					var countLbl = plot.Add.Text(kv.Value.ToString(), cx, cy);
					countLbl.LabelFontSize = 30;
					countLbl.LabelFontColor = Color.FromHex("#FFFFFF");
					countLbl.LabelBackgroundColor = Color.FromHex("#000000").WithAlpha(0.45);
					countLbl.LabelAlignment = Alignment.MiddleCenter;

					var nameLbl = plot.Add.Text(kv.Key, cx, cy - 26);
					nameLbl.LabelFontSize = 9;
					nameLbl.LabelFontColor = Color.FromHex("#DDDDDD");
					nameLbl.LabelBackgroundColor = Color.FromHex("#000000").WithAlpha(0.5);
					nameLbl.LabelAlignment = Alignment.MiddleCenter;
				}
				else
				{
					var cx = entry.PixelX;
					var cy = -entry.PixelY;
					var countLbl = plot.Add.Text(kv.Value.ToString(), cx, cy);
					countLbl.LabelFontSize = 14;
					countLbl.LabelFontColor = Color.FromHex("#FFFFFF");
					countLbl.LabelBackgroundColor = Color.FromHex("#000000").WithAlpha(0.55);
					countLbl.LabelAlignment = Alignment.MiddleCenter;
				}
			}
		}

		// Fights and groups gate independently — callers pass null for a hidden layer. Do NOT
		// re-wrap these in showFights: that made the Groups toggle dead whenever Fights was off.
		if(showFights&&fights != null||groups != null)
		{
			var zoneIdx = this.GetOrBuildZoneIndex(map);
			if(groups != null)
			{
				this.DrawActivityMarkers(plot, groups, false, zoneIdx);
			}

			if(showFights&&fights != null)
			{
				this.DrawActivityMarkers(plot, fights, true, zoneIdx);
			}
		}

		if(showEvents&&events != null)
		{
			this.DrawEventMarkers(plot, events, this.GetOrBuildZoneIndex(map));
		}

		plot.Axes.SetLimits(-20, 1440, -1580, 20);
	}

	public (string Name, TimeSpan Duration)? GetBurnTooltip(double mapX, double mapY, IReadOnlyDictionary<string, DateTime> combatStarts)
	{
		(string Name, double Px, double Py, bool IsKeep)? best = null;
		var bestDist = double.MaxValue;

		foreach(var entry in this.burningKeeps)
		{
			var dx = mapX - entry.Px;
			var dy = mapY - entry.Py;
			var dist = Math.Sqrt(dx * dx + dy * dy);
			var threshold = entry.IsKeep?20.0:12.0;
			if(dist <= threshold&&dist < bestDist)
			{
				best = entry;
				bestDist = dist;
			}
		}

		if(best == null)
		{
			return null;
		}

		var normalized = NormalizeName(best.Value.Name);
		if(!combatStarts.TryGetValue(normalized, out var startTime))
		{
			return null;
		}

		return (best.Value.Name, DateTime.UtcNow - startTime);
	}

	private Dictionary<int, PixelBounds> GetOrBuildZoneIndex(FrontierMapData map)
	{
		if(this.zonePixelIndex != null)
		{
			return this.zonePixelIndex;
		}

		this.zonePixelIndex = map.Zones.Where(z => z.PixelBounds != null).ToDictionary(z => z.ZoneId, z => z.PixelBounds!);
		return this.zonePixelIndex;
	}

	// Relic pads come from Eden in region-163 game units, not zone-local coordinates. See
	// "coordinateSystem" in Assets/frontier_zones.json: pixel = (game - regionOrigin) / 256.
	private const int RELIC_ORIGIN_X = 360448;
	private const int RELIC_ORIGIN_Y = 294912;
	private const double GAME_UNITS_PER_PIXEL = 256.0;

	private static (double X, double Y) GameToPixel(int gameX, int gameY)
	{
		return ((gameX - RELIC_ORIGIN_X) / GAME_UNITS_PER_PIXEL, (gameY - RELIC_ORIGIN_Y) / GAME_UNITS_PER_PIXEL);
	}

	/// <summary>
	/// Draws each relic on the pad it currently sits on. The diamond is filled in the realm that
	/// holds the relic and outlined in its home realm, so a captured relic reads as a mismatch at a
	/// glance. Returns the hit-test entries for <see cref="GetRelicTooltip"/>.
	/// </summary>
	private List<(WarmapRelicPlacement Relic, double Px, double Py)> DrawRelics(Plot plot, IReadOnlyList<WarmapRelicPlacement>? relics, bool withLabels, PixelBounds? clipBounds)
	{
		var hits = new List<(WarmapRelicPlacement Relic, double Px, double Py)>();

		if(relics == null)
		{
			return hits;
		}

		foreach(var relic in relics)
		{
			// A relic in transit has no pad, so it has no coordinates either: drawing it would put a
			// marker on the map origin.
			if(relic.IsMoving)
			{
				continue;
			}

			var (px, py) = GameToPixel(relic.GameX, relic.GameY);

			if(clipBounds != null&&(px < clipBounds.X||px > clipBounds.X + clipBounds.Width||py < clipBounds.Y||py > clipBounds.Y + clipBounds.Height))
			{
				continue;
			}

			hits.Add((relic, px, py));

			var ownerColor = RealmColor(RealmFromInt(relic.OwnerRealm));
			var homeColor = RealmColor(RealmFromInt(relic.OriginRealm));

			var halo = plot.Add.Marker(px, -py, MarkerShape.OpenDiamond, 24);
			halo.Color = homeColor;
			halo.MarkerLineWidth = 2.5f;

			var body = plot.Add.Marker(px, -py, MarkerShape.FilledDiamond, 15);
			body.Color = ownerColor;

			// Strength gets a dot in the middle, Power a ring -- readable without a legend and
			// without relying on colour, which is already carrying realm ownership.
			var pip = plot.Add.Marker(px, -py, relic.Type == 0?MarkerShape.FilledCircle:MarkerShape.OpenCircle, 6);
			pip.Color = Color.FromHex("#FFFFFF");
			pip.MarkerLineWidth = 1.5f;

			if(!withLabels)
			{
				continue;
			}

			var lbl = plot.Add.Text(relic.DisplayName, px, -py - 16);
			lbl.LabelFontSize = 9;
			lbl.LabelFontColor = relic.IsAtHome?Color.FromHex("#FFFFFF"):Color.FromHex("#FFCC00");
			lbl.LabelBackgroundColor = Color.FromHex("#000000").WithAlpha(0.65);
			lbl.LabelAlignment = Alignment.UpperCenter;
		}

		return hits;
	}

	/// <summary>
	/// Tooltip text for the relic under the given map coordinate, or null. <paramref name="mapY"/>
	/// is expected already negated by the caller, matching <see cref="GetBurnTooltip"/>.
	/// </summary>
	public string? GetRelicTooltip(double mapX, double mapY)
	{
		const double HIT_RADIUS = 14.0;
		(WarmapRelicPlacement Relic, double Px, double Py)? best = null;
		var bestDistance = double.MaxValue;

		foreach(var entry in this.relicHits)
		{
			var dx = mapX - entry.Px;
			var dy = mapY - entry.Py;
			var distance = Math.Sqrt(dx * dx + dy * dy);

			if(distance <= HIT_RADIUS&&distance < bestDistance)
			{
				bestDistance = distance;
				best = entry;
			}
		}

		if(best == null)
		{
			return null;
		}

		var r = best.Value.Relic;
		var owner = RealmFromInt(r.OwnerRealm) ?? "Unknown";
		var where = r.IsHomePad?$"{r.PadName} (relic keep)":r.PadName;

		return $"{r.DisplayName}\n{r.TypeName} relic\nNow at: {where}\nHeld by {owner}";
	}

	private void DrawKeepsAndTowers(Plot plot, IEnumerable<FrontierKeep> keeps, IReadOnlyDictionary<string, WarmapKeepState>? liveKeeps, List<(string Name, double Px, double Py, bool IsKeep)>? burning, PixelBounds? clipBounds = null)
	{
		foreach(var k in keeps.Where(k => k.Pixel != null&&k.Type is "keep" or "tower"))
		{
			var kx = k.Pixel!.X;
			var ky = k.Pixel.Y;

			if(clipBounds != null&&(kx < clipBounds.X||kx > clipBounds.X + clipBounds.Width||ky < clipBounds.Y||ky > clipBounds.Y + clipBounds.Height))
			{
				continue;
			}

			WarmapKeepState? live = null;
			liveKeeps?.TryGetValue(NormalizeName(k.Name), out live);
			var realm = live != null?RealmFromInt(live.Realm):k.DefaultRealm;
			var isKeep = k.Type == "keep";

			if(live?.InCombat == true)
			{
				burning?.Add((k.Name, kx, ky, isKeep));
				this.DrawFlame(plot, kx, -ky, isKeep);
			}

			if(isKeep)
			{
				this.DrawKeepIcon(plot, kx, -ky, realm);
				var lbl = plot.Add.Text(ShortKeepName(k.Name), kx, -ky + 12);
				lbl.LabelFontSize = 9;
				lbl.LabelFontColor = Color.FromHex("#FFFFFF");
				lbl.LabelBackgroundColor = Color.FromHex("#000000").WithAlpha(0.6);
				lbl.LabelAlignment = Alignment.LowerCenter;
			}
			else
			{
				this.DrawTowerIcon(plot, kx, -ky, realm);
			}
		}
	}

	private static bool TryZonePixel(Dictionary<int, PixelBounds> zoneIndex, int zone, int x, int y, out double px, out double py)
	{
		if(!zoneIndex.TryGetValue(zone, out var bounds))
		{
			px = 0;
			py = 0;
			return false;
		}

		var offsetX = ((x << 13) + 4096) / 256.0;
		var offsetY = ((y << 13) + 4096) / 256.0;
		px = bounds.X + offsetX;
		py = -(bounds.Y + offsetY);
		return true;
	}

	private void DrawActivityMarkers(Plot plot, IReadOnlyList<WarmapActivityEntry> entries, bool isFight, Dictionary<int, PixelBounds> zoneIndex)
	{
		var bitmaps = isFight?this.fightBitmaps:this.groupBitmaps;
		double[] halfSizes = [0.0, 7.0, 11.0, 15.0];

		foreach(var entry in entries)
		{
			if(!TryZonePixel(zoneIndex, entry.Zone, entry.X, entry.Y, out var px, out var py))
			{
				continue;
			}

			var s = Math.Clamp(entry.Size, 1, 3);
			var c = Math.Clamp(entry.Realm, 1, 3);
			var bmp = bitmaps[c, s];
			var half = halfSizes[s];

			if(bmp != null)
			{
				plot.Add.ImageRect(new Image(bmp), new CoordinateRect(px - half, px + half, py - half, py + half));
			}
			else
			{
				var m = plot.Add.Marker(px, py, MarkerShape.FilledCircle, (float)(half * 2));
				m.Color = RealmColor(RealmFromInt(c)).WithAlpha(0.8);
			}
		}
	}

	/// <summary>
	/// Finds the event whose marker is under the given map coordinate, or null. <paramref name="mapY"/>
	/// is expected already negated by the caller, matching <see cref="GetBurnTooltip"/>.
	/// </summary>
	public WarmapEvent? HitTestEvent(double mapX, double mapY, IReadOnlyList<WarmapEvent>? events, FrontierMapData map)
	{
		if(events == null||events.Count == 0)
		{
			return null;
		}

		var zoneIndex = this.GetOrBuildZoneIndex(map);
		const double HIT_RADIUS = 9.0;
		WarmapEvent? best = null;
		var bestDistance = double.MaxValue;

		foreach(var ev in events)
		{
			if(ev.IsPending)
			{
				continue;
			}

			if(!zoneIndex.TryGetValue(ev.Zone, out var bounds))
			{
				continue;
			}

			var px = bounds.X + ev.X / 256.0;
			var py = bounds.Y + ev.Y / 256.0;
			var dx = px - mapX;
			var dy = py - mapY;
			var distance = Math.Sqrt(dx * dx + dy * dy);

			if(distance <= HIT_RADIUS&&distance < bestDistance)
			{
				bestDistance = distance;
				best = ev;
			}
		}

		return best;
	}

	private void DrawEventMarkers(Plot plot, IReadOnlyList<WarmapEvent> events, Dictionary<int, PixelBounds> zoneIndex)
	{
		// Marker sizes are pixels but ScatterLine takes data coordinates, so the ring radius has to be
		// converted; the axis span differs by an order of magnitude between the full map and the minimap.
		var unitsPerPixel = plot.LastRender.UnitsPerPxX > 0?plot.LastRender.UnitsPerPxX:1.0;

		foreach(var ev in events)
		{
			if(ev.IsPending)
			{
				continue;
			}

			if(!zoneIndex.TryGetValue(ev.Zone, out var bounds))
			{
				continue;
			}

			// NOT the fights transform. Fight x/y are block indices (0-7, hence "<< 13"), but event
			// X/Y are zone-local GAME UNITS (0-65535). A zone is 65536 units wide and 256 px, so the
			// conversion is a plain /256. Using the fight transform puts markers ~1M px off-map.
			var px = bounds.X + ev.X / 256.0;
			var py = -(bounds.Y + ev.Y / 256.0);

			var shape = ev.IsCampaignSpawn?MarkerShape.FilledTriangleUp:MarkerShape.FilledCircle;
			var color = ev.IsCampaignSpawn?EventCampaignColor:EventMissionColor;
			var size = ev.Size switch
			           {
					           "Small" => 10f,
					           "Exploratory" => 13f,
					           "Skirmish" => 17f,
					           "Large" => 22f,
					           _ => 13f
			           };
			var m = plot.Add.Marker(px, py, shape, size);
			m.Color = color.WithAlpha(0.9);

			// A bare shape does not say WHICH event it is, and 15 markers across 13 zones is sparse
			// enough to label every one. Hover still gives size/state/countdown.
			var lbl = plot.Add.Text(ev.DisplayName, px, py - size * 0.55);
			lbl.LabelFontSize = 8;
			lbl.LabelFontColor = color.WithAlpha(0.9);
			lbl.LabelBackgroundColor = Color.FromHex("#000000").WithAlpha(0.55);
			lbl.LabelAlignment = Alignment.UpperCenter;

			if(ev.TimeRemaining is { } remaining)
			{
				var fraction = Math.Clamp(remaining.TotalSeconds / NominalMissionSeconds, 0, 1);
				if(fraction > 0)
				{
					const int segments = 32;
					var radius = size * 0.75 * unitsPerPixel;
					var sweep = fraction * 2 * Math.PI;
					var xs = new double[segments + 1];
					var ys = new double[segments + 1];
					for(var i = 0; i <= segments; i++)
					{
						var t = sweep * i / segments;
						xs[i] = px + radius * Math.Sin(t);
						ys[i] = py + radius * Math.Cos(t);
					}

					var ring = plot.Add.ScatterLine(xs, ys);
					ring.LineWidth = 1.5f;
					ring.Color = color.WithAlpha(0.75);
					ring.MarkerStyle = MarkerStyle.None;
				}
			}
		}
	}

	private Dictionary<string, LocationEntry> GetOrBuildIndex(FrontierMapData map)
	{
		if(this.locationIndex != null)
		{
			return this.locationIndex;
		}

		var idx = new Dictionary<string, LocationEntry>(StringComparer.OrdinalIgnoreCase);

		foreach(var z in map.Zones.Where(z => z.PixelBounds != null))
		{
			var b = z.PixelBounds!;
			idx[NormalizeName(z.Name)] = new LocationEntry(LocationKind.Zone, b.X, b.Y, b.Width, b.Height, z.Realm);
		}

		foreach(var k in map.Keeps.Where(k => k.Pixel != null))
		{
			var kind = k.Type switch
			{
					"keep" => LocationKind.Keep,
					"dock" => LocationKind.Dock,
					_ => LocationKind.Tower
			};
			idx[NormalizeName(k.Name)] = new LocationEntry(kind, k.Pixel!.X, k.Pixel.Y, 0, 0, k.DefaultRealm);
		}

		this.locationIndex = idx;
		return idx;
	}

	private void EnsureIconsLoaded()
	{
		if(this.keepR1 != null)
		{
			return;
		}

		this.keepR1 = LoadBitmap("avares://DAoCLogWatcher.UI/Assets/map/keep_r1.png");
		this.keepR2 = LoadBitmap("avares://DAoCLogWatcher.UI/Assets/map/keep_r2.png");
		this.keepR3 = LoadBitmap("avares://DAoCLogWatcher.UI/Assets/map/keep_r3.png");
		this.towerR1 = LoadBitmap("avares://DAoCLogWatcher.UI/Assets/map/tower_r1.png");
		this.towerR2 = LoadBitmap("avares://DAoCLogWatcher.UI/Assets/map/tower_r2.png");
		this.towerR3 = LoadBitmap("avares://DAoCLogWatcher.UI/Assets/map/tower_r3.png");
		this.flameBitmap = LoadBitmap("avares://DAoCLogWatcher.UI/Assets/map/keep_f.png");

		for(var realm = 1; realm <= 3; realm++)
		{
			for(var size = 1; size <= 3; size++)
			{
				this.fightBitmaps[realm, size] = LoadBitmap($"avares://DAoCLogWatcher.UI/Assets/map/fight_{realm}_{size}.png");
				this.groupBitmaps[realm, size] = LoadBitmap($"avares://DAoCLogWatcher.UI/Assets/map/group_{realm}_{size}.png");
			}
		}
	}

	private static SKBitmap? LoadBitmap(string uri)
	{
		try
		{
			using var stream = AssetLoader.Open(new Uri(uri));
			return SKBitmap.Decode(stream);
		}
		catch
		{
			return null;
		}
	}

	private void DrawFlame(Plot plot, double x, double y, bool isKeep)
	{
		if(this.flameBitmap == null)
		{
			return;
		}

		if(isKeep)
		{
			plot.Add.ImageRect(new Image(this.flameBitmap), new CoordinateRect(x - 22, x + 22, y - 20, y + 20));
		}
		else
		{
			plot.Add.ImageRect(new Image(this.flameBitmap), new CoordinateRect(x - 10, x + 10, y - 16, y + 16));
		}
	}

	private static string? RealmFromInt(int realm)
	{
		return realm switch
		{
				1 => "Albion",
				2 => "Midgard",
				3 => "Hibernia",
				_ => null
		};
	}

	private void DrawKeepIcon(Plot plot, double x, double y, string? realm)
	{
		var bmp = realm switch
		{
				"Albion" => this.keepR1,
				"Midgard" => this.keepR2,
				"Hibernia" => this.keepR3,
				_ => this.keepR2
		};

		if(bmp == null)
		{
			var m = plot.Add.Marker(x, y, MarkerShape.FilledCircle, 10);
			m.Color = RealmColor(realm);
			return;
		}

		plot.Add.ImageRect(new Image(bmp), new CoordinateRect(x - 15, x + 15, y - 12, y + 12));
	}

	private void DrawTowerIcon(Plot plot, double x, double y, string? realm)
	{
		var bmp = realm switch
		{
				"Albion" => this.towerR1,
				"Midgard" => this.towerR2,
				"Hibernia" => this.towerR3,
				_ => this.towerR2
		};

		if(bmp == null)
		{
			var m = plot.Add.Marker(x, y, MarkerShape.FilledCircle, 5);
			m.Color = RealmColor(realm).WithAlpha(0.85);
			return;
		}

		plot.Add.ImageRect(new Image(bmp), new CoordinateRect(x - 5, x + 5, y - 11, y + 11));
	}

	private static void ApplyGaussian(double[,] grid, double px, double py, double weight, double sigma = 1.5)
	{
		var cx = Math.Clamp((int)(px / CELL_W), 0, GRID_W - 1);
		var cy = Math.Clamp((int)(py / CELL_H), 0, GRID_H - 1);
		var radius = (int)Math.Ceiling(sigma * 3);
		var sigma2 = 2.0 * sigma * sigma;

		for(var dr = -radius; dr <= radius; dr++)
		{
			for(var dc = -radius; dc <= radius; dc++)
			{
				int r = cy + dr, c = cx + dc;
				if(r < 0||r >= GRID_H||c < 0||c >= GRID_W)
				{
					continue;
				}

				grid[r, c] += weight * Math.Exp(-(dr * dr + dc * dc) / sigma2);
			}
		}
	}

	private static string NormalizeName(string name)
	{
		return name.Replace('\u2019', '\'').Replace('\u2018', '\'').Trim();
	}

	private static Color HeatmapFillColor(double ratio)
	{
		ratio = Math.Clamp(ratio, 0.0, 1.0);
		double r, g, b, alpha;

		if(ratio < 0.33)
		{
			var t = ratio / 0.33;
			r = Lerp(0x22, 0x22, t);
			g = Lerp(0x55, 0xCC, t);
			b = Lerp(0xCC, 0xCC, t);
			alpha = Lerp(0.20, 0.38, t);
		}
		else if(ratio < 0.67)
		{
			var t = (ratio - 0.33) / 0.34;
			r = Lerp(0x22, 0xFF, t);
			g = Lerp(0xCC, 0xAA, t);
			b = Lerp(0xCC, 0x00, t);
			alpha = Lerp(0.38, 0.55, t);
		}
		else
		{
			var t = (ratio - 0.67) / 0.33;
			r = Lerp(0xFF, 0xFF, t);
			g = Lerp(0xAA, 0x22, t);
			b = Lerp(0x00, 0x00, t);
			alpha = Lerp(0.55, 0.70, t);
		}

		return new Color((byte)r, (byte)g, (byte)b, (byte)(alpha * 255));
	}

	private static SKBitmap BuildTerrainBitmap(FrontierMapData map)
	{
		var bitmap = new SKBitmap(1408, 1536, SKColorType.Rgb888x, SKAlphaType.Opaque);
		using var canvas = new SKCanvas(bitmap);
		canvas.Clear(new SKColor(30, 30, 30));

		foreach(var zone in map.Zones.Where(z => z.PixelBounds != null))
		{
			var uri = new Uri($"avares://DAoCLogWatcher.UI/Assets/map/z{zone.ZoneId}_512.jpg");
			try
			{
				using var stream = AssetLoader.Open(uri);
				using var zoneImg = SKBitmap.Decode(stream);
				var dst = new SKRect(zone.PixelBounds!.X, zone.PixelBounds.Y, zone.PixelBounds.X + zone.PixelBounds.Width, zone.PixelBounds.Y + zone.PixelBounds.Height);
				canvas.DrawBitmap(zoneImg, dst);
			}
			catch
			{
				// zone image unavailable — dark background shows through
			}
		}

		return bitmap;
	}

	private static SKBitmap BuildLegendBitmap()
	{
		const int W = 390, H = 32;
		var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
		using var canvas = new SKCanvas(bmp);
		canvas.Clear(new SKColor(10, 10, 10, 160));

		var gradRect = new SKRect(1, 1, W - 1, H - 1);
		var colors = new[]
		             {
				             new SKColor(0x22, 0x55, 0xCC, 50),
				             new SKColor(0x22, 0xCC, 0xCC, 97),
				             new SKColor(0xFF, 0xAA, 0x00, 140),
				             new SKColor(0xFF, 0x22, 0x00, 178)
		             };
		var positions = new float[]
		                {
				                0f,
				                0.33f,
				                0.67f,
				                1f
		                };

		using var shader = SKShader.CreateLinearGradient(new SKPoint(gradRect.Left, gradRect.Top), new SKPoint(gradRect.Right, gradRect.Top), colors, positions, SKShaderTileMode.Clamp);

		using var fillPaint = new SKPaint
		                      {
				                      Shader = shader
		                      };
		canvas.DrawRect(gradRect, fillPaint);

		using var borderPaint = new SKPaint
		                        {
				                        Color = new SKColor(80, 80, 80, 160),
				                        Style = SKPaintStyle.Stroke,
				                        StrokeWidth = 1
		                        };
		canvas.DrawRect(new SKRect(0, 0, W - 1, H - 1), borderPaint);

		return bmp;
	}

	private static Color RealmColor(string? realm)
	{
		return realm switch
		{
				"Albion" => Color.FromHex("#cc4444"),
				"Midgard" => Color.FromHex("#4488ff"),
				"Hibernia" => Color.FromHex("#44aa55"),
				_ => Color.FromHex("#888888")
		};
	}

	private static string ShortKeepName(string name)
	{
		return name.Replace("Caer ", "").Replace("Dun ", "").Replace("Faste ", "");
	}

	private static double Lerp(double a, double b, double t)
	{
		return a + (b - a) * t;
	}

	private sealed class AlphaScaledTurbo: IColormap
	{
		private static readonly ScottPlot.Colormaps.Turbo Inner = new();

		public string Name => "TurboAlpha";

		public Color GetColor(double position)
		{
			position = Math.Clamp(position, 0, 1);
			return Inner.GetColor(position).WithAlpha(Math.Pow(position, 0.4) * 0.85);
		}
	}
}
