using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class RemoteLayoutWindowTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> Cases => [
        ("layout window reuses draft and stops hidden/minimized polling", () => Sta(Lifecycle)),
        ("layout window reopen observes trial deadline and phone conflict", () => Sta(Reconcile)),
        ("layout window Keep requires phone acknowledgement across reopen", () => Sta(Keep)),
        ("layout window finite viewport keeps controls and callouts in bounds", () => Sta(Layout))
    ];
    private static Task Sta(Action test)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { test(); done.SetResult(); } catch(Exception e) { done.SetException(e); } finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return done.Task;
    }
    private static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(obj)!;
    private static IEnumerable<DependencyObject> Walk(DependencyObject obj)
    { yield return obj; for(int i=0;i<VisualTreeHelper.GetChildrenCount(obj);i++) foreach(var child in Walk(VisualTreeHelper.GetChild(obj,i))) yield return child; }
    private static void Click(RemoteLayoutView view, string id) => Walk(view).OfType<Button>().Single(b=>AutomationProperties.GetAutomationId(b)==id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private sealed class Fixture : IDisposable
    {
        public readonly List<LayoutMessage> Sends=[];
        public readonly RemoteLayoutClient Client;
        public readonly RemoteLayoutEditorWindow Window;
        public readonly RemoteLayoutView View;
        public readonly RemoteLayoutDraft Draft;
        public int Returns;
        public LayoutMessage Snapshot = new(128,0,0,99,123,5,1,0,0,1,1,0,1200,2670,[
            new("xbox.b.slide","B",0,new(1045,506,155,2164)),new("xbox.x.slide_lr","X",0,new(0,636,68,2034)),
            new("rightpad.input.mode","Mode",0,new(509,772,264,1881)),new("settings","设置",1,new(1010,42,70,70)),new("power","电源",1,new(1120,42,70,70))]);
        public Fixture()
        {
            Client=new(()=>new(99,Stopwatch.GetTimestamp(),true,"127.0.0.1"),(_,bytes)=>Sends.Add(RemoteLayoutProtocol.Decode(bytes)!));
            Window=new(()=>Client,()=>Returns++);
            Window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Rightpad.Receiver;component/Resources/DarkTheme.xaml",UriKind.Relative) });
            Check(Window.WindowState==WindowState.Maximized,"normal editor defaults maximized");
            // Off-screen native WPF host exercises Show/Hide/StateChanged without taking input focus.
            Window.WindowState=WindowState.Normal;Window.WindowStartupLocation=WindowStartupLocation.Manual;
            Window.Left=-10000;Window.Top=-10000;Window.ShowActivated=false;Window.ShowInTaskbar=false;
            Window.Show();Window.UpdateLayout();View=(RemoteLayoutView)Window.Content;Draft=Field<RemoteLayoutDraft>(View,"draft");
            Reply();View.SetPollingActive(false);View.SetPollingActive(true);Window.UpdateLayout();
        }
        public void Reply()
        {
            Client.Accept(RemoteLayoutProtocol.Encode(Snapshot with {Request=Sends.Last().Request}),IPAddress.Loopback,Stopwatch.GetTimestamp());
        }
        public void Dispose() => Window.CloseForShutdown();
    }
    private static void Lifecycle()
    {
        using var f=new Fixture();var timer=Field<DispatcherTimer>(f.View,"timer");
        Click(f.View,"LayoutXMinus");Equal(1044,f.Draft.Selection!.Rect.X,"only draft changes");
        var draft=f.Draft;
        for(int i=0;i<12;i++) {
            f.Window.Close();Check(!f.Window.IsVisible&&!timer.IsEnabled,"close hides and stops sole timer");
            int before=f.Sends.Count;Pump(25);Equal(before,f.Sends.Count,"hidden view performs no work");
            f.Window.Show();f.Window.UpdateLayout();Check(timer.IsEnabled,"reopen resumes sole timer");
            Check(ReferenceEquals(draft,f.Draft),"same draft instance");Equal(1044,f.Draft.Selection!.Rect.X,"reopen retains unsent edit");
        }
        f.Window.WindowState=WindowState.Minimized;Check(!timer.IsEnabled,"minimized polling stops");
        f.Window.WindowState=WindowState.Normal;Check(timer.IsEnabled,"restore resumes");
        Click(f.View,"Layout返回 Receiver");Check(!f.Window.IsVisible&&!timer.IsEnabled,"return hides");
        Equal(13,f.Returns,"close and return callback once each");
        Check(f.Sends.All(m=>m.Kind==RemoteLayoutProtocol.Query),"all host lifecycle operations are read-only");
    }
    private static void Reconcile()
    {
        using var f=new Fixture();Click(f.View,"LayoutXMinus");Click(f.View,"Layout试用布局");
        Equal(RemoteLayoutProtocol.Try,f.Sends.Last().Kind,"only explicit Try sends mutation");
        var originalItems = f.Snapshot.Items;
        f.Snapshot=f.Snapshot with {State=2,Transaction=1,Next=2,Serial=2,Remaining=60000,
            Items=f.Draft.Items.Concat(originalItems.Where(i=>i.Flags==1)).ToArray()};f.Reply();
        f.Window.Hide();int before=f.Sends.Count;Pump(30);Equal(before,f.Sends.Count,"trial hidden polling stops");
        int remaining=f.Client.Capture(Stopwatch.GetTimestamp()).Remaining;
        f.Window.Show();f.Window.UpdateLayout();
        f.Snapshot=f.Snapshot with {Serial=3,Remaining=60000};f.Reply();
        Check(f.Client.Capture(Stopwatch.GetTimestamp()).Remaining<=remaining,"reopen does not extend observed deadline");
        f.Window.Hide();f.Snapshot=f.Snapshot with {State=4,Serial=4,Remaining=0,Items=originalItems};f.Window.Show();f.Reply();
        f.View.SetPollingActive(false);f.View.SetPollingActive(true);
        Equal(1045,f.Draft.Selection!.Rect.X,"phone restore observed after reopen");Check(!f.Draft.Dirty,"restore clears trial draft");
        // Same-epoch remote revision conflict must preserve, rather than silently replace, an unsent draft.
        f.Snapshot=f.Snapshot with {State=0,Serial=5};f.Reply();f.View.SetPollingActive(false);f.View.SetPollingActive(true);
        Click(f.View,"LayoutXMinus");f.Window.Hide();f.Window.Show();f.Snapshot=f.Snapshot with {Revision=6,Serial=6};f.Reply();
        f.View.SetPollingActive(false);f.View.SetPollingActive(true);
        Check(f.Draft.Conflict&&f.Draft.Dirty,"reopen fences conflicting phone revision");Equal(1044,f.Draft.Selection!.Rect.X,"conflicting draft retained");
        Check(f.Sends.Count(m=>m.Kind==RemoteLayoutProtocol.Try)==1&&f.Sends.All(m=>m.Kind is 1 or 2),"reopen/close never saves/reverts/replays trial");
    }
    private static void Layout()
    {
        using var f=new Fixture();
        foreach(var size in new[]{new Size(800,560),new Size(1100,800),new Size(1920,1032)}) {
            f.Window.Width=size.Width;f.Window.Height=size.Height;f.Window.UpdateLayout();
            var bounds=new Rect(f.View.RenderSize);
            foreach(var button in Walk(f.View).OfType<Button>()) {
                var rect=button.TransformToAncestor(f.View).TransformBounds(new Rect(button.RenderSize));
                Check(bounds.Contains(rect),$"button {button.Content} within {size}: {rect}");Check(rect.Height>=42,"clickable height");
            }
            var canvas=Field<Canvas>(f.View,"preview");Check(canvas.Height>0,"visible complete preview");
            Check(Math.Abs(canvas.Width/canvas.Height-1200.0/2670)<1e-8,"true phone aspect");
            var phone=canvas.TransformToAncestor(f.View).TransformBounds(new Rect(canvas.RenderSize));Check(bounds.Contains(phone),"full phone fits");
            foreach(var label in canvas.Children.OfType<TextBlock>()) {
                var rect=label.TransformToAncestor(f.View).TransformBounds(new Rect(label.RenderSize));Check(bounds.Contains(rect),"external labels fit");
            }
            double height=canvas.Height;f.Window.UpdateLayout();Equal(height,canvas.Height,"layout converges without growth");
        }
        // Repeated reads/timer updates must not rebuild a stationary preview.
        var preview=Field<Canvas>(f.View,"preview");object tile=preview.Children[0];
        for(int i=0;i<10;i++){f.View.SetPollingActive(false);f.View.SetPollingActive(true);f.Window.UpdateLayout();}
        Check(ReferenceEquals(tile,preview.Children[0]),"unchanged viewport/snapshot reuses visual children");
    }
    private static void Keep()
    {
        using var f=new Fixture();Click(f.View,"LayoutXMinus");Click(f.View,"Layout试用布局");
        f.Snapshot=f.Snapshot with {State=2,Transaction=1,Next=2,Serial=2,Remaining=60000,
            Items=f.Draft.Items.Concat(f.Snapshot.Items.Where(i=>i.Flags==1)).ToArray()};f.Reply();
        f.View.SetPollingActive(false);f.View.SetPollingActive(true);
        var status=Field<TextBlock>(f.View,"status");
        Check(status.Text.Contains("试用中"),"Trial shown only after phone reply");
        Click(f.View,"Layout保留此布局");Equal(RemoteLayoutProtocol.Keep,f.Sends.Last().Kind,"explicit Keep uses existing client");
        Check(status.Text.Contains("正在保存")&&!status.Text.Contains("已保存"),"no speculative saved UI");
        f.Window.Close();f.Window.Show();
        f.Snapshot=f.Snapshot with {State=3,Revision=6,Serial=3,Remaining=0};f.Reply();
        f.View.SetPollingActive(false);f.View.SetPollingActive(true);
        Check(status.Text.Contains("已保存")&&!f.Draft.Dirty,"phone saved acknowledgement reconciles on reopen");
        f.Window.Close();f.Window.Show();f.Window.UpdateLayout();
        Check(f.Sends.All(m=>m.Kind is 1 or 2 or 3)&&f.Sends.Count(m=>m.Kind==2)==1,"closing never reverts committed layout or replays Try");
    }
    private static void Pump(int ms)
    {
        var frame=new DispatcherFrame();var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(ms)};
        timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
    }
}
