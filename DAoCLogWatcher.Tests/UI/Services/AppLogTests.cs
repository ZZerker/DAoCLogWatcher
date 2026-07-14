using System;
using System.IO;
using DAoCLogWatcher.UI.Services;
using FluentAssertions;
using Xunit;

namespace DAoCLogWatcher.Tests.UI.Services;

public sealed class AppLogTests
{
	[Fact]
	public void Exception_WritesTypeMessageAndStackTraceToLogFile()
	{
		AppLog.Initialize();
		var marker = $"marker-{Guid.NewGuid():N}";

		Exception thrown;

		try
		{
			throw new InvalidOperationException(marker, new ArgumentException("inner-cause"));
		}
		catch(Exception ex)
		{
			thrown = ex;
		}

		// Act
		AppLog.Exception("AppLogTests", thrown);

		// Assert — the whole point of the logger is that this survives to disk.
		var contents = ReadLogFile();
		contents.Should().Contain(marker);
		contents.Should().Contain("System.InvalidOperationException");
		contents.Should().Contain("---> System.ArgumentException: inner-cause");
		contents.Should().Contain("Exception_WritesTypeMessageAndStackTraceToLogFile");
	}

	[Fact]
	public void Warning_WritesToLogFile()
	{
		AppLog.Initialize();
		var marker = $"marker-{Guid.NewGuid():N}";

		AppLog.Warning("AppLogTests", marker);

		ReadLogFile().Should().Contain($"WARN  [AppLogTests] {marker}");
	}

	/// <summary>Opens with sharing — AppLog appends to this same file while the test reads it.</summary>
	private static string ReadLogFile()
	{
		using var stream = new FileStream(AppLog.CurrentLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using var reader = new StreamReader(stream);

		return reader.ReadToEnd();
	}
}
