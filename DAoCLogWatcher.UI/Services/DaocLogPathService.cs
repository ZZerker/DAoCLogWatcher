using System;
using System.IO;

namespace DAoCLogWatcher.UI.Services;

public sealed class DaocLogPathService: IDaocLogPathService
{
	public string? FindDaocLogPath()
	{
		if(OperatingSystem.IsWindows())
		{
			var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
			return Path.Combine(documents, "Electronic Arts", "Dark Age of Camelot", "chat.log");
		}

		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

		var candidates = new[]
		                 {
				                 // Default Wine prefix
				                 Path.Combine(home, ".wine", "drive_c", "users", Environment.UserName, "My Documents", "Electronic Arts", "Dark Age of Camelot", "chat.log"),

				                 // Lutris default Wine prefix
				                 Path.Combine(home, "Games", "dark-age-of-camelot", "drive_c", "users", Environment.UserName, "My Documents", "Electronic Arts", "Dark Age of Camelot", "chat.log"),

				                 // Lutris with "Documents" folder name
				                 Path.Combine(home, "Games", "dark-age-of-camelot", "drive_c", "users", Environment.UserName, "Documents", "Electronic Arts", "Dark Age of Camelot", "chat.log"),

				                 // Lutris with eden suffix
				                 Path.Combine(home, "Games", "dark-age-of-camelot-eden", "drive_c", "users", Environment.UserName, "Documents", "Electronic Arts", "Dark Age of Camelot", "chat.log"),

				                 // Default Users Home "Documents" folder name
				                 Path.Combine(home, "Documents", "Electronic Arts", "chat.log"),

				                 // Default Users Home "Documents" folder name (German)
				                 Path.Combine(home, "Dokumente", "Electronic Arts", "chat.log"),
		                 };

		return Array.Find(candidates, File.Exists);
	}
}
