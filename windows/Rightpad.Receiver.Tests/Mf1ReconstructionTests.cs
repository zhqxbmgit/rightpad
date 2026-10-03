using System.Text.Json;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class Mf1ReconstructionTests
{
    public static IEnumerable<(string Name, Action Run)> Cases =>
    [
        ("M-F1 exactly 4ms earlier reconstruction and first native effect with equal endpoint", Discrimination),
        .. new[] { "slow", "fast", "reversal", "stop", "micro", "sensitivity", "duplicate", "fallback" }
            .Select(s => ($"M-F1 M12 shifted trajectory/filtered position/Q0-C/endpoint {s}", (Action)(() => Shifted(s)))),
        ("M-F1 late admission remains causal at smaller fixed buffer", Late),
        ("M-F1 smaller offset intentionally moves late boundary without rewriting history", LateBoundary),
        ("M-F1 bounded input/history queues retain final earned target", Overflow),
        ("M-F1 and M12 dense history overflow visibly abort with identical capacity rule", HistoryFault),
        ("M-F1 delay frozen through M joining DOWN then C release/glide interruption and natural switch", Lifecycle),
        ("M-F1 missed wake actual-time evaluation fixed phase and stale reset fence", Missed),
        ("M-F1 source/run invalidation reselects fixed M delay without output", Invalidation),
        ("M-F1 mixed MotionTrace identities and profile-specific delay", Trace)
    ];
    private const long Origin = 100_000, Frequency = 1_000_000;
    private sealed class H : IDisposable
    {
        public long Now = Origin;
        public readonly LiveSensitivity Sensitivity = new(6,6);
        public readonly List<(long At, int X, int Y)> Native = [];
        public readonly ResampledMotion M;
        public H(MotionProfile profile = MotionProfile.Normal)
        {
            M = new((x,y)=>Native.Add((Now,x,y)),6,6,()=>Now,Frequency,
                finiteCriticalMode:MotionModes.ProductionMode, configuration:new(MotionModes.ProductionMode,18,90), liveSensitivity:Sensitivity);
            M.RequestProfile(profile);
        }
        public void Touch(TouchEventType e,int arrival,float x,float y=0,int? timestamp=null,uint session=1,ulong run=1)
        {Now=Origin+arrival*1000L;M.Process(new(new(2,e,1,session,(uint)arrival,run),[new((ulong)(timestamp??arrival)*1_000_000,x,y)]));}
        public void Tick(int t,long? generation=null){Now=Origin+t*1000L;M.Tick(Now,generation??M.Schedule.Generation);}
        public void Finish(int from){for(int t=from;t<3000 && M.Schedule.Deadline is not null;t++)Tick(t);Check(M.Schedule.Deadline is null,"finite completion");}
        public void Dispose()=>M.Dispose();
    }
    private static void Discrimination()
    {
        using var h=new H();long now=Origin;var oldNative=new List<(long At,int X,int Y)>();
        using var old=new M12HistoricalMotion((x,y)=>oldNative.Add((now,x,y)),6,6,()=>now,Frequency,
            finiteCriticalMode:MotionModes.ProductionMode,configuration:new(MotionModes.ProductionMode,18,90));
        void Send(TouchEventType e,int t,float x){h.Touch(e,t,x);now=Origin+t*1000L;old.Process(new(new(2,e,1,1,(uint)t,1),[new((ulong)t*1_000_000,x,0)]));}
        Send(TouchEventType.Down,0,0);Send(TouchEventType.Move,4,1000);
        Equal(Origin+8000,h.M.Schedule.Deadline,"M-F1 deadline discriminates from still-12ms implementation");
        Equal(Origin+12000,old.Schedule.Deadline,"historical deadline");
        int newBase=-1,oldBase=-1;
        for(int t=5;t<=200;t++)
        {
            if(t==5)Send(TouchEventType.Up,t,1000);
            h.Tick(t);now=Origin+t*1000L;old.Tick(now,old.Schedule.Generation);
            if(newBase<0 && h.M.ReconstructedPosition.X>0)newBase=t;
            if(oldBase<0 && old.ReconstructedPosition.X>0)oldBase=t;
        }
        Equal(9,newBase,"first causal reconstructed effect");Equal(4,oldBase-newBase,"reconstruction starts exactly 4ms earlier");
        Equal(4000L,oldNative[0].At-h.Native[0].At,"first native effect exactly 4ms earlier");
        Equal((6000L,0L),(h.M.TotalDx,h.M.TotalDy),"new earned endpoint");
        Equal((old.TotalDx,old.TotalDy),(h.M.TotalDx,h.M.TotalDy),"identical integer endpoint");
        Equal(0L,h.M.UpFlushCount,"no UP bypass");
        Console.WriteLine($"M-F1 discrimination: base={newBase}/{oldBase}ms native={(h.Native[0].At-Origin)/1000}/{(oldNative[0].At-Origin)/1000}ms endpoint={h.M.TotalDx}");
    }
    private static void Shifted(string kind)
    {
        using var h=new H();long now=Origin;var oldNative=new List<(long At,int X,int Y)>();var gain=new LiveSensitivity(6,6);
        using var old=new M12HistoricalMotion((x,y)=>oldNative.Add((now,x,y)),6,6,()=>now,Frequency,
            finiteCriticalMode:MotionModes.ProductionMode,configuration:new(MotionModes.ProductionMode,18,90),liveSensitivity:gain);
        var positions=new Dictionary<int,((double,double) Base,(double,double) Filter)>();
        float scale=kind switch{"slow"=>.02f,"fast"=>100f,"micro"=>.002f,_=>2f};
        float X(int t)=>kind=="reversal"?(t<=48?t:96-t)*scale:kind=="stop"?Math.Min(t,32)*scale:t*scale;
        for(int t=0;t<=250;t++)
        {
            now=Origin+t*1000L;
            if(kind=="sensitivity" && t==40){gain.Publish(3,2);h.Sensitivity.Publish(3,2);}
            if(t==0 || t>0 && t<=100 && t%4==0 || t==101)
            {
                var e=t==0?TouchEventType.Down:t==101?TouchEventType.Up:TouchEventType.Move;
                float x=X(Math.Min(t,100));
                // Fallback input arrives before either mapped point, so translation remains causal.
                int stamp=kind=="duplicate"&&t==44?40:kind=="fallback"&&t>=44?t-12:t;
                var p=new TouchPacket(new(2,e,1,1,(uint)t,1),[new((ulong)stamp*1_000_000,x,-x*.25f)]);
                h.Now=now;h.M.Process(p);old.Process(p);
            }
            h.Tick(t);old.Tick(now,old.Schedule.Generation);
            positions[t]=(h.M.ReconstructedPosition,h.M.Position);
            // Compare the continuous trajectory before either chain clears at completion.
            if(t>=16 && t<=100)
            {
                var earlier=positions[t-4];
                Equal(earlier.Base,old.ReconstructedPosition,"ordered cumulative reconstruction shifted exactly 4ms");
                Check(Math.Abs(earlier.Filter.Item1-old.Position.X)<1e-9 && Math.Abs(earlier.Filter.Item2-old.Position.Y)<1e-9,"same normalized kernel position on translated timeline");
            }
        }
        Check(h.Native.Select(v=>(v.At+4000,v.X,v.Y)).SequenceEqual(oldNative),"EVERY Q0-C native delta and time is a 4ms translation");
        Equal((old.TotalDx,old.TotalDy),(h.M.TotalDx,h.M.TotalDy),"earned integer endpoint unchanged");
        Equal(old.DuplicateTimestamps,h.M.DuplicateTimestamps,"duplicate classification unchanged");
        Equal(old.NonMonotonicTimestamps,h.M.NonMonotonicTimestamps,"fallback unchanged");
        Equal(0L,h.M.UpFlushCount,"settles earned distance only");
        Check(h.M.Schedule.Deadline is null && old.Schedule.Deadline is null,"parked after finite support");
        if(kind=="micro")Check(h.Native.Count>0,"fractional history accumulates to nonzero native output");
        Console.WriteLine($"M-F1 translated {kind}: native={h.Native.Count} endpoint={h.M.TotalDx}/{h.M.TotalDy}");
    }
    private static void Late()
    {
        using var h=new H();h.Touch(TouchEventType.Down,0,0);h.Touch(TouchEventType.Move,4,20);
        for(int t=8;t<=40;t++)h.Tick(t);
        var before=h.M.Position;h.Touch(TouchEventType.Move,41,30,timestamp:8);
        Equal(before,h.M.Position,"late arrival cannot rewrite emitted position");Equal(1L,h.M.LateSamples,"8+8 < arrival is classified late");
        h.Touch(TouchEventType.Move,42,35,timestamp:8);Equal(1L,h.M.DuplicateTimestamps,"duplicate preserves boundary");
        h.Touch(TouchEventType.Up,43,35,timestamp:4);Equal(1L,h.M.NonMonotonicTimestamps,"fixed fallback activated");
        h.Finish(44);Equal(210L,h.M.TotalDx,"late/fallback target preserved");Equal(0L,h.M.UpFlushCount,"no artificial distance");
    }
    private static void Overflow()
    {
        using var h=new H();h.Touch(TouchEventType.Down,0,0);
        // Overflow the input queue over 500ms while remaining below 4096 history
        // segments per 90ms support. Dense history exhaustion has a separate gate.
        var samples=Enumerable.Range(1,5000).Select(i=>new TouchSample((ulong)i*100_000,i*.001f,0)).ToArray();
        h.Now=Origin+1000;h.M.Process(new(new(2,TouchEventType.Move,1,1,1,1),samples));
        Check(h.M.BufferOverflows>0,"bounded queue overflow exercised");h.Touch(TouchEventType.Up,6,5);h.Finish(7);
        Equal(30L,h.M.TotalDx,"overflow retains final target");Check(double.IsFinite(h.M.Position.X),"finite bounded history");
    }
    private static void LateBoundary()
    {
        using var h=new H();long now=Origin;
        using var old=new M12HistoricalMotion((_,_)=>{},6,6,()=>now,Frequency,
            finiteCriticalMode:MotionModes.ProductionMode,configuration:new(MotionModes.ProductionMode,18,90));
        var down=new TouchPacket(new(2,TouchEventType.Down,1,1,0,1),[new(0,0,0)]);
        h.M.Process(down);old.Process(down);
        var first=new TouchPacket(new(2,TouchEventType.Move,1,1,1,1),[new(4_000_000,10,0)]);
        now=h.Now=Origin+4000;h.M.Process(first);old.Process(first);
        for(int t=8;t<=20;t++){h.Tick(t);now=Origin+t*1000L;old.Tick(now,old.Schedule.Generation);}
        var a=h.M.Position;var b=old.Position;
        var late=new TouchPacket(new(2,TouchEventType.Move,1,1,2,1),[new(11_000_000,20,0)]);
        now=h.Now=Origin+21000;h.M.Process(late);old.Process(late);
        Equal(1L,h.M.LateSamples,"M8 mapped 19ms < arrival21ms");Equal(0L,old.LateSamples,"M12 mapped23ms > arrival21ms");
        Equal(a,h.M.Position,"M8 never retroactively changes realized output");Equal(b,old.Position,"same historical causal policy");
        var up=new TouchPacket(new(2,TouchEventType.Up,1,1,3,1),[new(22_000_000,20,0)]);
        now=h.Now=Origin+22000;h.M.Process(up);old.Process(up);
        for(int t=23;t<=180;t++){h.Tick(t);now=Origin+t*1000L;old.Tick(now,old.Schedule.Generation);}
        Equal(120L,h.M.TotalDx,"M8 finishes existing earned target");Equal(old.TotalDx,h.M.TotalDx,"same endpoint under different lateness classification");
    }
    private static void HistoryFault()
    {
        using var h=new H();long now=Origin;
        using var old=new M12HistoricalMotion((_,_)=>{},6,6,()=>now,Frequency,
            finiteCriticalMode:MotionModes.ProductionMode,configuration:new(MotionModes.ProductionMode,18,90));
        var down=new TouchPacket(new(2,TouchEventType.Down,1,1,0,1),[new(0,0,0)]);
        h.M.Process(down);old.Process(down);now=h.Now=Origin+1000;
        var move=new TouchPacket(new(2,TouchEventType.Move,1,1,1,1),Enumerable.Range(1,5000).Select(i=>new TouchSample((ulong)i*1000,i*.001f,0)).ToArray());
        h.M.Process(move);old.Process(move);
        string? current=null,historical=null;
        try{for(int t=2;t<50;t++)h.Tick(t);}catch(InvalidOperationException e){current=e.Message;}
        try{for(int t=2;t<50;t++){now=Origin+t*1000L;old.Tick(now,old.Schedule.Generation);}}catch(InvalidOperationException e){historical=e.Message;}
        Equal("Fixed finite critical history capacity exceeded.",current,"visible deterministic history error");Equal(historical,current,"same M12 bounded history rule");
        Check(h.M.Schedule.Deadline is null && old.Schedule.Deadline is null,"both abort pending work");
    }
    private static void Lifecycle()
    {
        using var h=new H();h.Touch(TouchEventType.Down,0,0);h.M.RequestProfile(MotionProfile.Cinematic);
        h.Touch(TouchEventType.Move,4,100);h.Touch(TouchEventType.Up,5,100);h.Tick(20);
        Equal(8,h.M.ReconstructionDelayMs,"deferred C leaves M8");var phase=h.M.Schedule;
        h.Touch(TouchEventType.Down,21,500,session:2);Equal(phase,h.M.Schedule,"joining M DOWN retains phase/generation");Equal(8,h.M.ReconstructionDelayMs,"joining M retains 8ms");
        h.M.RequestProfile(MotionProfile.Normal);h.M.RequestProfile(MotionProfile.Cinematic);
        h.Touch(TouchEventType.Up,25,510,session:2);h.Finish(26);
        Equal(MotionProfile.Cinematic,h.M.ActiveMotionProfile,"latest C wins after earned boundary");
        h.Touch(TouchEventType.Down,200,0,session:3);Equal(12,h.M.ReconstructionDelayMs,"new C12");Equal(Origin+212000,h.M.Schedule.Deadline,"C phase uses 12ms");
        h.Touch(TouchEventType.Move,204,10000,session:3);for(int t=212;t<=260;t+=4)h.Tick(t);
        h.M.RequestProfile(MotionProfile.Normal);h.Touch(TouchEventType.Up,261,10000,session:3);
        Equal(12,h.M.ReconstructionDelayMs,"C release pending retains 12ms");for(int t=264;t<=276;t+=4)h.Tick(t);
        Check(h.M.CinematicDynamics.Gliding,"true glide exercised");Equal(12,h.M.ReconstructionDelayMs,"glide retains 12ms");
        var old=h.M.Schedule;int n=h.Native.Count;
        h.Touch(TouchEventType.Down,277,500,session:4);Equal(Origin+289000,h.M.Schedule.Deadline,"interrupted C phase keeps 12ms");h.Tick(300,old.Generation);Equal(n,h.Native.Count,"stale C generation fenced");
        h.Touch(TouchEventType.Up,301,500,session:4);h.Finish(302);
        Equal(MotionProfile.Normal,h.M.ActiveMotionProfile,"natural stop applies M");h.Touch(TouchEventType.Down,500,0,session:5);h.Touch(TouchEventType.Move,504,1,session:5);
        Equal(8,h.M.ReconstructionDelayMs,"new M8");Equal(Origin+508000,h.M.Schedule.Deadline,"fresh M phase 8ms");
    }
    private static void Missed()
    {
        using var h=new H();h.Touch(TouchEventType.Down,0,0);h.Touch(TouchEventType.Move,4,1000);var old=h.M.Schedule;
        h.Now=Origin+78500;h.M.Tick(h.Now,old.Generation);Equal(1L,h.M.TickCount,"one actual-time evaluation");Equal(70L,h.M.MissedTicks,"fixed 1ms skipped count");Equal(Origin+79000,h.M.Schedule.Deadline,"same absolute phase");
        int n=h.Native.Count;h.M.Tick(h.Now,old.Generation);Equal(n,h.Native.Count,"no catchup");h.M.Reset();h.Tick(500,old.Generation);Equal(n,h.Native.Count,"reset fences old wake");
        Equal(8,h.M.ReconstructionDelayMs,"reset keeps fixed selected delay");
    }
    private static void Invalidation()
    {
        using var h=new H(MotionProfile.Cinematic);using var receiver=new UdpReceiver(new(System.Net.IPAddress.Loopback,0),TextWriter.Null,motion:h.M,detailedLogging:false);
        receiver.ProcessDatagram(PresenceTests.Heartbeat(1),new(System.Net.IPAddress.Loopback,1234),h.Now);
        h.M.RequestProfile(MotionProfile.Cinematic);h.Touch(TouchEventType.Down,0,0);h.Touch(TouchEventType.Move,4,10000);h.Tick(16);
        var old=h.M.Schedule;int n=h.Native.Count;
        receiver.ProcessDatagram(PresenceTests.Heartbeat(2),new(System.Net.IPAddress.Parse("127.0.0.2"),1234),h.Now);
        h.Tick(200,old.Generation);Equal(n,h.Native.Count,"new source/run abort emits no tail");Equal(MotionProfile.Normal,h.M.ActiveMotionProfile,"new run defaults M");Equal(8,h.M.ReconstructionDelayMs,"new run M8 delay");
    }
    private static void Trace()
    {
        string dir=Path.Combine(Path.GetTempPath(),"rightpad-mf1-"+Guid.NewGuid());
        using(var trace=new MotionTrace(dir,MotionModes.ProductionMode,64)){trace.BeginRuntimeRun(1,MotionModes.ProductionMode);trace.Profile(MotionProfile.Cinematic);trace.Profile(MotionProfile.Normal);}
        using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,"metadata.json")));var root=json.RootElement;
        Check(root.GetProperty("Mode").GetString()!.StartsWith("M-F1"),"M trace experiment identity");Equal(8,root.GetProperty("PlayoutDelayMs").GetInt32(),"M trace delay");
        Equal(new[]{8,12,8}.Length,root.GetProperty("ProfileSegments").GetArrayLength(),"all mixed transitions");
        Check(root.GetProperty("ProfileSegments").EnumerateArray().Select(v=>v.GetProperty("PlayoutDelayMs").GetInt32()).SequenceEqual(new[]{8,12,8}),"mixed export never assigns M8 to C12");
    }
}
