using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DAoCLogWatcher.UI.Views.Controls;

public partial class StatNumber : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<StatNumber, string>(nameof(Label), string.Empty);

    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<StatNumber, string?>(nameof(Value));

    public static readonly StyledProperty<IBrush?> ValueColorProperty =
        AvaloniaProperty.Register<StatNumber, IBrush?>(nameof(ValueColor));

    public static readonly StyledProperty<double> ValueFontSizeProperty =
        AvaloniaProperty.Register<StatNumber, double>(nameof(ValueFontSize), 28.0);

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public IBrush? ValueColor { get => GetValue(ValueColorProperty); set => SetValue(ValueColorProperty, value); }
    public double ValueFontSize { get => GetValue(ValueFontSizeProperty); set => SetValue(ValueFontSizeProperty, value); }

    public StatNumber()
    {
        InitializeComponent();
    }
}
