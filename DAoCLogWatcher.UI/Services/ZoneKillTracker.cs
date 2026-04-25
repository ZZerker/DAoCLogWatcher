using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Models;

namespace DAoCLogWatcher.UI.Services;

public sealed class ZoneKillTracker
{
	private const string ZONE_EXPORT_FILE_NAME = "kill-zones.json";

	private static readonly JsonSerializerOptions JsonOptions = new()
	                                                            {
			                                                            WriteIndented = true
	                                                            };

	public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(10);
	public static readonly TimeSpan MaximumRetention = TimeSpan.FromMinutes(60);

	private readonly LinkedList<ZoneKillEntry> recentKills = new();
	private readonly Dictionary<string, int> currentCounts = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<KillActivityPoint> activityPoints = new();
	private readonly HashSet<string> uniqueZones = new(StringComparer.OrdinalIgnoreCase);
	private readonly string zoneExportPath;
	private readonly object uniqueZonesLock = new();
	private bool zoneFileDirty;
	private TimeSpan activeWindow = DefaultWindow;

	public ZoneKillTracker()
			: this(Path.Combine(AppContext.BaseDirectory, ZONE_EXPORT_FILE_NAME))
	{
	}

	internal ZoneKillTracker(string zoneExportPath)
	{
		this.zoneExportPath = zoneExportPath;
		this.LoadUniqueZonesFromDisk();
	}

	public event EventHandler? Updated;

	public TimeSpan ActiveWindow => this.activeWindow;

	public IReadOnlyDictionary<string, int> CurrentCounts => this.currentCounts;

	public IReadOnlyList<KillActivityPoint> KillActivityPoints => this.activityPoints;

	public void Track(KillEvent killEvent, DateTime? sessionStartTime)
	{
		var killDateTime = ResolveKillDateTime(killEvent.Timestamp, sessionStartTime);
		this.recentKills.AddLast(new ZoneKillEntry(killDateTime, killEvent.Zone));
		this.TrackUniqueZone(killEvent.Zone);
		this.ExpireTooOld(killDateTime);
		this.UpdateCounts(killDateTime);
		this.UpdateActivityPoints(killDateTime);
		this.Updated?.Invoke(this, EventArgs.Empty);
	}

	public void SetWindow(TimeSpan window)
	{
		if(window <= TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(window), nameof(window) + " must be positive.");
		}

		if(window > MaximumRetention)
		{
			window = MaximumRetention;
		}

		this.activeWindow = window;
		var now = this.GetEffectiveNow(DateTime.Now);
		this.UpdateCounts(now);
		this.UpdateActivityPoints(now);
		this.Updated?.Invoke(this, EventArgs.Empty);
	}

	public void Refresh(DateTime now)
	{
		var effective = this.GetEffectiveNow(now);
		if(this.ExpireTooOld(effective)||this.RecalculateCounts(effective))
		{
			this.UpdateActivityPoints(effective);
			this.Updated?.Invoke(this, EventArgs.Empty);
		}
	}

	private DateTime GetEffectiveNow(DateTime realNow)
	{
		var lastKill = this.recentKills.Last?.Value.Timestamp;
		if(lastKill.HasValue&&lastKill.Value < realNow - MaximumRetention)
		{
			return lastKill.Value;
		}

		return realNow;
	}

	public void Reset()
	{
		this.recentKills.Clear();
		this.currentCounts.Clear();
		this.activityPoints.Clear();
		this.Updated?.Invoke(this, EventArgs.Empty);
	}

	private void UpdateCounts(DateTime now)
	{
		this.currentCounts.Clear();
		var threshold = now - this.activeWindow;
		var node = this.recentKills.Last;

		while(node != null&&node.Value.Timestamp >= threshold)
		{
			var zone = node.Value.Zone;
			this.currentCounts.TryGetValue(zone, out var current);
			this.currentCounts[zone] = current + 1;
			node = node.Previous;
		}
	}

	private void UpdateActivityPoints(DateTime now)
	{
		this.activityPoints.Clear();

		if(this.activeWindow <= TimeSpan.Zero)
		{
			return;
		}

		var windowStart = now - this.activeWindow;
		var maxBuckets = 20;
		var bucketCount = Math.Min(maxBuckets, Math.Max(4, (int)Math.Ceiling(this.activeWindow.TotalMinutes * 2)));
		var bucketDuration = TimeSpan.FromTicks(this.activeWindow.Ticks / bucketCount);
		var counts = new int[bucketCount];

		var node = this.recentKills.Last;
		while(node != null&&node.Value.Timestamp >= windowStart)
		{
			var offset = node.Value.Timestamp - windowStart;
			var index = (int)Math.Floor(offset.Ticks / (double)bucketDuration.Ticks);
			if(index >= bucketCount)
			{
				index = bucketCount - 1;
			}
			else if(index < 0)
			{
				index = 0;
			}

			counts[index]++;
			node = node.Previous;
		}

		for(var i = 0; i < bucketCount; i++)
		{
			var bucketTime = windowStart + TimeSpan.FromTicks(bucketDuration.Ticks * i) + TimeSpan.FromTicks(bucketDuration.Ticks / 2);
			this.activityPoints.Add(new KillActivityPoint
			                        {
					                        Time = bucketTime,
					                        KillCount = counts[i]
			                        });
		}
	}

	private bool RecalculateCounts(DateTime now)
	{
		var previousCounts = new Dictionary<string, int>(this.currentCounts, StringComparer.OrdinalIgnoreCase);
		this.UpdateCounts(now);
		if(previousCounts.Count != this.currentCounts.Count)
		{
			return true;
		}

		foreach(var kvp in this.currentCounts)
		{
			if(!previousCounts.TryGetValue(kvp.Key, out var previous)||previous != kvp.Value)
			{
				return true;
			}
		}

		return false;
	}

	private bool ExpireTooOld(DateTime now)
	{
		var threshold = now - MaximumRetention;
		var expired = false;

		while(this.recentKills.First != null&&this.recentKills.First.Value.Timestamp < threshold)
		{
			expired = true;
			this.recentKills.RemoveFirst();
		}

		return expired;
	}

	private static DateTime ResolveKillDateTime(TimeOnly timestamp, DateTime? sessionStartTime)
	{
		var sessionDate = sessionStartTime?.Date ?? DateTime.Now.Date;
		var dateTime = sessionDate.Add(timestamp.ToTimeSpan());
		if(sessionStartTime.HasValue&&dateTime < sessionStartTime.Value)
		{
			dateTime = dateTime.AddDays(1);
		}

		return dateTime;
	}

	private void TrackUniqueZone(string? zone)
	{
		if(string.IsNullOrWhiteSpace(zone))
		{
			return;
		}

		var trimmedZone = zone.Trim();
		bool shouldPersist;
		lock(this.uniqueZonesLock)
		{
			shouldPersist = this.uniqueZones.Add(trimmedZone)||this.zoneFileDirty;
			this.zoneFileDirty = false;
		}

		if(shouldPersist)
		{
			this.PersistUniqueZonesToDisk();
		}
	}

	private void LoadUniqueZonesFromDisk()
	{
		try
		{
			if(!File.Exists(this.zoneExportPath))
			{
				return;
			}

			var json = File.ReadAllText(this.zoneExportPath);
			var zones = JsonSerializer.Deserialize<List<string>>(json);
			if(zones == null)
			{
				return;
			}

			foreach(var zone in zones)
			{
				if(!string.IsNullOrWhiteSpace(zone))
				{
					this.uniqueZones.Add(zone.Trim());
				}
			}

			// Normalize any existing file content (sort + dedupe) as soon as it is loaded.
			this.PersistUniqueZonesToDisk();
		}
		catch
		{
			// Ignore invalid/missing file content and start collecting zones from scratch.
			// Mark dirty so the file is rewritten on the next zone track.
			this.zoneFileDirty = true;
		}
	}

	private void PersistUniqueZonesToDisk()
	{
		List<string> snapshot;
		lock(this.uniqueZonesLock)
		{
			snapshot = this.uniqueZones.OrderBy(z => z, StringComparer.OrdinalIgnoreCase).ToList();
		}

		var directory = Path.GetDirectoryName(this.zoneExportPath);
		if(!string.IsNullOrWhiteSpace(directory))
		{
			Directory.CreateDirectory(directory);
		}

		var json = JsonSerializer.Serialize(snapshot, JsonOptions);
		File.WriteAllText(this.zoneExportPath, json);
	}

	private readonly record struct ZoneKillEntry(DateTime Timestamp, string Zone);
}
