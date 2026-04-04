using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using DAoCLogWatcher.UI.Services;
using ScottPlot;

namespace DAoCLogWatcher.UI.Views;

public partial class MainWindow: Window
{
	private const int MAX_BARS_PER_CHART = 7;
	private const int DEFAULT_LABEL_MAX_LENGTH = 12;
	private const int DMG_LABEL_MAX_LENGTH = 14;
	private const int COMBAT_CHART_DEBOUNCE_MS = 250;

	private ViewModels.MainWindowViewModel? vm;

	private DispatcherTimer? combatChartDebounceTimer;

	private ScottPlot.Plottables.Scatter? rpScatter;
	private ScottPlot.Plottables.Marker? rpHighlight;
	private ScottPlot.Plottables.Text? rpTooltip;

	private ScottPlot.Plottables.Scatter? rpsScatter;
	private ScottPlot.Plottables.Marker? rpsHighlight;
	private ScottPlot.Plottables.Text? rpsTooltip;

	public MainWindow()
	{
		this.InitializeComponent();

		this.InitializeChart();
		this.InitializeRpsHourlyChart();
		this.InitializeHealsByHealerChart();
		this.InitializeAvgDmgBySpellChart();
		this.InitializeDmgByTargetChart();
		this.InitializeHealsByTargetChart();

		this.RpChart.PointerMoved += (s, e) => this.OnChartPointerMoved(e, this.RpChart, this.rpScatter, this.rpHighlight, this.rpTooltip, "RP");
		this.RpChart.PointerExited += (s, e) => this.OnChartPointerExited(this.RpChart, this.rpHighlight, this.rpTooltip);

		this.RpsHourlyChart.PointerMoved += (s, e) => this.OnChartPointerMoved(e, this.RpsHourlyChart, this.rpsScatter, this.rpsHighlight, this.rpsTooltip, "RP/h");
		this.RpsHourlyChart.PointerExited += (s, e) => this.OnChartPointerExited(this.RpsHourlyChart, this.rpsHighlight, this.rpsTooltip);

		this.DataContextChanged += (s, e) =>
		                           {
			                           if(this.vm != null)
			                           {
				                           this.vm.ChartData.UpdateRequested -= this.OnChartUpdateRequested;
				                           this.vm.PropertyChanged -= this.OnViewModelPropertyChanged;
				                           this.vm.CombatSummary.PropertyChanged -= this.OnCombatSummaryPropertyChanged;
				                           this.vm.CombatSummary.ResetRequested -= this.OnCombatSummaryResetRequested;
				                           this.vm = null;
			                           }

			                           if(this.DataContext is ViewModels.MainWindowViewModel vm)
			                           {
				                           this.vm = vm;
				                           vm.ChartData.UpdateRequested += this.OnChartUpdateRequested;
				                           vm.PropertyChanged += this.OnViewModelPropertyChanged;
				                           vm.CombatSummary.PropertyChanged += this.OnCombatSummaryPropertyChanged;
				                           vm.CombatSummary.ResetRequested += this.OnCombatSummaryResetRequested;
				                           this.ApplyTheme(vm.IsDarkTheme);
			                           }
		                           };
	}

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint attr, ref int attrValue, uint attrSize);

	private const uint IMMERSIVE_DARK_MODE = 20; // DWMWA_USE_ImmersiveDarkMode, Windows 10+
	private const uint CAPTION_COLOR = 35; // DWMWA_CaptionColor, Windows 11+

	private void ApplyDarkTitleBar()
	{
		if(!OperatingSystem.IsWindows()) return;
		if(this.TryGetPlatformHandle() is not { } handle) return;

		var hwnd = handle.Handle;
		var dark = 1;
		DwmSetWindowAttribute(hwnd, IMMERSIVE_DARK_MODE, ref dark, 4u);

		// Pin caption to dark colour — silently no-ops on Windows 10
		var captionColor = 0x00252525; // #252525 as 0x00BBGGRR (grey, so R=G=B)
		DwmSetWindowAttribute(hwnd, CAPTION_COLOR, ref captionColor, 4u);
	}

	protected override void OnOpened(EventArgs e)
	{
		base.OnOpened(e);

		this.ApplyDarkTitleBar();

		// Move to a secondary screen if one is available — DAoC typically runs full-screen
		// on the primary monitor, so prefer any non-primary screen for the log watcher.
		var secondary = this.Screens.All.FirstOrDefault(s => !s.IsPrimary);
		if(secondary != null)
			this.Position = secondary.WorkingArea.TopLeft;

		var screen = this.Screens.ScreenFromWindow(this);
		if(screen == null) return;

		var workH = screen.WorkingArea.Height / screen.Scaling;
		if(workH < 1268)
		{
			this.Height = workH;

			if(this.DataContext is ViewModels.MainWindowViewModel newVm)
			{
				newVm.IsAbsoluteNumbersVisible = false;
				newVm.IsAbsoluteRpsVisible = false;
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

	private static (ScottPlot.Plottables.Marker highlight, ScottPlot.Plottables.Text tooltip) AddHoverOverlays(ScottPlot.Avalonia.AvaPlot chart, string accentHex)
	{
		var highlight = chart.Plot.Add.Marker(0, 0);
		highlight.Shape = MarkerShape.OpenCircle;
		highlight.Size = 12;
		highlight.Color = Color.FromHex(accentHex);
		highlight.IsVisible = false;

		var tooltip = chart.Plot.Add.Text("", 0, 0);
		tooltip.IsVisible = false;
		tooltip.LabelFontSize = 11;
		tooltip.LabelFontColor = Color.FromHex("#CCCCCC");
		tooltip.LabelBackgroundColor = Color.FromHex("#1E1E1E");
		tooltip.LabelBorderColor = Color.FromHex(accentHex);
		tooltip.LabelBorderWidth = 1;
		tooltip.LabelPadding = 5;
		tooltip.OffsetX = 10;
		tooltip.OffsetY = -10;

		return (highlight, tooltip);
	}

	private static void ApplyChartStyle(ScottPlot.Avalonia.AvaPlot chart, string bg, string dataBg, string gridMajor, string gridMinor, string fg)
	{
		chart.Plot.FigureBackground.Color = Color.FromHex(bg);
		chart.Plot.DataBackground.Color = Color.FromHex(dataBg);
		chart.Plot.Grid.MajorLineColor = Color.FromHex(gridMajor);
		chart.Plot.Grid.MinorLineColor = Color.FromHex(gridMinor);
		chart.Plot.Axes.Color(Color.FromHex(fg));
		chart.Plot.Axes.Title.Label.ForeColor = Color.FromHex(fg);
	}

	private void ApplyTheme(bool isDark)
	{
		Application.Current!.RequestedThemeVariant = isDark?ThemeVariant.Dark:ThemeVariant.Light;

		var bg = isDark?"#252525":"#FAFAFA";
		var dataBg = isDark?"#1E1E1E":"#FFFFFF";
		var gridMaj = isDark?"#3A3A3A":"#D0D0D0";
		var gridMin = isDark?"#2A2A2A":"#EBEBEB";
		var fg = isDark?"#CCCCCC":"#333333";

		ApplyChartStyle(this.RpChart, bg, dataBg, gridMaj, gridMin, fg);
		ApplyChartStyle(this.RpsHourlyChart, bg, dataBg, gridMaj, gridMin, fg);
		ApplyChartStyle(this.HealsByHealerChart, bg, dataBg, gridMaj, gridMin, fg);
		ApplyChartStyle(this.AvgDmgBySpellChart, bg, dataBg, gridMaj, gridMin, fg);
		ApplyChartStyle(this.DmgByTargetChart, bg, dataBg, gridMaj, gridMin, fg);
		ApplyChartStyle(this.HealsByTargetChart, bg, dataBg, gridMaj, gridMin, fg);
		this.RpChart.Refresh();
		this.RpsHourlyChart.Refresh();
		this.HealsByHealerChart.Refresh();
		this.AvgDmgBySpellChart.Refresh();
		this.DmgByTargetChart.Refresh();
		this.HealsByTargetChart.Refresh();
	}

	private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if(sender is not ViewModels.MainWindowViewModel newVm) return;

		if(e.PropertyName == nameof(ViewModels.MainWindowViewModel.IsDarkTheme))
			this.ApplyTheme(newVm.IsDarkTheme);
		else if(e.PropertyName == nameof(ViewModels.MainWindowViewModel.IsSidebarVisible))
			this.MainContentGrid.ColumnDefinitions[0].Width = newVm.IsSidebarVisible?new GridLength(320):new GridLength(0);
	}

	private void OnChartUpdateRequested(object? sender, EventArgs e)
	{
		if(this.DataContext is ViewModels.MainWindowViewModel newVm)
		{
			this.UpdateChart(newVm.ChartData.CumulativeDataPoints);
			this.UpdateRpsHourlyChart(newVm.ChartData.HourlyDataPoints);
		}
	}

	private void UpdateChart(List<(DateTime Time, double Rps)> dataPoints)
	{
		lock(this.RpChart.Plot.Sync)
		{
			this.RpChart.Plot.Clear();
			this.rpScatter = null;

			if(dataPoints.Count > 0)
			{
				var times = dataPoints.Select(p => p.Time.ToOADate()).ToArray();
				var rps = dataPoints.Select(p => p.Rps).ToArray();

				this.rpScatter = this.RpChart.Plot.Add.Scatter(times, rps);
				this.rpScatter.Color = Color.FromHex("#00D9FF");
				this.rpScatter.LineWidth = 2;
				this.rpScatter.MarkerSize = 0;

				this.RpChart.Plot.Axes.AutoScale();
			}

			(this.rpHighlight, this.rpTooltip) = AddHoverOverlays(this.RpChart, "#00D9FF");
		}

		this.RpChart.Refresh();
	}

	private void UpdateRpsHourlyChart(List<(DateTime Time, double RpsPerHour)> dataPoints)
	{
		lock(this.RpsHourlyChart.Plot.Sync)
		{
			this.RpsHourlyChart.Plot.Clear();
			this.rpsScatter = null;

			if(dataPoints.Count > 0)
			{
				var times = dataPoints.Select(p => p.Time.ToOADate()).ToArray();
				var values = dataPoints.Select(p => p.RpsPerHour).ToArray();

				this.rpsScatter = this.RpsHourlyChart.Plot.Add.Scatter(times, values);
				this.rpsScatter.Color = Color.FromHex("#FFAA44");
				this.rpsScatter.LineWidth = 2;
				this.rpsScatter.MarkerSize = 0;

				this.RpsHourlyChart.Plot.Axes.AutoScale();
			}

			(this.rpsHighlight, this.rpsTooltip) = AddHoverOverlays(this.RpsHourlyChart, "#FFAA44");
		}

		this.RpsHourlyChart.Refresh();
	}

	private void OnChartPointerMoved(PointerEventArgs e, ScottPlot.Avalonia.AvaPlot chart, ScottPlot.Plottables.Scatter? scatter, ScottPlot.Plottables.Marker? highlight, ScottPlot.Plottables.Text? tooltip, string unit)
	{
		if(scatter == null||highlight == null||tooltip == null) return;

		var pos = e.GetPosition(chart);
		var pixel = new Pixel((float)pos.X, (float)pos.Y);
		var coords = chart.Plot.GetCoordinates(pixel);

		var nearest = scatter.GetNearest(coords, chart.Plot.LastRender);

		highlight.IsVisible = nearest.IsReal;
		tooltip.IsVisible = nearest.IsReal;

		if(nearest.IsReal)
		{
			highlight.Location = nearest.Coordinates;
			tooltip.Location = nearest.Coordinates;
			var time = DateTime.FromOADate(nearest.X).ToString("HH:mm:ss");
			tooltip.LabelText = $"{time}\n{nearest.Y:N0} {unit}";
		}

		chart.Refresh();
	}

	private void OnChartPointerExited(ScottPlot.Avalonia.AvaPlot chart, ScottPlot.Plottables.Marker? highlight, ScottPlot.Plottables.Text? tooltip)
	{
		if(highlight == null||tooltip == null) return;
		highlight.IsVisible = false;
		tooltip.IsVisible = false;
		chart.Refresh();
	}

	private async void OnScreenshotClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => await ClipboardService.CaptureWindowToClipboardAsync(this);

	private void InitializeHealsByHealerChart()
	{
		ApplyChartStyle(this.HealsByHealerChart, "#252525", "#1E1E1E", "#3A3A3A", "#2A2A2A", "#CCCCCC");
		this.HealsByHealerChart.Plot.YLabel("HP healed");
		this.HealsByHealerChart.UserInputProcessor.IsEnabled = false;
		this.HealsByHealerChart.Refresh();
	}

	private void InitializeAvgDmgBySpellChart()
	{
		ApplyChartStyle(this.AvgDmgBySpellChart, "#252525", "#1E1E1E", "#3A3A3A", "#2A2A2A", "#CCCCCC");
		this.AvgDmgBySpellChart.Plot.YLabel("Avg dmg / hit");
		this.AvgDmgBySpellChart.UserInputProcessor.IsEnabled = false;
		this.AvgDmgBySpellChart.Refresh();
	}

	private void OnCombatSummaryResetRequested(object? sender, EventArgs e)
	{
		this.UpdateAvgDmgBySpellChart();
		this.UpdateDmgByTargetChart();
		this.UpdateHealsByHealerChart();
		this.UpdateHealsByTargetChart();
	}

	private void OnCombatSummaryPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if(e.PropertyName is not ("TotalHealsReceived" or "TotalDamageDealt" or "TotalDamageTaken" or "TotalHealingDone"))
			return;

		if(this.combatChartDebounceTimer == null)
		{
			this.combatChartDebounceTimer = new DispatcherTimer
			                                {
					                                Interval = TimeSpan.FromMilliseconds(COMBAT_CHART_DEBOUNCE_MS)
			                                };
			this.combatChartDebounceTimer.Tick += (_, _) =>
			                                      {
				                                      this.combatChartDebounceTimer.Stop();
				                                      this.UpdateAllCombatCharts();
			                                      };
		}

		this.combatChartDebounceTimer.Stop();
		this.combatChartDebounceTimer.Start();
	}

	private void UpdateAllCombatCharts()
	{
		this.UpdateAvgDmgBySpellChart();
		this.UpdateDmgByTargetChart();
		this.UpdateHealsByHealerChart();
		this.UpdateHealsByTargetChart();
	}

	private void InitializeHealsByTargetChart()
	{
		ApplyChartStyle(this.HealsByTargetChart, "#252525", "#1E1E1E", "#3A3A3A", "#2A2A2A", "#CCCCCC");
		this.HealsByTargetChart.Plot.YLabel("HP healed");
		this.HealsByTargetChart.UserInputProcessor.IsEnabled = false;
		this.HealsByTargetChart.Refresh();
	}

	private void UpdateHealsByTargetChart() => UpdateBarChart(this.HealsByTargetChart, this.vm?.CombatSummary.HealsByTarget.Select(kv => (kv.Key, (double)kv.Value)) ?? [], "#00D968");

	private void UpdateHealsByHealerChart() => UpdateBarChart(this.HealsByHealerChart, this.vm?.CombatSummary.HealsByHealer.Select(kv => (kv.Key, (double)kv.Value)) ?? [], "#00D968");

	private void InitializeDmgByTargetChart()
	{
		ApplyChartStyle(this.DmgByTargetChart, "#252525", "#1E1E1E", "#3A3A3A", "#2A2A2A", "#CCCCCC");
		this.DmgByTargetChart.Plot.YLabel("Dmg taken");
		this.DmgByTargetChart.UserInputProcessor.IsEnabled = false;
		this.DmgByTargetChart.Refresh();
	}

	private void UpdateDmgByTargetChart() => UpdateBarChart(this.DmgByTargetChart, this.vm?.CombatSummary.DamageTakenByAttacker.Select(kv => (kv.Key, (double)kv.Value)) ?? [], "#DC3545", DMG_LABEL_MAX_LENGTH);

	private void UpdateAvgDmgBySpellChart() =>
			UpdateBarChart(this.AvgDmgBySpellChart, this.vm?.CombatSummary.DamageBySpell.Where(kv => kv.Value.HitCount > 0).Select(kv => (kv.Key, (double)kv.Value.TotalDamage / kv.Value.HitCount)) ?? [], "#FF6644", DMG_LABEL_MAX_LENGTH);

	private static void UpdateBarChart(ScottPlot.Avalonia.AvaPlot chart, IEnumerable<(string Label, double Value)> data, string fillColor, int labelMaxLength = DEFAULT_LABEL_MAX_LENGTH)
	{
		lock(chart.Plot.Sync)
		{
			chart.Plot.Clear();
			var sorted = data.OrderByDescending(d => d.Value).Take(MAX_BARS_PER_CHART).ToList();

			if(sorted.Count > 0)
			{
				var bars = sorted.Select((d, i) => new ScottPlot.Bar
				                                   {
						                                   Position = i,
						                                   Value = d.Value,
						                                   FillColor = Color.FromHex(fillColor),
				                                   }).ToArray();

				chart.Plot.Add.Bars(bars);

				for(var i = 0; i < bars.Length; i++)
				{
					var label = chart.Plot.Add.Text(bars[i].Value.ToString("N0"), i, bars[i].Value);
					label.LabelFontSize = 10;
					label.LabelFontColor = Color.FromHex("#CCCCCC");
					label.LabelAlignment = Alignment.LowerCenter;
					label.OffsetY = -4;
				}

				var positions = Enumerable.Range(0, sorted.Count).Select(i => (double)i).ToArray();
				var labels = sorted.Select(d => d.Label.Length > labelMaxLength?d.Label[..labelMaxLength]:d.Label).ToArray();
				chart.Plot.Axes.Bottom.SetTicks(positions, labels);
				chart.Plot.Axes.AutoScale();
				chart.Plot.Axes.SetLimitsY(0, bars.Max(b => b.Value) * 1.2);
			}
		}

		chart.Refresh();
	}
}
