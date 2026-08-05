using System.Text.Json.Serialization;

namespace DAoCLogWatcher.UI.Models;

/// <summary>
/// Tracks whether the user has been asked about, and consented to, writing the KWin overlay
/// window rule into ~/.config/kwinrulesrc (KDE Plasma Wayland only).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum KWinRuleConsent
{
	NotAsked,
	Granted,
	Declined
}
