using System;
using System.IO;
using System.Text.Json;
using DAoCLogWatcher.UI.Models;

namespace DAoCLogWatcher.UI.Services;

public sealed class SettingsService: ISettingsService
{
	private static readonly string FilePath = Path.Combine(AppPaths.DataDirectory, "settings.json");

	private static readonly string TempPath = FilePath + ".tmp";

	private static readonly JsonSerializerOptions WriteOptions = new()
	                                                             {
			                                                             WriteIndented = true
	                                                             };

	public AppSettings Load()
	{
		try
		{
			if(File.Exists(FilePath))
			{
				return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
			}
		}
		catch(Exception ex)
		{
			AppLog.Exception("SettingsService.Load", ex);
		}

		return new AppSettings();
	}

	public void Save(AppSettings settings)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
			// Write beside the target and rename over it, so a kill mid-write can never leave a truncated settings.json.
			File.WriteAllText(TempPath, JsonSerializer.Serialize(settings, WriteOptions));
			File.Move(TempPath, FilePath, overwrite: true);
		}
		catch(Exception ex)
		{
			AppLog.Exception("SettingsService.Save", ex);
			try
			{
				File.Delete(TempPath);
			}
			catch(Exception cleanupEx)
			{
				AppLog.Exception("SettingsService.Save.Cleanup", cleanupEx);
			}
		}
	}
}
