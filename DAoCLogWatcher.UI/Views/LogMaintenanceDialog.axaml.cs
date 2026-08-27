using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DAoCLogWatcher.UI.ViewModels;

namespace DAoCLogWatcher.UI.Views;

public partial class LogMaintenanceDialog: Window
{
	public LogMaintenanceDialog()
	{
		this.InitializeComponent();
	}

	public LogMaintenanceDialog(LogMaintenanceViewModel vm)
			: this()
	{
		this.DataContext = vm;
	}

	private void OnOpened(object? sender, EventArgs e)
	{
		if(this.DataContext is LogMaintenanceViewModel vm)
		{
			vm.AnalyzeCommand.Execute(null);
		}
	}

	private void OnCloseClick(object? sender, RoutedEventArgs e)
	{
		this.Close();
	}
}
