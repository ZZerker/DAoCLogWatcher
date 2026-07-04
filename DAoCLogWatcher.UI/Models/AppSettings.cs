using System.Collections.Generic;

namespace DAoCLogWatcher.UI.Models;

public class AppSettings
{
	public bool HighlightMultiKills { get; set; } = true;

	public bool HighlightMultiHits { get; set; } = true;

	public string? CustomChatLogPath { get; set; }

	public bool ShowSendNotifications { get; set; } = true;

	public bool ShowDashboardTab { get; set; } = true;

	public bool ShowRealmPointsTab { get; set; } = true;

	public bool ShowCombatTab { get; set; } = true;

	public int ZoneKillWindowMinutes { get; set; } = 10;

	public bool ShowHealLogTab { get; set; } = true;

	public List<DashboardWidgetConfig> DashboardWidgets { get; set; } = [];

	public Dictionary<string, List<DashboardWidgetConfig>> DashboardProfiles { get; set; } = new();

	public string? ActiveDashboardProfile { get; set; }

	public bool OverlayEnabled { get; set; }

	public double? OverlayX { get; set; }

	public double? OverlayY { get; set; }

	public bool OverlayShowRp { get; set; } = true;

	public bool OverlayShowKd { get; set; } = true;

	public bool OverlayShowKillFeed { get; set; } = true;

	public double OverlayOpacity { get; set; } = 0.85;
}
