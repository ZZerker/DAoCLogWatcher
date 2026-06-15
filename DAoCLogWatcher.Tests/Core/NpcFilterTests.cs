using DAoCLogWatcher.Core;
using FluentAssertions;

namespace DAoCLogWatcher.Tests.Core;

public sealed class NpcFilterTests
{
	[Theory]
	[InlineData("Jarl Bleedmer")]
	[InlineData("Lord Grumbald")]
	[InlineData("Chieftain Maolduin")]
	public void IsNpc_TitlePrefixedNamedNpc_ReturnsTrue(string name)
	{
		NpcFilter.IsNpc(name).Should().BeTrue();
	}

	[Theory]
	[InlineData("Lordbob")]    // single-token player name, no space after the title
	[InlineData("Jarlsson")]
	[InlineData("Chieftainly")]
	public void IsNpc_PlayerNameSharingTitlePrefix_ReturnsFalse(string name)
	{
		NpcFilter.IsNpc(name).Should().BeFalse("player names are single tokens; only 'Title <name>' is an NPC");
	}

	[Theory]
	[InlineData("Cleric")]
	[InlineData("Scout")]
	[InlineData("Tower Captain")]
	[InlineData("Postern Door")]
	[InlineData("North Gate")]
	[InlineData("Greater Behemoth")]
	public void IsNpc_KnownNpcCategories_ReturnTrue(string name)
	{
		NpcFilter.IsNpc(name).Should().BeTrue();
	}

	[Theory]
	[InlineData("Healbot")]
	[InlineData("Mendalion")]
	[InlineData("")]
	[InlineData(null)]
	public void IsNpc_PlayerOrEmpty_ReturnsFalse(string? name)
	{
		NpcFilter.IsNpc(name).Should().BeFalse();
	}
}
