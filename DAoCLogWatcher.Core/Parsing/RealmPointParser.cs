using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using DAoCLogWatcher.Core.Models;

namespace DAoCLogWatcher.Core.Parsing;

public sealed partial class RealmPointParser
{
	private static readonly Regex RealmPointRegex = GenerateRealmPointRegex();

	private enum ParserState
	{
		Idle,
		AwaitingParticipation,
		AwaitingRelicCapture,
		AwaitingFairFight
	}

	private RealmPointEntry? pendingEntry;
	private RealmPointEntry? bufferedEntry;
	private ParserState state = ParserState.Idle;

	public bool TryParse(string line, out RealmPointEntry? entry)
	{
		// If a previous call produced two entries (e.g. pending flush + the line itself),
		// emit the buffered one now and parse the current line for next time.
		if(this.bufferedEntry != null)
		{
			entry = this.bufferedEntry;
			this.bufferedEntry = null;
			if(this.TryParseInternal(line, out var next))
			{
				this.bufferedEntry = next;
			}

			return true;
		}

		return this.TryParseInternal(line, out entry);
	}

	private bool TryParseInternal(string line, out RealmPointEntry? entry)
	{
		entry = null;

		if(string.IsNullOrWhiteSpace(line))
		{
			return false;
		}

		if(line.Contains("has stored the")&&this.state != ParserState.AwaitingRelicCapture)
		{
			this.state = ParserState.AwaitingRelicCapture;
			Debug.WriteLine($"[Parser] Relic capture detected");
			return false;
		}

		if(line.Contains("a fair fight"))
		{
			this.state = ParserState.AwaitingFairFight;
			return false;
		}

		if(this.state == ParserState.AwaitingParticipation&&this.pendingEntry != null)
		{
			// XP Guild Bonus line may appear between the RP line and the XP confirmation line
			if(line.Contains("XP Guild Bonus"))
				return false;

			RealmPointSource pendingSource;
			if(line.Contains("have captured"))
				pendingSource = RealmPointSource.Siege;
			else if(line.Contains("experience points")||line.Contains("Kill participation"))
				pendingSource = RealmPointSource.PlayerKill;
			else
				pendingSource = RealmPointSource.Misc;

			entry = new RealmPointEntry
			        {
					        Timestamp = this.pendingEntry.Timestamp,
					        Points = this.pendingEntry.Points,
					        Source = pendingSource,
					        RawLine = this.pendingEntry.RawLine
			        };

			this.pendingEntry = null;
			this.state = ParserState.Idle;

			// If the confirmation line is itself an RP line, re-parse it so its RP
			// are not silently consumed. The result is buffered for the next TryParse call.
			if(RealmPointRegex.IsMatch(line))
			{
				if(this.TryParseInternal(line, out var additional))
				{
					this.bufferedEntry = additional;
				}
			}

			return true;
		}

		var match = RealmPointRegex.Match(line);
		if(!match.Success)
		{
			return false;
		}

		if(!TimeOnly.TryParseExact(match.Groups["timestamp"].Value, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
		{
			return false;
		}

		if(!int.TryParse(match.Groups["points"].Value, out var points))
		{
			return false;
		}

		var reason = match.Groups["reason"].Value;

		if((reason.Contains("realm rank")||reason.Contains("guild's buff"))&&line.Contains("an additional"))
		{
			return false;
		}

		var source = DetermineSourceFromReason(reason);

		if(this.state == ParserState.AwaitingRelicCapture)
		{
			entry = new RealmPointEntry
			        {
					        Timestamp = timestamp,
					        Points = points,
					        Source = RealmPointSource.RelicCapture,
					        RawLine = line
			        };
			this.state = ParserState.Idle;
			Debug.WriteLine($"[Parser] Relic: {points} RP");
			return true;
		}

		if(this.state == ParserState.AwaitingFairFight)
		{
			this.state = ParserState.Idle;
			entry = new RealmPointEntry
			        {
					        Timestamp = timestamp,
					        Points = points,
					        Source = RealmPointSource.CampaignQuest,
					        SubSource = "Fair Fight",
					        RawLine = line
			        };
			return true;
		}

		if(source == RealmPointSource.Misc)
		{
			var subSource = DetermineSubSource(reason);
			if(subSource != null)
			{
				entry = new RealmPointEntry
				        {
						        Timestamp = timestamp,
						        Points = points,
						        Source = RealmPointSource.Misc,
						        SubSource = subSource,
						        RawLine = line
				        };
				return true;
			}

			this.pendingEntry = new RealmPointEntry
			                    {
					                    Timestamp = timestamp,
					                    Points = points,
					                    Source = RealmPointSource.Misc,
					                    RawLine = line
			                    };
			this.state = ParserState.AwaitingParticipation;
			return false;
		}

		entry = new RealmPointEntry
		        {
				        Timestamp = timestamp,
				        Points = points,
				        Source = source,
				        SubSource = DetermineSubSource(reason),
				        RawLine = line
		        };

		return true;
	}

	private static RealmPointSource DetermineSourceFromReason(string reason)
	{
		if(string.IsNullOrWhiteSpace(reason))
		{
			return RealmPointSource.Misc;
		}

		if(reason.Contains("Campaign Quest"))
		{
			return RealmPointSource.CampaignQuest;
		}

		if(reason.Contains("completing your mission")||reason.Contains("reaching Tier")||reason.Contains("Win Streak")||reason.Contains("War Supplies")||reason.Contains("Arena Match"))
		{
			return RealmPointSource.TimedMission;
		}

		if(reason.Contains("Tower Capture")||reason.Contains("Keep Capture"))
		{
			return RealmPointSource.Siege;
		}

		if(reason.Contains("Battle Tick"))
		{
			return RealmPointSource.Tick;
		}

		if(reason.Contains("Assault Order"))
		{
			return RealmPointSource.AssaultOrder;
		}

		if(reason.Contains("support activity in battle"))
		{
			return RealmPointSource.SupportActivity;
		}

		return RealmPointSource.Misc;
	}

	private static string? DetermineSubSource(string reason)
	{
		if(reason.Contains("completing your mission"))
			return "Mission Complete";

		var tierMatch = TierParticipationRegex().Match(reason);
		if(tierMatch.Success)
			return $"Tier {tierMatch.Groups["tier"].Value} Participation";

		if(reason.Contains("Win Streak"))
			return "Win Streak";

		if(reason.Contains("Arena Match"))
			return "Arena Match";

		if(reason.Contains("War Supplies"))
			return "War Supplies";

		if(reason.Contains("Tower Capture"))
			return "Tower Capture";

		if(reason.Contains("Keep Capture"))
			return "Keep Capture";

		if(reason.Contains("Battle Tick"))
			return "Battle Tick";

		if(reason.Contains("Assault Order"))
			return "Assault Order";

		if(reason.Contains("support activity in battle"))
			return "Support Activity";

		if(reason.Contains("repair"))
			return "Repair";

		return null;
	}

	[GeneratedRegex(@"Tier (?<tier>\d+) Participation", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex TierParticipationRegex();

	[GeneratedRegex(@"^\[(?<timestamp>\d{2}:\d{2}:\d{2})\] You get (?:an additional )?(?<points>\d+) realm points?(?<reason>.*)!$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex GenerateRealmPointRegex();
}
