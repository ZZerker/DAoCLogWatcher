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

	public void ApplyHeatmapOverlay(Plot plot, FrontierMapData map, IReadOnlyDictionary<string, int> zoneCounts, IReadOnlyDictionary<string, WarmapKeepState>? liveKeeps = null, IReadOnlyList<WarmapActivityEntry>? fights = null, IReadOnlyList<WarmapActivityEntry>? groups = null, bool showFights = true)
	{
		this.EnsureIconsLoaded();
		plot.Clear();

		if(this.terrain != null)
		{
			plot.Add.ImageRect(new Image(this.terrain), new CoordinateRect(0, 1408, -1536, 0));
		}

		var idx = this.GetOrBuildIndex(map);

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

		foreach(var k in map.Keeps.Where(k => k.Pixel != null&&k.Type == "keep"))
		{
			WarmapKeepState? live = null;
			liveKeeps?.TryGetValue(NormalizeName(k.Name), out live);
			var realm = live != null ? RealmFromInt(live.Realm) : k.DefaultRealm;
			if(live?.InCombat == true)
			{
				newBurningKeeps.Add((k.Name, k.Pixel!.X, k.Pixel.Y, true));
				this.DrawFlame(plot, k.Pixel!.X, -k.Pixel.Y, isKeep: true);
			}

			this.DrawKeepIcon(plot, k.Pixel!.X, -k.Pixel.Y, realm);
			var lbl = plot.Add.Text(ShortKeepName(k.Name), k.Pixel.X, -k.Pixel.Y + 12);
			lbl.LabelFontSize = 9;
			lbl.LabelFontColor = Color.FromHex("#FFFFFF");
			lbl.LabelBackgroundColor = Color.FromHex("#000000").WithAlpha(0.6);
			lbl.LabelAlignment = Alignment.LowerCenter;
		}

		foreach(var k in map.Keeps.Where(k => k.Pixel != null&&k.Type == "tower"))
		{
			WarmapKeepState? live = null;
			liveKeeps?.TryGetValue(NormalizeName(k.Name), out live);
			var realm = live != null ? RealmFromInt(live.Realm) : k.DefaultRealm;
			if(live?.InCombat == true)
			{
				newBurningKeeps.Add((k.Name, k.Pixel!.X, k.Pixel.Y, false));
				this.DrawFlame(plot, k.Pixel!.X, -k.Pixel.Y, isKeep: false);
			}

			this.DrawTowerIcon(plot, k.Pixel!.X, -k.Pixel.Y, realm);
		}

		this.burningKeeps = newBurningKeeps;

		foreach(var k in map.Keeps.Where(k => k.Pixel != null&&k.Type == "dock"))
		{
			var m = plot.Add.Marker(k.Pixel!.X, -k.Pixel.Y, MarkerShape.FilledTriangleUp, 8);
			m.Color = Color.FromHex("#FFFF00");
		}

		// kill count labels for all active locations (topmost layer)
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

		if(showFights)
		{
			var zoneIdx = this.GetOrBuildZoneIndex(map);
			if(groups != null)
			{
				this.DrawActivityMarkers(plot, groups, isFight: false, zoneIdx);
			}

			if(fights != null)
			{
				this.DrawActivityMarkers(plot, fights, isFight: true, zoneIdx);
			}
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
			var threshold = entry.IsKeep ? 20.0 : 12.0;
			if(dist <= threshold && dist < bestDist)
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

		this.zonePixelIndex = map.Zones
		                         .Where(z => z.PixelBounds != null)
		                         .ToDictionary(z => z.ZoneId, z => z.PixelBounds!);
		return this.zonePixelIndex;
	}

	private void DrawActivityMarkers(Plot plot, IReadOnlyList<WarmapActivityEntry> entries, bool isFight, Dictionary<int, PixelBounds> zoneIndex)
	{
		var bitmaps = isFight?this.fightBitmaps:this.groupBitmaps;
		double[] halfSizes = [0.0, 7.0, 11.0, 15.0];

		foreach(var entry in entries)
		{
			if(!zoneIndex.TryGetValue(entry.Zone, out var bounds))
			{
				continue;
			}

			var offsetX = ((entry.X << 13) + 4096) / 256.0;
			var offsetY = ((entry.Y << 13) + 4096) / 256.0;
			var px = bounds.X + offsetX;
			var py = -(bounds.Y + offsetY);

			var s = Math.Clamp(entry.Size, 1, 3);
			var c = Math.Clamp(entry.Realm, 1, 3);
			var bmp = bitmaps[c, s];
			var half = halfSizes[s];

			if(bmp != null)
			{
				plot.Add.ImageRect(new ScottPlot.Image(bmp), new CoordinateRect(px - half, px + half, py - half, py + half));
			}
			else
			{
				var m = plot.Add.Marker(px, py, MarkerShape.FilledCircle, (float)(half * 2));
				m.Color = RealmColor(RealmFromInt(c)).WithAlpha(0.8);
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
			plot.Add.ImageRect(new ScottPlot.Image(this.flameBitmap), new CoordinateRect(x - 22, x + 22, y - 20, y + 20));
		}
		else
		{
			plot.Add.ImageRect(new ScottPlot.Image(this.flameBitmap), new CoordinateRect(x - 10, x + 10, y - 16, y + 16));
		}
	}

	private static string? RealmFromInt(int realm) => realm switch
	{
			1 => "Albion",
			2 => "Midgard",
			3 => "Hibernia",
			_ => null
	};

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

		plot.Add.ImageRect(new ScottPlot.Image(bmp), new CoordinateRect(x - 15, x + 15, y - 12, y + 12));
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

		plot.Add.ImageRect(new ScottPlot.Image(bmp), new CoordinateRect(x - 5, x + 5, y - 11, y + 11));
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
