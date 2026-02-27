using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using DAoCLogWatcher.UI.Services;
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

        if (dataPoints.Count > 0)
        {
            var times = dataPoints.Select(p => p.Time.ToOADate()).ToArray();
            var rps   = dataPoints.Select(p => p.Rps).ToArray();

            var line = this.RpChart.Plot.Add.Scatter(times, rps);
            line.Color      = Color.FromHex("#00D9FF");
            line.LineWidth  = 2;
            line.MarkerSize = 0;

            this.RpChart.Plot.Axes.AutoScale();
        }

        this.RpChart.Refresh();
    }

    private void UpdateRpsHourlyChart(System.Collections.Generic.List<(DateTime Time, double RpsPerHour)> dataPoints)
    {
        this.RpsHourlyChart.Plot.Clear();

        if (dataPoints.Count > 0)
        {
            var times  = dataPoints.Select(p => p.Time.ToOADate()).ToArray();
            var values = dataPoints.Select(p => p.RpsPerHour).ToArray();

            var line = this.RpsHourlyChart.Plot.Add.Scatter(times, values);
            line.Color      = Color.FromHex("#FFAA44");
            line.LineWidth  = 2;
            line.MarkerSize = 0;

            this.RpsHourlyChart.Plot.Axes.AutoScale();
        }

        this.RpsHourlyChart.Refresh();
    }

    private async void OnScreenshotClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => await ClipboardService.CaptureWindowToClipboardAsync(this);
}
