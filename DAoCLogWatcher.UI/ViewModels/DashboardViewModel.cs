using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DAoCLogWatcher.UI.Models;
using DAoCLogWatcher.UI.Services;

namespace DAoCLogWatcher.UI.ViewModels;

public sealed partial class DashboardViewModel: ObservableObject
{
	private readonly AppSettings settings;
	private readonly ISettingsService settingsService;

	public DashboardViewModel(AppSettings settings, ISettingsService settingsService)
	{
		this.settings = settings;
		this.settingsService = settingsService;
		this.InitializeDashboardWidgets();
	}

	[ObservableProperty] private bool isDashboardCustomizeVisible;

	[RelayCommand]
	private void ToggleDashboardCustomize()
	{
		this.IsDashboardCustomizeVisible = !this.IsDashboardCustomizeVisible;
	}

	public ObservableCollection<DashboardWidgetViewModel> DashboardWidgets { get; } = new();

	private static readonly (DashboardWidgetId Id, string Label, DashboardWidgetSize DefaultSize)[] DefaultWidgetDefs =
	[
			(DashboardWidgetId.TotalRp, "Total RP", DashboardWidgetSize.Small),
			(DashboardWidgetId.RpPerHour, "RP per Hour", DashboardWidgetSize.Small),
			(DashboardWidgetId.Session, "Session", DashboardWidgetSize.Small),
			(DashboardWidgetId.PlayerKills, "Player Kills", DashboardWidgetSize.Small),
			(DashboardWidgetId.KdRatio, "K/D Ratio", DashboardWidgetSize.Small),
			(DashboardWidgetId.Deaths, "Deaths", DashboardWidgetSize.Small),
			(DashboardWidgetId.BestMultiKill, "Best Multi-Kill", DashboardWidgetSize.Small),
			(DashboardWidgetId.HottestZone, "Hottest Zone", DashboardWidgetSize.Small),
			(DashboardWidgetId.RpSources, "RP Sources", DashboardWidgetSize.Medium),
			(DashboardWidgetId.DamageOutput, "Damage Output", DashboardWidgetSize.Large),
			(DashboardWidgetId.TopTargets, "Top Targets", DashboardWidgetSize.Medium),
			(DashboardWidgetId.TopSpells, "Top Spells", DashboardWidgetSize.Medium),
			(DashboardWidgetId.TopHealers, "Top Healers", DashboardWidgetSize.Medium),
			(DashboardWidgetId.DamageTaken, "Damage Taken By", DashboardWidgetSize.Medium),
			(DashboardWidgetId.HealsDone, "Heals Done To", DashboardWidgetSize.Medium),
			(DashboardWidgetId.ZoneActivity, "Zone Activity", DashboardWidgetSize.Large),
			(DashboardWidgetId.RpLog, "RP Log", DashboardWidgetSize.Medium),
			(DashboardWidgetId.HealLog, "Heal Log", DashboardWidgetSize.Medium),
			(DashboardWidgetId.CombatLog, "Combat Log", DashboardWidgetSize.Medium),
			(DashboardWidgetId.Minimap, "Zone Minimap", DashboardWidgetSize.Medium),
			(DashboardWidgetId.GlobalActivity, "Global Activity", DashboardWidgetSize.Large)
	];

	public ObservableCollection<string> DashboardProfileNames { get; } = new();

	[ObservableProperty] private string? selectedDashboardProfile;
	[ObservableProperty] private bool isAddingDashboardProfile;
	[ObservableProperty] private string newDashboardProfileName = string.Empty;

	private bool suppressProfileLoad;

	private void InitializeDashboardWidgets()
	{
		this.RebuildDashboardWidgets(this.settings.DashboardWidgets);

		foreach(var name in this.settings.DashboardProfiles.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
		{
			this.DashboardProfileNames.Add(name);
		}

		this.suppressProfileLoad = true;
		this.SelectedDashboardProfile = this.settings.ActiveDashboardProfile is { } active&&this.settings.DashboardProfiles.ContainsKey(active)?active:null;
		this.suppressProfileLoad = false;
	}

	private void RebuildDashboardWidgets(IReadOnlyList<DashboardWidgetConfig> configs)
	{
		var allDefaults = DefaultWidgetDefs.ToDictionary(t => t.Id);
		var savedById = new Dictionary<DashboardWidgetId, DashboardWidgetConfig>();
		foreach(var c in configs)
		{
			if(allDefaults.ContainsKey(c.Id))
			{
				savedById[c.Id] = c;
			}
		}

		var orderedIds = configs.Select(c => c.Id).Where(allDefaults.ContainsKey).Concat(allDefaults.Keys.Except(savedById.Keys));

		this.DashboardWidgets.Clear();
		foreach(var id in orderedIds)
		{
			var (_, label, defaultSize) = allDefaults[id];
			var config = savedById.TryGetValue(id, out var c)?c:new DashboardWidgetConfig(id, true, defaultSize);
			this.DashboardWidgets.Add(new DashboardWidgetViewModel(id, label, config.IsVisible, config.Size, this.MoveDashboardWidgetUp, this.MoveDashboardWidgetDown, this.OnDashboardWidgetChanged));
		}
	}

	partial void OnSelectedDashboardProfileChanged(string? value)
	{
		this.SaveDashboardProfileCommand.NotifyCanExecuteChanged();
		this.DeleteDashboardProfileCommand.NotifyCanExecuteChanged();

		if(this.suppressProfileLoad)
		{
			return;
		}

		if(value != null&&this.settings.DashboardProfiles.TryGetValue(value, out var configs))
		{
			this.RebuildDashboardWidgets(configs);
			this.settings.DashboardWidgets = this.SnapshotCurrentWidgets();
		}

		this.settings.ActiveDashboardProfile = value;
		this.settingsService.Save(this.settings);
	}

	[RelayCommand(CanExecute = nameof(CanSaveDashboardProfile))]
	private void SaveDashboardProfile()
	{
		if(this.SelectedDashboardProfile is not { } name)
		{
			return;
		}

		this.settings.DashboardProfiles[name] = this.SnapshotCurrentWidgets();
		this.settingsService.Save(this.settings);
	}

	private bool CanSaveDashboardProfile()
	{
		return this.SelectedDashboardProfile != null;
	}

	[RelayCommand]
	private void BeginSaveAsDashboardProfile()
	{
		this.NewDashboardProfileName = string.Empty;
		this.IsAddingDashboardProfile = true;
	}

	[RelayCommand]
	private void CancelSaveAsDashboardProfile()
	{
		this.IsAddingDashboardProfile = false;
		this.NewDashboardProfileName = string.Empty;
	}

	[RelayCommand(CanExecute = nameof(CanConfirmSaveAsDashboardProfile))]
	private void ConfirmSaveAsDashboardProfile()
	{
		var name = this.NewDashboardProfileName.Trim();
		if(string.IsNullOrEmpty(name))
		{
			return;
		}

		var isNew = !this.settings.DashboardProfiles.ContainsKey(name);
		this.settings.DashboardProfiles[name] = this.SnapshotCurrentWidgets();
		this.settings.ActiveDashboardProfile = name;
		this.settingsService.Save(this.settings);

		if(isNew)
		{
			var insertAt = 0;
			while(insertAt < this.DashboardProfileNames.Count&&StringComparer.OrdinalIgnoreCase.Compare(this.DashboardProfileNames[insertAt], name) < 0)
			{
				insertAt++;
			}

			this.DashboardProfileNames.Insert(insertAt, name);
		}

		this.suppressProfileLoad = true;
		this.SelectedDashboardProfile = name;
		this.suppressProfileLoad = false;

		this.IsAddingDashboardProfile = false;
		this.NewDashboardProfileName = string.Empty;
	}

	private bool CanConfirmSaveAsDashboardProfile()
	{
		return !string.IsNullOrWhiteSpace(this.NewDashboardProfileName);
	}

	partial void OnNewDashboardProfileNameChanged(string value)
	{
		this.ConfirmSaveAsDashboardProfileCommand.NotifyCanExecuteChanged();
	}

	[RelayCommand(CanExecute = nameof(CanDeleteDashboardProfile))]
	private void DeleteDashboardProfile()
	{
		if(this.SelectedDashboardProfile is not { } name)
		{
			return;
		}

		this.settings.DashboardProfiles.Remove(name);
		this.DashboardProfileNames.Remove(name);
		this.settings.ActiveDashboardProfile = null;
		this.settingsService.Save(this.settings);

		this.suppressProfileLoad = true;
		this.SelectedDashboardProfile = null;
		this.suppressProfileLoad = false;
	}

	private bool CanDeleteDashboardProfile()
	{
		return this.SelectedDashboardProfile != null;
	}

	private List<DashboardWidgetConfig> SnapshotCurrentWidgets()
	{
		return this.DashboardWidgets.Select(w => new DashboardWidgetConfig(w.Id, w.IsVisible, w.Size)).ToList();
	}

	private void MoveDashboardWidgetUp(DashboardWidgetViewModel widget)
	{
		var idx = this.DashboardWidgets.IndexOf(widget);
		if(idx <= 0)
		{
			return;
		}

		this.DashboardWidgets.Move(idx, idx - 1);
		this.SaveDashboardWidgets();
	}

	private void MoveDashboardWidgetDown(DashboardWidgetViewModel widget)
	{
		var idx = this.DashboardWidgets.IndexOf(widget);
		if(idx < 0||idx >= this.DashboardWidgets.Count - 1)
		{
			return;
		}

		this.DashboardWidgets.Move(idx, idx + 1);
		this.SaveDashboardWidgets();
	}

	// Move a widget to an absolute index within DashboardWidgets, then persist.
	// Used by drag-to-reorder; the buttons go through MoveDashboardWidgetUp/Down.
	public void MoveDashboardWidget(DashboardWidgetViewModel widget, int targetIndex)
	{
		var from = this.DashboardWidgets.IndexOf(widget);
		if(from < 0)
		{
			return;
		}

		targetIndex = Math.Clamp(targetIndex, 0, this.DashboardWidgets.Count - 1);
		if(from == targetIndex)
		{
			return;
		}

		this.DashboardWidgets.Move(from, targetIndex);
		this.SaveDashboardWidgets();
	}

	// Commit a size chosen by border-drag resize (snap already applied in the view).
	// Rides the existing OnSizeChanged -> OnDashboardWidgetChanged -> SaveDashboardWidgets chain.
	public void SetDashboardWidgetSize(DashboardWidgetViewModel widget, DashboardWidgetSize size)
	{
		if(widget.Size == size)
		{
			return;
		}

		widget.Size = size;
	}

	private void OnDashboardWidgetChanged(DashboardWidgetViewModel widget)
	{
		this.SaveDashboardWidgets();
	}

	private void SaveDashboardWidgets()
	{
		this.settings.DashboardWidgets = this.SnapshotCurrentWidgets();
		this.settingsService.Save(this.settings);
	}
}
