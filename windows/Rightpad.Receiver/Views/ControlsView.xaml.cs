using System.Windows.Controls;
using System.Windows;
namespace Rightpad.Receiver;
public partial class ControlsView : UserControl
{
    public ControlsView() => InitializeComponent();
    private void OpenLayoutEditor(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.OpenLayoutEditor();
}
