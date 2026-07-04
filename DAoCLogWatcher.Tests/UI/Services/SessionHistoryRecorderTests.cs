using System;
using System.IO;
using DAoCLogWatcher.Core.Models;
using DAoCLogWatcher.UI.Models;
using DAoCLogWatcher.UI.Services;
using FluentAssertions;

namespace DAoCLogWatcher.Tests.UI.Services;

public sealed class SessionHistoryRecorderTests: IDisposable
{
	private readonly string tempDirectory;
	private readonly string filePath;

	public SessionHistoryRecorderTests()
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

	[Fact]
	public void BuildRecord_MapsStatsFromLiveProcessors()
	{
		var summary = new RealmPointSummary();
		var chartData = new RpsChartData();
		var processor = new RealmPointProcessor(summary, chartData);
		var combatSummary = new CombatSummary
		                    {
				                    TotalDamageDealt = 5000,
				                    TotalHealingDone = 1200
		                    };
		var recorder = new SessionHistoryRecorder(processor, summary, combatSummary);

		processor.Process(new UnknownLogLine("Statistics for Zordrak this Session:") { DetectedCharacterName = "Zordrak" }, null, out _, out _);
		processor.Process(new KillLogLine("[20:00:00] Xyrrath was just killed by Zordrak in Emain Macha.",
		                                  new KillEvent
		                                  {
				                                  Timestamp = new TimeOnly(20, 0, 0),
				                                  Victim = "Xyrrath",
				                                  Killer = "Zordrak",
				                                  Zone = "Emain Macha"
		                                  }),
		                  null,
		                  out _,
		                  out _);
		summary.TotalRealmPoints = 2500;

		var startTime = new DateTime(2026, 6, 1, 20, 0, 0);
		var record = recorder.BuildRecord(startTime, "Zordrak", endTime: null, bestMultiKill: 4);

		record.StartTime.Should().Be(startTime);
		record.EndTime.Should().BeNull();
		record.CharacterName.Should().Be("Zordrak");
		record.RealmPoints.Should().Be(2500);
		record.Kills.Should().Be(1);
		record.Deaths.Should().Be(0);
		record.BestMultiKill.Should().Be(4);
		record.DamageDone.Should().Be(5000);
		record.HealingDone.Should().Be(1200);
		record.TopZone.Should().Be("Emain Macha");
	}

	[Fact]
	public void BuiltRecord_RoundTripsThroughHistoryService()
	{
		var summary = new RealmPointSummary();
		var processor = new RealmPointProcessor(summary, new RpsChartData());
		var combatSummary = new CombatSummary();
		var historyService = new SessionHistoryService(this.filePath);
		var recorder = new SessionHistoryRecorder(processor, summary, combatSummary);
		var startTime = new DateTime(2026, 6, 1, 20, 0, 0);

		historyService.Upsert(recorder.BuildRecord(startTime, "Zordrak", endTime: null, bestMultiKill: 0));

		historyService.Load().Should().ContainSingle().Which.StartTime.Should().Be(startTime);
	}

	[Fact]
	public void BuildRecord_NoZoneKills_TopZoneIsNull()
	{
		var summary = new RealmPointSummary();
		var processor = new RealmPointProcessor(summary, new RpsChartData());
		var combatSummary = new CombatSummary();
		var recorder = new SessionHistoryRecorder(processor, summary, combatSummary);

		var record = recorder.BuildRecord(DateTime.Now, null, null, 0);

		record.TopZone.Should().BeNull();
	}
}
