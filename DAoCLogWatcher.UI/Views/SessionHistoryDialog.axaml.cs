using Avalonia.Controls;
using Avalonia.Interactivity;
using DAoCLogWatcher.UI.ViewModels;

namespace DAoCLogWatcher.UI.Views;

public partial class SessionHistoryDialog: Window
{
	public SessionHistoryDialog()
	{
		this.InitializeComponent();
	}

	public SessionHistoryDialog(SessionHistoryViewModel viewModel)
			: this()
	{
		this.DataContext = viewModel;
	}

	private void OnCloseClick(object? sender, RoutedEventArgs e)
	{
		this.Close();
	}
}
