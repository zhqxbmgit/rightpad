using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class EarnedSettleTests
{
    private const MotionMode Settle = MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5_SETTLE;
    private const MotionMode Control = MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5;
    private const long Origin = 100_000, Frequency = 1_000_000;
    private static readonly RuntimeSettings Settings = new(9, 9, 300, 8, 25, 130);
    private sealed class H : IDisposable
    {
        public long Now = Origin;
        public uint Session, Sequence;
        public readonly List<(long At, int X, int Y)> Moves = [];
        public readonly ResampledMotion Motion;
        public H(MotionMode mode = Settle) => Motion = new((x,y)=>Moves.Add((Now,x,y)),9,9,()=>Now,Frequency,finiteCriticalMode:mode);
        public void Advance(int ms)
        {
            long end = Origin + ms * 1000L;
            while (Motion.Schedule.Deadline is long tick && tick <= end)
            { Now=tick; Motion.Tick(tick,Motion.Schedule.Generation); }
            Now=end;
        }
        public TouchPacket Send(TouchEventType type, int ms, float x, float y=0, ulong run=1)
        {
            Advance(ms);
            if(type==TouchEventType.Down) Session++;
            var p=new TouchPacket(new(2,type,1,Session,Sequence++,run),[new((ulong)ms*1_000_000,x,y)]);
            Motion.ProcessAt(p,Now,new RuntimeSettings(10,10,300,8,25,130));
            return p;
        }
        public int X => Moves.Sum(m=>m.X);
        public void Dispose()=>Motion.Dispose();
    }
    public static IEnumerable<(string Name, Action Run)> Cases
    {
        get
        {
            yield return ("SETTLE short UP freezes target, no flush, exact endpoint and park",Short);
            yield return ("SETTLE identical K24 trajectory before UP",SameKernel);
            yield return ("SETTLE ten rapid short swipes preserve all earned distance",()=>Burst(false));
            yield return ("SETTLE opposite rapid swipes return to exact zero",()=>Burst(true));
            yield return ("SETTLE next DOWN preserves position, ledger and tick phase",Continue);
            yield return ("SETTLE fractional contacts keep canonical state until settled",Fractional);
            yield return ("SETTLE completed contact followed by new contact has clean baseline",Completed);
            foreach(string reason in new[]{"sender","disconnect","dispose"})
            { string captured=reason; yield return ($"SETTLE {reason} cancels frozen tail",()=>Cancel(captured)); }
            yield return ("SETTLE Reset rejects stale scheduled wake",Reset);
            yield return ("SETTLE real Runtime Stop cancels tail and releases backend",()=>RuntimeStop().GetAwaiter().GetResult());
            yield return ("SETTLE single tap, double tap drag, rearm and immediate button release",Gestures);
            yield return ("SETTLE output failure cancels tail",Failure);
            yield return ("SETTLE explicit CLI, fixed parameters and Q0C",Mode);
        }
    }
    private static void Short()
    {
        using var h=new H();h.Send(TouchEventType.Down,0,100);h.Send(TouchEventType.Move,4,110);
        h.Advance(9);int before=h.Moves.Count;
        h.Send(TouchEventType.Up,9,112);Equal(before,h.Moves.Count,"UP must not submit motion");
        Equal((uint?)null,h.Motion.ActiveSessionId,"touch ends while tail continues");
        Check(h.Motion.Schedule.Deadline is not null,"clock remains armed");
        h.Send(TouchEventType.Move,10,999); // Released contact cannot grow its final target.
        h.Advance(200);Equal(108,h.X,"final UP coordinate earned exactly once");
        Check(h.Moves.All(m=>m.X>=0),"no reverse or extra path on monotone input");
        Check(h.Motion.Schedule.Deadline is null,"finite park after completing target");
        Equal(0L,h.Motion.UpFlushCount,"no instant flush");
        before=h.Moves.Count;h.Advance(2000);Equal(before,h.Moves.Count,"no indefinite output");
    }
    private static void SameKernel()
    {
        using var a=new H();using var b=new H(Control);
        foreach(var h in new[]{a,b})h.Send(TouchEventType.Down,0,0);
        for(int t=4;t<=40;t+=4)
        {
            a.Send(TouchEventType.Move,t,t);b.Send(TouchEventType.Move,t,t);
            Equal(b.Motion.Position,a.Motion.Position,"same pre-UP continuous K24");
            Equal(b.X,a.X,"same pre-UP Q0C output");
        }
        a.Advance(41);b.Advance(41);int ax=a.X,bx=b.X;
        a.Send(TouchEventType.Up,41,40);b.Send(TouchEventType.Up,41,40);
        Equal(ax,a.X,"settle UP silent");Equal(360,b.X,"control still flushes");Check(b.X>bx,"control had backlog");
        a.Advance(200);Equal(b.X,a.X,"same final earned endpoint");
    }
    private static void Burst(bool opposite)
    {
        using var h=new H();
        for(int i=0;i<10;i++)
        {
            int t=i*20,sign=opposite&&i%2==1?-1:1;
            h.Send(TouchEventType.Down,t,500+i*100);
            h.Send(TouchEventType.Move,t+4,500+i*100+sign*10);
            h.Send(TouchEventType.Up,t+9,500+i*100+sign*10);
        }
        h.Advance(400);Equal(opposite?0:900,h.X,"contact offsets create no distance");
        if(!opposite)Check(h.Moves.All(m=>m.X>=0),"monotone burst has no overshoot/reversal");
        int total=0;foreach(var m in h.Moves){total+=m.X;Check(total>=0&&total<=(opposite?90:900),"within earned target envelope");}
        Equal(0.0,h.Motion.LifecycleAbortDiscardedDistance,"normal handoff discards no backlog");
        Check(h.Motion.Schedule.Deadline is null,"burst completed and parked");
    }
    private static void Continue()
    {
        using var h=new H();h.Send(TouchEventType.Down,0,0);h.Send(TouchEventType.Move,4,10);h.Send(TouchEventType.Up,9,10);
        h.Advance(25);var schedule=h.Motion.Schedule;var position=h.Motion.Position;int count=h.Moves.Count;
        h.Send(TouchEventType.Down,25,2000);
        Equal(schedule,h.Motion.Schedule,"DOWN preserves absolute 4ms phase and generation");
        Equal(position,h.Motion.Position,"DOWN cannot liquidate backlog");Equal(count,h.Moves.Count,"DOWN emits nothing");
        h.Send(TouchEventType.Move,29,2010);h.Send(TouchEventType.Up,30,2010);h.Advance(200);
        Equal(180,h.X,"both contacts fully paid");
    }
    private static void Fractional()
    {
        using var h=new H();
        for(int i=0;i<10;i++){h.Send(TouchEventType.Down,i*12,0);h.Send(TouchEventType.Up,i*12+4,.0625f);}
        h.Advance(300);Equal(5,h.X,"ten fractional targets sum to 5.625, not ten discarded fractions");
    }
    private static void Completed()
    {
        using var h=new H();h.Send(TouchEventType.Down,0,0);h.Send(TouchEventType.Up,4,10);h.Advance(200);
        h.Send(TouchEventType.Down,300,1000);h.Send(TouchEventType.Up,304,1002);h.Advance(500);
        Equal(108,h.X,"fresh baseline after park");
    }
    private static void Cancel(string reason)
    {
        long now=Stopwatch.Frequency*10;var moves=new List<int>();
        using var m=new ResampledMotion((x,_)=>moves.Add(x),9,9,()=>now,finiteCriticalMode:Settle);
        using var receiver=new UdpReceiver(new(IPAddress.Loopback,0),TextWriter.Null,motion:m,detailedLogging:false);
        void Send(byte[] data)=>receiver.ProcessDatagram(data,new(IPAddress.Loopback,1234),now);
        Send(PresenceTests.Touch(1,TouchEventType.Down,0));
        now+=Stopwatch.Frequency/250;Send(PresenceTests.Touch(1,TouchEventType.Up,1,10,4_000_000));
        long gen=m.Schedule.Generation;Check(m.Schedule.Deadline is not null,"tail exists before cancellation");
        if(reason=="sender")Send(PresenceTests.Heartbeat(2));
        else if(reason=="disconnect"){now+=Stopwatch.Frequency*2;receiver.CheckTimeouts(now);Check(!receiver.Presence.Connected,"disconnected");}
        else receiver.Dispose();
        m.Tick(now+Stopwatch.Frequency,gen);m.Tick(now+Stopwatch.Frequency,m.Schedule.Generation);
        Equal(0,moves.Count,"old tail never paid after lifecycle cancellation");
        Check(m.Schedule.Deadline is null,"clock parked on cancellation");
    }
    private static void Reset()
    {
        using var h=new H();h.Send(TouchEventType.Down,0,0);h.Send(TouchEventType.Up,4,10);
        var gen=h.Motion.Schedule.Generation;h.Motion.Reset();h.Motion.Tick(Origin+500000,gen);
        Equal(0,h.X,"Reset fences old wake");
        h.Send(TouchEventType.Down,20,100);h.Send(TouchEventType.Up,24,101);h.Advance(200);Equal(9,h.X,"reset discards only old tail");
    }
    private static async Task RuntimeStop()
    {
        var native=new VirtualHidTests.FakeNative();
        var runtime=new ReceiverRuntime(new(Settings),TextWriter.Null,MouseBackend.VirtualHid,new(IPAddress.Loopback,0),
            ()=>new LibVirtualHidMouseOutput(native),motionMode:Settle);
        using var sender=new UdpClient();
        try
        {
            await runtime.StartAsync();
            await sender.SendAsync(PresenceTests.Touch(1,TouchEventType.Down,0),runtime.LocalEndpoint!);
            await sender.SendAsync(PresenceTests.Touch(1,TouchEventType.Up,1,1000,4_000_000),runtime.LocalEndpoint!);
            await RuntimeTests.Until(()=>runtime.CaptureSnapshot().AcceptedPackets>=2);
            await runtime.StopAsync();int count=native.Events.Count;
            await Task.Delay(170);Equal(count,native.Events.Count,"no tail after Stop returned");
            Equal(ReceiverState.Stopped,runtime.CaptureSnapshot().RuntimeState,"Stop complete");
            Check(runtime.CaptureSnapshot().LastError is null,"no Stop error");
            Check(native.Events.Any(e=>e.Kind=="dispose"),"backend released");
        }
        finally{await runtime.StopAsync();}
    }
    private static void Gestures()
    {
        using var h=new H();var native=new VirtualHidTests.FakeNative();
        using var buttons=new LeftButtonController(native.LeftDown,native.LeftUp,_=>{},()=>throw new Exception("button failure"));
        var gesture=new GestureProcessor(hold=>buttons.Click(hold),Settings,buttons.BeginDrag,buttons.EndDrag);
        void Send(TouchEventType type,int time,float x)=>gesture.Process(h.Send(type,time,x));
        Send(TouchEventType.Down,100,0);Send(TouchEventType.Up,120,0);
        Check(SpinWait.SpinUntil(()=>native.Events.Count(e=>e.Kind=="up")==1,2000),"single tap releases");
        Equal(1L,gesture.ClicksTriggered,"single tap once");
        Send(TouchEventType.Down,150,1000);Check(gesture.IsDragging,"double tap begins drag");
        Send(TouchEventType.Move,154,1010);Send(TouchEventType.Up,155,1010);
        Check(!gesture.IsDragging&&native.Events.Last().Kind=="up","drag button released immediately at UP");
        Check(h.Motion.Schedule.Deadline is not null,"motion tail does not delay button UP");
        Send(TouchEventType.Down,170,2000);Check(gesture.IsDragging,"drag rearm during settlement");
        Send(TouchEventType.Move,174,1990);Send(TouchEventType.Up,175,1990);
        h.Advance(400);Equal(0,h.X,"opposite drags exact final endpoint");
        Equal(2L,gesture.DragStarts,"two drag starts");Equal(2L,gesture.DragEnds,"two drag ends");
        Check(native.Events.Select(e=>e.Kind).SequenceEqual(new[]{"down","up","down","up","down","up"}),"balanced buttons without extra click");
    }
    private static void Failure()
    {
        long now=Origin;
        using var m=new ResampledMotion((_,_)=>throw new IOException("test output failed"),9,9,()=>now,Frequency,finiteCriticalMode:Settle);
        m.Process(new(new(2,TouchEventType.Down,1,1,0,1),[new(0,0,0)]));
        now+=4000;m.Process(new(new(2,TouchEventType.Up,1,1,1,1),[new(4_000_000,100,0)]));
        now+=40000;
        RawMotionProcessorTests.Throws<IOException>(()=>m.Tick(now,m.Schedule.Generation));
        Check(m.Schedule.Deadline is null,"failed tail discarded without retry");
    }
    private static void Mode()
    {
        Equal(Settle,Receiver.Program.ParseLaunchArguments(["--dev-motion-mode",Settle.ToString()]).Motion,"explicit selection");
        Equal("Q0C",MotionModes.QuantizerName(Settle),"same quantizer");
        Equal(MotionModes.FiniteCriticalParameters(Control),MotionModes.FiniteCriticalParameters(Settle),"same kernel parameters");
        Equal(MotionMode.RAW,Receiver.Program.ParseLaunchArguments([]).Motion,"default unchanged");
    }
}
