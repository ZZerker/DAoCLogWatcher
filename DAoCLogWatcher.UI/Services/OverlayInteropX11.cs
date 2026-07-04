using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DAoCLogWatcher.UI.Services;

[SupportedOSPlatform("linux")]
public static class OverlayInteropX11
{
	// X11/extensions/shape.h — the input shape controls which pixels receive pointer events.
	private const int ShapeInput = 2;

	private delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);

	// Kept alive in a static field so the GC never collects the delegate Xlib holds a pointer to.
	private static readonly XErrorHandler ignoreErrors = (_, _) => 0;
	private static bool errorHandlerInstalled;

	[DllImport("libX11.so.6")]
	private static extern IntPtr XOpenDisplay(IntPtr displayName);

	[DllImport("libX11.so.6")]
	private static extern int XCloseDisplay(IntPtr display);

	[DllImport("libX11.so.6")]
	private static extern int XFlush(IntPtr display);

	[DllImport("libX11.so.6")]
	private static extern IntPtr XSetErrorHandler(XErrorHandler handler);

	[DllImport("libXfixes.so.3")]
	private static extern bool XFixesQueryExtension(IntPtr display, out int eventBase, out int errorBase);

	[DllImport("libXfixes.so.3")]
	private static extern IntPtr XFixesCreateRegion(IntPtr display, IntPtr rectangles, int nrectangles);

	[DllImport("libXfixes.so.3")]
	private static extern void XFixesDestroyRegion(IntPtr display, IntPtr region);

	[DllImport("libXfixes.so.3")]
	private static extern void XFixesSetWindowShapeRegion(IntPtr display, IntPtr window, int shapeKind, int xOff, int yOff, IntPtr region);

	public static void SetClickThrough(IntPtr xid, bool enabled)
	{
		if(xid == IntPtr.Zero)
		{
			return;
		}

		try
		{
			// Xlib's default error handler calls exit() on async protocol errors (e.g. BadWindow
			// from a stale xid) — install a no-op handler so a failed shape request can't kill the app.
			if(!errorHandlerInstalled)
			{
				XSetErrorHandler(ignoreErrors);
				errorHandlerInstalled = true;
			}

			var display = XOpenDisplay(IntPtr.Zero);
			if(display == IntPtr.Zero)
			{
				return;
			}

			try
			{
				if(!XFixesQueryExtension(display, out _, out _))
				{
					return;
				}

				if(enabled)
				{
					var region = XFixesCreateRegion(display, IntPtr.Zero, 0);
					if(region == IntPtr.Zero)
					{
						return;
					}

					XFixesSetWindowShapeRegion(display, xid, ShapeInput, 0, 0, region);
					XFixesDestroyRegion(display, region);
				}
				else
				{
					// region = None restores the window's default input shape.
					XFixesSetWindowShapeRegion(display, xid, ShapeInput, 0, 0, IntPtr.Zero);
				}

				XFlush(display);
			}
			finally
			{
				XCloseDisplay(display);
			}
		}
		catch(Exception ex)when(ex is DllNotFoundException||ex is EntryPointNotFoundException)
		{
			// libXfixes is absent on some minimal distros — degrade to a non-click-through overlay rather than crash.
		}
	}
}
