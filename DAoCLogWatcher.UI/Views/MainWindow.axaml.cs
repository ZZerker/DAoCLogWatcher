using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
	            this._vm.ChartUpdateRequested -= this.OnChartUpdateRequested;
	            this._vm.PropertyChanged -= this.OnViewModelPropertyChanged;
	            this._vm = null;
            }

            if (this.DataContext is ViewModels.MainWindowViewModel vm)
            {
	            this._vm = vm;
                vm.ChartUpdateRequested += this.OnChartUpdateRequested;
                vm.PropertyChanged += this.OnViewModelPropertyChanged;
                this.ApplyTheme(vm.IsDarkTheme);
            }
        };
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
	        this.UpdateChart(vm.ChartDataPoints);
	        this.UpdateRpsHourlyChart(vm.RpsHourlyChartDataPoints);
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

        using var stream = new MemoryStream();
        bitmap.Save(stream);
        var pngBytes = stream.ToArray();

        if (OperatingSystem.IsWindows())
            SetPngClipboardWin32(pngBytes);
        else
            await SetPngClipboardLinuxAsync(pngBytes);
    }

    [SupportedOSPlatform("windows")]
    private static void SetPngClipboardWin32(byte[] pngBytes)
    {
        var format = RegisterClipboardFormat("PNG");

        var hMem = GlobalAlloc(0x0002 /* GMEM_MOVEABLE */, (UIntPtr)pngBytes.Length);
        if (hMem == IntPtr.Zero) return;

        var ptr = GlobalLock(hMem);
        if (ptr == IntPtr.Zero)
        {
            GlobalFree(hMem);
            return;
        }

        Marshal.Copy(pngBytes, 0, ptr, pngBytes.Length);
        GlobalUnlock(hMem);

        if (!OpenClipboard(IntPtr.Zero))
        {
            GlobalFree(hMem);
            return;
        }

        EmptyClipboard();
        // On success the OS owns hMem; on failure we still own it and must free it.
        if (SetClipboardData(format, hMem) == IntPtr.Zero)
            GlobalFree(hMem);

        CloseClipboard();
    }

    private static async Task SetPngClipboardLinuxAsync(byte[] pngBytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "daoc-screenshot.png");
        await File.WriteAllBytesAsync(path, pngBytes);
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "bash",
            Arguments = $"-c \"xclip -selection clipboard -t image/png -i '{path}'\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
        if (process != null) await process.WaitForExitAsync();
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