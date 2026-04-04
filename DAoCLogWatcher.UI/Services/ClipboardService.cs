using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;

namespace DAoCLogWatcher.UI.Services;

public static class ClipboardService
{
	/// <summary>
	/// Renders <paramref name="window"/> to a bitmap and places it on the system clipboard.
	/// On Windows, sets both CF_DIBV5 (Discord, Chrome, Paint) and the custom "PNG" format (Paint.NET).
	/// On Linux/macOS, uses Avalonia's native clipboard API.
	/// </summary>
	public static async Task CaptureWindowToClipboardAsync(Window window)
	{
		var topLevel = TopLevel.GetTopLevel(window);
		if(topLevel is null)
			return;

		var scaling = topLevel.RenderScaling;
		var pixelSize = new PixelSize((int)(window.Bounds.Width * scaling), (int)(window.Bounds.Height * scaling));

		using var renderBitmap = new RenderTargetBitmap(pixelSize, new Vector(96 * scaling, 96 * scaling));
		renderBitmap.Render(window);

		if(OperatingSystem.IsWindows())
		{
			SetClipboardWin32(renderBitmap);
		}
		else if(topLevel.Clipboard is { } clipboard)
		{
			// The X11 clipboard serialises the bitmap lazily (when another app requests the
			// selection). RenderTargetBitmap would already be disposed by then, so we flush
			// it to a regular Bitmap backed by its own pixel buffer first.
			using var ms = new MemoryStream();
			renderBitmap.Save(ms);
			ms.Position = 0;

			// clipboardBitmap is intentionally not disposed here — the clipboard holds the
			// only reference and will release it when the selection is cleared.
			var clipboardBitmap = new Bitmap(ms);
			await clipboard.SetBitmapAsync(clipboardBitmap);
		}
	}

	// Sets two clipboard formats simultaneously so all Windows apps can paste:
	//   CF_DIBV5 (format 17)  – Discord, Chrome, Paint, standard Windows apps
	//   "PNG" custom format   – Paint.NET and other PNG-aware apps
	[SupportedOSPlatform("windows")]
	private static void SetClipboardWin32(RenderTargetBitmap avBitmap)
	{
		var w = avBitmap.PixelSize.Width;
		var h = avBitmap.PixelSize.Height;
		var stride = w * 4;

		// --- PNG bytes ---
		using var pngStream = new MemoryStream();
		avBitmap.Save(pngStream);
		var pngBytes = pngStream.ToArray();

		// --- Raw pixels (BGRA on Windows / Skia backend) ---
		var pixelsBgra = new byte[h * stride];
		var gcHandle = GCHandle.Alloc(pixelsBgra, GCHandleType.Pinned);
		try
		{
			avBitmap.CopyPixels(new Avalonia.PixelRect(0, 0, w, h), gcHandle.AddrOfPinnedObject(), pixelsBgra.Length, stride);
		}
		finally
		{
			gcHandle.Free();
		}

		// If Avalonia rendered RGBA instead of BGRA, swap R↔B so Windows sees BGRA.
		if(avBitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888)
		{
			for(var i = 0; i < pixelsBgra.Length; i += 4)
				(pixelsBgra[i], pixelsBgra[i + 2]) = (pixelsBgra[i + 2], pixelsBgra[i]);
		}

		// --- Build BITMAPV5HEADER (124 bytes) + pixel data ---
		// Masks describe BGRA layout: Blue=0xFF, Green=0xFF00, Red=0xFF0000, Alpha=0xFF000000
		const int V5Size = 124;
		const uint CF_DIBV5 = 17;

		var dib = new byte[V5Size + pixelsBgra.Length];
		var s = dib.AsSpan();
		WriteLE32(s, 0, V5Size); // bV5Size
		WriteLE32(s, 4, w); // bV5Width
		WriteLE32(s, 8, -h); // bV5Height (negative = top-down)
		WriteLE16(s, 12, 1); // bV5Planes
		WriteLE16(s, 14, 32); // bV5BitCount
		WriteLE32(s, 16, 3); // bV5Compression = BI_BITFIELDS
		WriteLE32(s, 20, pixelsBgra.Length); // bV5SizeImage

		// offsets 24-39: XPels, YPels, ClrUsed, ClrImportant — leave as 0
		WriteLE32(s, 40, unchecked((int)0x00FF0000)); // bV5RedMask
		WriteLE32(s, 44, unchecked((int)0x0000FF00)); // bV5GreenMask
		WriteLE32(s, 48, unchecked((int)0x000000FF)); // bV5BlueMask
		WriteLE32(s, 52, unchecked((int)0xFF000000)); // bV5AlphaMask
		WriteLE32(s, 56, 0x73524742); // bV5CSType = LCS_sRGB

		// CIEXYZTRIPLE (36 bytes at 60), gamma (12 bytes at 96) — leave as 0
		WriteLE32(s, 108, 4); // bV5Intent = LCS_GM_IMAGES

		// ProfileData, ProfileSize, Reserved — leave as 0
		pixelsBgra.CopyTo(dib.AsMemory(V5Size));

		// --- Write both formats to clipboard in one open/close ---
		var pngFormat = RegisterClipboardFormat("PNG");
		if(!OpenClipboard(IntPtr.Zero)) return;
		EmptyClipboard();
		PutGlobalMem(CF_DIBV5, dib);
		PutGlobalMem(pngFormat, pngBytes);
		CloseClipboard();
	}

	[SupportedOSPlatform("windows")]
	private static void PutGlobalMem(uint format, byte[] data)
	{
		var hMem = GlobalAlloc(0x0002 /* GMEM_MOVEABLE */, (UIntPtr)data.Length);
		if(hMem == IntPtr.Zero) return;
		var ptr = GlobalLock(hMem);
		if(ptr == IntPtr.Zero)
		{
			GlobalFree(hMem);
			return;
		}

		Marshal.Copy(data, 0, ptr, data.Length);
		GlobalUnlock(hMem);
		if(SetClipboardData(format, hMem) == IntPtr.Zero)
			GlobalFree(hMem);
	}

	private static void WriteLE32(Span<byte> buf, int offset, int value)
	{
		buf[offset] = (byte)value;
		buf[offset + 1] = (byte)(value >> 8);
		buf[offset + 2] = (byte)(value >> 16);
		buf[offset + 3] = (byte)(value >> 24);
	}

	private static void WriteLE16(Span<byte> buf, int offset, short value)
	{
		buf[offset] = (byte)value;
		buf[offset + 1] = (byte)(value >> 8);
	}

	[DllImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", CharSet = CharSet.Unicode)]
	[SupportedOSPlatform("windows")]
	private static extern uint RegisterClipboardFormat(string lpszFormat);

	[DllImport("user32.dll")]
	[SupportedOSPlatform("windows")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool OpenClipboard(IntPtr hWndNewOwner);

	[DllImport("user32.dll")]
	[SupportedOSPlatform("windows")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool EmptyClipboard();

	[DllImport("user32.dll")]
	[SupportedOSPlatform("windows")]
	private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

	[DllImport("user32.dll")]
	[SupportedOSPlatform("windows")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CloseClipboard();

	[DllImport("kernel32.dll")]
	[SupportedOSPlatform("windows")]
	private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

	[DllImport("kernel32.dll")]
	[SupportedOSPlatform("windows")]
	private static extern IntPtr GlobalFree(IntPtr hMem);

	[DllImport("kernel32.dll")]
	[SupportedOSPlatform("windows")]
	private static extern IntPtr GlobalLock(IntPtr hMem);

	[DllImport("kernel32.dll")]
	[SupportedOSPlatform("windows")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GlobalUnlock(IntPtr hMem);
}
