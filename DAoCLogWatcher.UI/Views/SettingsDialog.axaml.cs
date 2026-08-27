using Avalonia.Controls;
using Avalonia.Interactivity;
using DAoCLogWatcher.UI.ViewModels;

namespace DAoCLogWatcher.UI.Views;

public partial class SettingsDialog: Window
{
	public SettingsDialog()
	{
		this.InitializeComponent();
	}

	public SettingsDialog(MainWindowViewModel viewModel)
		: this()
	{
		this.DataContext = viewModel;
	}

	private void OnCloseClick(object? sender, RoutedEventArgs e)
	{
		this.Close();
	}
}
