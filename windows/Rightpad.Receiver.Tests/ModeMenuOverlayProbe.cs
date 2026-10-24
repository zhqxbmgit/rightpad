using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Point = System.Windows.Point;

namespace Rightpad.Receiver.Tests;

// Controlled real-cursor feasibility check. No production ports or output devices.
// Production menu movement is the normal Motion/HID path; this probe simulates
// that path's integer output with test-only SetCursorPos so selection and
// environment checks can be exercised against the real Windows cursor.
internal static class ModeMenuOverlayProbe
{
    public static int Diagnostic(string directory)
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        Directory.CreateDirectory(directory);
        using var log=new StreamWriter(Path.Combine(directory,"cursor-diagnostic.log")){AutoFlush=true};
        GetCursorPos(out var saved);
        try {
            return Task.Run(()=> {
                var c=new WindowsModeMenuCursor();var p=c.Capture();
                log.WriteLine($"start={p.CenterX},{p.CenterY} thread={Environment.CurrentManagedThreadId}");
                for(int i=0;i<4000;i++) {
                    try {var actual=c.Read(p);if(actual.X!=p.CenterX || actual.Y!=p.CenterY) log.WriteLine($"iteration={i} observed={actual} anchor={p.CenterX},{p.CenterY}");}
                    catch(Exception e){log.WriteLine($"iteration={i} {e}");return 1;}
                    Thread.Sleep(2);
                }
                log.WriteLine("PASS 4000 worker reads");return 0;
            }).GetAwaiter().GetResult();
        } finally {SetCursorPos(saved.X,saved.Y);}
    }
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool ClipCursor(ref Clip rect);
    [DllImport("user32.dll")] private static extern bool GetClipCursor(out Clip rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Pos pos);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x,int y);
    [StructLayout(LayoutKind.Sequential)] private struct Clip {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] private struct Pos {public int X,Y;}
    public static int Run(string directory,bool environmentOnly=false)
    {
        Exception? failure=null;
        GetClipCursor(out var savedClip);GetCursorPos(out var savedPosition);
        var t=new Thread(()=> {
            try {Show(directory,environmentOnly);} catch(Exception e){failure=e;}
            finally{ClipCursor(ref savedClip);SetCursorPos(savedPosition.X,savedPosition.Y);}
        });t.SetApartmentState(ApartmentState.STA);t.Start();t.Join();
        if(failure is not null){File.WriteAllText(Path.Combine(directory,"probe-error.txt"),failure.ToString());return 1;}return 0;
    }
    private static void Show(string directory,bool environmentOnly)
    {
        Directory.CreateDirectory(directory);using var log=new StreamWriter(Path.Combine(directory,"overlay-probe.log")){AutoFlush=true};
        var target=new Window {Title="Rightpad isolated system cursor feasibility target",Width=1000,Height=700,Background=Brushes.DarkSlateGray,
            WindowStartupLocation=WindowStartupLocation.CenterScreen,Content=new TextBlock {Text="系统光标受控验证\n运动只由受控测试模拟原 Motion 整数输出；不连接生产 Receiver。\n可见性／约束不否决真实坐标；测试后自动退出并恢复。",FontSize=26,Foreground=Brushes.White,Margin=new(40),TextWrapping=TextWrapping.Wrap}};
        var config=new ModeBinding {Purpose=ModePurpose.ShortcutMenu};
        var store=new RuntimeSettingsStore(RuntimeSettings.Default with {ModeControl=config});
        using var mode=new ModeSession(store,_=>true,(_,_)=>{},log:s=>log.WriteLine(s));
        using var presenter=new ModeMenuPresenter(()=>mode);
        var cursor=new WindowsModeMenuCursor();
        ulong gen=0;int tick=0,idleTicks=0;uint seq=0,contact=0,touchSeq=0,touchId=0;ulong ms=1000;
        long idleStart=0;double previousX=0,previousY=0;
        var raw=new RawMotionProcessor(store.Current.SensitivityX,store.Current.SensitivityY);
        var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(150)};
        Exception? failed=null;MenuPlacement? original=null;
        void ArmOpen() {
            gen=mode.Generation;seq=contact=0;
            mode.Process(new(7,gen,0,0,ModeContactPhase.Idle),System.Net.IPAddress.Loopback,Stopwatch.GetTimestamp());
            mode.Process(new(7,gen,++seq,++contact,ModeContactPhase.Down),System.Net.IPAddress.Loopback,Stopwatch.GetTimestamp());
            mode.Process(new(7,gen,++seq,contact,ModeContactPhase.Up),System.Net.IPAddress.Loopback,Stopwatch.GetTimestamp());
        }
        void Check(bool value,string message) {log.WriteLine($"check={message} pass={value}");if(!value)throw new Exception(message);}
        // Test-only stand-in for the original Motion/HID output: identical integer
        // displacement from absolute phone samples, submitted with SetCursorPos.
        void Apply(float x,float y) {
            var d=raw.Process(x-previousX,y-previousY);previousX=x;previousY=y;
            if(d.X==0 && d.Y==0) return;
            if(!GetCursorPos(out var at)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if(!SetCursorPos(at.X+d.X,at.Y+d.Y)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        void Route(TouchEventType type,float x=0,float y=0) {
            if(type==TouchEventType.Down) {
                touchId++;
                if(!GetCursorPos(out var start)) throw new Win32Exception(Marshal.GetLastWin32Error());
                previousX=start.X;previousY=start.Y;raw.Reset();
            }
            mode.RouteTouch(new(new(2,type,1,touchId,++touchSeq,7),[new((ms+=30)*1_000_000,x,y)]),store.Current,
                ()=>{if(type!=TouchEventType.Down)Apply(x,y);},
                ()=>throw new Exception("gesture leak"));
        }
        target.Loaded+=(_,_)=> {
            try {
                var center=target.PointToScreen(new Point(500,350));
                Check(SetCursorPos((int)center.X,(int)center.Y),"controlled test starting position");
                mode.UpdatePresence(new(7,Stopwatch.GetTimestamp(),true,"127.0.0.1"));ArmOpen();
                original=mode.MenuSnapshot!.Placement;idleStart=Stopwatch.GetTimestamp();log.WriteLine($"idle_start={DateTimeOffset.Now:O}");
                Check(mode.MenuSnapshot.Hovered==-1,"open neutral no jump");timer.Start();
            }catch(Exception e){failed=e;target.Close();}
        };
        timer.Tick+=(_,_)=> {
            try {
                mode.Process(new(7,gen,seq,contact,ModeContactPhase.Up),System.Net.IPAddress.Loopback,Stopwatch.GetTimestamp());
                if(!environmentOnly && Stopwatch.GetElapsedTime(idleStart)<TimeSpan.FromSeconds(62)) {idleTicks++;if(!mode.MenuOpen)throw new Exception("healthy real cursor idle closed");return;}
                if(tick==0) log.WriteLine(environmentOnly?"environment-only probe; idle tested separately in production":$"PASS idle_no_touch_no_click_seconds={Stopwatch.GetElapsedTime(idleStart).TotalSeconds:F2} checks={idleTicks} time={DateTimeOffset.Now:O}");
                tick++;
                if(tick<=60 && original is { } p) {
                    int slot=(tick-1)%6;double angle=slot*Math.PI/3;double radius=20*p.Area.Scale;
                    var actual=cursor.Read(p);
                    int x=(int)Math.Round(p.CenterX+radius*Math.Sin(angle)),y=(int)Math.Round(p.CenterY-radius*Math.Cos(angle));
                    Route(TouchEventType.Down);
                    Route(TouchEventType.Move,(float)(actual.X+(x-actual.X)/store.Current.SensitivityX),(float)(actual.Y+(y-actual.Y)/store.Current.SensitivityY));
                    Route(TouchEventType.Up,(float)(actual.X+(x-actual.X)/store.Current.SensitivityX),(float)(actual.Y+(y-actual.Y)/store.Current.SensitivityY));
                    var s=mode.MenuSnapshot!;Check(s.Hovered==slot && s.Placement==p,"short six direction recontact "+tick);
                    log.WriteLine($"actual={cursor.Read(p)} observed={s.CursorX},{s.CursorY} anchor={p.CenterX},{p.CenterY} dpi={p.Area.Scale*96} foreground={GetForegroundWindow()}");
                }
                if(tick==61) {
                    var point=target.PointToScreen(new Point(400,350));SetCursorPos((int)point.X,(int)point.Y);
                    var clip=new Clip {Left=(int)point.X,Top=(int)point.Y,Right=(int)point.X+2,Bottom=(int)point.Y+2};
                    ClipCursor(ref clip);mode.Tick(Stopwatch.GetTimestamp());Check(mode.MenuOpen,"inner clip no longer closes a readable cursor");
                    ClipCursor(IntPtr.Zero);
                }
                if(tick==62) {target.Activate();target.Cursor=System.Windows.Input.Cursors.None;mode.Tick(Stopwatch.GetTimestamp());Check(mode.MenuOpen,"hidden system cursor does not veto the real position");target.Cursor=System.Windows.Input.Cursors.Arrow;}
                if(tick==63) {Check(mode.MenuOpen,"menu stays open under clip and hidden cursor");mode.Process(new(7,gen,++seq,++contact,ModeContactPhase.Down),System.Net.IPAddress.Loopback,Stopwatch.GetTimestamp());Check(!mode.MenuOpen,"Mode close");target.Close();}
            }catch(Exception e){failed=e;target.Close();}
        };
        target.Closed+=(_,_)=>{timer.Stop();presenter.Dispose();};new Application().Run(target);
        if(failed is not null)throw failed;log.WriteLine("PASS controlled real cursor navigation, non-vetoing clip and hidden cursor, Mode close");
    }
    [DllImport("user32.dll")] private static extern bool ClipCursor(IntPtr rect);
}
