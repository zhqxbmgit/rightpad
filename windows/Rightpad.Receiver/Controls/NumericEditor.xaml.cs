using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Rightpad.Receiver;

public partial class NumericEditor : UserControl
{
    public NumericEditor() => InitializeComponent();
    private NumericField? Field => DataContext as NumericField;
    private void Decrease(object sender, RoutedEventArgs e) => Field?.Step(-1);
    private void Increase(object sender, RoutedEventArgs e) => Field?.Step(1);
    private void InputLostFocus(object sender, KeyboardFocusChangedEventArgs e) => Field?.Normalize();
    private void InputKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up: Field?.Step(1); break;
            case Key.Down: Field?.Step(-1); break;
            case Key.Enter: Field?.Normalize(); break;
            case Key.Escape: Field?.Restore(); break;
            default: return;
        }
        e.Handled = true;
    }
}
