using Avalonia;
using Avalonia.Controls;

namespace DAoCLogWatcher.UI.Views.Controls;

public partial class AttackTypeBarRow : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<AttackTypeBarRow, string>(nameof(Label), string.Empty);

    public static readonly StyledProperty<int> AvgDamageProperty =
        AvaloniaProperty.Register<AttackTypeBarRow, int>(nameof(AvgDamage));

    public static readonly StyledProperty<int> HitCountProperty =
        AvaloniaProperty.Register<AttackTypeBarRow, int>(nameof(HitCount));

    public static readonly StyledProperty<int> CritCountProperty =
        AvaloniaProperty.Register<AttackTypeBarRow, int>(nameof(CritCount));

    public static readonly StyledProperty<double> PercentageProperty =
        AvaloniaProperty.Register<AttackTypeBarRow, double>(nameof(Percentage));

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public int AvgDamage { get => GetValue(AvgDamageProperty); set => SetValue(AvgDamageProperty, value); }
    public int HitCount { get => GetValue(HitCountProperty); set => SetValue(HitCountProperty, value); }
    public int CritCount { get => GetValue(CritCountProperty); set => SetValue(CritCountProperty, value); }
    public double Percentage { get => GetValue(PercentageProperty); set => SetValue(PercentageProperty, value); }

    public AttackTypeBarRow()
    {
        InitializeComponent();
    }
}
