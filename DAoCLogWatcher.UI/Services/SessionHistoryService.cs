using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DAoCLogWatcher.UI.Models;

namespace DAoCLogWatcher.UI.Services;

public sealed class SessionHistoryService: ISessionHistoryService
{
	private readonly string filePath;
	private readonly System.Threading.Lock gate = new();
	private List<SessionRecord>? cache;

	public SessionHistoryService()
			: this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DAoCLogWatcher", "sessions.json"))
	{
	}

	internal SessionHistoryService(string filePath)
	{
		this.filePath = filePath;
	}

	public IReadOnlyList<SessionRecord> Load()
	{
		lock(this.gate)
		{
			this.cache ??= ReadFile(this.filePath);
			return [..this.cache];
		}
	}

	public void Upsert(SessionRecord record)
	{
		lock(this.gate)
		{
			try
			{
				this.cache ??= ReadFile(this.filePath);

				// Also match a record saved before the character name was detected
				// (CharacterName null) so it upgrades in place instead of duplicating.
				var existingIndex = this.cache.FindIndex(r => r.StartTime == record.StartTime&&(r.CharacterName == record.CharacterName || r.CharacterName is null));
				if(existingIndex >= 0)
				{
					this.cache[existingIndex] = record;
				}
				else
				{
					this.cache.Add(record);
				}

				this.cache.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
				this.WriteAtomic(this.cache);
			}
			catch(Exception ex)
			{
				Debug.WriteLine($"[SessionHistoryService.Upsert] {ex.GetType().Name}: {ex.Message}");
			}
		}
	}

	private static List<SessionRecord> ReadFile(string filePath)
	{
		try
		{
			if(File.Exists(filePath))
			{
				return JsonSerializer.Deserialize<List<SessionRecord>>(File.ReadAllText(filePath)) ?? [];
			}
		}
		catch(Exception ex)
		{
			Debug.WriteLine($"[SessionHistoryService.Load] {ex.GetType().Name}: {ex.Message}");
		}

		return [];
	}

	private void WriteAtomic(List<SessionRecord> records)
	{
		var directory = Path.GetDirectoryName(this.filePath)!;
		Directory.CreateDirectory(directory);
		var tempPath = Path.Combine(directory, $"{Path.GetFileName(this.filePath)}.{Guid.NewGuid():N}.tmp");
		File.WriteAllText(tempPath, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
		File.Move(tempPath, this.filePath, overwrite: true);
	}
}
