using System;
using System.Diagnostics;
using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Models;

namespace DAoCLogWatcher.UI.Services;

public sealed class RealmPointProcessor(RealmPointSummary summary, RpsChartData chartData): IRealmPointProcessor
{
	private readonly MultiKillDetector multiKillDetector = new();
	private readonly KillStatTracker killStatTracker = new();

	public event EventHandler<RealmPointLogEntry>? MultiKillDetected
	{
		add => this.multiKillDetector.MultiKillDetected += value;
		remove => this.multiKillDetector.MultiKillDetected -= value;
	}

	public event EventHandler<RealmPointLogEntry>? EntryProcessed;

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
		}

		if(logLine is not RealmPointLogLine { Entry: var entry })
		{
			return;
		}

		summary.TotalRealmPoints += entry.Points;

		var sessionDate = sessionStartTime?.Date ?? DateTime.Now.Date;
		var entryDateTime = sessionDate.Add(entry.Timestamp.ToTimeSpan());
		if(sessionStartTime.HasValue&&entryDateTime < sessionStartTime.Value)
		{
			entryDateTime = entryDateTime.AddDays(1);
		}

		summary.FirstEntryTime ??= entryDateTime;
		summary.LastEntryTime = entryDateTime;

		switch(entry.Source)
		{
			case RealmPointSource.PlayerKill:
				summary.PlayerKills++;
				summary.PlayerKillsRP += entry.Points;
				break;
			case RealmPointSource.CampaignQuest:
				summary.CampaignQuests++;
				summary.CampaignQuestsRP += entry.Points;
				break;
			case RealmPointSource.Tick:
				summary.Ticks++;
				summary.TicksRP += entry.Points;
				break;
			case RealmPointSource.Siege:
				summary.Siege++;
				summary.SiegeRP += entry.Points;
				break;
			case RealmPointSource.AssaultOrder:
				summary.AssaultOrder++;
				summary.AssaultOrderRP += entry.Points;
				break;
			case RealmPointSource.SupportActivity:
				summary.SupportActivity++;
				summary.SupportActivityRP += entry.Points;
				break;
			case RealmPointSource.RelicCapture:
				summary.RelicCapture++;
				summary.RelicCaptureRP += entry.Points;
				break;
			case RealmPointSource.TimedMission:
				summary.TimedMissions++;
				summary.TimedMissionsRP += entry.Points;
				break;
			case RealmPointSource.Misc:
				summary.Misc++;
				summary.MiscRP += entry.Points;
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

		chartData.Add(entryDateTime, summary.TotalRealmPoints, entry.Points);

		var details = entry.Victim != null?(entry.SubSource != null?$"{entry.Victim} ({entry.SubSource})":entry.Victim):entry.SubSource ?? sourceLabel;

		var logEntry = new RealmPointLogEntry
		               {
				               Timestamp = entry.Timestamp.ToString("HH:mm:ss"),
				               Points = entry.Points,
				               Source = entry.Source.ToString(),
				               Details = details
		               };

		if(entry.Source == RealmPointSource.PlayerKill)
		{
			this.multiKillDetector.OnPlayerKillRp(entry.Timestamp, entry.Points, logEntry);
		}

		EntryProcessed?.Invoke(this, logEntry);
	}

	public void Reset()
	{
		this.multiKillDetector.Reset();
		this.killStatTracker.Reset();
		this.DetectedCharacterName = null;
	}

	private static TimeOnly? GetTimestamp(LogLine logLine) =>
			logLine switch
			{
					KillLogLine k => (TimeOnly?)k.Event.Timestamp,
					RealmPointLogLine r => r.Entry.Timestamp,
					DamageLogLine d => d.Event.Timestamp,
					HealLogLine h => h.Event.Timestamp,
					_ => null
			};
}
