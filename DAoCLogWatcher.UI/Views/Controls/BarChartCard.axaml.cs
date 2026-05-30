using System.Collections;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DAoCLogWatcher.UI.Views.Controls;

public partial class BarChartCard: UserControl
{
	public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<BarChartCard, string>(nameof(Title), string.Empty);

	public static readonly StyledProperty<string?> TotalValueProperty = AvaloniaProperty.Register<BarChartCard, string?>(nameof(TotalValue));

	public static readonly StyledProperty<IBrush?> TotalValueColorProperty = AvaloniaProperty.Register<BarChartCard, IBrush?>(nameof(TotalValueColor));

	public static readonly StyledProperty<string> ChartTitleProperty = AvaloniaProperty.Register<BarChartCard, string>(nameof(ChartTitle), string.Empty);

	public static readonly StyledProperty<string?> ToggleIconProperty = AvaloniaProperty.Register<BarChartCard, string?>(nameof(ToggleIcon));

	public static readonly StyledProperty<ICommand?> ToggleCommandProperty = AvaloniaProperty.Register<BarChartCard, ICommand?>(nameof(ToggleCommand));

	public static readonly StyledProperty<bool> IsChartVisibleProperty = AvaloniaProperty.Register<BarChartCard, bool>(nameof(IsChartVisible));

	public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty = AvaloniaProperty.Register<BarChartCard, IEnumerable?>(nameof(ItemsSource));

	public static readonly StyledProperty<IBrush?> BarColorProperty = AvaloniaProperty.Register<BarChartCard, IBrush?>(nameof(BarColor));

	public static readonly StyledProperty<int> MaxItemsProperty = AvaloniaProperty.Register<BarChartCard, int>(nameof(MaxItems), 10);

	public static readonly StyledProperty<IEnumerable?> DisplayItemsProperty = AvaloniaProperty.Register<BarChartCard, IEnumerable?>(nameof(DisplayItems));

	public string Title
	{
		get => this.GetValue(TitleProperty);
		set => this.SetValue(TitleProperty, value);
	}

	public string? TotalValue
	{
		get => this.GetValue(TotalValueProperty);
		set => this.SetValue(TotalValueProperty, value);
	}

	public IBrush? TotalValueColor
	{
		get => this.GetValue(TotalValueColorProperty);
		set => this.SetValue(TotalValueColorProperty, value);
	}

	public string ChartTitle
	{
		get => this.GetValue(ChartTitleProperty);
		set => this.SetValue(ChartTitleProperty, value);
	}

	public string? ToggleIcon
	{
		get => this.GetValue(ToggleIconProperty);
		set => this.SetValue(ToggleIconProperty, value);
	}

	public ICommand? ToggleCommand
	{
		get => this.GetValue(ToggleCommandProperty);
		set => this.SetValue(ToggleCommandProperty, value);
	}

	public bool IsChartVisible
	{
		get => this.GetValue(IsChartVisibleProperty);
		set => this.SetValue(IsChartVisibleProperty, value);
	}

	public IEnumerable? ItemsSource
	{
		get => this.GetValue(ItemsSourceProperty);
		set => this.SetValue(ItemsSourceProperty, value);
	}

	public IBrush? BarColor
	{
		get => this.GetValue(BarColorProperty);
		set => this.SetValue(BarColorProperty, value);
	}

	public int MaxItems
	{
		get => this.GetValue(MaxItemsProperty);
		set => this.SetValue(MaxItemsProperty, value);
	}

	public IEnumerable? DisplayItems
	{
		get => this.GetValue(DisplayItemsProperty);
		private set => this.SetValue(DisplayItemsProperty, value);
	}

	public BarChartCard()
	{
		this.InitializeComponent();
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
