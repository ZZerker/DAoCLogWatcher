using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DAoCLogWatcher.UI.Services;

/// <summary>
/// Append-only diagnostic log on disk. Static (not DI) on purpose: it must be usable from
/// the global exception hooks in <see cref="Program"/>, which run before the container exists,
/// and as a drop-in for the <c>Debug.WriteLine</c> calls scattered through the catch blocks.
/// </summary>
/// <remarks>
/// Every method swallows its own failures — a logger that throws would take the app down with it,
/// which is the opposite of the point.
/// </remarks>
public static class AppLog
{
	private const int RETENTION_DAYS = 7;

	private static readonly Lock WriteLock = new();

	/// <summary>Sits beside settings.json and sessions.json so "zip this folder" collects everything.</summary>
	public static string LogDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DAoCLogWatcher", "logs");

	public static string CurrentLogFile => Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyy-MM-dd}.log");

	/// <summary>Writes the session header and prunes logs older than <see cref="RETENTION_DAYS"/>.</summary>
	public static void Initialize()
	{
		try
		{
			Directory.CreateDirectory(LogDirectory);
			PruneOldLogs();

			var version = typeof(AppLog).Assembly.GetName().Version?.ToString() ?? "unknown";
			Write($"=== DAoCLogWatcher {version} started on {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture}) ===");
		}
		catch(Exception ex)
		{
			Debug.WriteLine($"[AppLog.Initialize] {ex.GetType().Name}: {ex.Message}");
		}
	}

	public static void Info(string context, string message)
	{
		Write($"INFO  [{context}] {message}");
	}

	public static void Warning(string context, string message)
	{
		Write($"WARN  [{context}] {message}");
	}

	/// <summary>Logs the full exception — type, message and stack trace, plus inner exceptions.</summary>
	public static void Exception(string context, Exception ex)
	{
		var builder = new StringBuilder();
		builder.Append(CultureInfo.InvariantCulture, $"ERROR [{context}] {ex.GetType().FullName}: {ex.Message}");

		for(var inner = ex.InnerException; inner != null; inner = inner.InnerException)
		{
			builder.AppendLine();
			builder.Append(CultureInfo.InvariantCulture, $"        ---> {inner.GetType().FullName}: {inner.Message}");
		}

		if(!string.IsNullOrWhiteSpace(ex.StackTrace))
		{
			builder.AppendLine();
			builder.Append(ex.StackTrace);
		}

		Write(builder.ToString());
	}

	/// <summary>Reveals the log folder in the OS file manager.</summary>
	public static void OpenLogFolder()
	{
		try
		{
			Directory.CreateDirectory(LogDirectory);

			var fileName = OperatingSystem.IsWindows()?"explorer":OperatingSystem.IsMacOS()?"open":"xdg-open";
			Process.Start(new ProcessStartInfo(fileName, LogDirectory)
			              {
					              UseShellExecute = true
			              });
		}
		catch(Exception ex)
		{
			Exception("AppLog.OpenLogFolder", ex);
		}
	}

	private static void Write(string line)
	{
		try
		{
			var stamped = $"{DateTime.Now:HH:mm:ss.fff} {line}";
			Debug.WriteLine(stamped);

			lock(WriteLock)
			{
				Directory.CreateDirectory(LogDirectory);
				File.AppendAllText(CurrentLogFile, stamped + Environment.NewLine);
			}
		}
		catch(Exception ex)
		{
			// Disk full, permissions, whatever — logging must never be the reason the app dies.
			Debug.WriteLine($"[AppLog.Write] {ex.GetType().Name}: {ex.Message}");
		}
	}

	private static void PruneOldLogs()
	{
		var cutoff = DateTime.Now.AddDays(-RETENTION_DAYS);

		foreach(var file in Directory.GetFiles(LogDirectory, "app-*.log").Where(f => File.GetLastWriteTime(f) < cutoff))
		{
			try
			{
				File.Delete(file);
			}
			catch(Exception ex)
			{
				Debug.WriteLine($"[AppLog.PruneOldLogs] {ex.GetType().Name}: {ex.Message}");
			}
		}
	}
}
