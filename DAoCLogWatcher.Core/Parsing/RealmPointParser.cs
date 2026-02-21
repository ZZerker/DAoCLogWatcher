using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using DAoCLogWatcher.Core.Models;

namespace DAoCLogWatcher.Core.Parsing;

public sealed partial class RealmPointParser
{
	private static readonly Regex RealmPointRegex = GenerateRealmPointRegex();
	private static readonly Regex PlayerNameRegex = new(
		@"for (?:participating in )?the killing of (?<name>[\w]+)",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

	private RealmPointEntry? pendingEntry;
	private bool waitingForParticipationCheck;
	private bool waitingForRelicCapture;

	public bool TryParse(string line, out RealmPointEntry? entry)
	{
		entry = null;

		if(string.IsNullOrWhiteSpace(line))
		{
			return false;
		}

		if(line.Contains("has stored the")&&!this.waitingForRelicCapture)
		{
			this.waitingForRelicCapture = true;
			Debug.WriteLine($"[Parser] Relic capture detected");
			return false;
		}

		if(this.waitingForParticipationCheck&&this.pendingEntry != null)
		{
			var pendingSource = line.Contains("have captured") ? RealmPointSource.Siege : RealmPointSource.PlayerKill;
			entry = new RealmPointEntry
					{
							Timestamp = this.pendingEntry.Timestamp,
							Points = this.pendingEntry.Points,
							Source = pendingSource,
							PlayerName = pendingSource == RealmPointSource.PlayerKill ? this.pendingEntry.PlayerName : null,
							RawLine = this.pendingEntry.RawLine
					};

			this.pendingEntry = null;
			this.waitingForParticipationCheck = false;
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
			Debug.WriteLine($"[Parser] Skipped bonus: {points} RP");
			return false;
		}

		var source = DetermineSourceFromReason(reason);

		if(this.waitingForRelicCapture)
		{
			entry = new RealmPointEntry
			        {
					        Timestamp = timestamp,
					        Points = points,
					        Source = RealmPointSource.RelicCapture,
					        PlayerName = null,
					        RawLine = line
			        };
			this.waitingForRelicCapture = false;
			Debug.WriteLine($"[Parser] Relic: {points} RP");
			return true;
		}

		if(source == RealmPointSource.Unknown)
		{
			var playerNameMatch = PlayerNameRegex.Match(reason);
			var playerName = playerNameMatch.Success ? playerNameMatch.Groups["name"].Value : null;

			this.pendingEntry = new RealmPointEntry
								{
										Timestamp = timestamp,
										Points = points,
										Source = RealmPointSource.Unknown,
										PlayerName = playerName,
										RawLine = line
								};
			this.waitingForParticipationCheck = true;
			return false;
		}

		entry = new RealmPointEntry
		        {
				        Timestamp = timestamp,
				        Points = points,
				        Source = source,
				        PlayerName = null,
				        RawLine = line
		        };

		return true;
	}

	private static RealmPointSource DetermineSourceFromReason(string reason)
	{
		if(string.IsNullOrWhiteSpace(reason))
		{
			return RealmPointSource.Unknown;
		}

		if(reason.Contains("Campaign Quest"))
		{
			return RealmPointSource.CampaignQuest;
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

		return RealmPointSource.Unknown;
	}

	[GeneratedRegex(@"^\[(?<timestamp>\d{2}:\d{2}:\d{2})\] You get (?:an additional )?(?<points>\d+) realm points?(?<reason>.*)!$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex GenerateRealmPointRegex();
}
