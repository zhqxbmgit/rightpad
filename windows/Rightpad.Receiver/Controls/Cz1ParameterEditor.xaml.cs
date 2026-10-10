using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Rightpad.Receiver;
public partial class Cz1ParameterEditor : UserControl
{
    public Cz1ParameterEditor() => InitializeComponent();
    private void InputKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not NumericField field) return;
        if (e.Key == Key.Enter) { field.Normalize(); e.Handled = true; }
        else if (e.Key == Key.Escape) { field.Restore(); e.Handled = true; }
        else if (e.Key is Key.Up or Key.Down) { field.Step(e.Key == Key.Up ? 1 : -1); e.Handled = true; }
    }
    private void InputLostFocus(object sender, RoutedEventArgs e)
    { if (DataContext is NumericField field) field.Normalize(); }
}
