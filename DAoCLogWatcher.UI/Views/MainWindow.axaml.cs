using System;
using System.Linq;
using Avalonia.Controls;
using ScottPlot;

namespace DAoCLogWatcher.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        this.InitializeComponent();

        InitializeChart();

        this.DataContextChanged += (s, e) =>
        {
            Console.WriteLine($"[MainWindow] DataContext changed to: {this.DataContext?.GetType().Name}");
            if (this.DataContext != null)
            {
                var vm = this.DataContext as ViewModels.MainWindowViewModel;
                Console.WriteLine($"[MainWindow] ViewModel LogEntries count: {vm?.LogEntries?.Count}");

                if (vm != null)
                {
                    vm.ChartUpdateRequested += OnChartUpdateRequested;
                }
            }
        };
    }

    private void InitializeChart()
    {
        // Configure dark theme for chart (ScottPlot 5 API)
        RpChart.Plot.FigureBackground.Color = Color.FromHex("#252525");
        RpChart.Plot.DataBackground.Color = Color.FromHex("#1E1E1E");

        // Grid styling
        RpChart.Plot.Grid.MajorLineColor = Color.FromHex("#3A3A3A");
        RpChart.Plot.Grid.MinorLineColor = Color.FromHex("#2A2A2A");

        // Axis styling
        RpChart.Plot.Axes.Color(Color.FromHex("#CCCCCC"));

        // Set axis labels
        RpChart.Plot.XLabel("Time (minutes)");
        RpChart.Plot.YLabel("Cumulative Realm Points");

        RpChart.Plot.Axes.Title.Label.ForeColor = Color.FromHex("#FFFFFF");

        RpChart.Refresh();
    }

    private void OnChartUpdateRequested(object? sender, EventArgs e)
    {
        if (this.DataContext is ViewModels.MainWindowViewModel vm)
        {
            UpdateChart(vm.ChartDataPoints);
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
}