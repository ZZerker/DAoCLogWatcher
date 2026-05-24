using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace DAoCLogWatcher.UI.Views.Controls;

public partial class PlayerNameControl : UserControl
{
    public static readonly StyledProperty<string?> PlayerNameProperty =
        AvaloniaProperty.Register<PlayerNameControl, string?>(nameof(PlayerName));

    public static readonly StyledProperty<IBrush?> NameForegroundProperty =
        AvaloniaProperty.Register<PlayerNameControl, IBrush?>(nameof(NameForeground));

    public static readonly StyledProperty<double> NameFontSizeProperty =
        AvaloniaProperty.Register<PlayerNameControl, double>(nameof(NameFontSize), 13.0);

    public string? PlayerName { get => GetValue(PlayerNameProperty); set => SetValue(PlayerNameProperty, value); }
    public IBrush? NameForeground { get => GetValue(NameForegroundProperty); set => SetValue(NameForegroundProperty, value); }
    public double NameFontSize { get => GetValue(NameFontSizeProperty); set => SetValue(NameFontSizeProperty, value); }

    public PlayerNameControl()
    {
        InitializeComponent();
    }

    private void OnClick(object? sender, RoutedEventArgs e)
    {
        var name = PlayerName;
        if(string.IsNullOrEmpty(name))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(
            $"https://eden-daoc.net/herald?n=player&k={Uri.EscapeDataString(name)}")
            { UseShellExecute = true });
    }
}
