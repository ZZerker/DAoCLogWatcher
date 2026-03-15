namespace DAoCLogWatcher.Core.Models;

/// <summary>Base record for all parsed log lines. Use pattern matching on subtypes — do not null-check properties.</summary>
public abstract record LogLine(string Text)
{
	/// <summary>Set when the character name was detected from this line (e.g. a /stats header).</summary>
	public string? DetectedCharacterName { get; init; }
}

/// <summary>A line that did not match any known event pattern.</summary>
public sealed record UnknownLogLine(string Text) : LogLine(Text);

/// <summary>A line that yielded a realm point entry.</summary>
public sealed record RealmPointLogLine(string Text, RealmPointEntry Entry) : LogLine(Text);

/// <summary>A line (or multi-line sequence) that yielded a kill event (X was just killed by Y in Z).</summary>
public sealed record KillLogLine(string Text, KillEvent Event) : LogLine(Text);

/// <summary>A line (or multi-line sequence) that yielded a damage event.</summary>
public sealed record DamageLogLine(string Text, DamageEvent Event) : LogLine(Text);

/// <summary>A line that yielded a heal event.</summary>
public sealed record HealLogLine(string Text, HealEvent Event) : LogLine(Text);

/// <summary>A line that yielded a miss/block/resist event (outgoing attack failed).</summary>
public sealed record MissLogLine(string Text, MissEvent Event) : LogLine(Text);
