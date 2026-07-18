using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DAoCLogWatcher.UI.Views;

public partial class KWinRuleConsentDialog: Window
{
	public KWinRuleConsentDialog()
	{
		this.InitializeComponent();
	}

	private void OnApplyClick(object? sender, RoutedEventArgs e)
	{
		this.Close(true);
	}

	private void OnNotNowClick(object? sender, RoutedEventArgs e)
	{
		this.Close(false);
	}
}
