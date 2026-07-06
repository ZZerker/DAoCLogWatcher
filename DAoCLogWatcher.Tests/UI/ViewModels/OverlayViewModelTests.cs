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
				               OverlayShowKillFeed = true
		               };

		var vm = new OverlayViewModel(new RealmPointSummary(), settings);

		vm.ShowRp.Should().BeTrue();
		vm.ShowKillFeed.Should().BeTrue();
	}

	[Fact]
	public void CombatRow_HiddenWhenBothTotalsZero()
	{
		var vm = Create();

		vm.CombatRowVisible.Should().BeFalse();
		vm.PrimaryVisible.Should().BeFalse();
		vm.SecondaryVisible.Should().BeFalse();
	}

	[Fact]
	public void CombatRow_DamageOnly_ShowsSingleDamageEntry()
	{
		var vm = Create();

		vm.DamageTotal = 1500;

		vm.CombatRowVisible.Should().BeTrue();
		vm.PrimaryVisible.Should().BeTrue();
		vm.PrimaryLabel.Should().Be("Dmg");
		vm.PrimaryValue.Should().Be(1500L.ToString("N0"));
		vm.SecondaryVisible.Should().BeFalse();
	}

	[Fact]
	public void CombatRow_HealOnly_ShowsSingleHealEntry()
	{
		var vm = Create();

		vm.HealTotal = 900;

		vm.CombatRowVisible.Should().BeTrue();
		vm.PrimaryVisible.Should().BeTrue();
		vm.PrimaryLabel.Should().Be("Heal");
		vm.PrimaryValue.Should().Be(900L.ToString("N0"));
		vm.SecondaryVisible.Should().BeFalse();
	}

	[Fact]
	public void CombatRow_BothWithHealBigger_HealFirst()
	{
		var vm = Create();

		vm.DamageTotal = 400;
		vm.HealTotal = 2200;

		vm.PrimaryLabel.Should().Be("Heal");
		vm.PrimaryValue.Should().Be(2200L.ToString("N0"));
		vm.SecondaryLabel.Should().Be("Dmg");
		vm.SecondaryValue.Should().Be(400L.ToString("N0"));
		vm.PrimaryVisible.Should().BeTrue();
		vm.SecondaryVisible.Should().BeTrue();
	}

	[Fact]
	public void CombatRow_BothWithDamageBigger_DamageFirst()
	{
		var vm = Create();

		vm.DamageTotal = 5000;
		vm.HealTotal = 300;

		vm.PrimaryLabel.Should().Be("Dmg");
		vm.PrimaryValue.Should().Be(5000L.ToString("N0"));
		vm.SecondaryLabel.Should().Be("Heal");
		vm.SecondaryValue.Should().Be(300L.ToString("N0"));
	}
}
