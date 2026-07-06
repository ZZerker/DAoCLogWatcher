using System;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DAoCLogWatcher.UI.Models;

namespace DAoCLogWatcher.UI.ViewModels;

public sealed partial class OverlayViewModel: ViewModelBase
{
	private const int KILL_FEED_CAP = 5;

	private const string DamageBrushKey = "AppOverlayDamage";

	private const string HealBrushKey = "AppOverlayHeal";

	private const string DamageLabel = "Dmg";

	private const string HealLabel = "Heal";

	// AppOverlay* tokens are theme-independent, so the resolved brushes can be cached for the process lifetime.
	private static IBrush? damageBrush;

	private static IBrush? healBrush;

	private bool lastDamageIsPrimary = true;
	private bool lastCombatRowVisible;
	private bool lastPrimaryVisible;
	private bool lastSecondaryVisible;

	public OverlayViewModel(RealmPointSummary summary, AppSettings settings)
	{
		this.Summary = summary;
		this.showRp = settings.OverlayShowRp;
		this.showKillFeed = settings.OverlayShowKillFeed;
		this.backgroundOpacity = Math.Clamp(settings.OverlayOpacity, 0.2, 1.0);
	}

	public RealmPointSummary Summary { get; }

	public ObservableCollection<string> KillFeed { get; } = [];

	[ObservableProperty] private bool isLocked = true;

	[ObservableProperty] private long damageTotal;

	[ObservableProperty] private long healTotal;

	[ObservableProperty] private string? characterName;

	[ObservableProperty] private bool isLive;

	[ObservableProperty] private bool showRp;

	[ObservableProperty] private bool showKillFeed;

	[ObservableProperty] private double backgroundOpacity;

	public IBrush CardBackground => new SolidColorBrush(Color.FromArgb((byte)Math.Round(this.BackgroundOpacity * 255), 0, 0, 0));

	partial void OnBackgroundOpacityChanged(double value)
	{
		this.OnPropertyChanged(nameof(this.CardBackground));
	}

	private bool DamageIsPrimary => this.DamageTotal >= this.HealTotal;

	private long PrimaryTotal => this.DamageIsPrimary?this.DamageTotal:this.HealTotal;

	private long SecondaryTotal => this.DamageIsPrimary?this.HealTotal:this.DamageTotal;

	public bool CombatRowVisible => this.DamageTotal > 0||this.HealTotal > 0;

	public bool PrimaryVisible => this.PrimaryTotal > 0;

	public bool SecondaryVisible => this.SecondaryTotal > 0;

	public string PrimaryLabel => this.DamageIsPrimary?DamageLabel:HealLabel;

	public string SecondaryLabel => this.DamageIsPrimary?HealLabel:DamageLabel;

	public string PrimaryValue => this.PrimaryTotal.ToString("N0");

	public string SecondaryValue => this.SecondaryTotal.ToString("N0");

	public IBrush? PrimaryBrush => this.DamageIsPrimary?GetDamageBrush():GetHealBrush();

	public IBrush? SecondaryBrush => this.DamageIsPrimary?GetHealBrush():GetDamageBrush();

	partial void OnDamageTotalChanged(long value)
	{
		this.RaiseCombatRow();
	}

	partial void OnHealTotalChanged(long value)
	{
		this.RaiseCombatRow();
	}

	// Runs on every combat log line while fighting — only the values change each tick,
	// so labels/brushes/visibility are raised only when they actually flip.
	private void RaiseCombatRow()
	{
		this.OnPropertyChanged(nameof(this.PrimaryValue));
		this.OnPropertyChanged(nameof(this.SecondaryValue));

		if(this.DamageIsPrimary != this.lastDamageIsPrimary)
		{
			this.lastDamageIsPrimary = this.DamageIsPrimary;
			this.OnPropertyChanged(nameof(this.PrimaryLabel));
			this.OnPropertyChanged(nameof(this.SecondaryLabel));
			this.OnPropertyChanged(nameof(this.PrimaryBrush));
			this.OnPropertyChanged(nameof(this.SecondaryBrush));
		}

		if(this.CombatRowVisible != this.lastCombatRowVisible)
		{
			this.lastCombatRowVisible = this.CombatRowVisible;
			this.OnPropertyChanged(nameof(this.CombatRowVisible));
		}

		if(this.PrimaryVisible != this.lastPrimaryVisible)
		{
			this.lastPrimaryVisible = this.PrimaryVisible;
			this.OnPropertyChanged(nameof(this.PrimaryVisible));
		}

		if(this.SecondaryVisible != this.lastSecondaryVisible)
		{
			this.lastSecondaryVisible = this.SecondaryVisible;
			this.OnPropertyChanged(nameof(this.SecondaryVisible));
		}
	}

	private static IBrush? GetDamageBrush()
	{
		return damageBrush ??= ResolveOverlayBrush(DamageBrushKey);
	}

	private static IBrush? GetHealBrush()
	{
		return healBrush ??= ResolveOverlayBrush(HealBrushKey);
	}

	private static IBrush? ResolveOverlayBrush(string key)
	{
		var app = Application.Current;
		if(app != null&&app.TryGetResource(key, app.ActualThemeVariant, out var value)&&value is IBrush brush)
		{
			return brush;
		}

		return null;
	}

	[RelayCommand]
	private void ToggleLock()
	{
		this.IsLocked = !this.IsLocked;
	}

	public void AddKillFeedEntry(string entry)
	{
		this.KillFeed.Insert(0, entry);
		while(this.KillFeed.Count > KILL_FEED_CAP)
		{
			this.KillFeed.RemoveAt(this.KillFeed.Count - 1);
		}
	}
}
