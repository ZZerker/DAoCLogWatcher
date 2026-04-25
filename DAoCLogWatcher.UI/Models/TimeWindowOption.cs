using System;

namespace DAoCLogWatcher.UI.Models;

public sealed class TimeWindowOption
{
	public required string Label { get; init; }

	public required TimeSpan Value { get; init; }
}
