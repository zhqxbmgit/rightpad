using System.Diagnostics;
using System.Net;
using System.Text.Json;
using static Rightpad.Receiver.Tests.Program;
using static Rightpad.Receiver.Tests.RawMotionProcessorTests;

namespace Rightpad.Receiver.Tests;

internal static class FiniteCriticalMotionTests
{
    private const long Frequency = 1_000_000, Origin = 100_000;
    private static readonly MotionMode[] Modes = [MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5, MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K35_R4];
    private static TouchSample S(double ms, float x, float y = 0) => new((ulong)Math.Round(ms * 1_000_000), x, y);
    private static TouchPacket P(TouchEventType type, params TouchSample[] samples) => new(new(2, type, (ushort)samples.Length, 1, 0, 1), samples);
    private static void Near(double expected, double actual, double tolerance = 1e-9) => Check(Math.Abs(expected - actual) <= tolerance, $"expected {expected:R}, actual {actual:R}");
    private sealed class H : IDisposable
    {
        public long Now = Origin;
        public readonly ResampledMotion Motion;
        public readonly List<(long At, int X, int Y)> Moves = new();
        public H(MotionMode mode, double gain = 1)
        {
            Motion = new((x,y)=>Moves.Add((Now-Origin,x,y)),gain,gain,()=>Now,Frequency,finiteCriticalMode:mode);
            Motion.Process(P(TouchEventType.Down,S(0,0)));
        }
        public void Advance(double ms)
        {
            long end = Origin + (long)Math.Round(ms * 1000);
            while (Motion.Schedule.Deadline is long tick && tick <= end) { Now=tick; Motion.Tick(tick,Motion.Schedule.Generation); }
            Now=end;
        }
        public void Send(double ms, params TouchSample[] samples) { Advance(ms); Motion.Process(P(TouchEventType.Move,samples)); }
        public void Up(double ms,float x,float y=0) { Advance(ms);Motion.Process(P(TouchEventType.Up,S(ms,x,y))); }
        public int X => Moves.Sum(x=>x.X);
        public void Dispose()=>Motion.Dispose();
    }
    public static IEnumerable<(string Name, Action Run)> Cases
    {
        get
        {
            foreach (var mode in Modes)
            {
                string label=mode.ToString();
                yield return ($"{label} normalization / unit step / startup zero history",()=>Step(mode));
                yield return ($"{label} exact constant / large local anchor",()=>Constant(mode));
                yield return ($"{label} analytic linear ramp / mean delay",()=>Ramp(mode));
                yield return ($"{label} positive and negative symmetry",()=>Symmetry(mode));
                foreach(int target in new[]{1,2,3,5,10,20}) foreach(int sign in new[]{-1,1})
                {
                    int value=target*sign;
                    yield return ($"{label} held integer completion {value}",()=>Micro(mode,value));
                }
                foreach(float amplitude in new[]{.25f,.49f,.51f,.75f})
                {
                    yield return ($"{label} subcount {amplitude}",()=>Subcount(mode,amplitude));
                    foreach(int dwell in new[]{20,100,250})
                        yield return ($"{label} alternating {amplitude} dwell {dwell}",()=>Chatter(mode,amplitude,dwell));
                }
                yield return ($"{label} slow/medium/fast stop and both reversals",()=>StopReverse(mode));
                yield return ($"{label} late arrival realized prefix",()=>Late(mode));
                yield return ($"{label} duplicate timestamps and burst history",()=>Duplicate(mode));
                yield return ($"{label} missed ticks retain phase and fixed support",()=>Missed(mode));
                yield return ($"{label} serialized old wake does not rewind",()=>Serialized(mode));
                yield return ($"{label} UP backlog / fence / after completion",()=>Up(mode));
                yield return ($"{label} new DOWN and reset clear history",()=>Reset(mode));
                yield return ($"{label} UP concurrent with output",()=>Race(mode));
                yield return ($"{label} sender/presence/dispose lifecycle",()=>Presence(mode));
                yield return ($"{label} real clock output failure becomes Runtime Error",()=>RuntimeFailure(mode).GetAwaiter().GetResult());
                yield return ($"{label} bounded history / overflow / corruption",()=>Bound(mode));
                yield return ($"{label} per-run sensitivity frozen",()=>Gain(mode));
                yield return ($"{label} trace kernel / UP accounting / metadata",()=>Trace(mode));
                yield return ($"{label} mode CLI and read-only GUI",()=>Ui(mode));
            }
            yield return ("finite critical rejects unsupported modes and filter composition",()=>
            {
                Throws<ArgumentOutOfRangeException>(()=>new CausalFiniteCritical(MotionMode.RAW,Frequency));
                Throws<ArgumentException>(()=>new ResampledMotion((_,_)=>{},clockFrequency:Frequency,boxcarWindowMs:4,finiteCriticalMode:Modes[0]));
                Throws<ArgumentException>(()=>Receiver.Program.ParseLaunchArguments(["--tau","24"]));
                Equal(MotionMode.RAW,Receiver.Program.ParseLaunchArguments([]).Motion,"existing default unchanged");
            });
        }
    }
    private static void Step(MotionMode mode)
    {
        var k=new CausalFiniteCritical(mode,Frequency);k.Reset(0);double r=k.SupportMs/(double)k.TauMs;
        Near(1-Math.Exp(-r)*(1+r),k.Normalization,1e-15);double previous=0;
        for(int t=1;t<=k.SupportMs+10;t++)
        {
            k.Add((t-1)*1000,t*1000,1,0,1,0);var p=k.Position(t*1000,1,0);
            double z=Math.Min(t,k.SupportMs)/(double)k.TauMs,expected=(1-Math.Exp(-z)*(1+z))/k.Normalization;
            Near(expected,p.X);Check(p.X>=previous-1e-14 && p.X<=1+1e-14,"positive normalized step");previous=p.X;
        }
        Equal(1.0,previous,"finite exact endpoint without UP");
    }
    private static void Constant(MotionMode mode)
    {
        var k=new CausalFiniteCritical(mode,Frequency);k.Reset(0);k.Add(0,200000,1e12,-1e12,1e12,-1e12);
        var p=k.Position(200000,1e12,-1e12);Equal(1e12,p.X,"constant exact large anchor");Equal(-1e12,p.Y,"negative exact large anchor");
    }
    private static void Ramp(MotionMode mode)
    {
        var k=new CausalFiniteCritical(mode,Frequency);k.Reset(0);k.Add(0,500000,0,0,500,0);
        double r=k.SupportMs/(double)k.TauMs,mean=k.TauMs*(2-Math.Exp(-r)*(r*r+2*r+2))/k.Normalization;
        Near(500-mean,k.Position(500000,500,0).X,1e-10);
    }
    private static void Symmetry(MotionMode mode)
    {
        using var a=new H(mode);using var b=new H(mode);
        for(int t=4;t<=80;t+=4){a.Send(t,S(t,t));b.Send(t,S(t,-t));a.Advance(t+1);b.Advance(t+1);Near(a.Motion.Position.X,-b.Motion.Position.X);}
        a.Advance(250);b.Advance(250);Equal(a.X,-b.X,"Q0 symmetry");
    }
    private static void Micro(MotionMode mode,int target)
    {
        using var h=new H(mode);for(int t=4;t<=40;t+=4)h.Send(t,S(t,target*t/40f));int end=52+h.Motion.KernelSupportMs;h.Advance(end);
        Equal(target,h.X,"integer target completed held");Equal((double)target,h.Motion.Position.X,"continuous exact");
        Check(h.Motion.ActiveSessionId is not null,"UP not required");Check(h.Motion.Schedule.Deadline is null,"finite park");
        int n=h.Moves.Count;h.Advance(500);h.Up(500,target);Equal(n,h.Moves.Count,"no completed tail/UP double output");
    }
    private static void Subcount(MotionMode mode,float target)
    {
        using var h=new H(mode);h.Send(4,S(4,target));h.Advance(200);Equal(0,h.X,"no threshold lowering");Near(target,h.Motion.Position.X);h.Up(220,target);Equal(0,h.X,"UP uses original Q0");
    }
    private static void Chatter(MotionMode mode,float amplitude,int dwell)
    {
        using var h=new H(mode);
        for(int t=4;t<=2000;t+=4)h.Send(t,S(t,((t/dwell)%2==0?1:-1)*amplitude));
        h.Advance(2300);Equal(0,h.Moves.Count,"no QN-like integer threshold chatter");
    }
    private static void StopReverse(MotionMode mode)
    {
        foreach(int speed in new[]{40,400,4000})foreach(int sign in new[]{-1,1})
        {
            using var h=new H(mode);for(int t=4;t<=800;t+=4)h.Send(t,S(t,sign*speed*t/1000f));h.Advance(812+h.Motion.KernelSupportMs);
            Near(sign*speed*.8,h.Motion.Position.X,1e-4);Check(h.Motion.Schedule.Deadline is null,"stop finite park");
            for(int t=1004;t<=1800;t+=4)h.Send(t,S(t,sign*speed*(1800-t)/1000f));h.Advance(1812+h.Motion.KernelSupportMs);
            Equal(0.0,h.Motion.Position.X,"reversal net endpoint");Check(Math.Abs(h.X)<=1,"existing residual budget");Check(h.Moves.Any(m=>Math.Sign(m.X)==-sign),"opposite output");
        }
    }
    private static void Late(MotionMode mode)
    {
        using var h=new H(mode);h.Send(4,S(4,4));h.Advance(40);var before=h.Motion.Position;h.Send(40,S(8,8));
        Equal(before,h.Motion.Position,"arrival never emits or rewrites past output");h.Advance(40+h.Motion.KernelSupportMs);Equal(8,h.X,"late target completes");
        Check(h.Motion.LateSamples>0,"late event recorded");
    }
    private static void Duplicate(MotionMode mode)
    {
        using var h=new H(mode);h.Send(4,S(4,4),S(4,8),S(8,12),S(12,16));h.Advance(24+h.Motion.KernelSupportMs);
        Equal(16,h.X,"duplicate last target and burst endpoint");Equal(1L,h.Motion.DuplicateTimestamps,"duplicate diagnostic");
        h.Send(200,S(2,20));h.Advance(212+h.Motion.KernelSupportMs);Equal(20,h.X,"fixed abnormal timestamp fallback");
    }
    private static void Missed(MotionMode mode)
    {
        using var h=new H(mode);h.Send(0,S(100,100));h.Now=Origin+27000;h.Motion.Tick(h.Now,h.Motion.Schedule.Generation);
        Equal(3L,h.Motion.MissedTicks,"obsolete opportunities skipped");Equal((long?)Origin+28000,h.Motion.Schedule.Deadline,"same absolute phase");
        Equal(MotionModes.FiniteCriticalParameters(mode).SupportMs,h.Motion.KernelSupportMs,"same support");
    }
    private static void Serialized(MotionMode mode)
    {
        using var h=new H(mode);h.Send(4,S(4,4));h.Advance(16);h.Now=Origin+23000;h.Motion.ProcessAt(P(TouchEventType.Move,S(8,8)),h.Now,null);
        h.Motion.Tick(Origin+20000,h.Motion.Schedule.Generation);Equal((long?)Origin+24000,h.Motion.Schedule.Deadline,"serialized phase");h.Advance(200);Equal(8,h.X,"no history rewind");
    }
    private static void Up(MotionMode mode)
    {
        using var h=new H(mode);h.Send(4,S(4,500));h.Advance(16);long gen=h.Motion.Schedule.Generation;
        Near(h.Motion.BasePending.X+h.Motion.KernelPending.X,h.Motion.Pending.X);Check(h.Motion.KernelPending.X>400,"large known backlog");
        h.Up(17,600);Equal(600,h.X,"final real coordinate flushed once");int n=h.Moves.Count;h.Motion.Tick(Origin+500000,gen);Equal(n,h.Moves.Count,"fence rejects old generation");
        h.Motion.Process(P(TouchEventType.Down,S(17,600)));h.Send(21,S(21,601));h.Advance(250);h.Up(250,601);Equal(601,h.X,"new contact and completed UP");
    }
    private static void Reset(MotionMode mode)
    {
        using var h=new H(mode);h.Send(4,S(4,100));long old=h.Motion.Schedule.Generation;h.Motion.Reset();h.Motion.Tick(Origin+500000,old);Equal(0,h.X,"reset discards owed history");
        h.Motion.Process(P(TouchEventType.Down,S(4,100)));h.Send(8,S(8,102));h.Advance(200);Equal(2,h.X,"new zero history");
        h.Motion.Dispose();h.Motion.Tick(Origin+500000,old);Equal(2,h.X,"dispose no old output");
    }
    private static void Race(MotionMode mode)
    {
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();long now=Origin;var events=new List<string>();
        using var m=new ResampledMotion((_,_)=>{entered.Set();Check(release.Wait(3000),"release");lock(events)events.Add("move");},monotonicNow:()=>now,clockFrequency:Frequency,finiteCriticalMode:mode);
        m.Process(P(TouchEventType.Down,S(0,0)));m.Process(P(TouchEventType.Move,S(4,1000)));long gen=m.Schedule.Generation;
        var tick=Task.Run(()=>m.Tick(Origin+20000,gen));Check(entered.Wait(3000),"output entered");var up=Task.Run(()=>{m.Process(P(TouchEventType.Up,S(5,1000)));lock(events)events.Add("gesture-up");});
        release.Set();Task.WaitAll(tick,up);m.Tick(Origin+500000,gen);Equal("gesture-up",events[^1],"no output after UP returns");
    }
    private static void Presence(MotionMode mode)
    {
        long now=Stopwatch.Frequency*10;var moves=new List<int>();using var m=new ResampledMotion((x,_)=>moves.Add(x),monotonicNow:()=>now,finiteCriticalMode:mode);
        using var receiver=new UdpReceiver(new(IPAddress.Loopback,0),TextWriter.Null,motion:m,detailedLogging:false);
        void Send(byte[] data)=>receiver.ProcessDatagram(data,new(IPAddress.Loopback,1234),now);
        Send(PresenceTests.Touch(1,TouchEventType.Down,0));Send(PresenceTests.Touch(1,TouchEventType.Move,1,10,4_000_000));Send(PresenceTests.Heartbeat(2));m.Tick(now+Stopwatch.Frequency,m.Schedule.Generation);Equal(0,moves.Count,"run invalidation");
        Send(PresenceTests.Touch(2,TouchEventType.Down,0));Send(PresenceTests.Touch(2,TouchEventType.Move,1,10,4_000_000));now+=2*Stopwatch.Frequency;receiver.CheckTimeouts(now);m.Tick(now,m.Schedule.Generation);Equal(0,moves.Count,"presence expiry");
        Send(PresenceTests.Touch(2,TouchEventType.Down,2));Send(PresenceTests.Touch(2,TouchEventType.Move,3,10,4_000_000));receiver.Dispose();m.Tick(now+Stopwatch.Frequency,m.Schedule.Generation);Equal(0,moves.Count,"receiver dispose");
    }
    private static async Task RuntimeFailure(MotionMode mode)
    {
        var native=new VirtualHidTests.FakeNative{FailMove=true};var runtime=new ReceiverRuntime(new(),TextWriter.Null,MouseBackend.VirtualHid,new(IPAddress.Loopback,0),()=>new LibVirtualHidMouseOutput(native),motionMode:mode);
        await runtime.StartAsync();using var sender=new System.Net.Sockets.UdpClient();await sender.SendAsync(PresenceTests.Touch(1,TouchEventType.Down,0),runtime.LocalEndpoint!);await sender.SendAsync(PresenceTests.Touch(1,TouchEventType.Move,1,100,4_000_000),runtime.LocalEndpoint!);
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));Equal(ReceiverState.Error,runtime.CaptureSnapshot().RuntimeState,"visible error");Check(native.Events.Any(e=>e.Kind=="dispose"),"backend cleanup");await runtime.StopAsync();
    }
    private static void Bound(MotionMode mode)
    {
        var k=new CausalFiniteCritical(mode,Frequency);k.Reset(0);for(int i=0;i<CausalFiniteCritical.Capacity;i++)k.Add(i,i+1,i,0,i+1,0);
        Throws<InvalidOperationException>(()=>k.Add(CausalFiniteCritical.Capacity,CausalFiniteCritical.Capacity+1,0,0,1,0));Equal(CausalFiniteCritical.Capacity,k.SegmentCount,"fixed capacity");
        k.Reset(500000);k.Add(500000,700000,3,0,3,0);Equal(3.0,k.Position(700000,3,0).X,"recovery same kernel");
        Throws<InvalidOperationException>(()=>k.Add(700000,700001,double.NaN,0,0,0));Throws<InvalidOperationException>(()=>k.Add(710000,710001,0,0,1,0));
        long now=Origin;using var motion=new ResampledMotion((_,_)=>{},monotonicNow:()=>now,clockFrequency:Frequency,finiteCriticalMode:mode);
        motion.Process(P(TouchEventType.Down,S(0,0)));
        for(int i=1;i<=CausalFiniteCritical.Capacity;i++){now=Origin+i;motion.Process(P(TouchEventType.Move,new TouchSample((ulong)i*1000,i,0)));}
        now++;Throws<InvalidOperationException>(()=>motion.Process(P(TouchEventType.Move,new TouchSample((ulong)(CausalFiniteCritical.Capacity+1)*1000,5000,0))));
        Check(motion.ActiveSessionId is null && motion.Schedule.Deadline is null,"overflow aborts input rather than adapting support");
        Equal(MotionModes.FiniteCriticalParameters(mode).SupportMs,motion.KernelSupportMs,"fault does not mutate fixed configuration");
    }
    private static void Gain(MotionMode mode)
    {
        using var h=new H(mode,7);h.Advance(4);h.Motion.ProcessAt(P(TouchEventType.Move,S(4,1)),h.Now,new RuntimeSettings(50,50,300,8,25));h.Advance(200);Equal(7,h.X,"gain fixed at startup");
    }
    private static void Trace(MotionMode mode)
    {
        string dir=Path.Combine(Path.GetTempPath(),"rightpad-finite-"+Guid.NewGuid());
        try
        {
            long now=Origin;using(var trace=new MotionTrace(dir,mode))using(var m=new ResampledMotion((_,_)=>{},monotonicNow:()=>now,clockFrequency:Frequency,trace:trace,finiteCriticalMode:mode))
            {m.Process(P(TouchEventType.Down,S(0,0)));m.Process(P(TouchEventType.Move,S(4,500)));now+=16000;m.Tick(now,m.Schedule.Generation);now++;m.Process(P(TouchEventType.Up,S(5,600)));}
            var lines=File.ReadAllLines(Path.Combine(dir,"motion.csv"));foreach(string kind in new[]{"KernelPosition,","KernelIntegration,","KernelUpPending,","Fence,"})Check(lines.Any(x=>x.StartsWith(kind)),kind);
            var up=lines.Single(x=>x.StartsWith("UpFlush,")).Split(',');var debt=lines.Single(x=>x.StartsWith("KernelUpPending,")).Split(',');Near(double.Parse(up[7],System.Globalization.CultureInfo.InvariantCulture),double.Parse(up[9],System.Globalization.CultureInfo.InvariantCulture)+double.Parse(debt[7],System.Globalization.CultureInfo.InvariantCulture));
            using var meta=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,"metadata.json")));Equal(MotionModes.FiniteCriticalParameters(mode).SupportMs,meta.RootElement.GetProperty("SupportMs").GetInt32(),"fixed metadata");
        }
        finally {Directory.Delete(dir,true);}
    }
    private static void Ui(MotionMode mode)
    {
        Equal(mode,Receiver.Program.ParseLaunchArguments(["--dev-motion-mode",mode.ToString()]).Motion,"explicit CLI");var r=new ReceiverRuntime(new(),TextWriter.Null,MouseBackend.VirtualHid,motionMode:mode);var vm=new MainViewModel(r,null!,null!);
        Check(vm.MotionModeName.Contains("FINITE CRITICAL"),"actual readonly mode");Check(vm.MotionExplanation.Contains($"support {MotionModes.FiniteCriticalParameters(mode).SupportMs} ms"),"correct fixed support");
    }
}
