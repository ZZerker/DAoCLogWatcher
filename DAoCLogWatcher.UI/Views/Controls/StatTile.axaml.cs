using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DAoCLogWatcher.UI.Views.Controls;

public partial class StatTile : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<StatTile, string>(nameof(Label), string.Empty);

    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<StatTile, string?>(nameof(Value));

    public static readonly StyledProperty<IBrush?> ValueColorProperty =
        AvaloniaProperty.Register<StatTile, IBrush?>(nameof(ValueColor));

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public IBrush? ValueColor { get => GetValue(ValueColorProperty); set => SetValue(ValueColorProperty, value); }

    public StatTile()
    {
        InitializeComponent();
    }
}
