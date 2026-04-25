using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace DAoCLogWatcher.UI.Models;

public class AppSettings
{
	public bool HighlightMultiKills { get; set; } = true;

	public bool HighlightMultiHits { get; set; } = true;

	public string? CustomChatLogPath { get; set; }

	public bool ShowSendNotifications { get; set; } = true;

	public bool ShowRealmPointsTab { get; set; } = true;

	public bool ShowCombatTab { get; set; } = true;

	public bool ShowZoneKillsTab { get; set; } = true;

	public int ZoneKillWindowMinutes { get; set; } = 10;

	public bool ShowHealLogTab { get; set; } = true;

	public bool ShowCombatLogTab { get; set; } = true;

	public bool ShowKillHeatmapTab { get; set; } = true;
}
