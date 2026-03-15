using System;
using System.IO;
using System.Text.Json;

namespace DAoCLogWatcher.UI.Models;

public class AppSettings
{
	public bool HighlightMultiKills { get; set; } = true;
	public string? CustomChatLogPath { get; set; }

	// Tab visibility (all default on)
	public bool ShowRealmPointsTab { get; set; } = true;
	public bool ShowCombatTab { get; set; } = true;
	public bool ShowHealLogTab { get; set; } = true;
	public bool ShowCombatLogTab { get; set; } = true;
}

public static class SettingsService
{
	private static readonly string FilePath = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
		"DAoCLogWatcher", "settings.json");

	public static AppSettings Load()
	{
		try
		{
			if (File.Exists(FilePath))
				return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
		}
		catch { }
		return new();
	}

	public static void Save(AppSettings settings)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
		File.WriteAllText(FilePath, JsonSerializer.Serialize(settings,
			new JsonSerializerOptions { WriteIndented = true }));
	}
}
