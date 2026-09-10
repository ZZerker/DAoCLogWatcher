using DAoCLogWatcher.UI.ViewModels;
using FluentAssertions;

namespace DAoCLogWatcher.Tests.UI.ViewModels;

public sealed class SendNotificationControllerTests
{
	[Fact]
	public void DurationSeconds_DefaultsToOneMinute()
	{
		var controller = new SendNotificationController();

		controller.DurationSeconds.Should().Be(60);
	}

	[Fact]
	public void DurationSeconds_ClampsBelowMinimum()
	{
		var controller = new SendNotificationController();

		controller.DurationSeconds = 1;

		controller.DurationSeconds.Should().Be(5);
	}

	[Fact]
	public void DurationSeconds_ClampsAboveMaximum()
	{
		var controller = new SendNotificationController();

		controller.DurationSeconds = 1000;

		controller.DurationSeconds.Should().Be(300);
	}
}
