using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DAoCLogWatcher.UI.Services;

// 0=Current session  1=1 week  2=48h  3=24h  4=12h  5=6h  6=3h  7=2h  8=1h  9=Custom
// NOTE: index 0 ("Current session") is handled by MainWindowViewModel via session-boundary
// scanning, NOT here. GetFilterParameters() returning (false, 0) for index 0 is only used as the
// fall-through when no session can be resolved.
public partial class TimeFilterService: ObservableObject
{
	private const int CUSTOM_FILTER_INDEX = 9;

	[ObservableProperty] private int selectedTimeFilterIndex = 8;
	[ObservableProperty] private bool isCustomPopupVisible;
	[ObservableProperty] private decimal? customInputHours = 1;
	[ObservableProperty] private decimal? customInputMinutes = 0;

	private int previousFilterIndex = 3;
	private double customTotalHours = 1;
	private bool suppressFilterChange;

	/// <summary>Raised when the active filter changes and the watcher should restart.</summary>
	public event EventHandler? FilterChanged;

	/// <summary>Returns the current filter parameters to pass to <see cref="Core.LogWatcher"/>.</summary>
	public (bool Enabled, double Hours) GetFilterParameters()
	{
		if(this.SelectedTimeFilterIndex == 0)
		{
			return (false, 0);
		}

		var hours = this.SelectedTimeFilterIndex switch
		{
				1 => 168.0, // 1 week
				2 => 48.0,
				3 => 24.0,
				4 => 12.0,
				5 => 6.0,
				6 => 3.0,
				7 => 2.0,
				8 => 1.0,
				CUSTOM_FILTER_INDEX => this.customTotalHours,
				_ => 24.0
		};
		return (true, hours);
	}

	partial void OnSelectedTimeFilterIndexChanged(int value)
	{
		if(this.suppressFilterChange)
		{
			return;
		}

		if(value == CUSTOM_FILTER_INDEX)
		{
			this.IsCustomPopupVisible = true;
			return; // wait for Apply before firing FilterChanged
		}

		this.previousFilterIndex = value;
		this.IsCustomPopupVisible = false;
		this.FilterChanged?.Invoke(this, EventArgs.Empty);
	}

	[RelayCommand]
	private void ApplyCustomFilter()
	{
		this.customTotalHours = (double)(this.CustomInputHours ?? 0) + (double)(this.CustomInputMinutes ?? 0) / 60.0;
		if(this.customTotalHours <= 0)
		{
			this.customTotalHours = 1.0 / 60; // minimum 1 min
		}

		this.previousFilterIndex = CUSTOM_FILTER_INDEX;
		this.IsCustomPopupVisible = false;
		this.FilterChanged?.Invoke(this, EventArgs.Empty);
	}

	[RelayCommand]
	private void CancelCustomFilter()
	{
		this.IsCustomPopupVisible = false;
		this.suppressFilterChange = true;
		this.SelectedTimeFilterIndex = this.previousFilterIndex;
		this.suppressFilterChange = false;
	}

	/// <summary>Updates the dropdown selection without firing FilterChanged (used when a watcher
	/// is already being set up by a different code path, e.g. session-based open).</summary>
	public void SetIndexSilent(int index)
	{
		this.suppressFilterChange = true;
		this.SelectedTimeFilterIndex = index;
		this.previousFilterIndex = index;
		this.suppressFilterChange = false;
	}
}
