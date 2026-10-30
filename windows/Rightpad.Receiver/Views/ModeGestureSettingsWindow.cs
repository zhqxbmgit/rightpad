using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brushes=System.Windows.Media.Brushes;
using Brush=System.Windows.Media.Brush;
using Color=System.Windows.Media.Color;
namespace Rightpad.Receiver;

internal sealed class ModeGestureSettingsWindow : Window
{
    private readonly ModeSettingsViewModel model;
    private readonly Func<ModeGeometry?> capture;
    private readonly Canvas preview=new() {Background=Brushes.Black,Width=288,Height=640,ClipToBounds=true};
    private readonly TextBlock status=new() {FontSize=16,TextWrapping=TextWrapping.Wrap,Margin=new(8)};
    private readonly Button[] actions=new Button[3];
    private readonly TextBlock[] widths=new TextBlock[2];
    internal ModeGestureSettingsWindow(ModeSettingsViewModel model,Func<ModeGeometry?> capture)
    {
        this.model=model;this.capture=capture;
        Title="Mode · 手势绑定 · 固定三区域";Width=1040;Height=950;
        MaxWidth=SystemParameters.WorkArea.Width;MaxHeight=SystemParameters.WorkArea.Height;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(20,29,44));Foreground=Brushes.White;
        var root=new DockPanel {Margin=new(16)};Content=root;
        var done=Button("完成配置 · 返回底部统一 Save",Close);DockPanel.SetDock(done,Dock.Bottom);root.Children.Add(done);
        DockPanel.SetDock(status,Dock.Top);root.Children.Add(status);
        var content=new StackPanel();root.Children.Add(new ScrollViewer {Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        content.Children.Add(new TextBlock {Text="仅草稿。只有正常抬指执行一次；移动与停住只预选，不按时间升档。\n从 Mode 内落指。拉出后回原地抬指取消；越出完整矩形后，本次不能重新激活。",FontSize=18,TextWrapping=TextWrapping.Wrap,Margin=new(8)});
        var body=new Grid();body.ColumnDefinitions.Add(new() {Width=new(320)});body.ColumnDefinitions.Add(new());content.Children.Add(body);
        var left=new StackPanel {Margin=new(8)};left.Children.Add(preview);body.Children.Add(left);
        var settings=new StackPanel {Margin=new(12,0,0,0)};Grid.SetColumn(settings,1);body.Children.Add(settings);
        string[] labels=["原地 · 现有 Mode 矩形","一档 · Mode 右侧","二档 · 一档右侧"];
        for(int i=0;i<3;i++) {
            int entry=i;actions[i]=Button("",()=>Choose(entry));
            System.Windows.Automation.AutomationProperties.SetAutomationId(actions[i],"ModeGestureEntry"+i);
            actions[i].MinHeight=82;settings.Children.Add(new TextBlock {Text=labels[i],FontSize=18,Margin=new(8,12,8,0)});settings.Children.Add(actions[i]);
        }
        for(int i=0;i<2;i++) {
            int entry=i;settings.Children.Add(new TextBlock {Text=i==0?"一档区域宽度":"二档区域宽度",FontSize=20,Margin=new(8,12,8,4)});
            var row=new StackPanel {Orientation=Orientation.Horizontal};
            row.Children.Add(Button("−",()=>{model.Width(entry,-1);Refresh();}));
            widths[i]=new TextBlock {Width=120,FontSize=22,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center};row.Children.Add(widths[i]);
            row.Children.Add(Button("+",()=>{model.Width(entry,1);Refresh();}));settings.Children.Add(row);
        }
        settings.Children.Add(new TextBlock {Text="各自 1..50 dp，步长 0.5 dp。初始开发值 8／10 dp，尚未真人验收。宽度不随 Mode 缩放。",FontSize=16,TextWrapping=TextWrapping.Wrap,Margin=new(8)});
        settings.Children.Add(Button("配置现有 Ring · 六个槽位",()=>new ModeMenuSettingsWindow(model) {Owner=this}.ShowDialog()));
        settings.Children.Add(Button("刷新手机实际布局",Refresh));
        content.Children.Add(new TextBlock {Text="预览按手机实际 View 等比例绘制：绿色原地／蓝色一档／橙色二档，白线为两个内部边界。灰框为可用输入范围。移动／缩放请返回 Controls 打开现有大布局编辑器。\n已保存值与草稿见主页面；本窗口不独立保存。",FontSize=16,TextWrapping=TextWrapping.Wrap,Margin=new(8)});
        Refresh();
    }
    private void Refresh()
    {
        var g=model.Draft.Gesture;
        for(int i=0;i<3;i++) actions[i].Content=g[i].Name;
        widths[0].Text=$"{g.FirstWidthDp:0.#} dp";widths[1].Text=$"{g.SecondWidthDp:0.#} dp";
        preview.Children.Clear();
        var geometry=capture();
        if(geometry is null) {status.Text="手机布局／输入坐标空间未同步：预览不可用，统一 Save 会阻止启用手势绑定。";return;}
        double scale=Math.Min(288d/geometry.Width,640d/geometry.Height);
        preview.Width=geometry.Width*scale;preview.Height=geometry.Height*scale;
        void Rect(double x,double y,double w,double h,Brush fill,Brush stroke,double thickness=1) {
            var r=new System.Windows.Shapes.Rectangle {Width=w*scale,Height=h*scale,Fill=fill,Stroke=stroke,StrokeThickness=thickness};
            Canvas.SetLeft(r,x*scale);Canvas.SetTop(r,y*scale);preview.Children.Add(r);
        }
        Rect(geometry.Left,geometry.Top,geometry.Right-geometry.Left,geometry.Bottom-geometry.Top,Brushes.Transparent,Brushes.Gray);
        var m=geometry.Mode;double first=(float)g.FirstWidthDp*geometry.Density,second=(float)g.SecondWidthDp*geometry.Density;
        Rect(m.X,m.Y,m.Width,m.Height,Brushes.DarkGreen,Brushes.White);
        Rect(m.X+m.Width,m.Y,first,m.Height,Brushes.SteelBlue,Brushes.White);
        Rect(m.X+m.Width+first,m.Y,second,m.Height,Brushes.DarkOrange,Brushes.White);
        status.Text=(geometry.Error(g)??"完整三区域位于手机可用输入范围内")+$"\nView {geometry.Width}×{geometry.Height} · density {geometry.Density:0.###}\nMode X {m.X} / Y {m.Y} / 宽 {m.Width} / 高 {m.Height}\n布局身份 {geometry.Identity}";
    }
    private void Choose(int entry)
    {
        var w=new Window {Title="手势入口 · "+new[]{"原地","一档","二档"}[entry],Width=650,Height=600,Owner=this,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Background,Foreground=Foreground};
        var list=new StackPanel {Margin=new(16)};w.Content=list;
        foreach(var kind in new[]{ModeBindingKind.Keyboard,ModeBindingKind.Gamepad,ModeBindingKind.Mouse}) {
            list.Children.Add(Button(kind switch {ModeBindingKind.Keyboard=>"选择键盘键",ModeBindingKind.Gamepad=>"选择手柄键",_=>"选择鼠标键"},()=>{
                new ModeKeyPickerWindow(model,kind,gesture:entry) {Owner=w}.ShowDialog();w.Close();}));
        }
        list.Children.Add(Button("未绑定",()=>{model.GestureAction(entry,ModeBindingKind.Unbound,"");w.Close();}));
        if(entry==2) list.Children.Add(Button("打开／关闭电脑快捷菜单",()=>{model.GestureAction(entry,ModeBindingKind.Unbound,"",true);w.Close();}));
        list.Children.Add(Button("返回 · 保留当前草稿",w.Close));w.ShowDialog();Refresh();
    }
    private static Button Button(string label,Action click) {
        var b=new Button {Content=label,MinWidth=64,MinHeight=56,FontSize=18,Margin=new(6),Padding=new(10,6,10,6)};
        b.Click+=(_,_)=>click();return b;
    }
}
