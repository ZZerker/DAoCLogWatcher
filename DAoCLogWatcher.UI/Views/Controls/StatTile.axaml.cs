using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DAoCLogWatcher.UI.Views.Controls;

public partial class StatTile: UserControl
{
	public static readonly StyledProperty<string> LabelProperty = AvaloniaProperty.Register<StatTile, string>(nameof(Label), string.Empty);

	public static readonly StyledProperty<string?> ValueProperty = AvaloniaProperty.Register<StatTile, string?>(nameof(Value));

	public static readonly StyledProperty<IBrush?> ValueColorProperty = AvaloniaProperty.Register<StatTile, IBrush?>(nameof(ValueColor));

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

	public StatTile()
	{
		this.InitializeComponent();
	}
}
