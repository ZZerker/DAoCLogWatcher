using System;
using System.Text.Json;
using DAoCLogWatcher.UI.Models;
using FluentAssertions;

namespace DAoCLogWatcher.Tests.UI.Models;

public sealed class SessionRecordTests
{
	[Fact]
	public void RoundTrip_PreservesAllValues()
	{
		var record = new SessionRecord
		             {
				             StartTime = new DateTime(2026, 6, 1, 20, 0, 0),
				             EndTime = new DateTime(2026, 6, 1, 22, 30, 0),
				             CharacterName = "Zordrak",
				             RealmPoints = 12345,
				             RpPerHour = 4938.5,
				             Kills = 10,
				             Deaths = 2,
				             BestMultiKill = 3,
				             TopZone = "Emain Macha",
				             DamageDone = 98765,
				             HealingDone = 4321
		             };

		var json = JsonSerializer.Serialize(record);
		var deserialized = JsonSerializer.Deserialize<SessionRecord>(json);

		deserialized.Should().Be(record);
	}

	[Fact]
	public void Deserialize_ToleratesUnknownProperties()
	{
		const string json = """
		                     {
		                       "StartTime": "2026-06-01T20:00:00",
		                       "CharacterName": "Zordrak",
		                       "RealmPoints": 500,
		                       "FutureField": "some-new-value",
		                       "AnotherFutureField": 42
		                     }
		                     """;

		var record = JsonSerializer.Deserialize<SessionRecord>(json);

		record.Should().NotBeNull();
		record!.CharacterName.Should().Be("Zordrak");
		record.RealmPoints.Should().Be(500);
		record.SchemaVersion.Should().Be(1);
	}

	[Fact]
	public void EndTime_Null_MeansLiveSession()
	{
		var record = new SessionRecord
		             {
				             StartTime = DateTime.Now,
				             EndTime = null
		             };

		record.EndTime.Should().BeNull();
	}
}
