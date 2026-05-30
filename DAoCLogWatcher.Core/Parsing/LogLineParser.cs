using System.Globalization;
using System.Text.RegularExpressions;
using DAoCLogWatcher.Core.Models;

namespace DAoCLogWatcher.Core.Parsing;

internal sealed partial class LogLineParser
{
	private const int KILL_RP_CORRELATION_WINDOW_SECONDS = 30;

	private static readonly Regex KillLineRegex = GenerateKillLineRegex();
	private static readonly Regex SendLineRegex = GenerateSendLineRegex();
	private static readonly Regex RegionLineRegex = GenerateRegionLineRegex();

	private readonly RealmPointParser rpParser = new();
	private readonly CombatParser combatParser = new();
	private KillEvent? lastKillEvent;

	internal readonly record struct ParseResult(RealmPointEntry? Entry, DamageEvent? DamageEvent, HealEvent? HealEvent, MissEvent? MissEvent, KillEvent? KillEvent, SendEvent? SendEvent, RegionEvent? RegionEvent);

	internal ParseResult Parse(string line, string? currentCharacterName)
	{
		this.rpParser.TryParse(line, out var entry);
		this.combatParser.TryParse(line, out var damageEvent, out var healEvent, out var missEvent);

		var killEvent = TryDetectKillEvent(line);
		if(killEvent != null)
		{
			this.lastKillEvent = killEvent;
		}

		var sendEvent = TryDetectSendEvent(line);
		var regionEvent = TryDetectRegionEvent(line);
		return new ParseResult(CorrelateKillWithRp(entry, this.lastKillEvent, currentCharacterName), damageEvent, healEvent, missEvent, killEvent, sendEvent, regionEvent);
	}

	internal void Reset()
	{
		this.rpParser.Reset();
		this.combatParser.Reset();
		this.lastKillEvent = null;
	}

	private static RealmPointEntry? CorrelateKillWithRp(RealmPointEntry? entry, KillEvent? lastKillEvent, string? characterName)
	{
		if(entry == null||entry.Source != RealmPointSource.PlayerKill||lastKillEvent == null||lastKillEvent.IsNpc)
		{
			return entry;
		}

		var diffSeconds = TimeHelper.ShortestArcSeconds(entry.Timestamp, lastKillEvent.Timestamp);
		if(diffSeconds <= KILL_RP_CORRELATION_WINDOW_SECONDS)
		{
			return entry with
			       {
					       Victim = lastKillEvent.Victim,
					       IsDeathblow = characterName != null&&string.Equals(lastKillEvent.Killer, characterName, StringComparison.OrdinalIgnoreCase)
			       };
		}

		return entry;
	}

	private static KillEvent? TryDetectKillEvent(string line)
	{
		if(!line.Contains(" was just killed"))
		{
			return null;
		}

		var match = KillLineRegex.Match(line);
		if(!match.Success||!TryParseTimestamp(match, out var timestamp))
		{
			return null;
		}

		var victim = match.Groups["victim"].Value;
		return new KillEvent
		       {
				       Timestamp = timestamp,
				       Victim = victim,
				       Killer = match.Groups["killer"].Value,
				       Zone = match.Groups["zone"].Value,
				       IsNpc = NpcFilter.IsNpc(victim)
		       };
	}

	private static SendEvent? TryDetectSendEvent(string line)
	{
		if(!line.Contains("] @@"))
		{
			return null;
		}

		var match = SendLineRegex.Match(line);
		if(!match.Success||!TryParseTimestamp(match, out var timestamp))
		{
			return null;
		}

		return new SendEvent
		       {
				       Timestamp = timestamp,
				       Sender = match.Groups["sender"].Value,
				       Message = match.Groups["message"].Value
		       };
	}

	private static bool TryParseTimestamp(Match match, out TimeOnly timestamp)
	{
		return TimeOnly.TryParseExact(match.Groups["timestamp"].Value, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp);
	}

	[GeneratedRegex(@"^\[(?<timestamp>\d{2}:\d{2}:\d{2})\] (?<victim>.+?) was just killed by (?<killer>\w+) in (?<zone>.+)\.$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex GenerateKillLineRegex();

	[GeneratedRegex(@"^\[(?<timestamp>\d{2}:\d{2}:\d{2})\] @@(?<sender>\w+) sends, ""(?<message>.+)""$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex GenerateSendLineRegex();

	private static RegionEvent? TryDetectRegionEvent(string line)
	{
		if(!line.Contains("(Region) You have entered "))
		{
			return null;
		}

		var match = RegionLineRegex.Match(line);
		if(!match.Success||!TryParseTimestamp(match, out var timestamp))
		{
			return null;
		}

		return new RegionEvent(timestamp, match.Groups["location"].Value);
	}

	[GeneratedRegex(@"^\[(?<timestamp>\d{2}:\d{2}:\d{2})\] \(Region\) You have entered (?<location>.+)\.$", RegexOptions.Compiled|RegexOptions.CultureInvariant)]
	private static partial Regex GenerateRegionLineRegex();
}
