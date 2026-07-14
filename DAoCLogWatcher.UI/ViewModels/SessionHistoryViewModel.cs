using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using DAoCLogWatcher.UI.Models;

namespace DAoCLogWatcher.UI.ViewModels;

public sealed partial class SessionHistoryViewModel: ViewModelBase
{
	private const string ALL_CHARACTERS_FILTER = "All";

	private readonly List<SessionRecord> allRecords;

	[ObservableProperty] private string selectedCharacterFilter = ALL_CHARACTERS_FILTER;

	public ObservableCollection<string> CharacterFilters { get; } = [];

	public ObservableCollection<SessionHistoryRowViewModel> Sessions { get; } = [];

	[ObservableProperty] private long totalRealmPoints;
	[ObservableProperty] private long bestSessionRealmPoints;
	[ObservableProperty] private double bestRpPerHour;
	[ObservableProperty] private string averageDurationText = "--";

	public SessionHistoryViewModel(IReadOnlyList<SessionRecord> records)
	{
		this.allRecords = records.OrderByDescending(r => r.StartTime).ToList();

		this.CharacterFilters.Add(ALL_CHARACTERS_FILTER);
		foreach(var name in this.allRecords.Select(r => r.CharacterName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderBy(n => n))
		{
			this.CharacterFilters.Add(name!);
		}

		this.ApplyFilter();
	}

	partial void OnSelectedCharacterFilterChanged(string value)
	{
		this.ApplyFilter();
	}

	private void ApplyFilter()
	{
		var filtered = this.SelectedCharacterFilter == ALL_CHARACTERS_FILTER
				? this.allRecords
				: this.allRecords.Where(r => r.CharacterName == this.SelectedCharacterFilter).ToList();

		this.Sessions.Clear();
		foreach(var record in filtered)
		{
			this.Sessions.Add(new SessionHistoryRowViewModel(record));
		}

		this.TotalRealmPoints = filtered.Sum(r => r.RealmPoints);
		this.BestSessionRealmPoints = filtered.Count == 0?0:filtered.Max(r => r.RealmPoints);
		this.BestRpPerHour = filtered.Count == 0?0:filtered.Max(r => r.RpPerHour);
		this.AverageDurationText = ComputeAverageDurationText(filtered);
	}

	private static string ComputeAverageDurationText(List<SessionRecord> records)
	{
		var timed = records.Where(r => r.Duration > TimeSpan.Zero).ToList();
		if(timed.Count == 0)
		{
			return "--";
		}

		var averageTicks = (long)timed.Average(r => r.Duration.Ticks);
		return DurationFormat.Short(TimeSpan.FromTicks(averageTicks));
	}
}

public sealed class SessionHistoryRowViewModel(SessionRecord record)
{
	// The periodic flush runs every 60s, so a record with no EndTime that is older than this was
	// abandoned (app killed) rather than left running.
	private static readonly TimeSpan LiveThreshold = TimeSpan.FromMinutes(3);

	public string DateText => record.StartTime.ToString("yyyy-MM-dd HH:mm");

	public string? CharacterName => record.CharacterName;

	public bool IsLive => !record.EndTime.HasValue&&DateTime.Now - record.LastUpdated < LiveThreshold;

	public string DurationText => DurationFormat.Short(record.Duration);

	public long RealmPoints => record.RealmPoints;

	public string RpPerHourText => record.RpPerHour.ToString("N0");

	public int Kills => record.Kills;

	public int Deaths => record.Deaths;

	public int BestMultiKill => record.BestMultiKill;

	public string? TopZone => record.TopZone;
}
