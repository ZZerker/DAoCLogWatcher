namespace DAoCLogWatcher.Core.Models;

public sealed record SpellInfo(string Name, int DurationSeconds, int FrequencySeconds, bool IsAoe, bool IsAoeNuke = false);
