using System.Diagnostics;
using System.Net;
using System.Text.Json;
using static Rightpad.Receiver.Tests.Program;
using static Rightpad.Receiver.Tests.RawMotionProcessorTests;

namespace Rightpad.Receiver.Tests;

internal static class BoxcarMotionTests
{
    private const long Frequency = 1_000_000, Origin = 100_000;
    private static TouchSample S(double ms, float x, float y = 0) => new((ulong)Math.Round(ms * 1_000_000), x, y);
    private static TouchPacket P(TouchEventType type, params TouchSample[] samples) => new(new(2, type, (ushort)samples.Length, 1, 0, 1), samples);
    private static void Near(double expected, double actual, double tolerance = 1e-9) => Check(Math.Abs(expected - actual) <= tolerance, $"expected {expected:R}, actual {actual:R}");
    private sealed class H : IDisposable
    {
        public long Now = Origin;
        public readonly ResampledMotion Motion;
        public readonly List<(long At, int X, int Y)> Moves = new();
        public H(int window, double gain = 1)
        {
            Motion = new((x,y)=>Moves.Add((Now-Origin,x,y)),gain,gain,()=>Now,Frequency,boxcarWindowMs:window);
            Motion.Process(P(TouchEventType.Down,S(0,0)));
        }
        public void Advance(double ms)
        {
            long end=Origin+(long)Math.Round(ms*1000);
            while(Motion.Schedule.Deadline is long tick && tick<=end) { Now=tick;Motion.Tick(tick,Motion.Schedule.Generation); }
            Now=end;
        }
        public void Send(double ms, params TouchSample[] samples) { Advance(ms);Motion.Process(P(TouchEventType.Move,samples)); }
        public void Up(double ms, float x, float y=0) { Advance(ms);Motion.Process(P(TouchEventType.Up,S(ms,x,y))); }
        public int X=>Moves.Sum(x=>x.X);
        public int Y=>Moves.Sum(x=>x.Y);
        public void Dispose()=>Motion.Dispose();
    }
    public static IEnumerable<(string Name, Action Run)> Cases
    {
        get
        {
            foreach(int w in new[]{4,8})
            {
                yield return ($"F{w} constant target / zero extension",()=>Constant(w));
                yield return ($"F{w} exact linear ramp",()=>Linear(w));
                yield return ($"F{w} independent decimal piecewise integral",()=>Reference(w));
                yield return ($"F{w} startup full fixed denominator",()=>Startup(w));
                yield return ($"F{w} finite fast held stop",()=>Stop(w));
                yield return ($"F{w} negative movement",()=>Direction(w,-1,0));
                yield return ($"F{w} XY / diagonal movement",()=>Direction(w,1,-1));
                yield return ($"F{w} positive to negative / batch reversal",()=>Reversal(w,1));
                yield return ($"F{w} negative to positive / batch reversal",()=>Reversal(w,-1));
                foreach(int counts in new[]{1,2,3,5,10,20})
                    yield return ($"F{w} micro {counts} counts before UP",()=>Micro(w,counts));
                yield return ($"F{w} fractional residual ownership",()=>Fraction(w));
                yield return ($"F{w} UP combines B and filter pending",()=>Up(w));
                yield return ($"F{w} UP after base parked / stale generation",()=>ParkedUp(w));
                yield return ($"F{w} UP/tick native-write race",()=>Race(w));
                yield return ($"F{w} new DOWN resets window",()=>NewDown(w));
                yield return ($"F{w} run/presence/disposal cancels window",()=>Presence(w));
                yield return ($"F{w} dispose and output exception",()=>Failure(w));
                yield return ($"F{w} actual timer native failure cleanup",()=>RuntimeFailure(w).GetAwaiter().GetResult());
                yield return ($"F{w} original gesture/rearm/UP order",()=>Gesture(w));
                yield return ($"F{w} late sample does not rewrite past",()=>Late(w));
                yield return ($"F{w} partially late segment causal integral",()=>CausalArrival(w));
                yield return ($"F{w} missed tick skips without changing window",()=>Missed(w));
                yield return ($"F{w} lock serialization never rewinds window",()=>SerializedTime(w));
                yield return ($"F{w} timestamp duplicate/backward fallback",()=>Timestamp(w));
                yield return ($"F{w} GUI and runtime fixed mode",()=>Gui(w));
                yield return ($"F{w} trace base/filter/combined pending",()=>Trace(w));
            }
            yield return ("boxcar fixed allowed windows / bounded history",Bound);
            yield return ("boxcar mode CLI / RAW and B preserved",Arguments);
        }
    }
    private static void Constant(int w)
    {
        using var h=new H(w);h.Send(0,S(0,10));h.Advance(12+w);
        Equal(10,h.X,"constant target completes");Equal(0,h.Y,"no orthogonal output");
        Check(h.Motion.Schedule.Deadline is null,"finite park");int n=h.Moves.Count;h.Advance(1000);Equal(n,h.Moves.Count,"no drift");
    }
    private static void Linear(int w)
    {
        using var h=new H(w);h.Send(0,S(100,100));h.Advance(48);
        Near(36-w/2.0,h.Motion.Position.X);Equal(w,h.Motion.BoxcarWindowMs,"fixed W");
    }
    // Deliberately unbounded decimal oracle for timely available points, independent of the production ring.
    private static double ReferenceAverage((double T,double X)[] points,double at,int w)
    {
        decimal lo=(decimal)(at-w),hi=(decimal)at,area=0;
        var cuts=points.Select(p=>(decimal)p.T).Where(t=>t>lo&&t<hi).Prepend(lo).Append(hi).Order().ToArray();
        decimal Value(decimal t)
        {
            if(t<=(decimal)points[0].T)return (decimal)points[0].X;
            for(int i=1;i<points.Length;i++)if(t<=(decimal)points[i].T)
            {
                var a=points[i-1];var b=points[i];return (decimal)a.X+((decimal)b.X-(decimal)a.X)*(t-(decimal)a.T)/((decimal)b.T-(decimal)a.T);
            }
            return (decimal)points[^1].X;
        }
        for(int i=1;i<cuts.Length;i++)area+=(Value(cuts[i-1])+Value(cuts[i]))*(cuts[i]-cuts[i-1])/2;
        return (double)(area/w);
    }
    private static void Reference(int w)
    {
        using var h=new H(w);var samples=new[]{S(3,2,-3),S(7,11,4),S(11,-4,-2),S(18,8,8),S(29,9,-4),S(41,9,-4)};
        h.Send(0,samples);
        var xs=samples.Select(p=>(T:12+p.TimestampNs/1e6,X:(double)p.X)).Prepend((12d,0d)).ToArray();
        var ys=samples.Select(p=>(T:12+p.TimestampNs/1e6,X:(double)p.Y)).Prepend((12d,0d)).ToArray();
        for(int t=12;t<=72;t+=4){h.Advance(t);Near(ReferenceAverage(xs,t,w),h.Motion.Position.X);Near(ReferenceAverage(ys,t,w),h.Motion.Position.Y);}
    }
    private static void Startup(int w)
    {
        using var h=new H(w);h.Send(0,S(8,8));h.Advance(12);Near(0,h.Motion.Position.X);
        h.Advance(16);Near(8.0/w,h.Motion.Position.X); // integral of ramp 0..4 over the FULL W
    }
    private static void Stop(int w)
    {
        using var h=new H(w);for(int t=4;t<=800;t+=4)h.Send(t,S(t,t*4));
        h.Advance(812+w);Equal(3200,h.X,"endpoint without UP");Near(3200,h.Motion.Position.X);
        Check(h.Motion.Schedule.Deadline is null,"finite support parked");
        Equal((long)(812+w)*1000,h.Moves[^1].At,"last integer output deadline");
        int count=h.Moves.Count;h.Advance(2000);Equal(count,h.Moves.Count,"no exponential tail");
    }
    private static void Direction(int w,int x,int y)
    {
        using var h=new H(w);h.Send(0,S(4,4*x,4*y),S(8,8*x,8*y));h.Advance(20+w);
        Equal(8*x,h.X,"x endpoint");Equal(8*y,h.Y,"y endpoint");Check(h.Moves.All(m=>m.X*x>=0&&m.Y*y>=0),"no wrong direction");
    }
    private static void Reversal(int w,int sign)
    {
        using var h=new H(w);h.Send(0,S(4,4*sign),S(8,8*sign),S(12,4*sign),S(16,0));h.Advance(28+w);
        Equal(0,h.X,"net conserved");var directions=h.Moves.Where(m=>m.X!=0).Select(m=>Math.Sign(m.X)).ToArray();
        Equal(sign,directions[0],"starts original direction");Equal(-sign,directions[^1],"reverses");
        Equal(1,directions.Zip(directions.Skip(1)).Count(p=>p.First!=p.Second),"no velocity ringing");
        Check(h.Motion.Schedule.Deadline is null,"finite reversed tail");
    }
    private static void Micro(int w,int target)
    {
        using var h=new H(w);for(int t=4;t<=40;t+=4)h.Send(t,S(t,target*t/40f));h.Advance(52+w);
        Equal(target,h.X,"all counts arrive before UP");Equal((long)(52+w)*1000,h.Moves[^1].At,"expected finite-window arrival");
        Check(h.Moves.All(m=>m.X>=0),"monotonic");Check(h.Motion.ActiveSessionId is not null,"still held");
        int n=h.Moves.Count;h.Advance(540);Equal(n,h.Moves.Count,"held silence");h.Up(540,target);Equal(n,h.Moves.Count,"UP owes zero");
    }
    private static void Fraction(int w)
    {
        using var h=new H(w);h.Send(0,S(4,.25f),S(8,.5f),S(12,.75f),S(16,1));h.Advance(28+w);
        Equal(1,h.X,"existing residual accumulates fractions");Equal(1,h.Moves.Count,"no artificial +/-1");h.Up(40,1.75f);Equal(1,h.X,"fractional UP below one count");
    }
    private static void Up(int w)
    {
        using var h=new H(w);h.Send(0,S(4,4),S(8,8));h.Advance(20);
        var bp=h.Motion.BasePending;var fp=h.Motion.BoxcarPending;var pending=h.Motion.Pending;
        Near(bp.X+fp.X,pending.X);Check(fp.X>0,"filter pending visible");
        long gen=h.Motion.Schedule.Generation;h.Up(21,10);Equal(10,h.X,"UP final coordinate plus both pending components");
        h.Motion.Tick(Origin+100000,gen);Equal(10,h.X,"no stale motion");
    }
    private static void ParkedUp(int w)
    {
        using var h=new H(w);h.Send(4,S(4,8));h.Advance(16);Near(0,h.Motion.BasePending.X);Check(h.Motion.BoxcarPending.X>0,"filter owes after base endpoint");
        long gen=h.Motion.Schedule.Generation;h.Up(17,8);Equal(8,h.X,"pending filter flush");int n=h.Moves.Count;h.Motion.Tick(Origin+100000,gen);Equal(n,h.Moves.Count,"UP fence");
    }
    private static void NewDown(int w)
    {
        using var h=new H(w);h.Send(4,S(4,100));long gen=h.Motion.Schedule.Generation;h.Advance(8);h.Motion.Process(P(TouchEventType.Down,S(8,100)));
        h.Motion.Tick(Origin+100000,gen);h.Send(12,S(12,102));h.Advance(24+w);Equal(2,h.X,"new baseline and cleared history");
    }
    private static void Failure(int w)
    {
        using(var h=new H(w)){h.Send(4,S(4,100));h.Motion.Dispose();h.Advance(100);Equal(0,h.X,"disposed");}
        long now=Origin;using var m=new ResampledMotion((_,_)=>throw new IOException("test output"),monotonicNow:()=>now,clockFrequency:Frequency,boxcarWindowMs:w);
        m.Process(P(TouchEventType.Down,S(0,0)));m.Process(P(TouchEventType.Move,S(4,100)));
        Throws<IOException>(()=>m.Tick(Origin+20000,m.Schedule.Generation));Check(m.ActiveSessionId is null&&m.Schedule.Deadline is null,"failure clears pending and window");
    }
    private static void Race(int w)
    {
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();long now=Origin;var events=new List<string>();
        using var m=new ResampledMotion((_,_)=>{entered.Set();Check(release.Wait(3000),"release");lock(events)events.Add("move");},monotonicNow:()=>now,clockFrequency:Frequency,boxcarWindowMs:w);
        m.Process(P(TouchEventType.Down,S(0,0)));m.Process(P(TouchEventType.Move,S(4,100)));long gen=m.Schedule.Generation;
        var tick=Task.Run(()=>m.Tick(Origin+16000,gen));Check(entered.Wait(3000),"native entered");
        var up=Task.Run(()=>{m.Process(P(TouchEventType.Up,S(5,100)));lock(events)events.Add("gesture-up");});
        release.Set();Task.WaitAll(tick,up);m.Tick(Origin+50000,gen);Equal("gesture-up",events[^1],"all old output ordered before gesture");
    }
    private static void Presence(int w)
    {
        long now=Stopwatch.Frequency*10;var moves=new List<int>();using var m=new ResampledMotion((x,_)=>moves.Add(x),monotonicNow:()=>now,boxcarWindowMs:w);
        using var r=new UdpReceiver(new(IPAddress.Loopback,0),TextWriter.Null,motion:m,detailedLogging:false);
        void Send(byte[] b)=>r.ProcessDatagram(b,new(IPAddress.Loopback,1234),now);
        Send(PresenceTests.Touch(1,TouchEventType.Down,0));Send(PresenceTests.Touch(1,TouchEventType.Move,1,10,4_000_000));Send(PresenceTests.Heartbeat(2));
        m.Tick(now+Stopwatch.Frequency,m.Schedule.Generation);Equal(0,moves.Count,"run switch clears window");
        Send(PresenceTests.Touch(2,TouchEventType.Down,0));Send(PresenceTests.Touch(2,TouchEventType.Move,1,10,4_000_000));now+=2*Stopwatch.Frequency;r.CheckTimeouts(now);
        m.Tick(now,m.Schedule.Generation);Equal(0,moves.Count,"presence expiry clears window");
        Send(PresenceTests.Touch(2,TouchEventType.Down,2));Send(PresenceTests.Touch(2,TouchEventType.Move,3,10,4_000_000));r.Dispose();m.Tick(now+Stopwatch.Frequency,m.Schedule.Generation);Equal(0,moves.Count,"receiver dispose clears window");
    }
    private static void Gesture(int w)
    {
        long now=Stopwatch.Frequency*10;var events=new List<string>();using var m=new ResampledMotion((x,_)=>events.Add("move:"+x),monotonicNow:()=>now,boxcarWindowMs:w);
        var gesture=new GestureProcessor(()=>events.Add("click"),()=>events.Add("drag-down"),()=>events.Add("drag-up"));
        using var r=new UdpReceiver(new(IPAddress.Loopback,0),TextWriter.Null,motion:m,gesture:gesture,detailedLogging:false);uint seq=0;
        void Send(TouchEventType type,float x,ulong ns,uint session)=>r.ProcessDatagram(PacketDecoderTests.Encode(type,session,seq++,new TouchSample(ns,x,0)),new(IPAddress.Loopback,1234),now);
        Send(TouchEventType.Down,0,0,1);Send(TouchEventType.Up,0,10_000_000,1);Send(TouchEventType.Down,0,20_000_000,2);Send(TouchEventType.Move,10,24_000_000,2);Send(TouchEventType.Up,12,28_000_000,2);Send(TouchEventType.Down,0,38_000_000,3);Send(TouchEventType.Up,0,48_000_000,3);
        Check(events.SequenceEqual(new[]{"click","drag-down","move:12","drag-up","drag-down","drag-up"}),"raw gesture path / motion fence / rearm preserved");
    }
    private static void Late(int w)
    {
        using var h=new H(w);h.Send(40,S(4,8));h.Advance(44);Near(w==4?8:4,h.Motion.Position.X);h.Advance(48);Equal(8,h.X,"late jump finite completion");
    }
    private static void CausalArrival(int w)
    {
        using var h=new H(w);h.Send(4,S(4,4));h.Advance(16);h.Send(17,S(8,8));h.Advance(20);
        Near(w==4?5.875:3.9375,h.Motion.Position.X); // [16,17] was held at 4; later input cannot turn it into a ramp.
    }
    private static void Missed(int w)
    {
        using var h=new H(w);h.Send(0,S(100,100));h.Now=Origin+27000;h.Motion.Tick(h.Now,h.Motion.Schedule.Generation);
        Near(15-w/2.0,h.Motion.Position.X);Equal((long?)Origin+28000,h.Motion.Schedule.Deadline,"original fixed phase");Equal(3L,h.Motion.MissedTicks,"skip obsolete ticks");Equal(w,h.Motion.BoxcarWindowMs,"no window adjustment");
    }
    private static void SerializedTime(int w)
    {
        using var h=new H(w);h.Send(4,S(4,4));h.Advance(16);
        h.Now=Origin+23000;h.Motion.ProcessAt(P(TouchEventType.Move,S(8,8)),h.Now,null);
        h.Motion.Tick(Origin+20000,h.Motion.Schedule.Generation);
        Equal((long?)Origin+24000,h.Motion.Schedule.Deadline,"stale wake resumes original cadence");
        h.Advance(40);Equal(8,h.X,"no rewind or lost endpoint");
    }
    private static void Timestamp(int w)
    {
        using var h=new H(w);h.Send(4,S(4,4),S(4,8));h.Advance(16+w);Equal(8,h.X,"duplicate last wins");
        h.Send(32,S(2,10));h.Advance(44+w);Equal(10,h.X,"fixed abnormal timestamp fallback still completes");Equal(1L,h.Motion.NonMonotonicTimestamps,"backward recorded");
    }
    private static MotionMode Mode(int w)=>w==4?MotionMode.RESAMPLED_250HZ_BOXCAR_4MS:MotionMode.RESAMPLED_250HZ_BOXCAR_8MS;
    private static void Gui(int w)
    {
        var runtime=new ReceiverRuntime(new(),TextWriter.Null,MouseBackend.VirtualHid,motionMode:Mode(w));
        var vm=new MainViewModel(runtime,null!,null!);Check(vm.MotionModeName.Contains($"BOXCAR {w} ms"),"actual readonly mode");Check(vm.MotionExplanation.Contains("12 ms")&&vm.MotionExplanation.Contains($"fixed {w} ms"),"actual fixed configuration");
    }
    private static async Task RuntimeFailure(int w)
    {
        var native=new VirtualHidTests.FakeNative{FailMove=true};var runtime=new ReceiverRuntime(new(),TextWriter.Null,MouseBackend.VirtualHid,new(IPAddress.Loopback,0),()=>new LibVirtualHidMouseOutput(native),motionMode:Mode(w));
        await runtime.StartAsync();using var sender=new System.Net.Sockets.UdpClient();await sender.SendAsync(PresenceTests.Touch(1,TouchEventType.Down,0),runtime.LocalEndpoint!);await sender.SendAsync(PresenceTests.Touch(1,TouchEventType.Move,1,10,4_000_000),runtime.LocalEndpoint!);
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));Equal(ReceiverState.Error,runtime.CaptureSnapshot().RuntimeState,"real timer failure visible");Check(native.Events.Any(e=>e.Kind=="dispose"),"backend disposed after clock cleanup");await runtime.StopAsync();
    }
    private static void Trace(int w)
    {
        string dir=Path.Combine(Path.GetTempPath(),"rightpad-boxcar-"+Guid.NewGuid());
        try
        {
            long now=Origin;
            using(var trace=new MotionTrace(dir,Mode(w)))
            using(var m=new ResampledMotion((_,_)=>{},monotonicNow:()=>now,clockFrequency:Frequency,trace:trace,boxcarWindowMs:w))
            {
                m.Process(P(TouchEventType.Down,S(0,0)));m.Process(P(TouchEventType.Move,S(4,8)));now=Origin+16000;m.Tick(now,m.Schedule.Generation);now+=1000;m.Process(P(TouchEventType.Up,S(5,10)));
            }
            var lines=File.ReadAllLines(Path.Combine(dir,"motion.csv"));Check(lines.Any(x=>x.StartsWith("BoxcarPosition,"))&&lines.Any(x=>x.StartsWith("BoxcarUpPending,")),"minimal trace fields present");
            var up=lines.Single(x=>x.StartsWith("UpFlush,")).Split(',');var pending=lines.Single(x=>x.StartsWith("BoxcarUpPending,")).Split(',');
            Near(double.Parse(up[7],System.Globalization.CultureInfo.InvariantCulture),double.Parse(up[9],System.Globalization.CultureInfo.InvariantCulture)+double.Parse(pending[7],System.Globalization.CultureInfo.InvariantCulture));
            using var meta=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,"metadata.json")));Equal(w,meta.RootElement.GetProperty("BoxcarWindowMs").GetInt32(),"trace W fixed");
        }
        finally{Directory.Delete(dir,true);}
    }
    private static void Bound()
    {
        Throws<ArgumentOutOfRangeException>(()=>new CausalBoxcar(6,Frequency));var filter=new CausalBoxcar(8,Frequency);filter.Reset(0);
        for(int i=0;i<CausalBoxcar.Capacity;i++)filter.Add(i,i+1,i,i,i+1,i+1);
        Throws<InvalidOperationException>(()=>filter.Add(CausalBoxcar.Capacity,CausalBoxcar.Capacity+1,0,0,1,1));
        Equal(CausalBoxcar.Capacity,filter.SegmentCount,"bounded storage");Equal(8,filter.WindowMs,"overflow does not shrink W");
        filter.Reset(10000);filter.Add(10000,20000,0,0,0,0);Near(0,filter.Average(20000,0,0).X);Check(filter.IsSettled(20000),"reset clears history");
    }
    private static void Arguments()
    {
        foreach(var mode in new[]{MotionMode.RAW,MotionMode.RESAMPLED_250HZ,Mode(4),Mode(8)})Equal(mode,Receiver.Program.ParseLaunchArguments(["--dev-motion-mode",mode.ToString()]).Motion,"explicit fixed CLI mode");
        Throws<ArgumentException>(()=>Receiver.Program.ParseLaunchArguments(["--dev-motion-mode","RESAMPLED_250HZ_BOXCAR_6MS"]));
        Equal(MotionMode.RAW,Receiver.Program.ParseLaunchArguments([]).Motion,"no production default switch");
    }
}
