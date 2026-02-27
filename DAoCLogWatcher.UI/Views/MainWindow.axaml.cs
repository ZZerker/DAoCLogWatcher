using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using ScottPlot;

namespace DAoCLogWatcher.UI.Views;

public partial class MainWindow : Window
{
    private ViewModels.MainWindowViewModel? _vm;

    public MainWindow()
    {
        this.InitializeComponent();

        this.InitializeChart();
        this.InitializeRpsHourlyChart();

		this.DataContextChanged += (s, e) =>
		{
			if (this._vm != null)
			{
				this._vm.ChartData.UpdateRequested -= this.OnChartUpdateRequested;
				this._vm.PropertyChanged -= this.OnViewModelPropertyChanged;
				this._vm = null;
			}

			if (this.DataContext is ViewModels.MainWindowViewModel vm)
			{
				this._vm = vm;
				vm.ChartData.UpdateRequested += this.OnChartUpdateRequested;
				vm.PropertyChanged += this.OnViewModelPropertyChanged;
				this.ApplyTheme(vm.IsDarkTheme);
			}
		};
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        var screen = this.Screens.ScreenFromWindow(this);
        if (screen == null) return;

        var workH = screen.WorkingArea.Height / screen.Scaling;
        if (workH < 1268)
        {
            this.Height = workH;

            if (this.DataContext is ViewModels.MainWindowViewModel vm)
            {
                vm.IsAbsoluteNumbersVisible = false;
                vm.IsAbsoluteRpsVisible     = false;
            }
        }
    }

    private void InitializeChart()
    {
        ApplyChartStyle(this.RpChart, "#252525", "#1E1E1E", "#3A3A3A", "#2A2A2A", "#CCCCCC");
        this.RpChart.Plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.DateTimeAutomatic();
        this.RpChart.Plot.YLabel("Total RPs");
        this.RpChart.Refresh();
    }

    private void InitializeRpsHourlyChart()
    {
        ApplyChartStyle(this.RpsHourlyChart, "#252525", "#1E1E1E", "#3A3A3A", "#2A2A2A", "#CCCCCC");
        this.RpsHourlyChart.Plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.DateTimeAutomatic();
        this.RpsHourlyChart.Plot.YLabel("RP/h (rolling 1h)");
        this.RpsHourlyChart.Refresh();
    }

    private static void ApplyChartStyle(
        ScottPlot.Avalonia.AvaPlot chart,
        string bg, string dataBg, string gridMajor, string gridMinor, string fg)
    {
        chart.Plot.FigureBackground.Color = Color.FromHex(bg);
        chart.Plot.DataBackground.Color   = Color.FromHex(dataBg);
        chart.Plot.Grid.MajorLineColor     = Color.FromHex(gridMajor);
        chart.Plot.Grid.MinorLineColor     = Color.FromHex(gridMinor);
        chart.Plot.Axes.Color(Color.FromHex(fg));
        chart.Plot.Axes.Title.Label.ForeColor = Color.FromHex(fg);
    }

    private void ApplyTheme(bool isDark)
    {
        Application.Current!.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;

        var bg       = isDark ? "#252525" : "#FAFAFA";
        var dataBg   = isDark ? "#1E1E1E" : "#FFFFFF";
        var gridMaj  = isDark ? "#3A3A3A" : "#D0D0D0";
        var gridMin  = isDark ? "#2A2A2A" : "#EBEBEB";
        var fg       = isDark ? "#CCCCCC" : "#333333";

        ApplyChartStyle(this.RpChart,        bg, dataBg, gridMaj, gridMin, fg);
        ApplyChartStyle(this.RpsHourlyChart, bg, dataBg, gridMaj, gridMin, fg);
        this.RpChart.Refresh();
        this.RpsHourlyChart.Refresh();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not ViewModels.MainWindowViewModel vm)
            return;

        if (e.PropertyName == nameof(ViewModels.MainWindowViewModel.IsDarkTheme))
	        this.ApplyTheme(vm.IsDarkTheme);
        else if (e.PropertyName == nameof(ViewModels.MainWindowViewModel.IsSidebarVisible))
	        this.MainContentGrid.ColumnDefinitions[0].Width = vm.IsSidebarVisible
			                                                          ? new GridLength(320)
			                                                          : new GridLength(0);
    }

	private void OnChartUpdateRequested(object? sender, EventArgs e)
	{
		if (this.DataContext is ViewModels.MainWindowViewModel vm)
		{
			this.UpdateChart(vm.ChartData.CumulativeDataPoints);
			this.UpdateRpsHourlyChart(vm.ChartData.HourlyDataPoints);
		}
	}

	public void UpdateChart(System.Collections.Generic.List<(DateTime Time, double Rps)> dataPoints)
	{
		this.RpChart.Plot.Clear();

		if (dataPoints.Count > 0)
		{
			var times = dataPoints.Select(p => p.Time.ToOADate()).ToArray();
			var rps = dataPoints.Select(p => p.Rps).ToArray();

			var line = this.RpChart.Plot.Add.Scatter(times, rps);
			line.Color = Color.FromHex("#00D9FF");
			line.LineWidth = 2;
			line.MarkerSize = 0;

			this.RpChart.Plot.Axes.AutoScale();
        }

        this.RpChart.Refresh();
    }

	public void UpdateRpsHourlyChart(System.Collections.Generic.List<(DateTime Time, double RpsPerHour)> dataPoints)
	{
		this.RpsHourlyChart.Plot.Clear();

		if (dataPoints.Count > 0)
		{
			var times = dataPoints.Select(p => p.Time.ToOADate()).ToArray();
			var values = dataPoints.Select(p => p.RpsPerHour).ToArray();

            var line = this.RpsHourlyChart.Plot.Add.Scatter(times, values);
            line.Color = Color.FromHex("#FFAA44");
            line.LineWidth = 2;
            line.MarkerSize = 0;

            this.RpsHourlyChart.Plot.Axes.AutoScale();
        }

        this.RpsHourlyChart.Refresh();
    }

    private async void OnScreenshotClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => await this.CaptureToClipboardAsync();

    private async Task CaptureToClipboardAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var scaling = topLevel.RenderScaling;
        var pixelSize = new Avalonia.PixelSize(
            (int)(this.Bounds.Width  * scaling),
            (int)(this.Bounds.Height * scaling));

        using var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96 * scaling, 96 * scaling));
        bitmap.Render(this);

        if (OperatingSystem.IsWindows())
            SetClipboardWin32(bitmap);
        else if (topLevel.Clipboard is { } clipboard)
            await clipboard.SetBitmapAsync(bitmap);
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
            avBitmap.CopyPixels(new Avalonia.PixelRect(0, 0, w, h),
                                gcHandle.AddrOfPinnedObject(),
                                pixelsBgra.Length,
                                stride);
        }
        finally { gcHandle.Free(); }

        // If Avalonia rendered RGBA instead of BGRA, swap R↔B so Windows sees BGRA.
        if (avBitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888)
        {
            for (var i = 0; i < pixelsBgra.Length; i += 4)
                (pixelsBgra[i], pixelsBgra[i + 2]) = (pixelsBgra[i + 2], pixelsBgra[i]);
        }

        // --- Build BITMAPV5HEADER (124 bytes) + pixel data ---
        // Masks describe BGRA layout: Blue=0xFF, Green=0xFF00, Red=0xFF0000, Alpha=0xFF000000
        const int V5Size = 124;
        const uint CF_DIBV5 = 17;

        var dib = new byte[V5Size + pixelsBgra.Length];
        var s = dib.AsSpan();
        WriteLE32(s,   0, V5Size);                    // bV5Size
        WriteLE32(s,   4, w);                         // bV5Width
        WriteLE32(s,   8, -h);                        // bV5Height (negative = top-down)
        WriteLE16(s,  12, 1);                         // bV5Planes
        WriteLE16(s,  14, 32);                        // bV5BitCount
        WriteLE32(s,  16, 3);                         // bV5Compression = BI_BITFIELDS
        WriteLE32(s,  20, pixelsBgra.Length);         // bV5SizeImage
        // offsets 24-39: XPels, YPels, ClrUsed, ClrImportant — leave as 0
        WriteLE32(s,  40, unchecked((int)0x00FF0000)); // bV5RedMask
        WriteLE32(s,  44, unchecked((int)0x0000FF00)); // bV5GreenMask
        WriteLE32(s,  48, unchecked((int)0x000000FF)); // bV5BlueMask
        WriteLE32(s,  52, unchecked((int)0xFF000000)); // bV5AlphaMask
        WriteLE32(s,  56, 0x73524742);                // bV5CSType = LCS_sRGB
        // CIEXYZTRIPLE (36 bytes at 60), gamma (12 bytes at 96) — leave as 0
        WriteLE32(s, 108, 4);                         // bV5Intent = LCS_GM_IMAGES
        // ProfileData, ProfileSize, Reserved — leave as 0
        pixelsBgra.CopyTo(dib.AsMemory(V5Size));

        // --- Write both formats to clipboard in one open/close ---
        var pngFormat = RegisterClipboardFormat("PNG");
        if (!OpenClipboard(IntPtr.Zero)) return;
        EmptyClipboard();
        PutGlobalMem(CF_DIBV5, dib);
        PutGlobalMem(pngFormat, pngBytes);
        CloseClipboard();
    }

    [SupportedOSPlatform("windows")]
    private static void PutGlobalMem(uint format, byte[] data)
    {
        var hMem = GlobalAlloc(0x0002 /* GMEM_MOVEABLE */, (UIntPtr)data.Length);
        if (hMem == IntPtr.Zero) return;
        var ptr = GlobalLock(hMem);
        if (ptr == IntPtr.Zero) { GlobalFree(hMem); return; }
        Marshal.Copy(data, 0, ptr, data.Length);
        GlobalUnlock(hMem);
        if (SetClipboardData(format, hMem) == IntPtr.Zero)
            GlobalFree(hMem);
    }

    private static void WriteLE32(Span<byte> buf, int offset, int value)
    {
        buf[offset]     = (byte) value;
        buf[offset + 1] = (byte)(value >>  8);
        buf[offset + 2] = (byte)(value >> 16);
        buf[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteLE16(Span<byte> buf, int offset, short value)
    {
        buf[offset]     = (byte) value;
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
