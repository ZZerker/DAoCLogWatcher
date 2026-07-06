using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DAoCLogWatcher.UI.Services;

[SupportedOSPlatform("linux")]
public static class OverlayInteropX11
{
	// X11/extensions/shape.h — the input shape controls which pixels receive pointer events.
	private const int ShapeInput = 2;

	// Xlib: XA_ATOM predefined atom id and XChangeProperty replace mode.
	private static readonly IntPtr AtomType = new(4);
	private const int PropModeReplace = 0;

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

	[DllImport("libX11.so.6")]
	private static extern int XRaiseWindow(IntPtr display, IntPtr window);

	[DllImport("libX11.so.6", CharSet = CharSet.Ansi)]
	private static extern IntPtr XInternAtom(IntPtr display, string atomName, bool onlyIfExists);

	[DllImport("libX11.so.6")]
	private static extern int XChangeProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr type, int format, int mode, IntPtr[] data, int nelements);

	[DllImport("libXfixes.so.3")]
	private static extern bool XFixesQueryExtension(IntPtr display, out int eventBase, out int errorBase);

	[DllImport("libXfixes.so.3")]
	private static extern IntPtr XFixesCreateRegion(IntPtr display, IntPtr rectangles, int nrectangles);

	[DllImport("libXfixes.so.3")]
	private static extern void XFixesDestroyRegion(IntPtr display, IntPtr region);

	[DllImport("libXfixes.so.3")]
	private static extern void XFixesSetWindowShapeRegion(IntPtr display, IntPtr window, int shapeKind, int xOff, int yOff, IntPtr region);

	// KWin stacks OSD-type windows above even a focused fullscreen window — this is how
	// KDE's own volume/brightness OSDs work. Other WMs ignore the KDE atom and use the
	// "normal" fallback, so setting it is harmless everywhere else.
	public static void MarkAsOnScreenDisplay(IntPtr xid)
	{
		WithDisplay(xid, (display, window) =>
		{
			var windowTypeAtom = XInternAtom(display, "_NET_WM_WINDOW_TYPE", false);
			var osdAtom = XInternAtom(display, "_KDE_NET_WM_WINDOW_TYPE_ON_SCREEN_DISPLAY", false);
			var normalAtom = XInternAtom(display, "_NET_WM_WINDOW_TYPE_NORMAL", false);
			if(windowTypeAtom == IntPtr.Zero||osdAtom == IntPtr.Zero||normalAtom == IntPtr.Zero)
			{
				return;
			}

			var types = new[] { osdAtom, normalAtom };
			XChangeProperty(display, window, windowTypeAtom, AtomType, 32, PropModeReplace, types, types.Length);
		});
	}

	// The WM restacks a game window above us whenever it re-renders/raises itself;
	// call periodically to stay on top.
	public static void Raise(IntPtr xid)
	{
		WithDisplay(xid, (display, window) =>
		{
			XRaiseWindow(display, window);
		});
	}

	public static void SetClickThrough(IntPtr xid, bool enabled)
	{
		WithDisplay(xid, (display, window) =>
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

				XFixesSetWindowShapeRegion(display, window, ShapeInput, 0, 0, region);
				XFixesDestroyRegion(display, region);
			}
			else
			{
				// region = None restores the window's default input shape.
				XFixesSetWindowShapeRegion(display, window, ShapeInput, 0, 0, IntPtr.Zero);
			}
		});
	}

	private static void WithDisplay(IntPtr xid, Action<IntPtr, IntPtr> action)
	{
		if(xid == IntPtr.Zero)
		{
			return;
		}

		try
		{
			// Xlib's default error handler calls exit() on async protocol errors (e.g. BadWindow
			// from a stale xid) — install a no-op handler so a failed request can't kill the app.
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
				action(display, xid);
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
