using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
namespace Rightpad.Receiver;

internal sealed class ModeKeyPickerWindow : Window
{
    private readonly ModeSettingsViewModel model;
    private readonly ModeBindingKind kind;
    private readonly WrapPanel keys=new();
    private readonly TextBlock selection=new() {FontSize=20,TextWrapping=TextWrapping.Wrap,Margin=new(12)};
    private readonly List<Button> keyButtons=new();
    public ModeKeyPickerWindow(ModeSettingsViewModel model,ModeBindingKind kind)
    {
        this.model=model;this.kind=kind;
        Title=kind==ModeBindingKind.Keyboard?"Mode · 选择键盘键":"Mode · 选择 Xbox360 按钮";
        Width=900;Height=720;MaxWidth=SystemParameters.WorkArea.Width;MaxHeight=SystemParameters.WorkArea.Height;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(20,29,44));Foreground=Brushes.White;
        var root=new DockPanel {Margin=new(12)};Content=root;
        var done=new Button {Content="完成选择 · 返回统一 Save",MinHeight=56,FontSize=18,Margin=new(8)};
        done.Click+=(_,_)=>Close();DockPanel.SetDock(done,Dock.Bottom);root.Children.Add(done);
        DockPanel.SetDock(selection,Dock.Top);root.Children.Add(selection);
        var groups=new WrapPanel();DockPanel.SetDock(groups,Dock.Top);root.Children.Add(groups);
        foreach(string group in ModeKeys.All.Where(k=>k.Kind==kind).Select(k=>k.Group).Distinct()) {
            var b=new Button {Content=group,MinHeight=48,MinWidth=110,FontSize=16,Margin=new(5)};
            b.Click+=(_,_)=>ShowGroup(group);groups.Children.Add(b);
        }
        root.Children.Add(new ScrollViewer {Content=keys,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,PanningMode=PanningMode.VerticalOnly});
        var selected=model.Draft.Selected;
        ShowGroup(selected?.Kind==kind?selected.Group:ModeKeys.All.First(k=>k.Kind==kind).Group);
    }
    private void ShowGroup(string group)
    {
        keys.Children.Clear();keyButtons.Clear();
        foreach(var key in ModeKeys.All.Where(k=>k.Kind==kind && k.Group==group)) {
            var b=new Button {Content=key.Name,Tag=key,MinWidth=140,MinHeight=64,FontSize=18,Margin=new(6),Padding=new(12,8,12,8)};
            System.Windows.Automation.AutomationProperties.SetAutomationId(b,"ModeKey_"+key.Id);
            b.Click+=(_,_)=> {model.Select(kind,key.Id);Highlight();};
            keyButtons.Add(b);keys.Children.Add(b);
        }
        Highlight();
    }
    private void Highlight()
    {
        selection.Text=model.Summary;
        foreach(var b in keyButtons) {
            bool selected=((ModeKey)b.Tag).Id==model.Draft.Key && model.Draft.Kind==kind;
            b.BorderThickness=new Thickness(selected?4:1);b.BorderBrush=selected?Brushes.LimeGreen:Brushes.Gray;
            b.FontWeight=selected?FontWeights.Bold:FontWeights.Normal;
        }
    }
}
