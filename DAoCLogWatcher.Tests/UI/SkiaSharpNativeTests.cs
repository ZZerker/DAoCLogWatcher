using FluentAssertions;
using Xunit;

namespace DAoCLogWatcher.Tests.UI;

/// <summary>
/// A native/managed SkiaSharp version mismatch only shows at the first Skia call. The ubuntu CI
/// test job exercises the Linux native; on Windows only the Win32 native is covered.
/// </summary>
public sealed class SkiaSharpNativeTests
{
	[Fact]
	public void CheckNativeLibraryCompatible_NativeMatchesManaged()
	{
		var compatible = SkiaSharp.SkiaSharpVersion.CheckNativeLibraryCompatible(throwIfIncompatible: true);

		compatible.Should().BeTrue();
	}

	[Fact]
	public void SKFontManager_Default_LoadsNativeLibrary()
	{
		SkiaSharp.SKFontManager.Default.Should().NotBeNull();
	}
}
