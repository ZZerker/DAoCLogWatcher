using System.IO;
using DAoCLogWatcher.UI.Services;
using FluentAssertions;
using Xunit;

namespace DAoCLogWatcher.Tests.UI.Services;

public sealed class AppPathsTests
{
	/// <summary>
	/// With the default SpecialFolderOption .NET returns an empty base when ~/.config is missing, which
	/// silently turned every data path into one relative to the working directory (BUG-008).
	/// </summary>
	[Fact]
	public void DataDirectory_IsRootedAndNamedAfterTheApp()
	{
		Path.IsPathRooted(AppPaths.DataDirectory).Should().BeTrue();
		Path.GetFileName(AppPaths.DataDirectory).Should().Be("DAoCLogWatcher");
	}
}
