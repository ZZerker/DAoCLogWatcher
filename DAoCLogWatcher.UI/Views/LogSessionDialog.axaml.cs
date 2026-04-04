using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DAoCLogWatcher.Core.Models;

namespace DAoCLogWatcher.UI.Views;

public partial class LogSessionDialog: Window
{
	public LogSession? SelectedSession { get; private set; }

	public LogSessionDialog()
	{
		this.InitializeComponent();
	}

	public LogSessionDialog(List<LogSession> sessions)
			: this()
	{
		this.SessionList.ItemsSource = sessions;
		this.Title = $"Select Log Session ({sessions.Count} sessions)";

		if(sessions.Count > 0)
		{
			this.SessionList.SelectedIndex = 0;
		}
	}

	private void OnOpenClick(object? sender, RoutedEventArgs e)
	{
		this.SelectedSession = this.SessionList.SelectedItem as LogSession;
		this.Close(this.SelectedSession);
	}

	private void OnCancelClick(object? sender, RoutedEventArgs e)
	{
		this.Close(null);
	}
}
