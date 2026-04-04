using System;
using System.Collections.Generic;

namespace DAoCLogWatcher.UI.Models;

public sealed class RpsChartData
{
	// Dampens the early spike without distorting values for most of the warm-up period
	private const double MIN_ROLLING_WINDOW_HOURS = 10.0 / 60.0;

	private readonly Queue<(DateTime Time, int Points)> rawEntries = new();
	private int rollingWindowRps;
	private DateTime? startTime;

	public List<(DateTime Time, double Rps)> CumulativeDataPoints { get; } = new();

	public List<(DateTime Time, double RpsPerHour)> HourlyDataPoints { get; } = new();

	public event EventHandler? UpdateRequested;

	public void Add(DateTime entryTime, int cumulativeRps, int points)
	{
		this.startTime ??= entryTime;

		this.CumulativeDataPoints.Add((entryTime, cumulativeRps));

		this.rawEntries.Enqueue((entryTime, points));
		this.rollingWindowRps += points;

		var windowStart = entryTime.AddHours(-1);
		while(this.rawEntries.TryPeek(out var oldest)&&oldest.Time < windowStart)
		{
			this.rawEntries.Dequeue();
			this.rollingWindowRps -= oldest.Points;
		}

		var windowEffectiveStart = this.startTime.Value > windowStart?this.startTime.Value:windowStart;
		var actualWindowHours = Math.Max((entryTime - windowEffectiveStart).TotalHours, MIN_ROLLING_WINDOW_HOURS);

		this.HourlyDataPoints.Add((entryTime, this.rollingWindowRps / actualWindowHours));

		this.UpdateRequested?.Invoke(this, EventArgs.Empty);
	}

	public void Reset()
	{
		this.CumulativeDataPoints.Clear();
		this.HourlyDataPoints.Clear();
		this.rawEntries.Clear();
		this.rollingWindowRps = 0;
		this.startTime = null;

		this.UpdateRequested?.Invoke(this, EventArgs.Empty);
	}
}
