using DAoCLogWatcher.Core;
using FluentAssertions;

namespace DAoCLogWatcher.Tests.Core;

public sealed class CharacterDiscoveryServiceTests: IDisposable
{
	private readonly string testDirectory;

	public CharacterDiscoveryServiceTests()
	{
		this.testDirectory = Path.Combine(Path.GetTempPath(), $"daoc_test_{Guid.NewGuid()}");
		Directory.CreateDirectory(this.testDirectory);
	}

	public void Dispose()
	{
		if(Directory.Exists(this.testDirectory))
		{
			Directory.Delete(this.testDirectory, true);
		}
	}

	[Fact]
	public void GetCharacterNames_NonExistentDirectory_ReturnsEmpty()
	{
		var result = CharacterDiscoveryService.GetCharacterNames(Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid()}"));

		result.Should().BeEmpty();
	}

	[Fact]
	public void GetCharacterNames_EmptyDirectory_ReturnsEmpty()
	{
		var result = CharacterDiscoveryService.GetCharacterNames(this.testDirectory);

		result.Should().BeEmpty();
	}

	[Fact]
	public void GetCharacterNames_SingleFileWithSuffix_ReturnsStrippedName()
	{
		File.WriteAllText(Path.Combine(this.testDirectory, "Barnabas-41.ini"), string.Empty);

		var result = CharacterDiscoveryService.GetCharacterNames(this.testDirectory);

		result.Should().ContainSingle().Which.Should().Be("Barnabas");
	}

	[Fact]
	public void GetCharacterNames_SameNameMultipleServerSuffixes_DeduplicatesToOne()
	{
		File.WriteAllText(Path.Combine(this.testDirectory, "Barnabas-41.ini"), string.Empty);
		File.WriteAllText(Path.Combine(this.testDirectory, "Barnabas-42.ini"), string.Empty);

		var result = CharacterDiscoveryService.GetCharacterNames(this.testDirectory);

		result.Should().ContainSingle().Which.Should().Be("Barnabas");
	}

	[Fact]
	public void GetCharacterNames_MultipleCharacters_ReturnsSortedAlphabetically()
	{
		File.WriteAllText(Path.Combine(this.testDirectory, "Zara-41.ini"), string.Empty);
		File.WriteAllText(Path.Combine(this.testDirectory, "Amos-41.ini"), string.Empty);
		File.WriteAllText(Path.Combine(this.testDirectory, "Mira-41.ini"), string.Empty);

		var result = CharacterDiscoveryService.GetCharacterNames(this.testDirectory);

		result.Should().Equal("Amos", "Mira", "Zara");
	}

	[Fact]
	public void GetCharacterNames_NonIniFiles_AreIgnored()
	{
		File.WriteAllText(Path.Combine(this.testDirectory, "Barnabas-41.ini"), string.Empty);
		File.WriteAllText(Path.Combine(this.testDirectory, "notes.txt"), string.Empty);
		File.WriteAllText(Path.Combine(this.testDirectory, "config.xml"), string.Empty);

		var result = CharacterDiscoveryService.GetCharacterNames(this.testDirectory);

		result.Should().ContainSingle().Which.Should().Be("Barnabas");
	}

	[Fact]
	public void GetCharacterNames_DeduplicationIsCaseInsensitive()
	{
		File.WriteAllText(Path.Combine(this.testDirectory, "barnabas-41.ini"), string.Empty);
		File.WriteAllText(Path.Combine(this.testDirectory, "Barnabas-42.ini"), string.Empty);

		var result = CharacterDiscoveryService.GetCharacterNames(this.testDirectory);

		result.Should().ContainSingle();
	}
}
