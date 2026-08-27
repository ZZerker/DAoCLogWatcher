using Avalonia;
using Avalonia.Controls;

namespace DAoCLogWatcher.UI.Views.Controls;

public partial class SettingsSection: UserControl
{
	public static readonly StyledProperty<string> HeaderProperty = AvaloniaProperty.Register<SettingsSection, string>(nameof(Header), string.Empty);

	public static readonly StyledProperty<object?> SectionContentProperty = AvaloniaProperty.Register<SettingsSection, object?>(nameof(SectionContent));

	public string Header
	{
		get => this.GetValue(HeaderProperty);
		set => this.SetValue(HeaderProperty, value);
	}

	public object? SectionContent
	{
		get => this.GetValue(SectionContentProperty);
		set => this.SetValue(SectionContentProperty, value);
	}

	public SettingsSection()
	{
		this.InitializeComponent();
	}
}
