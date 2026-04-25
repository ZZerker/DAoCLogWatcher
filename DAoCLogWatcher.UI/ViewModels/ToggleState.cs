using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DAoCLogWatcher.UI.ViewModels;

public sealed partial class ToggleState: ObservableObject
{
	private readonly Action<bool>? onChanged;

	public ToggleState(bool initial = true, Action<bool>? onChanged = null)
	{
		this.value = initial;
		this.onChanged = onChanged;
	}

	[ObservableProperty] private bool value;

	public string Icon => this.Value?"▲":"▼";

	partial void OnValueChanged(bool value)
	{
		this.OnPropertyChanged(nameof(this.Icon));
		this.onChanged?.Invoke(value);
	}

	[RelayCommand]
	private void Toggle()
	{
		this.Value = !this.Value;
	}
}
