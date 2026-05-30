using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DAoCLogWatcher.UI.Views.Controls;

public partial class StatNumber: UserControl
{
	public static readonly StyledProperty<string> LabelProperty = AvaloniaProperty.Register<StatNumber, string>(nameof(Label), string.Empty);

	public static readonly StyledProperty<string?> ValueProperty = AvaloniaProperty.Register<StatNumber, string?>(nameof(Value));

	public static readonly StyledProperty<IBrush?> ValueColorProperty = AvaloniaProperty.Register<StatNumber, IBrush?>(nameof(ValueColor));

	public static readonly StyledProperty<double> ValueFontSizeProperty = AvaloniaProperty.Register<StatNumber, double>(nameof(ValueFontSize), 28.0);

	public string Label
	{
		get => this.GetValue(LabelProperty);
		set => this.SetValue(LabelProperty, value);
	}

	public string? Value
	{
		get => this.GetValue(ValueProperty);
		set => this.SetValue(ValueProperty, value);
	}

	public IBrush? ValueColor
	{
		get => this.GetValue(ValueColorProperty);
		set => this.SetValue(ValueColorProperty, value);
	}

	public double ValueFontSize
	{
		get => this.GetValue(ValueFontSizeProperty);
		set => this.SetValue(ValueFontSizeProperty, value);
	}

	public StatNumber()
	{
		this.InitializeComponent();
	}
}
