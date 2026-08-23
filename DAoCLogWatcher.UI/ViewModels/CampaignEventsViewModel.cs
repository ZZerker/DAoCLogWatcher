using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DAoCLogWatcher.UI.Models;
using DAoCLogWatcher.UI.Services;

namespace DAoCLogWatcher.UI.ViewModels;

public sealed partial class CampaignMissionItem: ObservableObject
{
	public CampaignMissionItem(string id, string name, string zone, int zoneId, string remaining, bool isPending)
	{
		this.Id = id;
		this.Name = name;
		this.Zone = zone;
		this.ZoneId = zoneId;
		this.remaining = remaining;
		this.isPending = isPending;
	}

	public string Id { get; }

	/// <summary>Frontier zone id, used to focus the warmap on this mission.</summary>
	public int ZoneId { get; }

	public string Name { get; }

	public string Zone { get; }

	[ObservableProperty] private string remaining;

	/// <summary>True for the ~10s window between announcement and the mission actually starting.</summary>
	[ObservableProperty] private bool isPending;
}

public sealed record CampaignSpawnItem(string Name, int Count);

public sealed partial class CampaignEventsViewModel: ObservableObject, IDisposable
{
	private readonly WarmapWebSocketService warmapService;
	private readonly Dictionary<int, string> zoneNames;
	private readonly DispatcherTimer tickTimer;

	public ObservableCollection<CampaignMissionItem> Missions { get; } = new();

	public ObservableCollection<CampaignSpawnItem> Spawns { get; } = new();

	[ObservableProperty] private string rotationPhase = "—";

	[ObservableProperty] private string rotationCountdown = string.Empty;

	public bool HasEvents => this.Missions.Count > 0||this.Spawns.Count > 0;

	/// <summary>
	/// Raised with a frontier zone id when the user asks to see a mission on the map. The window
	/// handles it — the widget lives on the Dashboard, so focusing means switching tabs first.
	/// </summary>
	public event EventHandler<int>? LocateRequested;

	[RelayCommand]
	private void Locate(CampaignMissionItem? item)
	{
		if(item != null)
		{
			this.LocateRequested?.Invoke(this, item.ZoneId);
		}
	}

	public CampaignEventsViewModel(WarmapWebSocketService warmapService, FrontierMapData map)
	{
		this.warmapService = warmapService;

		var names = new Dictionary<int, string>();
		foreach(var zone in map.Zones)
		{
			names.TryAdd(zone.ZoneId, zone.Name);
		}

		this.zoneNames = names;

		this.warmapService.EventsUpdated += this.OnEventsUpdated;

		this.tickTimer = new DispatcherTimer
		                 {
				                 Interval = TimeSpan.FromSeconds(1)
		                 };
		this.tickTimer.Tick += this.OnTick;
		this.tickTimer.Start();

		this.Refresh();
	}

	private void OnEventsUpdated(object? sender, EventArgs e)
	{
		Dispatcher.UIThread.Post(this.Refresh);
	}

	private void OnTick(object? sender, EventArgs e)
	{
		this.Refresh();
	}

	private void Refresh()
	{
		var snapshot = this.warmapService.GetEventsSnapshot();

		var missions = snapshot.Where(ev => !ev.IsCampaignSpawn)
		                        .OrderBy(ev => ev.HasDeadline?0:1)
		                        .ThenBy(ev => ev.EndsAtMs)
		                        .Select(ev => (ev.Id, ev.DisplayName, Zone: this.zoneNames.GetValueOrDefault(ev.Zone, $"Zone {ev.Zone}"), ZoneId: ev.Zone, Remaining: ev.TimeRemaining is { } remaining?DurationFormat.MinutesSeconds(remaining):"—", ev.IsPending))
		                        .ToList();

		var spawns = snapshot.Where(ev => ev.IsCampaignSpawn)
		                      .GroupBy(ev => ev.DisplayName)
		                      .Select(g => new CampaignSpawnItem(g.Key, g.Count()))
		                      .OrderByDescending(s => s.Count)
		                      .ThenBy(s => s.Name)
		                      .ToList();

		var sameOrder = this.Missions.Count == missions.Count&&this.Missions.Select(m => m.Id).SequenceEqual(missions.Select(m => m.Id));
		if(sameOrder)
		{
			for(var i = 0; i < missions.Count; i++)
			{
				this.Missions[i].Remaining = missions[i].Remaining;
				this.Missions[i].IsPending = missions[i].IsPending;
			}
		}
		else
		{
			this.Missions.Clear();
			foreach(var m in missions)
			{
				this.Missions.Add(new CampaignMissionItem(m.Id, m.DisplayName, m.Zone, m.ZoneId, m.Remaining, m.IsPending));
			}
		}

		if(!this.Spawns.SequenceEqual(spawns))
		{
			this.Spawns.Clear();
			foreach(var s in spawns)
			{
				this.Spawns.Add(s);
			}
		}

		var rotation = this.warmapService.GetRotation();
		this.RotationPhase = rotation == null?"—":$"Phase {rotation.Current} of {rotation.Of}";
		this.RotationCountdown = rotation?.TimeToNext is { } toNext?DurationFormat.MinutesSeconds(toNext):string.Empty;

		this.OnPropertyChanged(nameof(this.HasEvents));
	}

	public void Dispose()
	{
		this.tickTimer.Stop();
		this.tickTimer.Tick -= this.OnTick;
		this.warmapService.EventsUpdated -= this.OnEventsUpdated;
	}
}
