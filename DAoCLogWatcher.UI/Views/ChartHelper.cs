using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;

namespace DAoCLogWatcher.UI.Views;

internal static class ChartHelper
{
	public static void ApplyChartStyle(AvaPlot chart, string bg, string dataBg, string gridMajor, string gridMinor, string fg)
	{
		chart.Plot.FigureBackground.Color = Color.FromHex(bg);
		chart.Plot.DataBackground.Color = Color.FromHex(dataBg);
		chart.Plot.Grid.MajorLineColor = Color.FromHex(gridMajor);
		chart.Plot.Grid.MinorLineColor = Color.FromHex(gridMinor);
		chart.Plot.Axes.Color(Color.FromHex(fg));
		chart.Plot.Axes.Title.Label.ForeColor = Color.FromHex(fg);
	}

	public static (Marker highlight, Text tooltip) AddHoverOverlays(AvaPlot chart, string accentHex)
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

	public static void ApplyTheme(bool isDark, params AvaPlot[] charts)
	{
		var bg = isDark ? "#252525" : "#FAFAFA";
		var dataBg = isDark ? "#1E1E1E" : "#FFFFFF";
		var gridMaj = isDark ? "#3A3A3A" : "#D0D0D0";
		var gridMin = isDark ? "#2A2A2A" : "#EBEBEB";
		var fg = isDark ? "#CCCCCC" : "#333333";
		foreach(var chart in charts)
		{
			ApplyChartStyle(chart, bg, dataBg, gridMaj, gridMin, fg);
			chart.Refresh();
		}
	}

	public static void HandlePointerMoved(PointerEventArgs e, AvaPlot chart, Scatter? scatter, Marker? highlight, Text? tooltip, string unit)
	{
		if(scatter == null || highlight == null || tooltip == null)
		{
			return;
		}

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

	public static void HandlePointerExited(AvaPlot chart, Marker? highlight, Text? tooltip)
	{
		if(highlight == null || tooltip == null)
		{
			return;
		}

		highlight.IsVisible = false;
		tooltip.IsVisible = false;
		chart.Refresh();
	}

	public static void UpdateBarChart(AvaPlot chart, IEnumerable<(string Label, double Value)> data, string fillColor, int labelMaxLength = 12)
	{
		lock(chart.Plot.Sync)
		{
			chart.Plot.Clear();
			var sorted = data.OrderByDescending(d => d.Value).Take(7).ToList();

			if(sorted.Count > 0)
			{
				var bars = sorted.Select((d, i) => new Bar
				                                   {
						                                   Position = i,
						                                   Value = d.Value,
						                                   FillColor = Color.FromHex(fillColor)
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
