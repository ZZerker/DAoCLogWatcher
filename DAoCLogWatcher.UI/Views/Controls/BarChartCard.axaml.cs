using System.Collections;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DAoCLogWatcher.UI.Views.Controls;

public partial class BarChartCard : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<BarChartCard, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<string?> TotalValueProperty =
        AvaloniaProperty.Register<BarChartCard, string?>(nameof(TotalValue));

    public static readonly StyledProperty<IBrush?> TotalValueColorProperty =
        AvaloniaProperty.Register<BarChartCard, IBrush?>(nameof(TotalValueColor));

    public static readonly StyledProperty<string> ChartTitleProperty =
        AvaloniaProperty.Register<BarChartCard, string>(nameof(ChartTitle), string.Empty);

    public static readonly StyledProperty<string?> ToggleIconProperty =
        AvaloniaProperty.Register<BarChartCard, string?>(nameof(ToggleIcon));

    public static readonly StyledProperty<ICommand?> ToggleCommandProperty =
        AvaloniaProperty.Register<BarChartCard, ICommand?>(nameof(ToggleCommand));

    public static readonly StyledProperty<bool> IsChartVisibleProperty =
        AvaloniaProperty.Register<BarChartCard, bool>(nameof(IsChartVisible));

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<BarChartCard, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<IBrush?> BarColorProperty =
        AvaloniaProperty.Register<BarChartCard, IBrush?>(nameof(BarColor));

    public static readonly StyledProperty<int> MaxItemsProperty =
        AvaloniaProperty.Register<BarChartCard, int>(nameof(MaxItems), 10);

    public static readonly StyledProperty<IEnumerable?> DisplayItemsProperty =
        AvaloniaProperty.Register<BarChartCard, IEnumerable?>(nameof(DisplayItems));

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? TotalValue { get => GetValue(TotalValueProperty); set => SetValue(TotalValueProperty, value); }
    public IBrush? TotalValueColor { get => GetValue(TotalValueColorProperty); set => SetValue(TotalValueColorProperty, value); }
    public string ChartTitle { get => GetValue(ChartTitleProperty); set => SetValue(ChartTitleProperty, value); }
    public string? ToggleIcon { get => GetValue(ToggleIconProperty); set => SetValue(ToggleIconProperty, value); }
    public ICommand? ToggleCommand { get => GetValue(ToggleCommandProperty); set => SetValue(ToggleCommandProperty, value); }
    public bool IsChartVisible { get => GetValue(IsChartVisibleProperty); set => SetValue(IsChartVisibleProperty, value); }
    public IEnumerable? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public IBrush? BarColor { get => GetValue(BarColorProperty); set => SetValue(BarColorProperty, value); }
    public int MaxItems { get => GetValue(MaxItemsProperty); set => SetValue(MaxItemsProperty, value); }
    public IEnumerable? DisplayItems { get => GetValue(DisplayItemsProperty); private set => SetValue(DisplayItemsProperty, value); }

    public BarChartCard()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if(change.Property == ItemsSourceProperty)
        {
            if(change.OldValue is INotifyCollectionChanged old)
            {
                old.CollectionChanged -= this.OnSourceCollectionChanged;
            }
            if(change.NewValue is INotifyCollectionChanged next)
            {
                next.CollectionChanged += this.OnSourceCollectionChanged;
            }
            this.RefreshDisplayItems();
        }
        else if(change.Property == MaxItemsProperty)
        {
            this.RefreshDisplayItems();
        }
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        this.RefreshDisplayItems();
    }

    private void RefreshDisplayItems()
    {
        this.DisplayItems = this.ItemsSource?.Cast<object>().Take(this.MaxItems).ToList();
    }
}
