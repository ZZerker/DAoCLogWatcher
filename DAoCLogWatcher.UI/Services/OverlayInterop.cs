using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DAoCLogWatcher.UI.Services;

[SupportedOSPlatform("windows")]
public static class OverlayInterop
{
	// Extended window style flags for a click-through, non-activating layered overlay.
	private const int GWL_EXSTYLE = -20;
	private const int WS_EX_LAYERED = 0x80000;
	private const int WS_EX_TRANSPARENT = 0x20;
	private const int WS_EX_NOACTIVATE = 0x8000000;

	private static readonly IntPtr HWND_TOPMOST = new(-1);
	private const uint SWP_NOSIZE = 0x0001;
	private const uint SWP_NOMOVE = 0x0002;
	private const uint SWP_NOACTIVATE = 0x0010;

	// The *LongPtrW exports exist only in 64-bit user32 — fine for our win-x64 builds,
	// but a win-x86 target would need GetWindowLongW/SetWindowLongW instead.
	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
	private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
	private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

	[DllImport("user32.dll")]
	private static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint flags);

	// Games and other topmost windows can restack above us; call periodically to stay on top.
	public static void AssertTopmost(IntPtr hwnd)
	{
		if(hwnd == IntPtr.Zero)
		{
			return;
		}

		SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
	}

	public static void SetClickThrough(IntPtr hwnd, bool enabled)
	{
		if(hwnd == IntPtr.Zero)
		{
			return;
		}

		var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
		style |= WS_EX_LAYERED | WS_EX_NOACTIVATE;

		if(enabled)
		{
			style |= WS_EX_TRANSPARENT;
		}
		else
		{
			style &= ~(long)WS_EX_TRANSPARENT;
		}

		SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
	}
}
