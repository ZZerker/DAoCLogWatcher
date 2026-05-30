using Avalonia;
using Avalonia.Controls;

namespace DAoCLogWatcher.UI.Views.Controls;

public partial class AttackTypeBarRow: UserControl
{
	public static readonly StyledProperty<string> LabelProperty = AvaloniaProperty.Register<AttackTypeBarRow, string>(nameof(Label), string.Empty);

	public static readonly StyledProperty<int> AvgDamageProperty = AvaloniaProperty.Register<AttackTypeBarRow, int>(nameof(AvgDamage));

	public static readonly StyledProperty<int> HitCountProperty = AvaloniaProperty.Register<AttackTypeBarRow, int>(nameof(HitCount));

	public static readonly StyledProperty<int> CritCountProperty = AvaloniaProperty.Register<AttackTypeBarRow, int>(nameof(CritCount));

	public static readonly StyledProperty<double> PercentageProperty = AvaloniaProperty.Register<AttackTypeBarRow, double>(nameof(Percentage));

	public string Label
	{
		get => this.GetValue(LabelProperty);
		set => this.SetValue(LabelProperty, value);
	}

	public int AvgDamage
	{
		get => this.GetValue(AvgDamageProperty);
		set => this.SetValue(AvgDamageProperty, value);
	}

	public int HitCount
	{
		get => this.GetValue(HitCountProperty);
		set => this.SetValue(HitCountProperty, value);
	}

	public int CritCount
	{
		get => this.GetValue(CritCountProperty);
		set => this.SetValue(CritCountProperty, value);
	}

	public double Percentage
	{
		get => this.GetValue(PercentageProperty);
		set => this.SetValue(PercentageProperty, value);
	}

	public AttackTypeBarRow()
	{
		this.InitializeComponent();
	}
}
