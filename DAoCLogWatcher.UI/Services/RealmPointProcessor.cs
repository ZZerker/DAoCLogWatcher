using System;
using System.Collections.Generic;
using System.Diagnostics;
using DAoCLogWatcher.Core;
using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Models;

namespace DAoCLogWatcher.UI.Services;

public sealed class RealmPointProcessor: IRealmPointProcessor
{
	private readonly IMultiKillDetector multiKillDetector;
	private readonly KillStatTracker killStatTracker = new();
	private readonly ZoneKillTracker zoneKillTracker = new();

	public RealmPointProcessor(RealmPointSummary summary, RpsChartData chartData, IMultiKillDetector? multiKillDetector = null)
	{
		this.summary = summary;
		this.chartData = chartData;
		this.multiKillDetector = multiKillDetector ?? new MultiKillDetector();
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

		this.multiKillDetector.AdvanceTimestamp(logLine.Timestamp);

		if(logLine.DetectedCharacterName != null&&logLine.DetectedCharacterName != this.DetectedCharacterName)
		{
			this.DetectedCharacterName = logLine.DetectedCharacterName;
			characterChanged = true;
			this.killStatTracker.OnCharacterChanged(this.DetectedCharacterName, ref killStatsChanged);
		}

		if(logLine is KillLogLine { Event: { IsNpc: false } killEvent })
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

		var (sourceLabel, accumulate) = GetSourceMeta(entry.Source);
		accumulate(this.summary, entry.Points);

		this.chartData.Add(entryDateTime, this.summary.TotalRealmPoints, entry.Points);

		var details = entry.Victim != null?entry.SubSource != null?$"{entry.Victim} ({entry.SubSource})":entry.Victim:entry.SubSource ?? sourceLabel;

		var logEntry = new RealmPointLogEntry
		               {
				               Timestamp = entry.Timestamp.ToString("HH:mm:ss"),
				               Points = entry.Points,
				               Source = entry.Source.ToString(),
				               Details = details,
				               VictimName = entry.Victim,
			               IsDeathblow = entry.IsDeathblow
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

	private static (string Label, Action<RealmPointSummary, int> Accumulate) GetSourceMeta(RealmPointSource source) =>
		source switch
		{
				RealmPointSource.PlayerKill => ("Player Kill", (s, p) => { s.PlayerKills++; s.PlayerKillsRP += p; }),
				RealmPointSource.CampaignQuest => ("Campaign Quest completed", (s, p) => { s.CampaignQuests++; s.CampaignQuestsRP += p; }),
				RealmPointSource.Tick => ("Battle Tick", (s, p) => { s.Ticks++; s.TicksRP += p; }),
				RealmPointSource.Siege => ("Siege (Tower/Keep Capture)", (s, p) => { s.Siege++; s.SiegeRP += p; }),
				RealmPointSource.AssaultOrder => ("Assault Order", (s, p) => { s.AssaultOrder++; s.AssaultOrderRP += p; }),
				RealmPointSource.SupportActivity => ("Support Tick", (s, p) => { s.SupportActivity++; s.SupportActivityRP += p; }),
				RealmPointSource.RelicCapture => ("Relic Capture", (s, p) => { s.RelicCapture++; s.RelicCaptureRP += p; }),
				RealmPointSource.TimedMission => ("Timed Mission", (s, p) => { s.TimedMissions++; s.TimedMissionsRP += p; }),
				RealmPointSource.Misc => ("Other", (s, p) => { s.Misc++; s.MiscRP += p; }),
				_ => throw new UnreachableException()
		};

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
