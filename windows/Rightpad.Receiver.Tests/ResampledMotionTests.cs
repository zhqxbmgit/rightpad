using System.Diagnostics;
using System.Net;
using System.Text.Json;
using static Rightpad.Receiver.Tests.Program;
using static Rightpad.Receiver.Tests.RawMotionProcessorTests;

namespace Rightpad.Receiver.Tests;

internal static class ResampledMotionTests
{
    private const long Frequency = 1_000_000;
    private static ulong Ns(double ms) => (ulong)Math.Round(ms * 1_000_000);
    private static TouchSample S(double ms, float x, float y = 0) => new(Ns(ms), x, y);
    private static TouchPacket P(TouchEventType type, params TouchSample[] samples) =>
        new(new(2, type, (ushort)samples.Length, 1, 0, 1), samples);
    private sealed class H : IDisposable
    {
        public long Now = 100_000;
        public readonly List<(long Time, int X, int Y)> Moves = new();
        public readonly ResampledMotion B;
        public H(double gain = 1) { B = new((x, y) => Moves.Add((Now, x, y)), gain, gain, () => Now, Frequency); Down(); }
        public void Down(float x = 0) => B.Process(P(TouchEventType.Down, S(0, x)));
        public void At(double ms) => Now = 100_000 + (long)Math.Round(ms * 1000);
        public void Send(double arrival, params TouchSample[] samples) { At(arrival); B.Process(P(TouchEventType.Move, samples)); }
        public void Tick(double ms) { At(ms); B.Tick(Now, B.Schedule.Generation); }
        public void Up(double ms, float x, float y = 0) { At(ms); B.Process(P(TouchEventType.Up, S(ms, x, y))); }
        public int X => Moves.Sum(m => m.X);
        public int Y => Moves.Sum(m => m.Y);
        public void Dispose() => B.Dispose();
    }
    public static (string Name, Action Run)[] Cases =>
    [
        ("resample single sample", Single), ("resample two sample batch", Two),
        ("resample four sample batch", Four), ("resample linear constant speed", Linear),
        ("resample batch reversal path", BatchReversal), ("resample duplicate time", Duplicate),
        ("resample backward time fallback", Backward), ("resample huge timestamp fallback", HugeTimestamp),
        ("resample late packet", Late), ("resample starvation parks without extrapolation", Starvation),
        ("resample missed deadline skips catchup", Missed), ("resample fixed anchor and delay", Anchor),
        ("resample positive endpoint", () => Endpoint(1)), ("resample negative endpoint", () => Endpoint(-1)),
        ("resample XY endpoint", XY), ("resample fractional residual", Fraction),
        ("resample multiple batches versus RAW", RawEndpoint), ("resample gain snapshots", Gain),
        ("resample slow stationary", () => Stationary(1)), ("resample fast stationary", () => Stationary(10000)),
        ("resample immediate UP flush", Up), ("resample old generation after UP", Stale),
        ("resample new DOWN aborts", NewDown), ("resample mismatched session aborts", Mismatch),
        ("resample dispose cancels", Dispose), ("resample error cancels", Error),
        ("resample positive to negative", () => Reversal(1)), ("resample negative to positive", () => Reversal(-1)),
        ("resample bounded buffer", Bound), ("resample UP and tick race", FenceRace),
        ("resample run/presence/dispose gate", Presence), ("resample gesture fence and rearm", Gesture),
        ("resample mode arguments", Arguments), ("resample trace ring and freeze", Trace),
        ("resample actual timer native failure cleanup", RuntimeFailure)
    ];
    private static void Near(double expected, double actual, double epsilon = 1e-8) => Check(Math.Abs(expected - actual) <= epsilon, $"expected {expected}, actual {actual}");
    private static void Single() { using var h = new H(); h.Send(4, S(4, 8)); Equal(0, h.X, "no immediate output"); h.Tick(14); Equal(4, h.X, "interpolate cumulative target"); h.Tick(16); Equal(8, h.X, "endpoint"); }
    private static void Two() { using var h = new H(); h.Send(8, S(4, 4), S(8, 8)); h.Tick(16); Equal(4, h.X, "first point"); h.Tick(20); Equal(8, h.X, "second point"); }
    private static void Four() { using var h = new H(); h.Send(16, S(4, 4), S(8, 8), S(12, 12), S(16, 16)); foreach (int t in new[] { 20, 24, 28 }) h.Tick(t); Equal(16, h.X, "four endpoints"); Check(h.Moves.Select(m => m.X).SequenceEqual(new[] { 8, 4, 4 }), "no packet burst"); }
    private static void Linear() { using var h = new H(); h.Send(8, S(4, 4), S(8, 8), S(12, 12), S(16, 16)); foreach (int t in new[] { 12, 16, 20, 24, 28 }) h.Tick(t); Check(h.Moves.All(m => m.X == 4), "equal displacement on equal intervals"); }
    private static void BatchReversal() { using var h = new H(); h.Send(8, S(4, 10), S(8, 20), S(12, 8)); foreach (int t in new[] { 16, 20, 24 }) h.Tick(t); Check(h.Moves.Select(m => m.X).SequenceEqual(new[] { 10, 10, -12 }), "all buffered reversal points retained"); }
    private static void Duplicate() { using var h = new H(); h.Send(4, S(4, 10), S(4, 20), S(4, 8)); h.Tick(16); Equal(8, h.X, "last arrival at equal time"); Equal(2L, h.B.DuplicateTimestamps, "count duplicates"); }
    private static void Backward() { using var h = new H(); h.Send(8, S(8, 8), S(4, 4)); h.Tick(20); Equal(4, h.X, "arrival order fallback"); h.Send(24, S(12, 12)); h.Tick(32); Check(h.X <= 12, "no overshoot"); h.Tick(36); Equal(12, h.X, "fallback endpoint"); Equal(1L, h.B.NonMonotonicTimestamps, "backward counter"); }
    private static void HugeTimestamp() { using var h = new H(); h.Send(4, new TouchSample(ulong.MaxValue, 8, 0)); h.Tick(16); Equal(8, h.X, "extreme timestamp does not overflow or stall for centuries"); }
    private static void Late() { using var h = new H(); h.Send(40, S(4, 4), S(8, 8)); h.Tick(44); Equal(8, h.X, "late input caught up once"); Equal(1, h.Moves.Count, "no late replay burst"); Equal(2L, h.B.LateSamples, "late count"); }
    private static void Starvation() { using var h = new H(); h.Send(4, S(4, 4)); h.Tick(20); h.Tick(200); Equal(4, h.X, "no extrapolation"); Check(h.B.Schedule.Deadline is null, "parked"); h.Send(204, S(204, 8)); h.Tick(216); Equal(8, h.X, "resumed known movement"); Check(h.B.StarvationTicks > 0, "starvation duration"); }
    private static void Missed() { using var h = new H(); h.Send(4, S(4, 4), S(8, 8), S(12, 12), S(20, 20)); h.Tick(27); Equal(15, h.X, "evaluate actual time"); Equal(1, h.Moves.Count, "single current output"); Equal(3L, h.B.MissedTicks, "missed 16/20/24 after deadline12"); Equal((long?)128000, h.B.Schedule.Deadline, "next absolute deadline"); }
    private static void Anchor() { using var h = new H(); h.Send(7, S(4, 4)); h.Tick(16); Equal(4, h.X, "12ms from original timestamp not receive"); h.Send(19, S(8, 8)); h.Tick(20); Equal(8, h.X, "anchor not shifted"); }
    private static void Endpoint(int sign) { using var h = new H(6); h.Send(4, S(4, sign * 1.125f)); h.Tick(16); h.Up(20, sign * 1.125f); Check(Math.Abs(sign * 6.75 - h.X) < 1, "endpoint budget"); }
    private static void XY() { using var h = new H(6); h.Send(8, S(4, 2, -3), S(8, -1, 4)); h.Tick(16); h.Tick(20); h.Up(24, -1, 4); Equal(-6, h.X, "x"); Equal(24, h.Y, "y"); }
    private static void Fraction() { using var h = new H(); h.Send(4, S(4, .25f), S(8, .5f), S(12, .75f), S(16, 1)); foreach (int t in new[] {16,20,24,28}) h.Tick(t); Equal(1, h.X, "interpolation residual accumulated"); Equal(1, h.Moves.Count, "no fabricated +/-1"); }
    private static void RawEndpoint()
    {
        var random = new Random(76);
        for (int contact = 0; contact < 50; contact++)
        {
            using var h = new H(6); int ax = 0, ay = 0; var a = new TouchSessionProcessor((x, y) => { ax += x; ay += y; }, 6, 6);
            a.Process(P(TouchEventType.Down, S(0, 0))); float x = 0, y = 0;
            for (int i = 1; i <= 100; i++)
            {
                x += (float)(random.NextDouble() - .5); y += (float)(random.NextDouble() - .5);
                var sample = S(i * 4, x, y); h.Send(i * 4, sample); a.Process(P(TouchEventType.Move, sample)); h.Tick(i * 4);
            }
            h.Up(404, x, y); a.Process(P(TouchEventType.Up, S(404, x, y)));
            Check(Math.Abs(h.X - 6 * x) < 1.000001 && Math.Abs(h.Y - 6 * y) < 1.000001, "B endpoint");
            Check(Math.Abs(h.X - ax) <= 1 && Math.Abs(h.Y - ay) <= 1, "A/B integer endpoint difference <=1");
        }
    }
    private static void Gain() { using var h = new H(); h.B.Process(P(TouchEventType.Move, S(4, 1)), 2, 2); h.B.Process(P(TouchEventType.Move, S(8, 2)), 3, 3); h.Tick(20); Equal(5, h.X, "sum each segment snapshot, no rescale old target"); }
    private static void Stationary(float distance) { using var h = new H(); h.Send(4, S(4, distance)); h.Tick(16); h.Send(20, S(20, distance)); h.Tick(32); int n = h.Moves.Count; h.Tick(1000); Equal(n, h.Moves.Count, "no stationary drift"); Equal((int)distance, h.X, "finite endpoint"); }
    private static void Up() { using var h = new H(); h.Send(4, S(4, 100)); h.Up(5, 110); Equal(110, h.X, "UP actual coordinate and pending net delta"); Equal(1L, h.B.UpFlushCount, "flush"); Check(h.B.ActiveSessionId is null, "ended"); }
    private static void Stale() { using var h = new H(); h.Send(4, S(4, 100)); var generation = h.B.Schedule.Generation; h.Up(5, 110); h.B.Tick(200000, generation); Equal(1, h.Moves.Count, "old tick never writes after fence"); }
    private static void NewDown() { using var h = new H(); h.Send(4, S(4, 10)); var gen = h.B.Schedule.Generation; h.Down(100); h.B.Tick(200000, gen); h.Send(8, S(4, 102)); h.Tick(20); Equal(2, h.X, "new baseline"); Near(10, h.B.LifecycleAbortDiscardedDistance); }
    private static void Mismatch() { using var h = new H(); h.Send(4, S(4, 10)); h.B.Process(new(new(2,TouchEventType.Move,1,2,0,1),[S(8,999)])); h.Tick(30); Equal(0, h.X, "mismatched session cancels pending"); }
    private static void Dispose() { using var h = new H(); h.Send(4, S(4, 10)); h.B.Dispose(); h.Tick(30); Equal(0, h.X, "disposed writer silent"); }
    private static void Error() { long now=100000; using var b=new ResampledMotion((_,_)=>throw new IOException("test"),monotonicNow:()=>now,clockFrequency:Frequency); b.Process(P(TouchEventType.Down,S(0,0))); b.Process(P(TouchEventType.Move,S(4,10))); Throws<IOException>(()=>b.Tick(116000,b.Schedule.Generation)); Check(b.ActiveSessionId is null,"error clears"); }
    private static void Reversal(int sign) { using var h=new H(); h.Send(8,S(4,sign*4),S(8,sign*8),S(12,sign*4),S(16,0)); foreach(int t in new[]{16,20,24,28})h.Tick(t); Check(h.Moves.Select(m=>m.X).SequenceEqual(new[]{sign*4,sign*4,-sign*4,-sign*4}),"no velocity tail"); }
    private static void Bound() { using var h=new H(); for(int i=1;i<5000;i++)h.Send(4,S(i*.01,i)); Check(h.B.BufferOverflows>0,"overload visible"); h.Up(6,5000); Equal(5000,h.X,"overload endpoint retained"); }
    private static void FenceRace()
    {
        using var entered=new ManualResetEventSlim(); using var release=new ManualResetEventSlim();
        var events=new List<string>(); long now=100000;
        using var b=new ResampledMotion((_,_)=>{entered.Set();if(!release.Wait(3000))throw new TimeoutException();lock(events)events.Add("move");},monotonicNow:()=>now,clockFrequency:Frequency);
        b.Process(P(TouchEventType.Down,S(0,0))); b.Process(P(TouchEventType.Move,S(4,4),S(8,8)));
        long gen=b.Schedule.Generation;
        var tick=Task.Run(()=>b.Tick(116000,gen)); Check(entered.Wait(3000),"tick entered native boundary");
        var up=Task.Run(()=>{b.Process(P(TouchEventType.Up,S(8,8)));lock(events)events.Add("gesture-up");});
        release.Set(); Task.WaitAll(tick,up); b.Tick(200000,gen);
        Equal("gesture-up",events[^1],"fence orders in-flight and final motion before gesture");
    }
    private static void Presence()
    {
        long now=Stopwatch.Frequency*10; var moves=new List<int>();
        using var b=new ResampledMotion((x,_)=>moves.Add(x),monotonicNow:()=>now);
        using var r=new UdpReceiver(new(IPAddress.Loopback,0),TextWriter.Null,motion:b,detailedLogging:false);
        void Send(byte[] bytes)=>r.ProcessDatagram(bytes,new(IPAddress.Loopback,1234),now);
        Send(PresenceTests.Touch(1,TouchEventType.Down,0)); Send(PresenceTests.Touch(1,TouchEventType.Move,1,10,4_000_000));
        Send(PresenceTests.Heartbeat(2)); b.Tick(now+Stopwatch.Frequency,b.Schedule.Generation); Equal(0,moves.Count,"run cancels");
        Send(PresenceTests.Touch(2,TouchEventType.Down,0)); Send(PresenceTests.Touch(2,TouchEventType.Move,1,10,4_000_000));
        now+=2*Stopwatch.Frequency; r.CheckTimeouts(now); b.Tick(now,b.Schedule.Generation); Equal(0,moves.Count,"presence cancels");
        Send(PresenceTests.Touch(2,TouchEventType.Down,2)); Send(PresenceTests.Touch(2,TouchEventType.Move,3,10,4_000_000));
        r.Dispose(); b.Tick(now+Stopwatch.Frequency,b.Schedule.Generation); Equal(0,moves.Count,"receiver disposal cancels");
    }
    private static void Gesture()
    {
        var events=new List<string>();long now=Stopwatch.Frequency*10;
        using var b=new ResampledMotion((x,_)=>events.Add("move:"+x),monotonicNow:()=>now);
        var g=new GestureProcessor(()=>events.Add("click"),()=>events.Add("drag-down"),()=>events.Add("drag-up"));
        using var r=new UdpReceiver(new(IPAddress.Loopback,0),TextWriter.Null,motion:b,gesture:g,detailedLogging:false);
        uint seq=0;
        void Send(TouchEventType t,float x,ulong ns,uint session){var bytes=PacketDecoderTests.Encode(t,session,seq++,new TouchSample(ns,x,0));r.ProcessDatagram(bytes,new(IPAddress.Loopback,1234),now);}
        Send(TouchEventType.Down,0,0,1); Send(TouchEventType.Up,0,10_000_000,1);
        Send(TouchEventType.Down,0,20_000_000,2); Send(TouchEventType.Move,10,24_000_000,2); Send(TouchEventType.Up,12,28_000_000,2);
        Send(TouchEventType.Down,0,38_000_000,3); Send(TouchEventType.Up,0,48_000_000,3);
        Check(events.SequenceEqual(new[]{"click","drag-down","move:12","drag-up","drag-down","drag-up"}),"original click/rearm, flush before release");
        b.Tick(now+Stopwatch.Frequency,b.Schedule.Generation); Equal(6,events.Count,"nothing after UP");
    }
    private static void Arguments()
    {
        Equal(MotionMode.RAW,Receiver.Program.ParseLaunchArguments([]).Motion,"RAW default");
        Equal(MotionMode.RESAMPLED_250HZ,Receiver.Program.ParseLaunchArguments(["--dev-motion-mode","RESAMPLED_250HZ"]).Motion,"explicit B");
        foreach(var a in new[]{new[]{"--dev-motion-mode"},new[]{"--dev-motion-mode","500HZ"},new[]{"--diagnostics","--dev-motion-mode","RAW"},new[]{"--dev-motion-mode","RAW","--dev-motion-mode","RAW"}})Throws<ArgumentException>(()=>Receiver.Program.ParseLaunchArguments(a));
    }
    private static void Trace()
    {
        string dir=Path.Combine(Path.GetTempPath(),"rightpad-motion-test-"+Guid.NewGuid());
        try
        {
            using(var trace=new MotionTrace(dir,MotionMode.RAW,4)){for(int i=0;i<6;i++)trace.Write(MotionEventKind.Tick,100+i);trace.Freeze();trace.Write(MotionEventKind.Tick,999);}
            Equal(5,File.ReadAllLines(Path.Combine(dir,"motion.csv")).Length,"bounded four records plus header");
            using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,"metadata.json")));Equal(2L,json.RootElement.GetProperty("Overwritten").GetInt64(),"overwrite visible");
        }
        finally{Directory.Delete(dir,true);}
    }
    private static void RuntimeFailure()
    {
        RuntimeFailureAsync().GetAwaiter().GetResult();
    }
    private static async Task RuntimeFailureAsync()
    {
        var native=new VirtualHidTests.FakeNative{FailMove=true};
        var runtime=new ReceiverRuntime(new(),TextWriter.Null,MouseBackend.VirtualHid,new(IPAddress.Loopback,0),()=>new LibVirtualHidMouseOutput(native),motionMode:MotionMode.RESAMPLED_250HZ);
        await runtime.StartAsync(); using var sender=new System.Net.Sockets.UdpClient();
        await sender.SendAsync(PresenceTests.Touch(1,TouchEventType.Down,0),runtime.LocalEndpoint!);
        await sender.SendAsync(PresenceTests.Touch(1,TouchEventType.Move,1,10,4_000_000),runtime.LocalEndpoint!);
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Equal(ReceiverState.Error,runtime.CaptureSnapshot().RuntimeState,"ticker error visible without another packet");
        Check(native.Events.Any(e=>e.Kind=="dispose"),"native disposed after ticker joined"); await runtime.StopAsync();
    }
}
