using System.Globalization;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;

namespace Rightpad.Receiver;

// A click-through, non-activating renderer. Selection never depends on Windows
// mouse messages: the input owner routes accepted phone packets to ModeMenu.
internal sealed class ModeMenuWindow : Window
{
    private readonly ModeMenuVisual visual = new();
    private MenuArea? area;
    private HwndTarget? renderTarget;
    internal RenderMode? OverlayRenderMode=>renderTarget?.RenderMode;
    public ModeMenuWindow()
    {
        Title="rightpad · 电脑快捷菜单";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
        AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;
        ShowActivated=false;Focusable=false;Topmost=true;IsHitTestVisible=false;Content=visual;
        SourceInitialized+=(_,_)=> {
            var h=new WindowInteropHelper(this).Handle;
            // Keep this one transparent overlay off the game's GPU drawing queue.
            // This is a fixed window-local choice, not a process-wide mode switch.
            renderTarget=HwndSource.FromHwnd(h)?.CompositionTarget??throw new InvalidOperationException("菜单绘制目标不可用");
            renderTarget.RenderMode=RenderMode.SoftwareOnly;
            SetWindowLongPtr(h,-20,new IntPtr(GetWindowLongPtr(h,-20).ToInt64()|0x08000000|0x00000020|0x00000080));
        };
    }
    public void Present(ModeMenuSnapshot state)
    {
        visual.State=state;
        if(!IsVisible) Show();
        if(area!=state.Placement.Area) {
            area=state.Placement.Area;var a=area.Value;
            if(!SetWindowPos(new WindowInteropHelper(this).Handle,new IntPtr(-1),(int)a.X,(int)a.Y,(int)a.Width,(int)a.Height,0x0010|0x0040))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
    }
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
}

internal sealed class ModeMenuPresenter : IDisposable
{
    private readonly DispatcherTimer timer=new(DispatcherPriority.Render) {Interval=TimeSpan.FromMilliseconds(16)};
    private readonly Func<ModeSession?> session;
    private ModeMenuWindow? window;
    private bool rendering;
    private readonly ModeMenuFrameMetrics metrics=new();
    private ModeSession? metricsOwner;
    public ModeMenuPresenter(Func<ModeSession?> session)
    {
        this.session=session;timer.Tick+=(_,_)=>Refresh();timer.Start();
    }
    private void Refresh()
    {
        long fetchStart=Stopwatch.GetTimestamp();
        var owner=session();
        try {
            if(owner?.MenuObservation is { } observation) {
                long start=Stopwatch.GetTimestamp();
                window??=new();window.Present(observation.Snapshot);owner.MenuPresented();
                metricsOwner=owner;
                long end=Stopwatch.GetTimestamp();
                if(metrics.Record(observation,end,end-start,start-fetchStart) is { } report)
                    owner.MenuPresentationDiagnostic(report.Line("interval",window.OverlayRenderMode));
                if(!rendering) {
                    // The idle timer discovers opening only. An open menu consumes
                    // the latest shared snapshot at WPF's actual render opportunity.
                    rendering=true;timer.Stop();CompositionTarget.Rendering+=RenderFrame;
                }
            } else StopRendering();
        } catch(Exception e) {owner?.MenuFault(e.Message);StopRendering();}
    }
    private void RenderFrame(object? sender,EventArgs e)=>Refresh();
    private void StopRendering()
    {
        FlushMetrics("close");
        window?.Hide();
        if(rendering) {CompositionTarget.Rendering-=RenderFrame;rendering=false;timer.Start();}
    }
    public void Dispose() {
        FlushMetrics("dispose");
        timer.Stop();if(rendering) {CompositionTarget.Rendering-=RenderFrame;rendering=false;}
        window?.Close();
    }
    private void FlushMetrics(string reason) {
        if(metrics.Finish() is { } report)metricsOwner?.MenuPresentationDiagnostic(report.Line(reason,window?.OverlayRenderMode));
        metricsOwner=null;
    }
}

// Bounded counters on the existing sampling/render paths. No extra sampling,
// position model, callbacks, timers, threads, per-frame log or diagnostic output device.
internal readonly record struct ModeMenuFrameReport(long FromTicks,long ToTicks,int Callbacks,long Reads,long PositionChanges,
    int SnapshotChanges,double MaxGapMs,double MaxReadAgeMs,double MeanReadAgeMs,double MaxPresentCallMs,double MaxSnapshotFetchMs) {
    public string Line(string reason,RenderMode? renderMode=null)=>FormattableString.Invariant($"mode_menu_presentation: wallTime={DateTimeOffset.Now:O} reason={reason} renderMode={renderMode?.ToString()??"Unknown"} fromTicks={FromTicks} toTicks={ToTicks} callbacks={Callbacks} reads={Reads} positionChanges={PositionChanges} snapshotChanges={SnapshotChanges} maxGapMs={MaxGapMs:F3} maxReadAgeMs={MaxReadAgeMs:F3} meanReadAgeMs={MeanReadAgeMs:F3} maxPresentCallMs={MaxPresentCallMs:F3} maxSnapshotFetchMs={MaxSnapshotFetchMs:F3}");
}
internal sealed class ModeMenuFrameMetrics(long frequency=0)
{
    private readonly long frequency=frequency>0?frequency:Stopwatch.Frequency;
    private long from,last,baseReads,baseChanges,lastReads,lastChanges,revision;
    private int callbacks,snapshotChanges;
    private bool active;
    private double maxGap,maxAge,totalAge,maxPresent,maxFetch;
    public ModeMenuFrameReport? Record(ModeMenuObservation observation,long now,long presentCallTicks,long fetchTicks=0)
    {
        if(!active) {from=last=now;baseReads=observation.Reads;baseChanges=observation.Changes;revision=observation.Snapshot.Revision;active=true;}
        else {
            maxGap=Math.Max(maxGap,(now-last)*1000d/frequency);
            if(revision!=observation.Snapshot.Revision)snapshotChanges++;
        }
        last=now;lastReads=observation.Reads;lastChanges=observation.Changes;revision=observation.Snapshot.Revision;callbacks++;
        double age=Math.Max(0,(now-observation.LastReadTicks)*1000d/frequency);
        maxAge=Math.Max(maxAge,age);totalAge+=age;maxPresent=Math.Max(maxPresent,presentCallTicks*1000d/frequency);
        maxFetch=Math.Max(maxFetch,fetchTicks*1000d/frequency);
        return now-from>=frequency?Take(false):null;
    }
    public ModeMenuFrameReport? Finish()=>Take(true);
    private ModeMenuFrameReport? Take(bool endCycle)
    {
        if(callbacks==0) {if(endCycle)active=false;return null;}
        var result=new ModeMenuFrameReport(from,last,callbacks,Math.Max(0,lastReads-baseReads),Math.Max(0,lastChanges-baseChanges),
            snapshotChanges,maxGap,maxAge,totalAge/callbacks,maxPresent,maxFetch);
        callbacks=snapshotChanges=0;maxGap=maxAge=totalAge=maxPresent=maxFetch=0;
        from=last;baseReads=lastReads;baseChanges=lastChanges;if(endCycle)active=false;
        return result;
    }
}

internal sealed class ModeMenuVisual : FrameworkElement
{
    private ModeMenuSnapshot? state;
    private bool preview;
    private readonly DrawingVisual cursorVisual=new();
    private readonly MatrixTransform cursorTransform=new();
    public ModeMenuSnapshot? State {
        get=>state;
        set {
            if(ReferenceEquals(state,value)) return;
            bool sameDecoration=state is { } old && value is { } next &&
                old.Placement==next.Placement && old.Configuration==next.Configuration &&
                old.Hovered==next.Hovered && old.Pressed==next.Pressed;
            state=value;UpdateCursor();
            if(!sameDecoration) InvalidateVisual();
        }
    }
    public bool Preview {
        get=>preview;
        set {if(preview==value)return;preview=value;UpdateCursor();InvalidateVisual();}
    }
    private static readonly Brush Tile=new SolidColorBrush(Color.FromRgb(34,54,44));
    private static readonly Brush Hover=new SolidColorBrush(Color.FromRgb(58,99,73));
    private static readonly Brush Pressed=new SolidColorBrush(Color.FromRgb(88,133,96));
    private static readonly Brush Neutral=new SolidColorBrush(Color.FromRgb(22,34,28));
    private static readonly Pen Edge=new(new SolidColorBrush(Color.FromRgb(143,177,152)),1.5);
    // DIP geometry: the outer silhouette's tip is exactly (0,0), without a
    // centered stroke extending the hotspot. This only displays an observation.
    private static readonly Geometry CursorOutline=Geometry.Parse("M0,0 L0,23 6,17.5 11,27 16,24.5 11,15 20,15 Z");
    private static readonly Geometry CursorBody=Geometry.Parse("M2,4.2 L2,18.5 6.6,14.3 11.9,24.3 13.3,23.6 7.7,13 14,13 Z");
    // Frozen drawing resources are safe for a preview and the overlay if their
    // owning WPF dispatchers differ, without changing either visual.
    static ModeMenuVisual() { Tile.Freeze();Hover.Freeze();Pressed.Freeze();Neutral.Freeze();Edge.Brush.Freeze();Edge.Freeze();CursorOutline.Freeze();CursorBody.Freeze(); }
    public ModeMenuVisual()
    {
        ClipToBounds=true;
        // Retain the small arrow once. Moving it changes only its transform,
        // rather than rebuilding Ring geometry and shaping every label per frame.
        using(var dc=cursorVisual.RenderOpen()) {
            dc.DrawGeometry(Brushes.Black,null,CursorOutline);
            dc.DrawGeometry(Brushes.White,null,CursorBody);
        }
        cursorVisual.Transform=cursorTransform;cursorVisual.Opacity=0;
        AddVisualChild(cursorVisual);
    }
    protected override int VisualChildrenCount=>1;
    protected override Visual GetVisualChild(int index)=>index==0?cursorVisual:throw new ArgumentOutOfRangeException(nameof(index));
    protected override void OnRenderSizeChanged(SizeChangedInfo info) {base.OnRenderSizeChanged(info);UpdateCursor();}
    private void UpdateCursor()
    {
        if(preview || state is not { } s) {cursorVisual.Opacity=0;return;}
        var a=s.Placement.Area;
        double x=ActualWidth/a.Width,y=ActualHeight/a.Height;
        cursorTransform.Matrix=new(a.Scale*x,0,0,a.Scale*y,(s.CursorX-a.X)*x,(s.CursorY-a.Y)*y);
        cursorVisual.Opacity=1;
    }
    protected override void OnRender(DrawingContext dc)
    {
        if(State is not { } s) return;
        var p=s.Placement;var a=p.Area;
        dc.PushClip(new RectangleGeometry(new Rect(0,0,ActualWidth,ActualHeight)));
        dc.PushTransform(new ScaleTransform(ActualWidth/a.Width,ActualHeight/a.Height));
        dc.PushTransform(new TranslateTransform(-a.X,-a.Y));
        double d=p.Diameter;
        for(int i=0;i<6;i++) {
            Brush fill=s.Pressed==i?Pressed:s.Hovered==i?Hover:Tile;
            double start=(i*60-120)*Math.PI/180,end=(i*60-60)*Math.PI/180;
            Point At(double r,double angle)=>new(p.CenterX+d*r*Math.Cos(angle),p.CenterY+d*r*Math.Sin(angle));
            var path=new StreamGeometry();using(var g=path.Open()) {
                g.BeginFigure(At(.18,start),true,true);g.LineTo(At(.5,start),true,false);
                g.ArcTo(At(.5,end),new(d*.5,d*.5),0,false,SweepDirection.Clockwise,true,false);
                g.LineTo(At(.18,end),true,false);g.ArcTo(At(.18,start),new(d*.18,d*.18),0,false,SweepDirection.Counterclockwise,true,false);
            }
            dc.DrawGeometry(fill,Edge,path);
            var center=p.SlotCenter(i);var action=s.Configuration.Slots[i];
            Text(dc,action.Name,center.X,center.Y-d*.035,d*.033,d*.255,Brushes.White);
            Text(dc,action.Kind switch {ModeBindingKind.Keyboard=>"键盘 · 点按",ModeBindingKind.Gamepad=>"手柄 · 点按",_=>"未绑定"},center.X,center.Y+d*.045,d*.024,d*.25,Brushes.LightGray);
        }
        dc.DrawEllipse(Neutral,Edge,new(p.CenterX,p.CenterY),d*.175,d*.175);
        Text(dc,"短移选方向",p.CenterX,p.CenterY-d*.025,d*.03,d*.31,Brushes.White);
        Text(dc,"再点 Mode 关闭",p.CenterX,p.CenterY+d*.04,d*.022,d*.31,Brushes.LightGray);
        // The anchor/dead-zone stays fixed; Windows remains the sole position authority.
        dc.DrawEllipse(null,Edge,new(p.CenterX,p.CenterY),p.NeutralRadius,p.NeutralRadius);
        if(!Preview) {
            // Keep selection readable even when edge clipping hides the selected tile.
            double width=Math.Min(d*.7,a.Width),height=60*a.Scale;
            double x=Math.Clamp(p.CenterX,a.X+width/2,a.X+a.Width-width/2);
            double y=Math.Clamp(p.CenterY+d*.57,a.Y+height/2,a.Y+a.Height-height/2);
            dc.DrawRoundedRectangle(Neutral,Edge,new(x-width/2,y-height/2,width,height),8*a.Scale,8*a.Scale);
            Text(dc,s.Hovered<0?"中性 · 再点 Mode 关闭":ModeMenuSlots.Directions[s.Hovered]+" · "+s.Configuration.Slots[s.Hovered].Name,
                x,y,18*a.Scale,width-12*a.Scale,Brushes.White);
            // The retained child arrow is composed above this Ring and caption.
        }
        dc.Pop();dc.Pop();dc.Pop();
    }
    private void Text(DrawingContext dc,string text,double x,double y,double size,double width,Brush brush)
    {
        var ft=new FormattedText(text,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"),size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip) {
            MaxTextWidth=width,TextAlignment=TextAlignment.Center,Trimming=TextTrimming.CharacterEllipsis,
            MaxLineCount=2
        };
        dc.DrawText(ft,new(x-width/2,y-ft.Height/2));
    }
}
