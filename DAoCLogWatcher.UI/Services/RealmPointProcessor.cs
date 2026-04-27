using System;
using System.Collections.Generic;
using System.Diagnostics;
using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Models;

namespace DAoCLogWatcher.UI.Services;

public sealed class RealmPointProcessor: IRealmPointProcessor
{
	private readonly MultiKillDetector multiKillDetector;
	private readonly KillStatTracker killStatTracker = new();
	private readonly ZoneKillTracker zoneKillTracker = new();

	public RealmPointProcessor(RealmPointSummary summary, RpsChartData chartData)
	{
		this.summary = summary;
		this.chartData = chartData;
		this.multiKillDetector = new MultiKillDetector();
		this.multiKillDetector.MultiKillDetected += this.OnMultiKillDetected;
		this.zoneKillTracker.Updated += (_, _) => this.ZoneKillsUpdated?.Invoke(this, EventArgs.Empty);
	}

	private readonly RealmPointSummary summary;
	private readonly RpsChartData chartData;

	public event EventHandler<RealmPointLogEntry>? MultiKillDetected;

	public event EventHandler<RealmPointLogEntry>? EntryProcessed;

	public event EventHandler? ZoneKillsUpdated;

	public IReadOnlyDictionary<string, int> CurrentZoneKills => this.zoneKillTracker.CurrentCounts;

	public IReadOnlyList<KillActivityPoint> KillActivityPoints => this.zoneKillTracker.KillActivityPoints;

	public IReadOnlyList<KillActivityPoint> GetSessionActivityPoints(DateTime sessionStart, DateTime now) =>
		this.zoneKillTracker.GetSessionActivityPoints(sessionStart, now);

	public string? DetectedCharacterName { get; private set; }

	public int Kills => this.killStatTracker.Kills;

	public int Deaths => this.killStatTracker.Deaths;

	public void Process(LogLine logLine, DateTime? sessionStartTime, out bool characterChanged, out bool killStatsChanged)
	{
		characterChanged = false;
		killStatsChanged = false;

		this.multiKillDetector.AdvanceTimestamp(GetTimestamp(logLine));

		if(logLine.DetectedCharacterName != null&&logLine.DetectedCharacterName != this.DetectedCharacterName)
		{
			this.DetectedCharacterName = logLine.DetectedCharacterName;
			characterChanged = true;
			this.killStatTracker.OnCharacterChanged(this.DetectedCharacterName, ref killStatsChanged);
		}

		if(logLine is KillLogLine { Event: var killEvent })
		{
			this.killStatTracker.OnKillEvent(killEvent, this.DetectedCharacterName, ref killStatsChanged);
			this.zoneKillTracker.Track(killEvent, sessionStartTime);
		}

		if(logLine is not RealmPointLogLine { Entry: var entry })
		{
			return;
		}

		this.summary.TotalRealmPoints += entry.Points;

		var sessionDate = sessionStartTime?.Date ?? DateTime.Now.Date;
		var entryDateTime = sessionDate.Add(entry.Timestamp.ToTimeSpan());
		if(sessionStartTime.HasValue&&entryDateTime < sessionStartTime.Value)
		{
			entryDateTime = entryDateTime.AddDays(1);
		}

		this.summary.FirstEntryTime ??= entryDateTime;
		this.summary.LastEntryTime = entryDateTime;

		switch(entry.Source)
		{
			case RealmPointSource.PlayerKill:
				this.summary.PlayerKills++;
				this.summary.PlayerKillsRP += entry.Points;
				break;
			case RealmPointSource.CampaignQuest:
				this.summary.CampaignQuests++;
				this.summary.CampaignQuestsRP += entry.Points;
				break;
			case RealmPointSource.Tick:
				this.summary.Ticks++;
				this.summary.TicksRP += entry.Points;
				break;
			case RealmPointSource.Siege:
				this.summary.Siege++;
				this.summary.SiegeRP += entry.Points;
				break;
			case RealmPointSource.AssaultOrder:
				this.summary.AssaultOrder++;
				this.summary.AssaultOrderRP += entry.Points;
				break;
			case RealmPointSource.SupportActivity:
				this.summary.SupportActivity++;
				this.summary.SupportActivityRP += entry.Points;
				break;
			case RealmPointSource.RelicCapture:
				this.summary.RelicCapture++;
				this.summary.RelicCaptureRP += entry.Points;
				break;
			case RealmPointSource.TimedMission:
				this.summary.TimedMissions++;
				this.summary.TimedMissionsRP += entry.Points;
				break;
			case RealmPointSource.Misc:
				this.summary.Misc++;
				this.summary.MiscRP += entry.Points;
				break;
			default:
				throw new ArgumentOutOfRangeException();
		}

		var sourceLabel = entry.Source switch
		{
				RealmPointSource.PlayerKill => "Player Kill",
				RealmPointSource.CampaignQuest => "Campaign Quest completed",
				RealmPointSource.Tick => "Battle Tick",
				RealmPointSource.Siege => "Siege (Tower/Keep Capture)",
				RealmPointSource.AssaultOrder => "Assault Order",
				RealmPointSource.SupportActivity => "Support Tick",
				RealmPointSource.RelicCapture => "Relic Capture",
				RealmPointSource.TimedMission => "Timed Mission",
				RealmPointSource.Misc => "Other",
				_ => throw new UnreachableException()
		};

		this.chartData.Add(entryDateTime, this.summary.TotalRealmPoints, entry.Points);

		var details = entry.Victim != null?entry.SubSource != null?$"{entry.Victim} ({entry.SubSource})":entry.Victim:entry.SubSource ?? sourceLabel;

		var logEntry = new RealmPointLogEntry
		               {
				               Timestamp = entry.Timestamp.ToString("HH:mm:ss"),
				               Points = entry.Points,
				               Source = entry.Source.ToString(),
				               Details = details
		               };

		if(entry.Source == RealmPointSource.PlayerKill)
		{
			this.multiKillDetector.OnPlayerKillRp(entry.Timestamp, entry.Points, () => logEntry.IsMultiKill = true);
		}

		this.EntryProcessed?.Invoke(this, logEntry);
	}

	public void RefreshZoneKills()
	{
		this.zoneKillTracker.Refresh(DateTime.Now);
	}

	public void SetZoneKillWindow(TimeSpan window)
	{
		this.zoneKillTracker.SetWindow(window);
	}

	public void Reset()
	{
		this.multiKillDetector.Reset();
		this.killStatTracker.Reset();
		this.zoneKillTracker.Reset();
		this.DetectedCharacterName = null;
	}

	private static TimeOnly? GetTimestamp(LogLine logLine)
	{
		return logLine switch
		{
				KillLogLine k => (TimeOnly?)k.Event.Timestamp,
				RealmPointLogLine r => r.Entry.Timestamp,
				DamageLogLine d => d.Event.Timestamp,
				HealLogLine h => h.Event.Timestamp,
				_ => null
		};
	}

	private void OnMultiKillDetected(object? sender, MultiKillResult result)
	{
		this.MultiKillDetected?.Invoke(this,
		                               new RealmPointLogEntry
		                               {
				                               Timestamp = result.Start.ToString("HH:mm:ss"),
				                               Points = result.TotalRp,
				                               Source = "Multi-Kill",
				                               Details = $"{result.KillCount}x player kills",
				                               IsMultiKill = true,
				                               KillCount = result.KillCount
		                               });
	}
}
