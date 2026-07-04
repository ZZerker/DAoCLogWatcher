using DAoCLogWatcher.UI.Models;
using DAoCLogWatcher.UI.ViewModels;
using FluentAssertions;

namespace DAoCLogWatcher.Tests.UI.ViewModels;

public sealed class OverlayViewModelTests
{
	private static OverlayViewModel Create()
	{
		return new OverlayViewModel(new RealmPointSummary(), new AppSettings());
	}

	[Fact]
	public void IsLocked_DefaultsToTrue()
	{
		var vm = Create();

		vm.IsLocked.Should().BeTrue();
	}

	[Fact]
	public void AddKillFeedEntry_CapsAtFive_NewestFirst()
	{
		var vm = Create();

		for(var i = 1; i <= 7; i++)
		{
			vm.AddKillFeedEntry($"kill {i}");
		}

		vm.KillFeed.Should().HaveCount(5);
		vm.KillFeed[0].Should().Be("kill 7");
		vm.KillFeed[4].Should().Be("kill 3");
	}

	[Fact]
	public void SectionVisibility_SourcedFromSettings()
	{
		var settings = new AppSettings
		               {
				               OverlayShowRp = true,
				               OverlayShowKd = false,
				               OverlayShowKillFeed = true
		               };

		var vm = new OverlayViewModel(new RealmPointSummary(), settings);

		vm.ShowRp.Should().BeTrue();
		vm.ShowKd.Should().BeFalse();
		vm.ShowKillFeed.Should().BeTrue();
	}
}
