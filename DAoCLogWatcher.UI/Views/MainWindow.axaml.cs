using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using ScottPlot;

namespace DAoCLogWatcher.UI.Views;

public partial class MainWindow : Window
{
    private ViewModels.MainWindowViewModel? _vm;

    public MainWindow()
    {
        this.InitializeComponent();

        InitializeChart();
        InitializeRpsHourlyChart();

        this.DataContextChanged += (s, e) =>
        {
            if (_vm != null)
            {
                _vm.ChartUpdateRequested -= OnChartUpdateRequested;
                _vm.PropertyChanged -= OnViewModelPropertyChanged;
                _vm = null;
            }

            if (this.DataContext is ViewModels.MainWindowViewModel vm)
            {
                _vm = vm;
                vm.ChartUpdateRequested += OnChartUpdateRequested;
                vm.PropertyChanged += OnViewModelPropertyChanged;
                ApplyTheme(vm.IsDarkTheme);
            }
        };
    }

    private void InitializeChart()
    {
        ApplyChartStyle(RpChart, "#252525", "#1E1E1E", "#3A3A3A", "#2A2A2A", "#CCCCCC");
        RpChart.Plot.XLabel("Time (minutes)");
        RpChart.Plot.YLabel("Total RPs");
        RpChart.Refresh();
    }

    private void InitializeRpsHourlyChart()
    {
        ApplyChartStyle(RpsHourlyChart, "#252525", "#1E1E1E", "#3A3A3A", "#2A2A2A", "#CCCCCC");
        RpsHourlyChart.Plot.XLabel("Time (minutes)");
        RpsHourlyChart.Plot.YLabel("RP/h (rolling 1h)");
        RpsHourlyChart.Refresh();
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

        ApplyChartStyle(RpChart,        bg, dataBg, gridMaj, gridMin, fg);
        ApplyChartStyle(RpsHourlyChart, bg, dataBg, gridMaj, gridMin, fg);
        RpChart.Refresh();
        RpsHourlyChart.Refresh();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not ViewModels.MainWindowViewModel vm)
            return;

        if (e.PropertyName == nameof(ViewModels.MainWindowViewModel.IsDarkTheme))
            ApplyTheme(vm.IsDarkTheme);
        else if (e.PropertyName == nameof(ViewModels.MainWindowViewModel.IsSidebarVisible))
            MainContentGrid.ColumnDefinitions[0].Width = vm.IsSidebarVisible
                ? new GridLength(320)
                : new GridLength(0);
    }

    private void OnChartUpdateRequested(object? sender, EventArgs e)
    {
        if (this.DataContext is ViewModels.MainWindowViewModel vm)
        {
            UpdateChart(vm.ChartDataPoints);
            UpdateRpsHourlyChart(vm.RpsHourlyChartDataPoints);
        }
    }

    public void UpdateChart(System.Collections.Generic.List<(double Time, double Rps)> dataPoints)
    {
        RpChart.Plot.Clear();

        if (dataPoints.Count > 0)
        {
            var times = dataPoints.Select(p => p.Time).ToArray();
            var rps = dataPoints.Select(p => p.Rps).ToArray();

            var line = RpChart.Plot.Add.Scatter(times, rps);
            line.Color = Color.FromHex("#00D9FF");
            line.LineWidth = 2;
            line.MarkerSize = 0;

            RpChart.Plot.Axes.AutoScale();
        }

        RpChart.Refresh();
    }

    public void UpdateRpsHourlyChart(System.Collections.Generic.List<(double TimeMinutes, double RpsPerHour)> dataPoints)
    {
        RpsHourlyChart.Plot.Clear();

        if (dataPoints.Count > 0)
        {
            var times = dataPoints.Select(p => p.TimeMinutes).ToArray();
            var values = dataPoints.Select(p => p.RpsPerHour).ToArray();

            var line = RpsHourlyChart.Plot.Add.Scatter(times, values);
            line.Color = Color.FromHex("#FFAA44");
            line.LineWidth = 2;
            line.MarkerSize = 0;

            RpsHourlyChart.Plot.Axes.AutoScale();
        }

        RpsHourlyChart.Refresh();
    }
}