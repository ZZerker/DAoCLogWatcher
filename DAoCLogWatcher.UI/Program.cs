using Avalonia;
using System;
using System.Threading.Tasks;
using DAoCLogWatcher.UI.Services;
using Velopack;

namespace DAoCLogWatcher.UI;

internal sealed class Program
{
	// Initialization code. Don't use any Avalonia, third-party APIs or any
	// SynchronizationContext-reliant code before AppMain is called: things aren't initialized
	// yet and stuff might break.
	[STAThread]
	public static void Main(string[] args)
	{
		AppLog.Initialize();
		AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
		TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

		// Must be called before anything else to handle Velopack install/uninstall hooks
		VelopackApp.Build().Run();

		try
		{
			BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
		}
		catch(Exception ex)
		{
			AppLog.Exception("Program.Main", ex);
			throw;
		}
	}

	// Avalonia configuration, don't remove; also used by visual designer.
	public static AppBuilder BuildAvaloniaApp()
	{
		// The X11/XWayland WM_CLASS must equal the StartupWMClass of whichever .desktop file the
		// running package installed, otherwise the task manager shows a second, unmatched icon
		// instead of grouping the window under the launcher entry:
		//   - AppImage: Velopack generates DAoCLogWatcher.desktop with StartupWMClass=DAoCLogWatcher
		//               (derived from --packId in appimage/build-appimage.sh).
		//   - Flatpak / dev runs: flatpak/io.github.zzerker.DAoCLogWatcher.desktop.
		// $APPIMAGE is set only by the AppImage runtime, so use it to select the matching class.
		var wmClass = Environment.GetEnvironmentVariable("APPIMAGE") is not null
			              ? "DAoCLogWatcher"
			              : "io.github.zzerker.DAoCLogWatcher";

		return AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace()
		                 .With(new X11PlatformOptions
		                       {
				                       WmClass = wmClass
		                       });
	}

	private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
	{
		if(e.ExceptionObject is Exception ex)
		{
			AppLog.Exception($"AppDomain.UnhandledException (terminating: {e.IsTerminating})", ex);
		}
		else
		{
			AppLog.Warning("AppDomain.UnhandledException", $"Non-Exception object thrown: {e.ExceptionObject}");
		}
	}

	// Faulted fire-and-forget tasks reach the finalizer unobserved; without this they vanish silently.
	private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
	{
		AppLog.Exception("TaskScheduler.UnobservedTaskException", e.Exception);
		e.SetObserved();
	}
}
