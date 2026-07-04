using System.Text.Json;
using DAoCLogWatcher.UI.Models;
using FluentAssertions;

namespace DAoCLogWatcher.Tests.UI.Models;

public sealed class AppSettingsOverlayTests
{
	[Fact]
	public void RoundTrip_PreservesOverlayProperties()
	{
		var settings = new AppSettings
		               {
				               OverlayEnabled = true,
				               OverlayX = 1234.5,
				               OverlayY = 678.0,
				               OverlayShowRp = false,
				               OverlayShowKd = false,
				               OverlayShowKillFeed = false
		               };

		var json = JsonSerializer.Serialize(settings);
		var deserialized = JsonSerializer.Deserialize<AppSettings>(json);

		deserialized.Should().NotBeNull();
		deserialized!.OverlayEnabled.Should().BeTrue();
		deserialized.OverlayX.Should().Be(1234.5);
		deserialized.OverlayY.Should().Be(678.0);
		deserialized.OverlayShowRp.Should().BeFalse();
		deserialized.OverlayShowKd.Should().BeFalse();
		deserialized.OverlayShowKillFeed.Should().BeFalse();
	}

	[Fact]
	public void Deserialize_OldSettingsWithoutOverlayFields_AppliesDefaults()
	{
		const string json = """
		                     {
		                       "HighlightMultiKills": true,
		                       "ShowDashboardTab": true
		                     }
		                     """;

		var settings = JsonSerializer.Deserialize<AppSettings>(json);

		settings.Should().NotBeNull();
		settings!.OverlayEnabled.Should().BeFalse();
		settings.OverlayX.Should().BeNull();
		settings.OverlayY.Should().BeNull();
		settings.OverlayShowRp.Should().BeTrue();
		settings.OverlayShowKd.Should().BeTrue();
		settings.OverlayShowKillFeed.Should().BeTrue();
	}
}
