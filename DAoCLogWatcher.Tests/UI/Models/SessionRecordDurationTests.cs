using System;
using DAoCLogWatcher.UI.Models;
using FluentAssertions;
using Xunit;

namespace DAoCLogWatcher.Tests.UI.Models;

public sealed class SessionRecordDurationTests
{
	[Fact]
	public void Duration_UsesStoredDurationSeconds()
	{
		var record = new SessionRecord
		             {
				             StartTime = new DateTime(2026, 7, 8, 21, 27, 0),
				             DurationSeconds = 6765,
				             RealmPoints = 38046,
				             RpPerHour = 20246
		             };

		record.Duration.Should().Be(TimeSpan.FromSeconds(6765));
	}

	[Fact]
	public void Duration_LegacyRecordWithoutDurationSeconds_RecoversFromRpPerHour()
	{
		// Log session stayed open for days, so EndTime/LastUpdated span is ~145h of wall clock,
		// but only ~1.9h was actually played.
		var record = new SessionRecord
		             {
				             StartTime = new DateTime(2026, 7, 8, 21, 27, 0),
				             LastUpdated = new DateTime(2026, 7, 14, 22, 47, 0),
				             RealmPoints = 38046,
				             RpPerHour = 20246
		             };

		record.Duration.TotalHours.Should().BeApproximately(1.879, 0.01);
	}

	[Fact]
	public void Duration_LegacyRecordWithDefaultLastUpdated_IsNotNegative()
	{
		// Written before LastUpdated existed, so it deserializes as DateTime.MinValue (year 1)
		// and the wall-clock span goes about -2025 years negative.
		var record = new SessionRecord
		             {
				             StartTime = new DateTime(2026, 7, 5, 12, 30, 0),
				             LastUpdated = default,
				             RealmPoints = 0,
				             RpPerHour = 0
		             };

		record.Duration.Should().Be(TimeSpan.Zero);
	}

	[Fact]
	public void Duration_CompletedRecordWithoutDurationSeconds_FallsBackToEndTime()
	{
		var record = new SessionRecord
		             {
				             StartTime = new DateTime(2026, 7, 6, 20, 57, 0),
				             EndTime = new DateTime(2026, 7, 6, 22, 21, 0),
				             RealmPoints = 0,
				             RpPerHour = 0
		             };

		record.Duration.Should().Be(TimeSpan.FromMinutes(84));
	}
}
