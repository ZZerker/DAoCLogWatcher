using System;
using System.Collections.Generic;
using System.Diagnostics;
using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Models;

namespace DAoCLogWatcher.UI.Services;

public sealed class RealmPointProcessor(RealmPointSummary summary, RpsChartData chartData)
{
	private readonly List<KillEvent> killEventBuffer = new();
	private int killWindowRpCount;
	private int killWindowRps;
	private TimeOnly killWindowStart;
	private RealmPointLogEntry? killWindowFirstEntry;

	public event EventHandler<RealmPointLogEntry>? MultiKillDetected;

	public string? DetectedCharacterName { get; private set; }
	public int Kills { get; private set; }
	public int Deaths { get; private set; }

	/// <summary>
	/// Processes a single log line, updating summary and chart data.
	/// Returns a <see cref="RealmPointLogEntry"/> if the line contained RP data, null otherwise.
	/// <paramref name="characterChanged"/> is set when the detected character name changed.
	/// <paramref name="killStatsChanged"/> is set when kills or deaths changed.
	/// </summary>
	public RealmPointLogEntry? Process(
		LogLine logLine,
		DateTime? sessionStartTime,
		out bool characterChanged,
		out bool killStatsChanged)
	{
		characterChanged = false;
		killStatsChanged = false;

		if (this.killWindowRpCount > 0)
		{
			var currentTs = logLine switch
			{
				KillLogLine k       => (TimeOnly?)k.Event.Timestamp,
				RealmPointLogLine r => r.Entry.Timestamp,
				DamageLogLine d     => d.Event.Timestamp,
				HealLogLine h       => h.Event.Timestamp,
				_                   => null
			};
			if (currentTs.HasValue)
			{
				var diffSec = (currentTs.Value.ToTimeSpan() - this.killWindowStart.ToTimeSpan()).TotalSeconds;
				if (diffSec < 0) diffSec += TimeSpan.FromDays(1).TotalSeconds;
				if (diffSec > 5 + this.killWindowRpCount * 0.2)
					this.FinalizeKillWindow();
			}
		}

		if (logLine.DetectedCharacterName != null && logLine.DetectedCharacterName != this.DetectedCharacterName)
		{
			this.DetectedCharacterName = logLine.DetectedCharacterName;
			characterChanged = true;
			this.RecomputeKillStats(ref killStatsChanged);
		}

		if (logLine is KillLogLine { Event: var killEvent })
		{
			this.killEventBuffer.Add(killEvent);
			if (this.DetectedCharacterName != null)
				this.RecomputeKillStats(ref killStatsChanged);
		}

		if (logLine is not RealmPointLogLine { Entry: var entry })
			return null;

		summary.TotalRealmPoints += entry.Points;

		var sessionDate = sessionStartTime?.Date ?? DateTime.Now.Date;
		var entryDateTime = sessionDate.Add(entry.Timestamp.ToTimeSpan());
		// Handle midnight crossing: if reconstructed time is before session start, entry is next day
		if (sessionStartTime.HasValue && entryDateTime < sessionStartTime.Value)
			entryDateTime = entryDateTime.AddDays(1);

		summary.FirstEntryTime ??= entryDateTime;
		summary.LastEntryTime = entryDateTime;

		var isFirstKillInWindow = entry.Source == RealmPointSource.PlayerKill && this.killWindowRpCount == 0;

		switch (entry.Source)
		{
			case RealmPointSource.PlayerKill:
				summary.PlayerKills++;
				summary.PlayerKillsRP += entry.Points;
				if (this.killWindowRpCount == 0)
					this.killWindowStart = entry.Timestamp;
				this.killWindowRpCount++;
				this.killWindowRps += entry.Points;
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
			RealmPointSource.PlayerKill     => "Player Kill",
			RealmPointSource.CampaignQuest  => "Campaign Quest completed",
			RealmPointSource.Tick           => "Battle Tick",
			RealmPointSource.Siege          => "Siege (Tower/Keep Capture)",
			RealmPointSource.AssaultOrder   => "Assault Order",
			RealmPointSource.SupportActivity => "Support Tick",
			RealmPointSource.RelicCapture   => "Relic Capture",
			RealmPointSource.TimedMission   => "Timed Mission",
			RealmPointSource.Misc           => "Other",
			_                               => throw new UnreachableException()
		};

		chartData.Add(entryDateTime, summary.TotalRealmPoints, entry.Points);

		var details = entry.Victim != null
			? (entry.SubSource != null ? $"{entry.Victim} ({entry.SubSource})" : entry.Victim)
			: entry.SubSource ?? sourceLabel;

		var logEntry = new RealmPointLogEntry
		{
			Timestamp = entry.Timestamp.ToString("HH:mm:ss"),
			Points    = entry.Points,
			Source    = entry.Source.ToString(),
			Details   = details
		};

		if (isFirstKillInWindow)
			this.killWindowFirstEntry = logEntry;

		return logEntry;
	}

	public void Reset()
	{
		this.killWindowRpCount = 0;
		this.killWindowRps = 0;
		this.killWindowFirstEntry = null;
		this.killEventBuffer.Clear();
		this.DetectedCharacterName = null;
		this.Kills = 0;
		this.Deaths = 0;
	}

	private void FinalizeKillWindow()
	{
		if (this.killWindowRpCount >= 3)
		{
			if (this.killWindowFirstEntry != null)
				this.killWindowFirstEntry.IsMultiKill = true;

			this.MultiKillDetected?.Invoke(this, new RealmPointLogEntry
			{
				Timestamp   = this.killWindowStart.ToString("HH:mm:ss"),
				Points      = this.killWindowRps,
				Source      = "Multi-Kill",
				Details     = $"{this.killWindowRpCount}x player kills",
				IsMultiKill = true
			});
		}
		this.killWindowRpCount = 0;
		this.killWindowRps = 0;
		this.killWindowFirstEntry = null;
	}

	private void RecomputeKillStats(ref bool killStatsChanged)
	{
		var name = this.DetectedCharacterName;
		if (name == null)
		{
			if (this.Kills != 0 || this.Deaths != 0)
			{
				this.Kills = 0;
				this.Deaths = 0;
				killStatsChanged = true;
			}
			return;
		}

		var kills = 0;
		var deaths = 0;
		foreach (var ev in this.killEventBuffer)
		{
			if (ev.Killer == name) kills++;
			if (ev.Victim == name) deaths++;
		}

		if (kills != this.Kills || deaths != this.Deaths)
		{
			this.Kills = kills;
			this.Deaths = deaths;
			killStatsChanged = true;
		}
	}
}
