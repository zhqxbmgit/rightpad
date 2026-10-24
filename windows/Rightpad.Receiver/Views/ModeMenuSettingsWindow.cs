using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;

namespace Rightpad.Receiver;

internal sealed class ModeMenuSettingsWindow : Window
{
    private readonly ModeSettingsViewModel model;
    private readonly ModeMenuVisual preview=new() {Width=220,Height=220,Preview=true};
    private readonly List<Button> slots=new();
    public ModeMenuSettingsWindow(ModeSettingsViewModel model)
    {
        this.model=model;Title="Mode · 电脑快捷菜单设置";Width=1040;Height=840;
        MaxWidth=SystemParameters.WorkArea.Width;MaxHeight=SystemParameters.WorkArea.Height;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background= new SolidColorBrush(System.Windows.Media.Color.FromRgb(20,29,44));Foreground=Brushes.White;
        var root=new DockPanel {Margin=new(16)};Content=root;
        var done=Button("完成配置 · 返回主界面统一 Save",()=>Close());
        DockPanel.SetDock(done,Dock.Bottom);root.Children.Add(done);
        var hint=new TextBlock {Text="六个按钮共用一套绑定 · 点击槽位选择键盘、手柄或未绑定\nMode 开关；系统光标短移选方向，手机点按执行，无需移到按钮内。中心 12 DIP 中性且保持打开；已绑定动作完成或未绑定点按后确认并关闭。",FontSize=18,TextWrapping=TextWrapping.Wrap,Margin=new(8)};
        DockPanel.SetDock(hint,Dock.Top);root.Children.Add(hint);
        var content=new StackPanel();root.Children.Add(new ScrollViewer {Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var previewPanel=new StackPanel {HorizontalAlignment=HorizontalAlignment.Center,Margin=new(8)};
        previewPanel.Children.Add(new TextBlock {Text="六个按钮预览 · 固定环形",FontSize=20,HorizontalAlignment=HorizontalAlignment.Center,Margin=new(4)});
        var previewHost=new Border {BorderBrush=Brushes.Gray,BorderThickness=new(1),Padding=new(6),Child=preview};
        System.Windows.Automation.AutomationProperties.SetAutomationId(previewHost,"MenuAppearancePreview");
        previewPanel.Children.Add(previewHost);content.Children.Add(previewPanel);
        var grid=new UniformGrid {Columns=3};content.Children.Add(grid);
        for(int i=0;i<6;i++) {
            int slot=i;var b=Button("",()=> {ChooseSlot(slot);Refresh();});b.MinHeight=90;
            System.Windows.Automation.AutomationProperties.SetAutomationId(b,"MenuSlot_"+i);
            slots.Add(b);grid.Children.Add(b);
        }
        Refresh();
    }
    private void Refresh()
    {
        preview.State=new(MenuPlacement.Create(new(0,0,480,480),240,240),model.Draft,240,240,-1,-1,0);
        preview.InvalidateVisual();
        for(int i=0;i<6;i++) slots[i].Content=$"{i+1} · {ModeMenuSlots.Directions[i]}\n{model.Draft.Slots[i].Name}";
    }
    private void ChooseSlot(int slot)
    {
        var window=new Window {Title="菜单按钮 · "+ModeMenuSlots.Directions[slot],Width=600,Height=420,
            Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Background,Foreground=Brushes.White};
        var stack=new StackPanel {Margin=new(16)};window.Content=stack;
        stack.Children.Add(new TextBlock {Text="选择绑定类型 · 草稿",FontSize=22,Margin=new(8)});
        foreach(var kind in new[]{ModeBindingKind.Keyboard,ModeBindingKind.Gamepad}) {
            var b=Button(kind==ModeBindingKind.Keyboard?"选择键盘键":"选择手柄键",()=> {
                new ModeKeyPickerWindow(model,kind,slot) {Owner=window}.ShowDialog();window.Close();
            });stack.Children.Add(b);
        }
        stack.Children.Add(Button("未绑定",()=> {model.Slot(slot,ModeBindingKind.Unbound,"");window.Close();}));
        stack.Children.Add(Button("返回 · 保留当前草稿",()=>window.Close()));window.ShowDialog();
    }
    private static Button Button(string label,Action action)
    {
        var b=new Button {Content=label,MinHeight=56,FontSize=18,Margin=new(6),Padding=new(12,6,12,6)};
        b.Click+=(_,_)=>action();return b;
    }
}
