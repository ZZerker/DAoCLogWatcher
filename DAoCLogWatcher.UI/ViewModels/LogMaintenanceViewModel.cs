using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DAoCLogWatcher.Core;
using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Services;

namespace DAoCLogWatcher.UI.ViewModels;

public sealed partial class RetentionOption: ObservableObject
{
	public required string Label { get; init; }

	public required int Months { get; init; }

	[ObservableProperty] private string costText = string.Empty;

	[ObservableProperty] private bool isSelected;
}

public sealed partial class LogMaintenanceViewModel: ObservableObject
{
	private readonly string filePath;
	private readonly bool isWatching;

	private LogFileStats? stats;
	private TrimPlan? plan;

	public LogMaintenanceViewModel(string logFilePath, bool isWatching)
	{
		this.filePath = logFilePath;
		this.isWatching = isWatching;

		this.RetentionOptions = [];
	}

	public string LogFilePath => this.filePath;

	[ObservableProperty] private string sizeText = "-";
	[ObservableProperty] private string lineCountText = "-";
	[ObservableProperty] private string sessionCountText = "-";
	[ObservableProperty] private string ageText = "-";
	[ObservableProperty] private string dateRangeText = "-";

	[ObservableProperty] private bool isAnalyzing;
	[ObservableProperty] private bool hasStats;

	public ObservableCollection<RetentionOption> RetentionOptions { get; }

	[ObservableProperty] private RetentionOption? selectedRetention;

	[ObservableProperty] private string archiveSummary = string.Empty;
	[ObservableProperty] private string archiveRangeText = string.Empty;
	[ObservableProperty] private string archiveFileText = string.Empty;
	[ObservableProperty] private string keepSummary = string.Empty;
	[ObservableProperty] private string keepRangeText = string.Empty;

	[ObservableProperty] private string? blockerMessage;
	[ObservableProperty] private bool canTrim;
	[ObservableProperty] private bool isTrimming;
	[ObservableProperty] private double trimProgress;
	[ObservableProperty] private string? resultMessage;

	[RelayCommand]
	private async Task Analyze()
	{
		this.IsAnalyzing = true;
		this.ResultMessage = null;

		try
		{
			var result = await Task.Run(() => LogSessionScanner.Analyze(this.filePath));
			this.stats = result;
			this.HasStats = true;

			this.SizeText = FormatBytes(result.ByteCount);
			this.LineCountText = FormatCount(result.LineCount);
			this.SessionCountText = result.SessionCount.ToString("N0", CultureInfo.InvariantCulture);
			this.AgeText = result.OldestSessionStart.HasValue?FormatAge(result.OldestSessionStart.Value):"-";
			this.DateRangeText = result.OldestSessionStart.HasValue&&result.NewestSessionEnd.HasValue
					?$"{result.OldestSessionStart.Value:d MMM yyyy} to {FormatEnd(result.NewestSessionEnd.Value)}"
					:"-";

			this.RebuildRetentionOptions(result.AgeBuckets);

			this.UpdatePreview();
		}
		catch(Exception ex)
		{
			AppLog.Exception("LogMaintenance.Analyze", ex);
			this.ResultMessage = "Could not read the log file. See the log for details.";
		}
		finally
		{
			this.IsAnalyzing = false;
		}
	}

	/// <summary>Rebuilds the retention list from the Core age buckets so a bucket added in <see cref="LogSessionScanner"/> shows up here automatically, preserving the current selection where possible.</summary>
	private void RebuildRetentionOptions(IReadOnlyList<LogAgeBucket> ageBuckets)
	{
		var previouslySelectedMonths = this.SelectedRetention?.Months;

		this.RetentionOptions.Clear();
		foreach(var bucket in ageBuckets)
		{
			var option = new RetentionOption { Label = bucket.Label, Months = bucket.Months };
			option.CostText = bucket.SessionCount == 0
					?"nothing to trim"
					:$"{bucket.SessionCount:N0} sessions, {FormatBytes(bucket.ByteCount)}, {FormatCount(bucket.LineCount)} lines";

			option.PropertyChanged += (_, e) =>
			{
				if(e.PropertyName == nameof(RetentionOption.IsSelected)&&option.IsSelected)
				{
					this.SelectedRetention = option;
				}
			};

			this.RetentionOptions.Add(option);
		}

		if(this.RetentionOptions.Count == 0)
		{
			this.SelectedRetention = null;
			return;
		}

		var match = previouslySelectedMonths.HasValue
				?this.RetentionOptions.FirstOrDefault(o => o.Months == previouslySelectedMonths.Value)
				:null;

		this.SelectedRetention = match ?? this.RetentionOptions[^1];
	}

	partial void OnSelectedRetentionChanged(RetentionOption? oldValue, RetentionOption? newValue)
	{
		if(oldValue != null)
		{
			oldValue.IsSelected = false;
		}

		if(newValue != null)
		{
			newValue.IsSelected = true;
		}

		this.UpdatePreview();
	}

	private void UpdatePreview()
	{
		if(this.stats == null||this.SelectedRetention == null)
		{
			this.plan = null;
			this.ArchiveSummary = string.Empty;
			this.ArchiveRangeText = string.Empty;
			this.ArchiveFileText = string.Empty;
			this.KeepSummary = string.Empty;
			this.KeepRangeText = string.Empty;
			this.BlockerMessage = null;
			this.CanTrim = false;
			return;
		}

		var cutoff = DateTime.Now.AddMonths(-this.SelectedRetention.Months);
		var newPlan = LogFileMaintenance.PlanTrim(this.stats, cutoff);
		this.plan = newPlan;

		this.ArchiveSummary = $"{newPlan.ArchivedSessionCount:N0} sessions, {FormatBytes(newPlan.ArchivedByteCount)}, {FormatCount(newPlan.ArchivedLineCount)} lines";
		this.ArchiveRangeText = newPlan.ArchivedFrom.HasValue&&newPlan.ArchivedTo.HasValue
				?$"{newPlan.ArchivedFrom.Value:d MMM yyyy} to {newPlan.ArchivedTo.Value:d MMM yyyy}"
				:"-";
		this.ArchiveFileText = $"Compressed into {LogFileMaintenance.ARCHIVE_FILE_NAME}";

		this.KeepSummary = $"{newPlan.KeptSessionCount:N0} sessions, {FormatBytes(newPlan.KeptByteCount)}, {FormatCount(newPlan.KeptLineCount)} lines";
		this.KeepRangeText = newPlan.KeptFrom.HasValue&&newPlan.KeptTo.HasValue
				?$"{newPlan.KeptFrom.Value:d MMM yyyy} to {FormatEnd(newPlan.KeptTo.Value)}"
				:"-";

		this.BlockerMessage = this.isWatching
				?"Stop watching before trimming."
				:newPlan.Blocker switch
				  {
						  TrimBlocker.NothingToTrim => "Nothing is older than this cutoff.",
						  TrimBlocker.WouldEmptyLog => "This would remove every session. Choose a shorter retention.",
						  _ => null
				  };

		this.CanTrim = !this.isWatching&&newPlan.CanExecute&&!this.IsTrimming&&!this.IsAnalyzing;
	}

	[RelayCommand]
	private async Task Trim()
	{
		if(this.plan == null||!this.CanTrim)
		{
			return;
		}

		this.IsTrimming = true;
		this.TrimProgress = 0;
		this.ResultMessage = null;
		this.CanTrim = false;

		try
		{
			var progress = new Progress<double>(p => this.TrimProgress = p);
			var currentPlan = this.plan;
			var result = await Task.Run(() => LogFileMaintenance.ExecuteTrim(currentPlan, progress));

			this.ResultMessage = result.Success
					?$"Archived {FormatBytes(result.BytesRemoved)} into {LogFileMaintenance.ARCHIVE_FILE_NAME}, now {FormatBytes(result.ArchiveSizeOnDisk)} on disk."
					:$"Trim failed: {result.ErrorMessage}";

			if(result.Success)
			{
				await this.Analyze();
			}
		}
		catch(Exception ex)
		{
			AppLog.Exception("LogMaintenance.Trim", ex);
			this.ResultMessage = "Trim failed unexpectedly. See the log for details.";
		}
		finally
		{
			this.IsTrimming = false;
			this.UpdatePreview();
		}
	}

	private static string FormatAge(DateTime oldestStart)
	{
		var span = DateTime.Now - oldestStart;
		if(span.TotalDays >= 365)
		{
			return $"{(int)(span.TotalDays / 365)} years";
		}

		if(span.TotalDays >= 30)
		{
			return $"{(int)(span.TotalDays / 30)} months";
		}

		return $"{(int)span.TotalDays} days";
	}

	private static string FormatEnd(DateTime end)
	{
		return end.Date == DateTime.Now.Date?"today":end.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
	}

	private static string FormatBytes(long bytes)
	{
		const long KB = 1024;
		const long MB = KB * 1024;
		const long GB = MB * 1024;

		if(bytes >= GB)
		{
			return $"{bytes / (double)GB:0.0} GB";
		}

		if(bytes >= MB)
		{
			return $"{bytes / (double)MB:0.0} MB";
		}

		if(bytes >= KB)
		{
			return $"{bytes / (double)KB:0.0} KB";
		}

		return $"{bytes} B";
	}

	private static string FormatCount(long count)
	{
		if(count >= 1_000_000)
		{
			return $"{count / 1_000_000.0:0.#}M";
		}

		if(count >= 10_000)
		{
			return $"{count / 1_000.0:0.#}k";
		}

		return count.ToString("N0", CultureInfo.InvariantCulture);
	}
}
