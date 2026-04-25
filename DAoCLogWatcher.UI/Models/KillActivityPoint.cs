using System;

namespace DAoCLogWatcher.UI.Models;

public sealed class KillActivityPoint
{
	public required DateTime Time { get; init; }

	public int KillCount { get; init; }
}
