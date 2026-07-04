using System;
using System.IO;
using System.Linq;
using DAoCLogWatcher.UI.Models;
using DAoCLogWatcher.UI.Services;
using FluentAssertions;

namespace DAoCLogWatcher.Tests.UI.Services;

public sealed class SessionHistoryServiceTests: IDisposable
{
	private readonly string tempDirectory;
	private readonly string filePath;

	public SessionHistoryServiceTests()
	{
		this.tempDirectory = Path.Combine(Path.GetTempPath(), $"DAoCLogWatcherTests_{Guid.NewGuid():N}");
		Directory.CreateDirectory(this.tempDirectory);
		this.filePath = Path.Combine(this.tempDirectory, "sessions.json");
	}

	public void Dispose()
	{
		if(Directory.Exists(this.tempDirectory))
		{
			Directory.Delete(this.tempDirectory, recursive: true);
		}
	}

	private SessionHistoryService BuildService()
	{
		return new SessionHistoryService(this.filePath);
	}

	private static SessionRecord MakeRecord(DateTime startTime, string character = "Zordrak", long realmPoints = 100)
	{
		return new SessionRecord
		       {
				       StartTime = startTime,
				       CharacterName = character,
				       RealmPoints = realmPoints
		       };
	}

	[Fact]
	public void Load_MissingFile_ReturnsEmpty()
	{
		var service = this.BuildService();

		service.Load().Should().BeEmpty();
	}

	[Fact]
	public void Load_CorruptFile_ReturnsEmpty()
	{
		File.WriteAllText(this.filePath, "{ not valid json ][");
		var service = this.BuildService();

		service.Load().Should().BeEmpty();
	}

	[Fact]
	public void Upsert_NewRecord_Appends()
	{
		var service = this.BuildService();
		var record = MakeRecord(new DateTime(2026, 6, 1, 20, 0, 0));

		service.Upsert(record);

		service.Load().Should().ContainSingle().Which.Should().Be(record);
	}

	[Fact]
	public void Upsert_SameNaturalKey_ReplacesExisting()
	{
		var service = this.BuildService();
		var startTime = new DateTime(2026, 6, 1, 20, 0, 0);
		service.Upsert(MakeRecord(startTime, realmPoints: 100));

		service.Upsert(MakeRecord(startTime, realmPoints: 250));

		var records = service.Load();
		records.Should().ContainSingle();
		records[0].RealmPoints.Should().Be(250);
	}

	[Fact]
	public void Upsert_DifferentCharacterSameStartTime_Appends()
	{
		var service = this.BuildService();
		var startTime = new DateTime(2026, 6, 1, 20, 0, 0);
		service.Upsert(MakeRecord(startTime, character: "Zordrak"));

		service.Upsert(MakeRecord(startTime, character: "Xyrrath"));

		service.Load().Should().HaveCount(2);
	}

	[Fact]
	public void Upsert_NameDetectedAfterFirstFlush_UpgradesNullCharacterRecordInPlace()
	{
		var service = this.BuildService();
		var startTime = new DateTime(2026, 6, 1, 20, 0, 0);
		service.Upsert(new SessionRecord { StartTime = startTime, CharacterName = null, RealmPoints = 100 });

		service.Upsert(MakeRecord(startTime, realmPoints: 250));

		var records = service.Load();
		records.Should().ContainSingle();
		records[0].CharacterName.Should().Be("Zordrak");
		records[0].RealmPoints.Should().Be(250);
	}

	[Fact]
	public void Upsert_PersistsSortedByStartTime()
	{
		var service = this.BuildService();
		var later = new DateTime(2026, 6, 2, 20, 0, 0);
		var earlier = new DateTime(2026, 6, 1, 20, 0, 0);

		service.Upsert(MakeRecord(later));
		service.Upsert(MakeRecord(earlier));

		var records = service.Load();
		records.Select(r => r.StartTime).Should().BeInAscendingOrder();
	}
}
