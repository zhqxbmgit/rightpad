using System.Windows.Controls;
using System.Windows;
namespace Rightpad.Receiver;
public partial class MotionView : UserControl
{
    public MotionView() => InitializeComponent();
    private static Cz1SettingsViewModel? CModel(object sender) => (sender as FrameworkElement)?.DataContext as Cz1SettingsViewModel;
    private void Cz1Baseline(object sender, RoutedEventArgs e) { if (CModel(sender) is { } c) c.Amax.Text = "80000"; }
    private void Cz1Reversal(object sender, RoutedEventArgs e) { if (CModel(sender) is { } c) c.Amax.Text = "160000"; }
    private void ApplyCz1(object sender, RoutedEventArgs e) => CModel(sender)?.Apply();
    private async void SaveCz1(object sender, RoutedEventArgs e) { if (CModel(sender) is { } c) await c.SaveAsync(); }
    private void RestoreCz1(object sender, RoutedEventArgs e) => CModel(sender)?.RestoreDefaults();
    private async void SelectDirect(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel model) await model.SelectTouchModeAsync(TouchMode.M_R1);
    }
    private async void SelectCinematic(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel model) await model.SelectTouchModeAsync(TouchMode.C_Z1);
    }
}
