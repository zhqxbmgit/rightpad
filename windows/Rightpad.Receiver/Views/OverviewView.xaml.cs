using System.Windows.Controls;
namespace Rightpad.Receiver;
public partial class OverviewView : UserControl
{
    public OverviewView() => InitializeComponent();
    private async void ToggleReceiver(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel model) await model.ToggleAsync();
    }
    private void StartupChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (IsLoaded && DataContext is MainViewModel model && sender is System.Windows.Controls.Primitives.ToggleButton toggle)
            model.Startup.SetEnabled(toggle.IsChecked == true);
    }
}
