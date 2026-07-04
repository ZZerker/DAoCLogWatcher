using System;
using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DAoCLogWatcher.UI.Helpers;
using DAoCLogWatcher.UI.Models;

namespace DAoCLogWatcher.UI.ViewModels;

public sealed partial class OverlayViewModel: ViewModelBase
{
	private const int KILL_FEED_CAP = 5;

	public OverlayViewModel(RealmPointSummary summary, AppSettings settings)
	{
		this.Summary = summary;
		this.showRp = settings.OverlayShowRp;
		this.showKd = settings.OverlayShowKd;
		this.showKillFeed = settings.OverlayShowKillFeed;
		this.backgroundOpacity = Math.Clamp(settings.OverlayOpacity, 0.2, 1.0);
	}

	public RealmPointSummary Summary { get; }

	public ObservableCollection<string> KillFeed { get; } = [];

	[ObservableProperty] private bool isLocked = true;

	[ObservableProperty] private int kills;

	[ObservableProperty] private int deaths;

	[ObservableProperty] private string? characterName;

	[ObservableProperty] private bool isLive;

	[ObservableProperty] private bool showRp;

	[ObservableProperty] private bool showKd;

	[ObservableProperty] private bool showKillFeed;

	[ObservableProperty] private double backgroundOpacity;

	public IBrush CardBackground => new SolidColorBrush(Color.FromArgb((byte)Math.Round(this.BackgroundOpacity * 255), 0, 0, 0));

	partial void OnBackgroundOpacityChanged(double value)
	{
		this.OnPropertyChanged(nameof(this.CardBackground));
	}

	public double KdRatio => StatMath.KdRatio(this.Kills, this.Deaths);

	partial void OnKillsChanged(int value)
	{
		this.OnPropertyChanged(nameof(this.KdRatio));
	}

	partial void OnDeathsChanged(int value)
	{
		this.OnPropertyChanged(nameof(this.KdRatio));
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
