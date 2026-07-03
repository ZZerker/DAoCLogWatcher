using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using DAoCLogWatcher.UI.Helpers;
using DAoCLogWatcher.UI.Models;
using DAoCLogWatcher.UI.Services;

namespace DAoCLogWatcher.UI.ViewModels;

/// <summary>Owns the per-zone kill summaries (heatmap/table ranking) and the session kill-activity
/// chart data. <paramref name="getSessionStartTime"/> reads the current <c>RealmPointSummary.SessionStartTime</c>
/// so this VM does not need to depend on the summary model directly.</summary>
public sealed partial class ZoneActivityViewModel: ObservableObject, IDisposable
{
	private readonly IRealmPointProcessor processor;
	private readonly AppSettings settings;
	private readonly ISettingsService settingsService;
	private readonly Func<DateTime?> getSessionStartTime;

	public IReadOnlyList<TimeWindowOption> ZoneKillWindowOptions { get; } = new[]
	                                                                        {
			                                                                        new TimeWindowOption
			                                                                        {
					                                                                        Label = "2 min",
					                                                                        Value = TimeSpan.FromMinutes(2)
			                                                                        },
			                                                                        new TimeWindowOption
			                                                                        {
					                                                                        Label = "5 min",
					                                                                        Value = TimeSpan.FromMinutes(5)
			                                                                        },
			                                                                        new TimeWindowOption
			                                                                        {
					                                                                        Label = "10 min",
					                                                                        Value = TimeSpan.FromMinutes(10)
			                                                                        },
			                                                                        new TimeWindowOption
			                                                                        {
					                                                                        Label = "20 min",
					                                                                        Value = TimeSpan.FromMinutes(20)
			                                                                        },
			                                                                        new TimeWindowOption
			                                                                        {
					                                                                        Label = "30 min",
					                                                                        Value = TimeSpan.FromMinutes(30)
			                                                                        },
			                                                                        new TimeWindowOption
			                                                                        {
					                                                                        Label = "60 min",
					                                                                        Value = TimeSpan.FromMinutes(60)
			                                                                        }
	                                                                        };

	public ObservableCollection<ZoneKillSummary> ZoneKills { get; } = new();

	[ObservableProperty] private TimeWindowOption selectedZoneKillWindow = null!;

	[ObservableProperty] private int recentZoneKillCount;

	[ObservableProperty] private ZoneKillSummary? hottestZone;

	public ObservableCollection<KillActivityPoint> KillActivityPoints { get; } = new();

	public event EventHandler? KillActivityUpdated;

	public ZoneActivityViewModel(IRealmPointProcessor processor, AppSettings settings, ISettingsService settingsService, Func<DateTime?> getSessionStartTime)
	{
		this.processor = processor;
		this.settings = settings;
		this.settingsService = settingsService;
		this.getSessionStartTime = getSessionStartTime;
		this.SelectedZoneKillWindow = this.ZoneKillWindowOptions.FirstOrDefault(option => option.Value.TotalMinutes == this.settings.ZoneKillWindowMinutes) ?? this.ZoneKillWindowOptions[1];
		this.processor.ZoneKillsUpdated += this.OnZoneKillsUpdated;
	}

	partial void OnSelectedZoneKillWindowChanged(TimeWindowOption value)
	{
		this.settings.ZoneKillWindowMinutes = (int)value.Value.TotalMinutes;
		this.settingsService.Save(this.settings);
		this.processor.SetZoneKillWindow(value.Value);
		this.UpdateZoneKills();
		this.UpdateKillActivity();
	}

	public IReadOnlyList<KillActivityPoint> GetSessionKillActivityPoints()
	{
		var sessionStart = this.getSessionStartTime() ?? DateTime.Now;
		return this.processor.GetSessionActivityPoints(sessionStart, DateTime.Now);
	}

	private void OnZoneKillsUpdated(object? sender, EventArgs e)
	{
		this.UpdateZoneKills();
	}

	public void UpdateZoneKills()
	{
		var zoneCounts = this.processor.CurrentZoneKills.Where(kv => kv.Value > 1).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Take(20).ToList();

		var totalCount = zoneCounts.Sum(kv => kv.Value);
		var maxCount = zoneCounts.Count > 0?zoneCounts.Max(kv => kv.Value):1;

		this.ZoneKills.Clear();
		foreach(var kv in zoneCounts)
		{
			var heatRatio = maxCount == 0?0.0:(double)kv.Value / maxCount;
			this.ZoneKills.Add(new ZoneKillSummary
			                   {
					                   Zone = kv.Key,
					                   KillCount = kv.Value,
					                   Percentage = totalCount == 0?0.0:(double)kv.Value / totalCount * 100.0,
					                   HeatColor = ColorUtil.GetZoneHeatColor(heatRatio)
			                   });
		}

		this.RecentZoneKillCount = totalCount;
		this.HottestZone = this.ZoneKills.Count > 0?this.ZoneKills[0]:null;
		this.UpdateKillActivity();
	}

	public void UpdateKillActivity()
	{
		this.KillActivityPoints.Clear();
		foreach(var point in this.processor.KillActivityPoints)
		{
			this.KillActivityPoints.Add(point);
		}

		this.KillActivityUpdated?.Invoke(this, EventArgs.Empty);
	}

	public void Reset()
	{
		this.ZoneKills.Clear();
		this.HottestZone = null;
		this.KillActivityPoints.Clear();
		this.RecentZoneKillCount = 0;
	}

	public void Dispose()
	{
		this.processor.ZoneKillsUpdated -= this.OnZoneKillsUpdated;
		GC.SuppressFinalize(this);
	}
}
