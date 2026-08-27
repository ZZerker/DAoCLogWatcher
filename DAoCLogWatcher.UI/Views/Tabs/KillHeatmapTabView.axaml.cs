using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using DAoCLogWatcher.UI.Models;
using DAoCLogWatcher.UI.Services;
using DAoCLogWatcher.UI.ViewModels;
using DAoCLogWatcher.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using ScottPlot;

namespace DAoCLogWatcher.UI.Views.Tabs;

public partial class KillHeatmapTabView: UserControl
{
	private MainWindowViewModel? vm;
	private ZoneMapService? zoneMapService;
	private WarmapWebSocketService? warmapService;
	private AppSettings? appSettings;
	private ISettingsService? settingsService;
	private readonly DispatcherTimer renderTimer;
	private bool isDirty;
	private bool suppressToggleSave;

	public KillHeatmapTabView()
	{
		this.InitializeComponent();

		this.renderTimer = new DispatcherTimer
		                   {
				                   Interval = TimeSpan.FromMilliseconds(1000.0 / 30)
		                   };
		this.renderTimer.Tick += (_, _) =>
		                         {
			                         if(!this.isDirty)
			                         {
				                         return;
			                         }

			                         this.isDirty = false;
			                         this.RenderHeatmap();
		                         };
		this.renderTimer.Start();

		this.PropertyChanged += (_, e) =>
		                        {
			                        if(e.Property != IsVisibleProperty)
			                        {
				                        return;
			                        }

			                        if(this.IsVisible)
			                        {
				                        this.renderTimer.Start();
			                        }
			                        else
			                        {
				                        this.renderTimer.Stop();
			                        }
		                        };

		this.DataContextChanged += (_, _) =>
		                           {
			                           if(this.vm != null)
			                           {
				                           this.vm.ZoneActivity.KillActivityUpdated -= this.OnKillActivityUpdated;
				                           this.vm.SettingsPopup.PropertyChanged -= this.OnViewModelPropertyChanged;
				                           this.vm = null;
			                           }

			                           if(this.DataContext is not MainWindowViewModel newVm)
			                           {
				                           return;
			                           }

			                           this.vm = newVm;
			                           var app = (App)Application.Current!;
			                           this.zoneMapService ??= app.Services.GetRequiredService<ZoneMapService>();
			                           this.appSettings ??= app.Services.GetRequiredService<AppSettings>();
			                           this.settingsService ??= app.Services.GetRequiredService<ISettingsService>();
			                           if(this.warmapService == null)
			                           {
				                           this.warmapService = app.Services.GetRequiredService<WarmapWebSocketService>();
				                           this.warmapService.KeepsUpdated += this.OnKeepsUpdated;
				                           this.warmapService.FightsUpdated += this.OnOverlayUpdated;
				                           this.warmapService.EventsUpdated += this.OnOverlayUpdated;
				                           this.warmapService.RelicsUpdated += this.OnOverlayUpdated;
			                           }

			                           this.InitializeToggleStates();
			                           this.InitKillHeatmapChart();
			                           ChartHelper.ApplyTheme(newVm.SettingsPopup.IsDarkTheme, this.KillHeatmapPlot);
			                           newVm.ZoneActivity.KillActivityUpdated += this.OnKillActivityUpdated;
			                           newVm.SettingsPopup.PropertyChanged += this.OnViewModelPropertyChanged;
		                           };

		this.ShowHeatmapToggle.IsCheckedChanged += this.OnLayerToggleChanged;
		this.ShowFightsToggle.IsCheckedChanged += this.OnLayerToggleChanged;
		this.ShowGroupsToggle.IsCheckedChanged += this.OnLayerToggleChanged;
		this.ShowEventsToggle.IsCheckedChanged += this.OnLayerToggleChanged;
		this.ShowRelicsToggle.IsCheckedChanged += this.OnLayerToggleChanged;

		this.KillHeatmapPlot.PointerMoved += this.OnHeatmapPointerMoved;
		this.KillHeatmapPlot.PointerExited += (_, _) => this.BurnTooltip.IsVisible = false;
	}

	private void InitializeToggleStates()
	{
		if(this.appSettings == null)
		{
			return;
		}

		this.suppressToggleSave = true;
		try
		{
			this.ShowHeatmapToggle.IsChecked = this.appSettings.WarmapShowHeatmap;
			this.ShowFightsToggle.IsChecked = this.appSettings.WarmapShowFights;
			this.ShowGroupsToggle.IsChecked = this.appSettings.WarmapShowGroups;
			this.ShowEventsToggle.IsChecked = this.appSettings.WarmapShowEvents;
			this.ShowRelicsToggle.IsChecked = this.appSettings.WarmapShowRelics;
		}
		finally
		{
			this.suppressToggleSave = false;
		}
	}

	private void OnLayerToggleChanged(object? sender, EventArgs e)
	{
		if(this.suppressToggleSave)
		{
			return;
		}

		if(this.appSettings != null&&this.settingsService != null)
		{
			this.appSettings.WarmapShowHeatmap = this.ShowHeatmapToggle.IsChecked == true;
			this.appSettings.WarmapShowFights = this.ShowFightsToggle.IsChecked == true;
			this.appSettings.WarmapShowGroups = this.ShowGroupsToggle.IsChecked == true;
			this.appSettings.WarmapShowEvents = this.ShowEventsToggle.IsChecked == true;
			this.appSettings.WarmapShowRelics = this.ShowRelicsToggle.IsChecked == true;
			this.settingsService.Save(this.appSettings);
		}

		this.isDirty = true;
	}

	private void InitKillHeatmapChart()
	{
		if(this.vm == null||this.zoneMapService == null)
		{
			return;
		}

		this.zoneMapService.InitializePlot(this.KillHeatmapPlot.Plot, this.vm.FrontierMap);
		ChartHelper.HideAxes(this.KillHeatmapPlot.Plot);
		this.KillHeatmapPlot.Refresh();
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if(e.PropertyName == nameof(SettingsPopupViewModel.IsDarkTheme)&&this.vm != null)
		{
			ChartHelper.ApplyTheme(this.vm.SettingsPopup.IsDarkTheme, this.KillHeatmapPlot);
			this.isDirty = true;
		}
	}

	private void OnKillActivityUpdated(object? sender, EventArgs e)
	{
		this.isDirty = true;
	}

	private void OnKeepsUpdated(object? sender, EventArgs e)
	{
		this.isDirty = true;
	}

	private void OnOverlayUpdated(object? sender, EventArgs e)
	{
		this.isDirty = true;
	}

	private void RenderHeatmap()
	{
		if(this.vm == null||this.zoneMapService == null)
		{
			return;
		}

		var showFights = this.appSettings?.WarmapShowFights ?? true;
		var showEvents = this.appSettings?.WarmapShowEvents ?? true;
		var showHeatmap = this.appSettings?.WarmapShowHeatmap ?? true;
		var showGroups = this.appSettings?.WarmapShowGroups ?? true;
		var showRelics = this.appSettings?.WarmapShowRelics ?? true;

		var liveKeeps = this.warmapService?.GetSnapshot();
		var fights = showFights?this.warmapService?.GetFightsSnapshot():null;
		var groups = showGroups?this.warmapService?.GetGroupsSnapshot():null;
		var relics = showRelics?this.warmapService?.GetRelicPlacements():null;

		this.UpdateRelicPanel(relics, showRelics);

		lock(this.KillHeatmapPlot.Plot.Sync)
		{
			this.zoneMapService.ApplyHeatmapOverlay(this.KillHeatmapPlot.Plot, this.vm.FrontierMap, this.vm.CurrentZoneKills, liveKeeps, fights, groups, showFights, this.warmapService?.GetEventsSnapshot(), showEvents, showHeatmap, relics, showRelics);
		}

		this.KillHeatmapPlot.Refresh();
	}

	// Eden's own relic ids, in the 2x3 layout the warmap uses: Strength on top, Power below,
	// Albion / Hibernia / Midgard across. The tiles are the x:Name fields rather than a
	// FindControl lookup, so a renamed control breaks the build instead of silently doing nothing.
	private (Border Frame, Border Tint, int RelicId)[] RelicTiles()
	{
		return
		[
				(this.RelicAlbStr, this.RelicAlbStrTint, 2),
				(this.RelicHibStr, this.RelicHibStrTint, 6),
				(this.RelicMidStr, this.RelicMidStrTint, 4),
				(this.RelicAlbMagic, this.RelicAlbMagicTint, 1),
				(this.RelicHibMagic, this.RelicHibMagicTint, 5),
				(this.RelicMidMagic, this.RelicMidMagicTint, 3)
		];
	}

	private void UpdateRelicPanel(IReadOnlyList<WarmapRelicPlacement>? relics, bool showRelics)
	{
		this.RelicPanel.IsVisible = showRelics;

		if(!showRelics)
		{
			return;
		}

		foreach(var (frame, tint, relicId) in this.RelicTiles())
		{
			var placement = relics?.FirstOrDefault(r => r.RelicId == relicId);
			var owner = placement?.OwnerRealm ?? 0;

			// The frame alone is too thin to read at 48px, so the owning realm is also washed
			// over the icon itself -- the tint border sits on top of the image, not behind it.
			frame.BorderBrush = RealmBrush(owner, false);
			tint.Background = RealmBrush(owner, true);

			ToolTip.SetTip(frame, BuildRelicTileTooltip(placement, relicId));
		}
	}

	// Realm colours are identity, not theme: the same values live in App.axaml as AppRealm*
	// (and the plot uses them in ZoneMapService.RealmColor). Built here rather than looked up so a
	// missed resource can never silently leave every tile grey.
	private static readonly IBrush AlbionBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xCC, 0x44, 0x44));
	private static readonly IBrush MidgardBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x44, 0x88, 0xFF));
	private static readonly IBrush HiberniaBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x44, 0xAA, 0x55));
	private static readonly IBrush UnknownBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x88, 0x88, 0x88));
	private static readonly IBrush AlbionWashBrush = new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x66, 0xCC, 0x44, 0x44));
	private static readonly IBrush MidgardWashBrush = new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x66, 0x44, 0x88, 0xFF));
	private static readonly IBrush HiberniaWashBrush = new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x66, 0x44, 0xAA, 0x55));
	private static readonly IBrush UnknownWashBrush = new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x44, 0x88, 0x88, 0x88));

	private static IBrush RealmBrush(int realm, bool wash)
	{
		return realm switch
		       {
				       1 => wash?AlbionWashBrush:AlbionBrush,
				       2 => wash?MidgardWashBrush:MidgardBrush,
				       3 => wash?HiberniaWashBrush:HiberniaBrush,
				       _ => wash?UnknownWashBrush:UnknownBrush
		       };
	}

	private static string BuildRelicTileTooltip(WarmapRelicPlacement? placement, int relicId)
	{
		if(placement is { IsMoving: true })
		{
			return $"{placement.DisplayName}\n{placement.TypeName} relic\nBeing carried right now";
		}

		if(placement == null)
		{
			// No pad holds it: either a player is carrying it right now, or no snapshot has arrived yet.
			return $"Relic {relicId}\nNot reported yet";
		}

		var where = placement.IsHomePad?$"{placement.PadName} (relic keep)":placement.PadName;

		return $"{placement.DisplayName}\n{placement.TypeName} relic\nNow at: {where}\nHeld by {RealmName(placement.OwnerRealm)}";
	}

	private static string RealmName(int realm)
	{
		return realm switch
		       {
				       1 => "Albion",
				       2 => "Midgard",
				       3 => "Hibernia",
				       _ => "Unknown"
		       };
	}

	/// <summary>Zooms the warmap to the given frontier zone. No-op if the zone has no pixel bounds.</summary>
	public void FocusZone(int zoneId)
	{
		var bounds = this.vm?.FrontierMap.Zones.FirstOrDefault(z => z.ZoneId == zoneId)?.PixelBounds;
		if(bounds == null)
		{
			return;
		}

		const int MARGIN = 40;
		lock(this.KillHeatmapPlot.Plot.Sync)
		{
			this.KillHeatmapPlot.Plot.Axes.SetLimits(bounds.X - MARGIN,
			                                         bounds.X + bounds.Width + MARGIN,
			                                         -(bounds.Y + bounds.Height + MARGIN),
			                                         -(bounds.Y - MARGIN));
		}

		this.KillHeatmapPlot.Refresh();
	}

	private static string BuildEventTooltip(WarmapEvent ev, FrontierMapData map)
	{
		var zoneName = map.Zones.FirstOrDefault(z => z.ZoneId == ev.Zone)?.Name ?? $"Zone {ev.Zone}";
		var kind = ev.IsCampaignSpawn?"Campaign spawn":"Mission";
		var text = $"{ev.DisplayName}\n{kind} · {ev.Size} · {zoneName}";

		if(ev.IsPending)
		{
			text += "\nPending — not started yet";
		}

		if(ev.TimeRemaining is { } remaining)
		{
			text += $"\nEnds in {DurationFormat.MinutesSeconds(remaining)}";
		}

		return text;
	}

	private void ShowTooltip(PointerEventArgs e, string text)
	{
		this.BurnTooltipText.Text = text;
		var canvasPos = e.GetPosition(this.TooltipCanvas);
		Canvas.SetLeft(this.BurnTooltip, canvasPos.X + 14);
		Canvas.SetTop(this.BurnTooltip, canvasPos.Y + 14);
		this.BurnTooltip.IsVisible = true;
	}

	private void OnHeatmapPointerMoved(object? sender, PointerEventArgs e)
	{
		if(this.zoneMapService == null||this.warmapService == null)
		{
			return;
		}

		var pos = e.GetPosition(this.KillHeatmapPlot);
		var pixel = new Pixel((float)pos.X, (float)pos.Y);
		var coords = this.KillHeatmapPlot.Plot.GetCoordinates(pixel);

		// Events take priority over the burning-keep tooltip: they are the smaller target, so if the
		// cursor is on one the user is almost certainly pointing at it.
		if(this.vm != null&&(this.appSettings?.WarmapShowEvents ?? true))
		{
			var hit = this.zoneMapService.HitTestEvent(coords.X, -coords.Y, this.warmapService.GetEventsSnapshot(), this.vm.FrontierMap);
			if(hit != null)
			{
				this.ShowTooltip(e, BuildEventTooltip(hit, this.vm.FrontierMap));
				return;
			}
		}

		if(this.appSettings?.WarmapShowRelics ?? true)
		{
			var relicTip = this.zoneMapService.GetRelicTooltip(coords.X, -coords.Y);
			if(relicTip != null)
			{
				this.ShowTooltip(e, relicTip);
				return;
			}
		}

		var combatStarts = this.warmapService.GetCombatStartSnapshot();
		var tip = this.zoneMapService.GetBurnTooltip(coords.X, -coords.Y, combatStarts);

		if(tip != null)
		{
			this.BurnTooltipText.Text = $"{tip.Value.Name}\nBurning for {DurationFormat.MinutesSeconds(tip.Value.Duration)}";

			var canvasPos = e.GetPosition(this.TooltipCanvas);
			Canvas.SetLeft(this.BurnTooltip, canvasPos.X + 14);
			Canvas.SetTop(this.BurnTooltip, canvasPos.Y + 14);
			this.BurnTooltip.IsVisible = true;
		}
		else
		{
			this.BurnTooltip.IsVisible = false;
		}
	}
}
