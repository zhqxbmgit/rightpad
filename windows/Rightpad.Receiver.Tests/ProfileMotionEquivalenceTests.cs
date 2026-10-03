using static Rightpad.Receiver.Tests.Program;
namespace Rightpad.Receiver.Tests;

// M gate: a deferred C request must never disturb the production chain.
internal static class ProfileMotionEquivalenceTests
{
    public static IEnumerable<(string Name, Action Run)> Cases =>
    [
        .. new[] { "slow", "medium", "fast", "reversal", "stop", "micro", "missed", "live sensitivity" }
            .Select(label => ($"M deferred C exact position/logical/native/timing/settle {label}", (Action)(() => Equivalence(label)))),
        ("M saved kernels exact deferred-request equivalence at six configurations", Saved),
        ("Profile deferred stationary contact and settlement with joining DOWN", Deferred),
        ("Profile Reset stale tick and fresh run baseline", Reset),
        ("Profile diagnostics actual M/C algorithm and cadence", Diagnostics)
    ];
    private const long Origin=100_000, Frequency=1_000_000;
    private sealed class H : IDisposable
    {
        public long Now=Origin;
        public readonly List<(long At,int X,int Y)> Native=[];
        public readonly ResampledMotion Motion;
        public readonly HitchTraceRecorder Trace;
        public readonly LiveSensitivity Sensitivity=new(2.75,1.125);
        private uint sequence;
        public H(MotionProfile profile,int tau=18,int support=90)
        {
            Trace=new(8192,()=>Now,Frequency);
            Motion=new((x,y)=>Native.Add((Now,x,y)),2.75,1.125,()=>Now,Frequency,
                finiteCriticalMode:MotionModes.ProductionMode,configuration:new(MotionModes.ProductionMode,tau,support),
                liveSensitivity:Sensitivity,hitchTrace:Trace);
            Motion.RequestProfile(profile);
        }
        public void Touch(TouchEventType type,int t,float x,float y=0,uint session=1)
        {
            Now=Origin+t*1000L;
            Motion.Process(new(new(2,type,1,session,sequence++,1),[new((ulong)t*1_000_000,x,y)]));
        }
        public void Tick(int t){Now=Origin+t*1000L;Motion.Tick(Now,Motion.Schedule.Generation);}
        public (long X,long Y) Total=>(Native.Sum(v=>(long)v.X),Native.Sum(v=>(long)v.Y));
        public (int,int)[] Logical()
        {var s=Trace.Snapshot();return s.Records.Take(s.Count).Where(v=>v.Kind==HitchKind.Output).Select(v=>(v.Dx,v.Dy)).ToArray();}
        public void Dispose()=>Motion.Dispose();
    }
    private static void Compare(H m,H c)
    {
        Equal(m.Motion.Position,c.Motion.Position,"binary64 continuous position");
        Equal(m.Motion.Pending,c.Motion.Pending,"target/played pending");
        Equal(m.Motion.BasePending,c.Motion.BasePending,"reconstruction pending");
        Equal(m.Motion.KernelPending,c.Motion.KernelPending,"kernel pending");
        Equal(m.Motion.Schedule,c.Motion.Schedule,"deadline/generation/cadence");
        Equal(m.Motion.ActiveSessionId,c.Motion.ActiveSessionId,"contact/settlement");
        Equal(m.Motion.TickCount,c.Motion.TickCount,"ticks");Equal(m.Motion.MissedTicks,c.Motion.MissedTicks,"missed behavior");
        Equal((m.Motion.TotalDx,m.Motion.TotalDy),(c.Motion.TotalDx,c.Motion.TotalDy),"logical cumulative");
        Check(m.Native.SequenceEqual(c.Native),"every native dx/dy and actual submit time");
    }
    private static void Equivalence(string label,int tau=18,int support=90)
    {
        using var m=new H(MotionProfile.Normal,tau,support);using var c=new H(MotionProfile.Normal,tau,support);
        float scale=label switch{"slow"=>.05f,"medium"=>2,"fast"=>40,"micro"=>.005f,_=>10};
        float X(int t)=>label=="reversal"?(t<=48?t:96-t)*scale:label=="stop"?Math.Min(t,32)*scale:t*scale;
        for(int t=0;t<=650;t++)
        {
            if(t==0){m.Touch(TouchEventType.Down,t,0);c.Touch(TouchEventType.Down,t,0);Compare(m,c);}
            if(t==1)c.Motion.RequestProfile(MotionProfile.Cinematic);
            if(t==102)c.Motion.RequestProfile(MotionProfile.Normal);
            if(label=="live sensitivity" && t==40){m.Sensitivity.Publish(6,3);c.Sensitivity.Publish(6,3);Compare(m,c);}
            if(t>0 && t<=100 && t%4==0)
            {m.Touch(TouchEventType.Move,t,X(t),X(t)*.37f);c.Touch(TouchEventType.Move,t,X(t),X(t)*.37f);Compare(m,c);}
            if(t==101){m.Touch(TouchEventType.Up,t,X(100),X(100)*.37f);c.Touch(TouchEventType.Up,t,X(100),X(100)*.37f);Compare(m,c);}
            if(label=="missed" && (t is >=30 and <62 || t is >=80 and <130 || t is >=140 and <240))continue;
            m.Tick(t);c.Tick(t);Compare(m,c);
        }
        Check(m.Logical().Length>0 && m.Logical().SequenceEqual(c.Logical()),"EVERY Q0-C logical event including zero");
        Equal(m.Total,c.Total,"native cumulative exact");Equal(m.Total,(m.Motion.TotalDx,m.Motion.TotalDy),"logical endpoint paid immediately");
        Check(m.Motion.Schedule.Deadline is null && c.Motion.Schedule.Deadline is null,"same finite settlement and park");
        Equal(0L,m.Motion.UpFlushCount,"Earned-Settle no UP bypass");Equal(0L,c.Motion.UpFlushCount,"C same lifecycle");
        Console.WriteLine($"M deferred-request equivalence {label} Tau/Support={tau}/{support} logical={m.Logical().Length} native={m.Native.Count} final={m.Total} EXACT");
    }
    private static void Saved()
    {foreach(var pair in new[]{(8,40),(13,83),(18,90),(18,100),(24,120),(60,300)})Equivalence("reversal",pair.Item1,pair.Item2);}
    private static void Deferred()
    {
        foreach(var profile in new[]{MotionProfile.Normal})
        {
            using var h=new H(profile);var next=profile==MotionProfile.Normal?MotionProfile.Cinematic:MotionProfile.Normal;
            h.Touch(TouchEventType.Down,0,0);h.Motion.RequestProfile(next);Equal(profile,h.Motion.ActiveMotionProfile,"stationary held defers");
            h.Touch(TouchEventType.Move,4,100);h.Touch(TouchEventType.Up,5,100);h.Tick(20);
            Equal(profile,h.Motion.ActiveMotionProfile,"settlement defers");var position=h.Motion.Position;var phase=h.Motion.Schedule;
            h.Touch(TouchEventType.Down,21,200,session:2);Equal(position,h.Motion.Position,"joined chain history");Equal(phase,h.Motion.Schedule,"joined clock phase");
            h.Motion.RequestProfile(profile);h.Motion.RequestProfile(next);h.Touch(TouchEventType.Up,24,200,session:2);
            for(int t=25;t<200;t++){h.Tick(t);if(h.Motion.Schedule.Deadline is not null)Equal(profile,h.Motion.ActiveMotionProfile,"whole chain retains active label");}
            Equal(next,h.Motion.ActiveMotionProfile,"latest request after full settle");Equal(275L,h.Total.X,"earned endpoint unchanged");
            Equal((18,90),(h.Motion.KernelTauMs,h.Motion.KernelSupportMs),"label switch never rebuilds/rescales kernel");
        }
    }
    private static void Reset()
    {
        using var m=new H(MotionProfile.Normal);using var c=new H(MotionProfile.Normal);
        foreach(var h in new[]{m,c}){h.Touch(TouchEventType.Down,0,0);h.Touch(TouchEventType.Move,4,100);h.Tick(20);}
        var generation=m.Motion.Schedule.Generation;foreach(var h in new[]{m,c})h.Motion.Reset();Compare(m,c);
        foreach(var h in new[]{m,c}){h.Now+=100000;h.Motion.Tick(h.Now,generation);}Compare(m,c);
        foreach(var h in new[]{m,c}){h.Touch(TouchEventType.Down,150,300,session:2);h.Touch(TouchEventType.Move,154,304,session:2);h.Touch(TouchEventType.Up,155,304,session:2);}
        for(int t=156;t<400;t++){m.Tick(t);c.Tick(t);Compare(m,c);}
    }
    private static void Diagnostics()
    {
        foreach(var profile in new[]{MotionProfile.Normal,MotionProfile.Cinematic})
        {
            using var h=new H(profile,13,83);var a=h.Motion.ActiveAlgorithm;var vm=new RuntimeStatsViewModel();
            vm.Refresh(new(1,ReceiverState.Running,MotionProfile:profile,ActiveMotionProfile:a.Profile,
                ActiveMotionTauMs:a.TauMs,ActiveMotionSupportMs:a.SupportMs,ActiveMotionAlgorithm:a.Algorithm,NativeOutputCadence:h.Motion.NativeOutputCadence),0);
            Equal(profile==MotionProfile.Normal?"M-F1 · Finite-Critical · Reconstruction 8 ms · Q0-C · 1 ms / 1000 Hz · Earned-Settle":"C-Z1 · Reconstruction 12 ms · zhq-derived servo · Amax 80000 · Vmax 15000 · Java-compatible rounding · true glide",vm.MotionAlgorithm,"active algorithm");Equal(profile==MotionProfile.Normal?"13 ms":"35 ms",vm.ActiveTau,"active Tau");
            Equal(profile==MotionProfile.Normal?"83 ms":"—",vm.ActiveSupport,"active Support");Equal(profile==MotionProfile.Normal?"1000 Hz":"250 Hz",vm.NativeOutputCadence,"active cadence");
        }
    }
}
