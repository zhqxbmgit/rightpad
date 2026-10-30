using System.Windows.Controls;
using System.Windows;
namespace Rightpad.Receiver;
public partial class ControlsView : UserControl
{
    public ControlsView() {
        InitializeComponent();
        DataContextChanged+=(_,e)=> {
            if(e.OldValue is MainViewModel previous) previous.Settings.Mode.PropertyChanged-=ModeChanged;
            if(e.NewValue is MainViewModel next) next.Settings.Mode.PropertyChanged+=ModeChanged;
            Highlight();
        };
    }
    private void ModeChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e)=>Highlight();
    private void Highlight() {
        if(DataContext is not MainViewModel m) return;
        bool toggle=m.Settings.Mode.Draft.Behavior==ModePressBehavior.Toggle;
        MomentaryButton.BorderThickness=new Thickness(toggle?1:4);ToggleButton.BorderThickness=new Thickness(toggle?4:1);
        MomentaryButton.BorderBrush=ToggleButton.BorderBrush=System.Windows.Media.Brushes.DodgerBlue;
        bool menu=m.Settings.Mode.Draft.Purpose==ModePurpose.ShortcutMenu;
        bool gesture=m.Settings.Mode.Draft.Purpose==ModePurpose.GestureBinding;
        SinglePurpose.BorderThickness=new Thickness(!menu&&!gesture?4:1);MenuPurpose.BorderThickness=new Thickness(menu?4:1);
        GesturePurpose.BorderThickness=new Thickness(gesture?4:1);
        SinglePurpose.BorderBrush=MenuPurpose.BorderBrush=GesturePurpose.BorderBrush=System.Windows.Media.Brushes.DodgerBlue;
        SingleSettings.Visibility=menu||gesture?Visibility.Collapsed:Visibility.Visible;
        MenuConfigure.Visibility=menu?Visibility.Visible:Visibility.Collapsed;
        GestureConfigure.Visibility=gesture?Visibility.Visible:Visibility.Collapsed;
    }
    private void ChooseKeyboard(object sender,RoutedEventArgs e)=>Choose(ModeBindingKind.Keyboard);
    private void ChooseGamepad(object sender,RoutedEventArgs e)=>Choose(ModeBindingKind.Gamepad);
    private void ChooseMouse(object sender,RoutedEventArgs e)=>Choose(ModeBindingKind.Mouse);
    private void Choose(ModeBindingKind kind) {
        if(DataContext is MainViewModel m && !m.Settings.IsSaving) new ModeKeyPickerWindow(m.Settings.Mode,kind) {Owner=Window.GetWindow(this)}.ShowDialog();
    }
    private void Unbind(object sender,RoutedEventArgs e) {if(DataContext is MainViewModel m && !m.Settings.IsSaving) m.Settings.Mode.Select(ModeBindingKind.Unbound,"");}
    private void Momentary(object sender,RoutedEventArgs e) {if(DataContext is MainViewModel m && !m.Settings.IsSaving) m.Settings.Mode.Behavior(ModePressBehavior.Momentary);}
    private void Toggle(object sender,RoutedEventArgs e) {if(DataContext is MainViewModel m && !m.Settings.IsSaving) m.Settings.Mode.Behavior(ModePressBehavior.Toggle);}
    private void SingleMode(object sender,RoutedEventArgs e) {if(DataContext is MainViewModel m && !m.Settings.IsSaving) m.Settings.Mode.Purpose(ModePurpose.SingleBinding);}
    private void MenuMode(object sender,RoutedEventArgs e) {if(DataContext is MainViewModel m && !m.Settings.IsSaving) m.Settings.Mode.Purpose(ModePurpose.ShortcutMenu);}
    private void GestureMode(object sender,RoutedEventArgs e) {if(DataContext is MainViewModel m && !m.Settings.IsSaving) m.Settings.Mode.Purpose(ModePurpose.GestureBinding);}
    private void ConfigureGesture(object sender,RoutedEventArgs e) {
        if(DataContext is MainViewModel m && !m.Settings.IsSaving)
            new ModeGestureSettingsWindow(m.Settings.Mode,m.Settings.ModeGeometry) {Owner=Window.GetWindow(this)}.ShowDialog();
    }
    private void ConfigureMenu(object sender,RoutedEventArgs e) {
        if(DataContext is MainViewModel m && !m.Settings.IsSaving) new ModeMenuSettingsWindow(m.Settings.Mode) {Owner=Window.GetWindow(this)}.ShowDialog();
    }
    private void OpenLayoutEditor(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.OpenLayoutEditor();
}
