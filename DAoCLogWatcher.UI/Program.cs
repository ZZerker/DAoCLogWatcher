using Avalonia;
using System;
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
		// Must be called before anything else to handle Velopack install/uninstall hooks
		VelopackApp.Build().Run();

		BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
	}

	// Avalonia configuration, don't remove; also used by visual designer.
	public static AppBuilder BuildAvaloniaApp()
	{
		return AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace()

		                 // EXPLICITLY SET THE WM_CLASS FOR X11 AND XWAYLAND SESSIONS
		                 // THIS ENSURES THE DESKTOP ENVIRONMENT RECOGNIZES THE WINDOW
		                 .With(new X11PlatformOptions
		                       {
				                       WmClass = "io.github.zzerker.DAoCLogWatcher"
		                       });
	}
}
