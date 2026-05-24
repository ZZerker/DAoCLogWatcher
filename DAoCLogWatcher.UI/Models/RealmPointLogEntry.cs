using System;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DAoCLogWatcher.UI.Models;

public partial class RealmPointLogEntry: ObservableObject
{
	public required string Timestamp { get; init; }

	public required int Points { get; init; }

	public required string Source { get; init; }

	public required string Details { get; init; }

	[ObservableProperty] private bool isMultiKill;

	public int KillCount { get; init; }

	public string? VictimName { get; init; }

	public bool IsDeathblow { get; init; }

	[RelayCommand]
	private void OpenHerald()
	{
		if(this.VictimName == null)
		{
			return;
		}

		Process.Start(new ProcessStartInfo(
			$"https://eden-daoc.net/herald?n=player&k={Uri.EscapeDataString(this.VictimName)}")
			{ UseShellExecute = true });
	}
}
