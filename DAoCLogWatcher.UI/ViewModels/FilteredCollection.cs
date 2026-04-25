using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DAoCLogWatcher.UI.ViewModels;

public sealed partial class FilteredCollection<T>: ObservableObject
{
	private const int CAPACITY = 1000;

	private readonly Func<T, string, bool> matches;

	public FilteredCollection(Func<T, string, bool> matches)
	{
		this.matches = matches;
	}

	public ObservableCollection<T> Items { get; } = [];

	public ObservableCollection<T> Filtered { get; } = [];

	[ObservableProperty] private string filterText = string.Empty;

	partial void OnFilterTextChanged(string value)
	{
		this.Refresh();
	}

	public void Add(T item)
	{
		this.Items.Insert(0, item);
		if(this.Items.Count > CAPACITY)
		{
			this.Items.RemoveAt(this.Items.Count - 1);
		}

		if(this.matches(item, this.FilterText))
		{
			this.Filtered.Insert(0, item);
			if(this.Filtered.Count > CAPACITY)
			{
				this.Filtered.RemoveAt(this.Filtered.Count - 1);
			}
		}
	}

	public void Clear()
	{
		this.Items.Clear();
		this.Filtered.Clear();
	}

	private void Refresh()
	{
		this.Filtered.Clear();
		foreach(var e in this.Items)
		{
			if(this.matches(e, this.FilterText))
			{
				this.Filtered.Add(e);
				if(this.Filtered.Count >= CAPACITY)
				{
					break;
				}
			}
		}
	}

	[RelayCommand]
	private void ClearFilter()
	{
		this.FilterText = string.Empty;
	}
}
