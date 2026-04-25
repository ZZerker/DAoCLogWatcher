using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DAoCLogWatcher.UI.Models;

public partial class RealmPointSummary: ObservableObject
{
	[ObservableProperty] private int totalRealmPoints;

	[ObservableProperty] private DateTime? firstEntryTime;

	[ObservableProperty] private DateTime? lastEntryTime;

	[ObservableProperty] private int playerKills;

	[ObservableProperty] private int campaignQuests;

	[ObservableProperty] private int ticks;

	[ObservableProperty] private int siege;

	[ObservableProperty] private int assaultOrder;

	[ObservableProperty] private int supportActivity;

	[ObservableProperty] private int relicCapture;

	[ObservableProperty] private int timedMissions;

	[ObservableProperty] private int misc;

	[ObservableProperty] private int playerKillsRP;

	[ObservableProperty] private int campaignQuestsRP;

	[ObservableProperty] private int ticksRP;

	[ObservableProperty] private int siegeRP;

	[ObservableProperty] private int assaultOrderRP;

	[ObservableProperty] private int supportActivityRP;

	[ObservableProperty] private int relicCaptureRP;

	[ObservableProperty] private int timedMissionsRP;

	[ObservableProperty] private int miscRP;

	public int TotalEntries => this.PlayerKills + this.CampaignQuests + this.Ticks + this.Siege + this.AssaultOrder + this.SupportActivity + this.RelicCapture + this.TimedMissions + this.Misc;

	public double PlayerKillsPercentage => this.TotalRealmPoints > 0?this.PlayerKillsRP * 100.0 / this.TotalRealmPoints:0;

	public double CampaignQuestsPercentage => this.TotalRealmPoints > 0?this.CampaignQuestsRP * 100.0 / this.TotalRealmPoints:0;

	public double TicksPercentage => this.TotalRealmPoints > 0?this.TicksRP * 100.0 / this.TotalRealmPoints:0;

	public double SiegePercentage => this.TotalRealmPoints > 0?this.SiegeRP * 100.0 / this.TotalRealmPoints:0;

	public double AssaultOrderPercentage => this.TotalRealmPoints > 0?this.AssaultOrderRP * 100.0 / this.TotalRealmPoints:0;

	public double SupportActivityPercentage => this.TotalRealmPoints > 0?this.SupportActivityRP * 100.0 / this.TotalRealmPoints:0;

	public double RelicCapturePercentage => this.TotalRealmPoints > 0?this.RelicCaptureRP * 100.0 / this.TotalRealmPoints:0;

	public double TimedMissionsPercentage => this.TotalRealmPoints > 0?this.TimedMissionsRP * 100.0 / this.TotalRealmPoints:0;

	public double MiscPercentage => this.TotalRealmPoints > 0?this.MiscRP * 100.0 / this.TotalRealmPoints:0;

	public bool IsLive { get; set; }

	public double RpsPerHour
	{
		get
		{
			if(!this.FirstEntryTime.HasValue||this.TotalRealmPoints == 0)
			{
				return 0;
			}

			var endTime = this.IsLive?DateTime.Now:this.LastEntryTime ?? DateTime.Now;
			var duration = endTime - this.FirstEntryTime.Value;

			if(duration.TotalHours <= 0)
			{
				return 0;
			}

			return this.TotalRealmPoints / duration.TotalHours;
		}
	}

	public void Reset()
	{
		this.IsLive = false;
		this.TotalRealmPoints = 0;
		this.FirstEntryTime = null;
		this.LastEntryTime = null;
		this.PlayerKills = 0;
		this.CampaignQuests = 0;
		this.Ticks = 0;
		this.Siege = 0;
		this.AssaultOrder = 0;
		this.SupportActivity = 0;
		this.RelicCapture = 0;
		this.TimedMissions = 0;
		this.Misc = 0;
		this.PlayerKillsRP = 0;
		this.CampaignQuestsRP = 0;
		this.TicksRP = 0;
		this.SiegeRP = 0;
		this.AssaultOrderRP = 0;
		this.SupportActivityRP = 0;
		this.RelicCaptureRP = 0;
		this.TimedMissionsRP = 0;
		this.MiscRP = 0;

		// Percentage and TotalEntries changes are raised by the individual setters above via OnXChanged.
		// RpsPerHour has no setter-driven notification, so it must be raised explicitly.
		this.OnPropertyChanged(nameof(this.RpsPerHour));
	}

	public void RefreshRpsPerHour()
	{
		this.OnPropertyChanged(nameof(this.RpsPerHour));
	}

	partial void OnPlayerKillsChanged(int value)
	{
		this.NotifyPercentagesChanged();
	}

	partial void OnCampaignQuestsChanged(int value)
	{
		this.NotifyPercentagesChanged();
	}

	partial void OnTicksChanged(int value)
	{
		this.NotifyPercentagesChanged();
	}

	partial void OnSiegeChanged(int value)
	{
		this.NotifyPercentagesChanged();
	}

	partial void OnAssaultOrderChanged(int value)
	{
		this.NotifyPercentagesChanged();
	}

	partial void OnSupportActivityChanged(int value)
	{
		this.NotifyPercentagesChanged();
	}

	partial void OnRelicCaptureChanged(int value)
	{
		this.NotifyPercentagesChanged();
	}

	partial void OnTimedMissionsChanged(int value)
	{
		this.NotifyPercentagesChanged();
	}

	partial void OnMiscChanged(int value)
	{
		this.NotifyPercentagesChanged();
	}

	private void NotifyPercentagesChanged()
	{
		this.OnPropertyChanged(nameof(this.TotalEntries));
		this.OnPropertyChanged(nameof(this.PlayerKillsPercentage));
		this.OnPropertyChanged(nameof(this.CampaignQuestsPercentage));
		this.OnPropertyChanged(nameof(this.TicksPercentage));
		this.OnPropertyChanged(nameof(this.SiegePercentage));
		this.OnPropertyChanged(nameof(this.AssaultOrderPercentage));
		this.OnPropertyChanged(nameof(this.SupportActivityPercentage));
		this.OnPropertyChanged(nameof(this.RelicCapturePercentage));
		this.OnPropertyChanged(nameof(this.TimedMissionsPercentage));
		this.OnPropertyChanged(nameof(this.MiscPercentage));
	}
}
