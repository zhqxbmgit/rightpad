using System.Windows.Controls;
namespace Rightpad.Receiver;
public partial class DiagnosticsView : UserControl
{
    public DiagnosticsView() => InitializeComponent();
    private async void FreezeHitchTrace(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel model) await model.FreezeHitchTraceAsync();
    }
}
