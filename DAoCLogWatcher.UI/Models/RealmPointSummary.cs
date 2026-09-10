using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DAoCLogWatcher.UI.Models;

public partial class RealmPointSummary: ObservableObject
{
	private static readonly TimeSpan RollingWindow = TimeSpan.FromHours(1);

	// Dampens the early spike while the session is still shorter than the window
	private const double MIN_WINDOW_HOURS = 10.0 / 60.0;

	private readonly Queue<(DateTime Time, int Points)> rollingEntries = new();
	private int rollingWindowPoints;

	[ObservableProperty] private int totalRealmPoints;

	[ObservableProperty] private DateTime? firstEntryTime;

	public DateTime? SessionStartTime { get; set; }

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

	public string SessionStartText
	{
		get
		{
			var t = this.SessionStartTime ?? this.FirstEntryTime;
			return t.HasValue?t.Value.ToString("HH:mm"):"--:--";
		}
	}

	/// <summary>
	/// Elapsed session time, measured to the last RP entry rather than the wall clock: a chat log
	/// session can stay open for days across which only a couple of hours were actually played.
	/// </summary>
	public TimeSpan SessionDuration
	{
		get
		{
			var start = this.SessionStartTime ?? this.FirstEntryTime;
			if(!start.HasValue)
			{
				return TimeSpan.Zero;
			}

			var end = this.IsLive?DateTime.Now:this.LastEntryTime ?? DateTime.Now;
			var duration = end - start.Value;

			return duration > TimeSpan.Zero?duration:TimeSpan.Zero;
		}
	}

	public string SessionDurationText
	{
		get
		{
			var start = this.SessionStartTime ?? this.FirstEntryTime;

			return start.HasValue?DurationFormat.Short(this.SessionDuration):"";
		}
	}

	/// <summary>RP per hour over the trailing hour, not the session average.</summary>
	public double RpsPerHour
	{
		get
		{
			var end = this.RollingWindowEnd;
			this.TrimRollingWindow(end);

			if(this.rollingWindowPoints == 0)
			{
				return 0;
			}

			var windowStart = end - RollingWindow;
			var sessionStart = this.SessionStartTime ?? this.FirstEntryTime;
			if(sessionStart.HasValue&&sessionStart.Value > windowStart)
			{
				windowStart = sessionStart.Value;
			}

			var windowHours = Math.Max((end - windowStart).TotalHours, MIN_WINDOW_HOURS);

			return this.rollingWindowPoints / windowHours;
		}
	}

	/// <summary>Session average, for the history record.</summary>
	public double SessionRpsPerHour
	{
		get
		{
			if(this.TotalRealmPoints == 0)
			{
				return 0;
			}

			var duration = this.SessionDuration;

			if(duration.TotalHours <= 0)
			{
				return 0;
			}

			return this.TotalRealmPoints / duration.TotalHours;
		}
	}

	/// <summary>Feeds one RP entry into the rolling window behind <see cref="RpsPerHour" />.</summary>
	public void AddEntry(DateTime entryTime, int points)
	{
		this.rollingEntries.Enqueue((entryTime, points));
		this.rollingWindowPoints += points;
		this.TrimRollingWindow(entryTime);

		this.OnPropertyChanged(nameof(this.RpsPerHour));
		this.OnPropertyChanged(nameof(this.SessionRpsPerHour));
	}

	/// <summary>Wall clock while watching, last entry otherwise.</summary>
	private DateTime RollingWindowEnd
	{
		get
		{
			if(!this.IsLive)
			{
				return this.LastEntryTime ?? DateTime.Now;
			}

			var now = DateTime.Now;

			return this.LastEntryTime > now?this.LastEntryTime.Value:now;
		}
	}

	private void TrimRollingWindow(DateTime end)
	{
		var windowStart = end - RollingWindow;
		while(this.rollingEntries.TryPeek(out var oldest)&&oldest.Time < windowStart)
		{
			this.rollingEntries.Dequeue();
			this.rollingWindowPoints -= oldest.Points;
		}
	}

	public void Reset()
	{
		this.IsLive = false;
		this.SessionStartTime = null;
		this.TotalRealmPoints = 0;
		this.rollingEntries.Clear();
		this.rollingWindowPoints = 0;
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

		this.OnPropertyChanged(nameof(this.RpsPerHour));
		this.OnPropertyChanged(nameof(this.SessionRpsPerHour));
		this.OnPropertyChanged(nameof(this.SessionStartText));
		this.OnPropertyChanged(nameof(this.SessionDurationText));
	}

	public void RefreshRpsPerHour()
	{
		this.OnPropertyChanged(nameof(this.RpsPerHour));
		this.OnPropertyChanged(nameof(this.SessionRpsPerHour));
		this.OnPropertyChanged(nameof(this.SessionStartText));
		this.OnPropertyChanged(nameof(this.SessionDurationText));
	}

	partial void OnFirstEntryTimeChanged(DateTime? value)
	{
		this.OnPropertyChanged(nameof(this.SessionStartText));
		this.OnPropertyChanged(nameof(this.SessionDurationText));
	}

	partial void OnLastEntryTimeChanged(DateTime? value)
	{
		this.OnPropertyChanged(nameof(this.SessionDurationText));
	}

	partial void OnTotalRealmPointsChanged(int value)
	{
		this.NotifyPercentagesChanged();
	}

	partial void OnPlayerKillsRPChanged(int value)
	{
		this.NotifyPercentagesChanged();
	}

	partial void OnCampaignQuestsRPChanged(int value)
	{
		this.NotifyPercentagesChanged();
	}

	partial void OnTicksRPChanged(int value)
	{
		this.NotifyPercentagesChanged();
	}

	partial void OnSiegeRPChanged(int value)
	{
		this.NotifyPercentagesChanged();
	}

	partial void OnAssaultOrderRPChanged(int value)
	{
		this.NotifyPercentagesChanged();
	}

	partial void OnSupportActivityRPChanged(int value)
	{
		this.NotifyPercentagesChanged();
	}

	partial void OnRelicCaptureRPChanged(int value)
	{
		this.NotifyPercentagesChanged();
	}

	partial void OnTimedMissionsRPChanged(int value)
	{
		this.NotifyPercentagesChanged();
	}

	partial void OnMiscRPChanged(int value)
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
