using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DAoCLogWatcher.UI.Views.Controls;

public partial class StatBarRow : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<StatBarRow, string>(nameof(Label), string.Empty);

    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<StatBarRow, string?>(nameof(Value));

    public static readonly StyledProperty<double> PercentageProperty =
        AvaloniaProperty.Register<StatBarRow, double>(nameof(Percentage));

    public static readonly StyledProperty<IBrush?> BarColorProperty =
        AvaloniaProperty.Register<StatBarRow, IBrush?>(nameof(BarColor));

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Percentage { get => GetValue(PercentageProperty); set => SetValue(PercentageProperty, value); }
    public IBrush? BarColor { get => GetValue(BarColorProperty); set => SetValue(BarColorProperty, value); }

    public StatBarRow()
    {
        InitializeComponent();
    }
}
