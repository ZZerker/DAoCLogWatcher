using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using DAoCLogWatcher.UI.Services;
using ScottPlot;

namespace DAoCLogWatcher.UI.Views;

public partial class MainWindow : Window
{
    private ViewModels.MainWindowViewModel? _vm;

    private ScottPlot.Plottables.Scatter? rpScatter;
    private ScottPlot.Plottables.Marker?  rpHighlight;
    private ScottPlot.Plottables.Text?    rpTooltip;

    private ScottPlot.Plottables.Scatter? rpsScatter;
    private ScottPlot.Plottables.Marker?  rpsHighlight;
    private ScottPlot.Plottables.Text?    rpsTooltip;

    public MainWindow()
    {
        this.InitializeComponent();

        this.InitializeChart();
        this.InitializeRpsHourlyChart();

        this.RpChart.PointerMoved  += (s, e) => this.OnChartPointerMoved(e, this.RpChart,        this.rpScatter,  this.rpHighlight,  this.rpTooltip,  "RP");
        this.RpChart.PointerExited += (s, e) => this.OnChartPointerExited(this.RpChart,           this.rpHighlight,  this.rpTooltip);

        this.RpsHourlyChart.PointerMoved  += (s, e) => this.OnChartPointerMoved(e, this.RpsHourlyChart, this.rpsScatter, this.rpsHighlight, this.rpsTooltip, "RP/h");
        this.RpsHourlyChart.PointerExited += (s, e) => this.OnChartPointerExited(this.RpsHourlyChart,    this.rpsHighlight, this.rpsTooltip);

        this.DataContextChanged += (s, e) =>
        {
            if (this._vm != null)
            {
                this._vm.ChartData.UpdateRequested -= this.OnChartUpdateRequested;
                this._vm.PropertyChanged           -= this.OnViewModelPropertyChanged;
                this._vm = null;
            }

            if (this.DataContext is ViewModels.MainWindowViewModel vm)
            {
                this._vm = vm;
                vm.ChartData.UpdateRequested += this.OnChartUpdateRequested;
                vm.PropertyChanged           += this.OnViewModelPropertyChanged;
                this.ApplyTheme(vm.IsDarkTheme);
            }
        };
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint attr, ref int attrValue, uint attrSize);

    private const uint IMMERSIVE_DARK_MODE = 20; // DWMWA_USE_IMMERSIVE_DARK_MODE, Windows 10+
    private const uint CAPTION_COLOR       = 35; // DWMWA_CAPTION_COLOR, Windows 11+

    private void ApplyDarkTitleBar()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (this.TryGetPlatformHandle() is not { } handle) return;

        var hwnd = handle.Handle;
        int dark = 1;
        DwmSetWindowAttribute(hwnd, IMMERSIVE_DARK_MODE, ref dark, 4u);

        // Pin caption to dark colour — silently no-ops on Windows 10
        int captionColor = 0x00252525; // #252525 as 0x00BBGGRR (grey, so R=G=B)
        DwmSetWindowAttribute(hwnd, CAPTION_COLOR, ref captionColor, 4u);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        this.ApplyDarkTitleBar();

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
        (this.rpHighlight, this.rpTooltip) = AddHoverOverlays(this.RpChart, "#00D9FF");
        this.RpChart.Refresh();
    }

    private void InitializeRpsHourlyChart()
    {
        ApplyChartStyle(this.RpsHourlyChart, "#252525", "#1E1E1E", "#3A3A3A", "#2A2A2A", "#CCCCCC");
        this.RpsHourlyChart.Plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.DateTimeAutomatic();
        this.RpsHourlyChart.Plot.YLabel("RP/h (rolling 1h)");
        (this.rpsHighlight, this.rpsTooltip) = AddHoverOverlays(this.RpsHourlyChart, "#FFAA44");
        this.RpsHourlyChart.Refresh();
    }

    private static (ScottPlot.Plottables.Marker highlight, ScottPlot.Plottables.Text tooltip)
        AddHoverOverlays(ScottPlot.Avalonia.AvaPlot chart, string accentHex)
    {
        var highlight = chart.Plot.Add.Marker(0, 0);
        highlight.Shape     = MarkerShape.OpenCircle;
        highlight.Size      = 12;
        highlight.Color     = Color.FromHex(accentHex);
        highlight.IsVisible = false;

        var tooltip = chart.Plot.Add.Text("", 0, 0);
        tooltip.IsVisible            = false;
        tooltip.LabelFontSize        = 11;
        tooltip.LabelFontColor       = Color.FromHex("#CCCCCC");
        tooltip.LabelBackgroundColor = Color.FromHex("#1E1E1E");
        tooltip.LabelBorderColor     = Color.FromHex(accentHex);
        tooltip.LabelBorderWidth     = 1;
        tooltip.LabelPadding         = 5;
        tooltip.OffsetX              = 10;
        tooltip.OffsetY              = -10;

        return (highlight, tooltip);
    }

    private static void ApplyChartStyle(
        ScottPlot.Avalonia.AvaPlot chart,
        string bg, string dataBg, string gridMajor, string gridMinor, string fg)
    {
        chart.Plot.FigureBackground.Color    = Color.FromHex(bg);
        chart.Plot.DataBackground.Color      = Color.FromHex(dataBg);
        chart.Plot.Grid.MajorLineColor       = Color.FromHex(gridMajor);
        chart.Plot.Grid.MinorLineColor       = Color.FromHex(gridMinor);
        chart.Plot.Axes.Color(Color.FromHex(fg));
        chart.Plot.Axes.Title.Label.ForeColor = Color.FromHex(fg);
    }

    private void ApplyTheme(bool isDark)
    {
        Application.Current!.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;

        var bg      = isDark ? "#252525" : "#FAFAFA";
        var dataBg  = isDark ? "#1E1E1E" : "#FFFFFF";
        var gridMaj = isDark ? "#3A3A3A" : "#D0D0D0";
        var gridMin = isDark ? "#2A2A2A" : "#EBEBEB";
        var fg      = isDark ? "#CCCCCC" : "#333333";

        ApplyChartStyle(this.RpChart,        bg, dataBg, gridMaj, gridMin, fg);
        ApplyChartStyle(this.RpsHourlyChart, bg, dataBg, gridMaj, gridMin, fg);
        this.RpChart.Refresh();
        this.RpsHourlyChart.Refresh();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not ViewModels.MainWindowViewModel vm) return;

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

    private void UpdateChart(System.Collections.Generic.List<(DateTime Time, double Rps)> dataPoints)
    {
        this.RpChart.Plot.Clear();
        this.rpScatter = null;

        if (dataPoints.Count > 0)
        {
            var times = dataPoints.Select(p => p.Time.ToOADate()).ToArray();
            var rps   = dataPoints.Select(p => p.Rps).ToArray();

            this.rpScatter = this.RpChart.Plot.Add.Scatter(times, rps);
            this.rpScatter.Color      = Color.FromHex("#00D9FF");
            this.rpScatter.LineWidth  = 2;
            this.rpScatter.MarkerSize = 0;

            this.RpChart.Plot.Axes.AutoScale();
        }

        (this.rpHighlight, this.rpTooltip) = AddHoverOverlays(this.RpChart, "#00D9FF");
        this.RpChart.Refresh();
    }

    private void UpdateRpsHourlyChart(System.Collections.Generic.List<(DateTime Time, double RpsPerHour)> dataPoints)
    {
        this.RpsHourlyChart.Plot.Clear();
        this.rpsScatter = null;

        if (dataPoints.Count > 0)
        {
            var times  = dataPoints.Select(p => p.Time.ToOADate()).ToArray();
            var values = dataPoints.Select(p => p.RpsPerHour).ToArray();

            this.rpsScatter = this.RpsHourlyChart.Plot.Add.Scatter(times, values);
            this.rpsScatter.Color      = Color.FromHex("#FFAA44");
            this.rpsScatter.LineWidth  = 2;
            this.rpsScatter.MarkerSize = 0;

            this.RpsHourlyChart.Plot.Axes.AutoScale();
        }

        (this.rpsHighlight, this.rpsTooltip) = AddHoverOverlays(this.RpsHourlyChart, "#FFAA44");
        this.RpsHourlyChart.Refresh();
    }

    private void OnChartPointerMoved(
        PointerEventArgs e,
        ScottPlot.Avalonia.AvaPlot chart,
        ScottPlot.Plottables.Scatter? scatter,
        ScottPlot.Plottables.Marker? highlight,
        ScottPlot.Plottables.Text? tooltip,
        string unit)
    {
        if (scatter == null || highlight == null || tooltip == null) return;

        var pos    = e.GetPosition(chart);
        var pixel  = new Pixel((float)pos.X, (float)pos.Y);
        var coords = chart.Plot.GetCoordinates(pixel);

        var nearest = scatter.GetNearest(coords, chart.Plot.LastRender);

        highlight.IsVisible = nearest.IsReal;
        tooltip.IsVisible   = nearest.IsReal;

        if (nearest.IsReal)
        {
            highlight.Location = nearest.Coordinates;
            tooltip.Location   = nearest.Coordinates;
            var time           = DateTime.FromOADate(nearest.X).ToString("HH:mm:ss");
            tooltip.LabelText  = $"{time}\n{nearest.Y:N0} {unit}";
        }

        chart.Refresh();
    }

    private void OnChartPointerExited(
        ScottPlot.Avalonia.AvaPlot chart,
        ScottPlot.Plottables.Marker? highlight,
        ScottPlot.Plottables.Text? tooltip)
    {
        if (highlight == null || tooltip == null) return;
        highlight.IsVisible = false;
        tooltip.IsVisible   = false;
        chart.Refresh();
    }

    private async void OnScreenshotClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => await ClipboardService.CaptureWindowToClipboardAsync(this);
}
