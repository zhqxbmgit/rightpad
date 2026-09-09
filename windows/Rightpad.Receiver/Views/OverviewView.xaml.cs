using System.Windows.Controls;
namespace Rightpad.Receiver;
public partial class OverviewView : UserControl
{
    public OverviewView() => InitializeComponent();
    private async void ToggleReceiver(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel model) await model.ToggleAsync();
    }
}
