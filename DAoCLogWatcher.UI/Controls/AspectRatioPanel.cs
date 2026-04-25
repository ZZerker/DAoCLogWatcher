using System;
using Avalonia;
using Avalonia.Controls;

namespace DAoCLogWatcher.UI.Controls;

/// Panel that maintains a fixed width:height ratio for its single child,
/// fitting within the available space.
public class AspectRatioPanel: Panel
{
	public static readonly StyledProperty<double> RatioProperty = AvaloniaProperty.Register<AspectRatioPanel, double>(nameof(Ratio), 1.0);

	public double Ratio
	{
		get => this.GetValue(RatioProperty);
		set => this.SetValue(RatioProperty, value);
	}

	protected override Size MeasureOverride(Size availableSize)
	{
		var size = this.ComputeSize(availableSize);
		foreach(var child in this.Children)
		{
			child.Measure(size);
		}

		return size;
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		var size = this.ComputeSize(finalSize);
		foreach(var child in this.Children)
		{
			child.Arrange(new Rect(0, 0, size.Width, size.Height));
		}

		return size;
	}

	private Size ComputeSize(Size available)
	{
		var ratio = this.Ratio > 0?this.Ratio:1.0;
		double w, h;

		if(double.IsInfinity(available.Width)&&double.IsInfinity(available.Height))
		{
			return new Size(0, 0);
		}
		else if(double.IsInfinity(available.Width))
		{
			h = available.Height;
			w = h * ratio;
		}
		else if(double.IsInfinity(available.Height))
		{
			w = available.Width;
			h = w / ratio;
		}
		else
		{
			w = Math.Min(available.Width, available.Height * ratio);
			h = w / ratio;
		}

		return new Size(Math.Max(0, w), Math.Max(0, h));
	}
}
