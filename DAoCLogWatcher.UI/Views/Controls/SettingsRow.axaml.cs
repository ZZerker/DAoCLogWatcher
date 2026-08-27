using Avalonia;
using Avalonia.Controls;

namespace DAoCLogWatcher.UI.Views.Controls;

public partial class SettingsRow: UserControl
{
	public static readonly StyledProperty<string> LabelProperty = AvaloniaProperty.Register<SettingsRow, string>(nameof(Label), string.Empty);

	public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<SettingsRow, string?>(nameof(Description));

	public static readonly StyledProperty<object?> ActionProperty = AvaloniaProperty.Register<SettingsRow, object?>(nameof(Action));

	public string Label
	{
		get => this.GetValue(LabelProperty);
		set => this.SetValue(LabelProperty, value);
	}

	public string? Description
	{
		get => this.GetValue(DescriptionProperty);
		set => this.SetValue(DescriptionProperty, value);
	}

	public object? Action
	{
		get => this.GetValue(ActionProperty);
		set => this.SetValue(ActionProperty, value);
	}

	public SettingsRow()
	{
		this.InitializeComponent();
	}
}
