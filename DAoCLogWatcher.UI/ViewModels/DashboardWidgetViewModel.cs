using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DAoCLogWatcher.UI.Models;

namespace DAoCLogWatcher.UI.ViewModels;

public sealed partial class DashboardWidgetViewModel : ObservableObject
{
	public DashboardWidgetId Id { get; }
	public string Label { get; }

	[ObservableProperty] private bool isVisible;
	[ObservableProperty] private DashboardWidgetSize size;

	public bool IsSizeSmall => this.Size == DashboardWidgetSize.Small;
	public bool IsSizeMedium => this.Size == DashboardWidgetSize.Medium;
	public bool IsSizeLarge => this.Size == DashboardWidgetSize.Large;

	public IRelayCommand MoveUpCommand { get; }
	public IRelayCommand MoveDownCommand { get; }
	public IRelayCommand SetSmallCommand { get; }
	public IRelayCommand SetMediumCommand { get; }
	public IRelayCommand SetLargeCommand { get; }

	private readonly Action<DashboardWidgetViewModel> onChanged;

	public DashboardWidgetViewModel(
		DashboardWidgetId id, string label, bool isVisible, DashboardWidgetSize size,
		Action<DashboardWidgetViewModel> onMoveUp,
		Action<DashboardWidgetViewModel> onMoveDown,
		Action<DashboardWidgetViewModel> onChanged)
	{
		this.Id = id;
		this.Label = label;
		this.isVisible = isVisible;
		this.size = size;
		this.onChanged = onChanged;
		this.MoveUpCommand = new RelayCommand(() => onMoveUp(this));
		this.MoveDownCommand = new RelayCommand(() => onMoveDown(this));
		this.SetSmallCommand = new RelayCommand(() => this.Size = DashboardWidgetSize.Small);
		this.SetMediumCommand = new RelayCommand(() => this.Size = DashboardWidgetSize.Medium);
		this.SetLargeCommand = new RelayCommand(() => this.Size = DashboardWidgetSize.Large);
	}

	partial void OnIsVisibleChanged(bool value) => this.onChanged(this);

	partial void OnSizeChanged(DashboardWidgetSize value)
	{
		this.OnPropertyChanged(nameof(this.IsSizeSmall));
		this.OnPropertyChanged(nameof(this.IsSizeMedium));
		this.OnPropertyChanged(nameof(this.IsSizeLarge));
		this.onChanged(this);
	}
}
