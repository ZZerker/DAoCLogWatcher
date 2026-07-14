using System;
using System.IO;
using System.Text.Json;
using DAoCLogWatcher.UI.Models;

namespace DAoCLogWatcher.UI.Services;

public sealed class SettingsService: ISettingsService
{
	private static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DAoCLogWatcher", "settings.json");

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
			File.WriteAllText(FilePath,
			                  JsonSerializer.Serialize(settings,
			                                           new JsonSerializerOptions
			                                           {
					                                           WriteIndented = true
			                                           }));
		}
		catch(Exception ex)
		{
			AppLog.Exception("SettingsService.Save", ex);
		}
	}
}
