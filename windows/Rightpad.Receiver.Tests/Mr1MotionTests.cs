using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using static Rightpad.Receiver.Tests.Program;
using static Rightpad.Receiver.Tests.RawMotionProcessorTests;

namespace Rightpad.Receiver.Tests;

internal static class Mr1MotionTests
{
    private const long Origin = 100_000, Frequency = 1_000_000;
    // Pinned only after the independent reconstruction/Q oracle and mechanism review.
    private const string OrdinaryFingerprint = "9280E2AFAF78A13D52A24708697330C08057BA9EE00A5C6F3AFD01D146B3CC68";
    private const string MissedFingerprint = "F4964FB39C0402429904D8EBEA101C9B0AAB7699B3DE448CC26C3631F72F67FF";
    public static IEnumerable<(string Name, Action Run)> Cases =>
    [
        ("M-R1 independent direct interpolation nonzero interior and endpoint", Direct),
        .. new[] { "slow", "fast", "micro", "reversal", "stop", "fractional" }
            .Select(s => ($"M-R1 independent R(t)/exact Q0-C endpoint {s}", (Action)(() => Replay(s, false)))),
        ("M-R1 ordinary independent golden", () => Replay("reversal", false, golden: true)),
        ("M-R1 missed-tick independent golden", () => Replay("reversal", true, golden: true)),
        ("M-R1 no Tau/Support shaping or filter settlement", NoFilter),
        ("M-R1 UP final delta once no flush reconstruction completion", Release),
        ("M-R1 Q0-C signed fraction cross-zero history and missed reversal", Fractions),
        ("M-R1 roughness timely low rate and irregular ramp evidence", TimelyRoughness),
        ("M-R1 roughness lookahead hold late jump immutable observed prefix", LateRoughness),
        ("M-R1 duplicate backward abnormal timestamp and fresh DOWN fallback", Fallback),
        ("M-R1 causal stale wake watermark and same-time ordering", Watermark),
        ("M-R1 bounded 4096 input queue final target retention", QueueBound),
        ("M-R1 bounded history same capacity fault as M-F1", HistoryBound),
        .. new[] { "before", "same-before", "same-after", "after" }
            .Select(s => ($"M-R1 joining/fresh DOWN {s}", (Action)(() => Join(s)))),
        ("M-R1 different run Reset Dispose stale generations", Abort),
        ("M-R1 stationary/release pending latest Requested and C glide boundaries", Profiles),
        .. new[] { "ordinary", "missed", "late", "duplicate", "fallback", "interrupt", "interrupt-glide", "sensitivity" }
            .Select(s => ($"M-R1 vs M-F1 Active C-Z1 EXACT {s}", (Action)(() => CExact(s)))),
        ("M-R1 current-run/source profile authority and presence expiry", Admission),
        ("M-R1 native failure abort no retry and uncommitted ledger", Failure),
        ("M-R1 disk Save/draft/failure live gain only new real delta", () => Save().GetAwaiter().GetResult()),
        ("M-R1 Diagnostics trace no convolution H1 zero events", Identity),
        ("M-R1 isolated fake Runtime startup metadata and failure cleanup", () => Runtime().GetAwaiter().GetResult())
    ];

    private sealed class H : IDisposable
    {
        public long Now = Origin;
        public readonly List<(long At, int X, int Y)> Native = [];
        public readonly HitchTraceRecorder Hitch = new(16384);
        public readonly LiveSensitivity Gain;
        public readonly ResampledMotion M;
        private uint sequence;
        public H(MotionMode mode = MotionMode.M_R1, double sx = 1, double sy = 1,
            int tau = 18, int support = 90, LiveSensitivity? gain = null, MotionTrace? trace = null)
        {
            Gain = gain ?? new(sx, sy);
            M = new((x,y) => Native.Add((Now,x,y)), sx, sy, () => Now, Frequency,
                finiteCriticalMode: mode, configuration: new(mode,tau,support), liveSensitivity: Gain,
                hitchTrace: Hitch, trace: trace);
        }
        public void Touch(TouchEventType e, double arrival, float x, float y = 0, double? stamp = null, uint session = 1, ulong run = 1)
        {
            Now = Time(arrival);
            M.Process(new(new(2,e,1,session,sequence++,run),[new((ulong)((stamp ?? arrival)*1_000_000),x,y)]));
        }
        public void Batch(double arrival, params TouchSample[] samples)
        { Now = Time(arrival); M.Process(new(new(2,TouchEventType.Move,(ushort)samples.Length,1,sequence++,1),samples)); }
        public void Tick(double t, long? generation = null) { Now = Time(t); M.Tick(Now,generation ?? M.Schedule.Generation); }
        public (long X,long Y) Total => (M.TotalDx,M.TotalDy);
        public (int X,int Y)[] Logical()
        { var s=Hitch.Snapshot(); return s.Records.Take(s.Count).Where(r=>r.Kind==HitchKind.Output).Select(r=>(r.Dx,r.Dy)).ToArray(); }
        public void Finish(int from)
        { for(int t=from;t<2000 && M.Schedule.Deadline is not null;t++)Tick(t); Check(M.Schedule.Deadline is null,"finite completion"); }
        public void Dispose() => M.Dispose();
    }
    private static long Time(double t) => Origin + (long)Math.Round(t*1000);
    private static TouchSample S(double t,float x,float y=0) => new((ulong)(t*1_000_000),x,y);
    private static string ArtifactDirectory(string kind) => Path.Combine(
        Environment.GetEnvironmentVariable("RIGHTPAD_MR1_EVIDENCE") ?? Path.GetTempPath(),
        "rightpad-mr1-"+kind+"-"+Guid.NewGuid());

    // Independent dyadic rational classification: no production quantizer or floating P-I.
    private static int Q(double p, long i)
    {
        ulong bits=BitConverter.DoubleToUInt64Bits(p); int exponent=(int)((bits>>52)&2047);
        BigInteger n=bits&0xfffffffffffffUL; if(exponent!=0)n+=BigInteger.One<<52;
        if(bits>>63!=0)n=-n; int power=exponent==0?-1074:exponent-1075;
        BigInteger result=power>=0?(n<<power)-i:(n-(new BigInteger(i)<<-power))/(BigInteger.One<<-power);
        return checked((int)result);
    }
    // Ordered, timely trajectory oracle. Sample accumulation and linear reconstruction are
    // independent of ResampledMotion; late/fallback boundaries have separate explicit fixtures.
    private sealed class R
    {
        private readonly List<(double T,double X,double Y)> knots=[(8,0,0)];
        private double rawX,rawY,targetX,targetY;
        public void Add(double stamp,float x,float y,double sx,double sy)
        { targetX+=((double)x-rawX)*sx;targetY+=((double)y-rawY)*sy;rawX=x;rawY=y;knots.Add((stamp+8,targetX,targetY)); }
        public (double X,double Y) At(double t)
        {
            var left=knots[0]; if(t<left.T)return (0,0);
            foreach(var right in knots.Skip(1))
            {
                if(t<right.T){double f=(t-left.T)/(right.T-left.T);return(left.X+(right.X-left.X)*f,left.Y+(right.Y-left.Y)*f);}
                left=right;
            }
            return(left.X,left.Y);
        }
    }
    private static void Direct()
    {
        using var h=new H();h.Touch(TouchEventType.Down,0,100,50);h.Touch(TouchEventType.Move,4,140,30);
        foreach(int t in Enumerable.Range(8,5))
        { h.Tick(t); Equal(((t-8)*10d,-(t-8)*5d),h.M.Position,"P equals independent linear R, including nonzero interiors");Equal(h.M.ReconstructedPosition,h.M.Position,"base authority"); }
        Equal((40L,-20L),h.Total,"integer endpoint");Equal(0,h.M.KernelSegmentsIntegrated,"never evaluate kernel");
        int n=h.Native.Count;h.Tick(100);Equal(n,h.Native.Count,"held endpoint parks");
    }
    private static void Replay(string kind,bool missed,bool golden=false)
    {
        using var h=new H(sx:2.75,sy:1.125);var oracle=new R();long ix=0,iy=0;
        var expectedNative=new List<(long,int,int)>();var logical=new List<(int,int)>();var states=new StringBuilder();
        float scale=kind switch{"slow"=>.125f,"fast"=>100f,"micro"=>.015625f,"fractional"=>.09375f,_=>2f};
        float X(int t)=>scale*(kind=="reversal"?(t<=40?t:80-t):kind=="stop"?Math.Min(t,32):t);
        for(int t=0;t<=110;t++)
        {
            if(t==0)h.Touch(TouchEventType.Down,0,0);
            if(t>0 && t<=80 && t%4==0 || t==81)
            {
                float x=X(Math.Min(t,80)),y=-x*.375f;
                h.Touch(t==81?TouchEventType.Up:TouchEventType.Move,t,x,y);oracle.Add(t,x,y,2.75,1.125);
            }
            if(missed && (t is >=25 and <43 || t is >=85 and <97))continue;
            bool due=h.M.Schedule.Deadline is long d && d<=Time(t);
            var p=oracle.At(t);h.Tick(t);
            if(due)
            {
                int dx=Q(p.X,ix),dy=Q(p.Y,iy);ix+=dx;iy+=dy;logical.Add((dx,dy));
                if(dx!=0||dy!=0)expectedNative.Add((Time(t),dx,dy));
                if(h.M.ActiveSessionId is not null || h.M.Schedule.Deadline is not null)
                {Equal(p,h.M.Position,"non-cleared P(t)=independent R(t)");Equal(p,h.M.ReconstructedPosition,"reconstruction authority");}
                Equal(0,h.M.KernelSegmentsIntegrated,"no convolution evaluation");
            }
            states.Append(CultureInfo.InvariantCulture,$"{t}:{BitConverter.DoubleToInt64Bits(h.M.Position.X)},{BitConverter.DoubleToInt64Bits(h.M.Position.Y)}:{h.M.Schedule}:{h.Total}:{h.M.MissedTicks};");
        }
        Check(expectedNative.SequenceEqual(h.Native),"every independent native delta/time");Check(logical.SequenceEqual(h.Logical()),"every logical opportunity including zero");
        Equal((ix,iy),h.Total,"exact Q ledger at endpoint");var end=oracle.At(110);
        Equal(0,Q(end.X,ix),"fractional endpoint is complete X");Equal(0,Q(end.Y,iy),"fractional endpoint is complete Y");
        Check(h.M.Schedule.Deadline is null,"UP reconstruction completed");Equal(0L,h.M.UpFlushCount,"no UP flush");
        int count=h.Native.Count;h.Tick(1000);Equal(count,h.Native.Count,"no glide/correction after completion");
        if(golden)
        {
            string payload=states+"|"+string.Join(";",h.Native)+"|"+string.Join(";",h.Logical());
            string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
            string expected=missed?MissedFingerprint:OrdinaryFingerprint;
            if(expected.Length!=0)Equal(expected,hash,"M-R1 independently reviewed fingerprint");
            Console.WriteLine($"M-R1 fingerprint missed={missed} {hash} oracle=PASS pinned={expected.Length!=0}");
        }
    }
    private static void NoFilter()
    {
        using var a=new H(tau:8,support:40);using var b=new H(tau:60,support:300);
        foreach(var h in new[]{a,b}){h.Touch(TouchEventType.Down,0,0);h.Touch(TouchEventType.Move,4,40);h.Touch(TouchEventType.Up,5,44);}
        for(int t=6;t<=14;t++){a.Tick(t);b.Tick(t);Equal(a.M.Position,b.M.Position,"Tau/Support cannot shape direct position");Equal(a.M.Schedule,b.M.Schedule,"cannot delay completion");}
        Check(a.Native.SequenceEqual(b.Native),"all native delta/time independent of kernel settings");Equal((44L,0L),a.Total,"final real target");
        Equal(0,a.M.KernelTauMs,"no active Tau");Equal(0,a.M.KernelSupportMs,"no active Support");
        Check(a.Native[^1].At==Time(13),"completion at final reconstruction boundary, not support");
    }
    private static void Release()
    {
        using var h=new H();h.Touch(TouchEventType.Down,0,0);h.Touch(TouchEventType.Move,4,40);h.Touch(TouchEventType.Up,5,45);
        Equal(0,h.Native.Count,"UP does not flush");Check(h.M.ActiveSessionId is null,"physical contact released");
        h.Touch(TouchEventType.Move,6,999);h.Touch(TouchEventType.Up,7,999);Equal(2L,h.M.IgnoredSessionPackets,"old contact sealed");
        h.Tick(8);Equal((0L,0L),h.Total,"zero event does not complete");Check(h.M.Schedule.Deadline is not null,"future points retained");
        h.Tick(12);Equal((40L,0L),h.Total,"UP endpoint not mature");h.Tick(13);Equal((45L,0L),h.Total,"UP final delta once");
        Check(h.M.Schedule.Deadline is null,"immediate park");int n=h.Native.Count;h.Tick(14);h.Tick(999);Equal(n,h.Native.Count,"no tail");
    }
    private static void Fractions()
    {
        foreach(int sign in new[]{1,-1})
        {
            using var h=new H();h.Touch(TouchEventType.Down,0,0);h.Touch(TouchEventType.Move,4,sign*2);h.Tick(12);
            h.Touch(TouchEventType.Move,16,sign*.75f);h.Tick(24);Equal((long)sign,h.Total.X,"fraction approached from above magnitude");
            // Use an exactly representable cumulative endpoint. At 2^-54, the raw
            // subtraction from .75 rounds to -.75 before Q even receives a position.
            h.Touch(TouchEventType.Move,28,sign*MathF.Pow(2,-24));h.Tick(36);Equal((long)sign,h.Total.X,"exact tiny side of zero does not emit early");
            h.Touch(TouchEventType.Move,40,0);h.Tick(48);Equal(0L,h.Total.X,"exact cross zero returns ledger");
            h.Touch(TouchEventType.Up,49,sign*.75f);h.Tick(57);Equal(0L,h.Total.X,"subcount endpoint, no correction");
        }
        using var nominal=new H();using var skip=new H();
        foreach(var h in new[]{nominal,skip}){h.Touch(TouchEventType.Down,0,0);h.Batch(1,S(4,2),S(8,.75f));}
        nominal.Tick(12);nominal.Tick(16);skip.Tick(16);
        Equal(1L,nominal.Total.X,"reversal history");Equal(0L,skip.Total.X,"missed reversal changes lawful fractional ledger");
        Equal(0,Q(.75,nominal.Total.X),"nominal oracle complete");Equal(0,Q(.75,skip.Total.X),"missed oracle complete");
    }
    private static void TimelyRoughness()
    {
        using var h=new H();h.Touch(TouchEventType.Down,0,0);h.Batch(1,S(20,40),S(27,19),S(60,52));
        for(int t=8;t<=68;t++)
        {
            h.Tick(t);double expected=t<=28?(t-8)*2d:t<=35?40-(t-28)*3d:19+(t-35);
            Equal(expected,h.M.Position.X,"timely low-rate/irregular piecewise linear reconstruction");
            Console.WriteLine(FormattableString.Invariant($"M-R1 roughness timely t={t} R={h.M.Position.X:R} I={h.Total.X} native={h.Native.LastOrDefault()}"));
        }
        Check(h.Native.Any(v=>v.X==2)&&h.Native.Any(v=>v.X==-3)&&h.Native.Any(v=>v.X==1),"sample spacing slope imprint exposed");
    }
    private static void LateRoughness()
    {
        using var h=new H();h.Touch(TouchEventType.Down,0,0);h.Touch(TouchEventType.Move,4,4);
        for(int t=8;t<=12;t++)h.Tick(t);h.Tick(20);Equal(4d,h.M.Position.X,"no future endpoint holds, no extrapolation");
        var prefix=h.Native.ToArray();h.Touch(TouchEventType.Move,30,12,stamp:8);Equal(4d,h.M.Position.X,"arrival changes no emitted position");
        h.Tick(31);Equal(12d,h.M.Position.X,"late real endpoint appears only on next valid opportunity");
        Check(prefix.SequenceEqual(h.Native.Take(prefix.Length)),"observed prefix immutable");Equal(1L,h.M.LateSamples,"late classification");
        Console.WriteLine("M-R1 roughness gap: R(12)=4; R(20)=4 HOLD; arrival(30,stamp8) emits nothing; R(31)=12 JUMP; no prediction");
        h.Touch(TouchEventType.Move,40,20,stamp:40);h.Tick(44);Check(h.M.Position.X>12 && h.M.Position.X<20,"known future endpoint resumes interpolation");h.Tick(48);Equal(20d,h.M.Position.X,"resumed endpoint");
    }
    private static void Fallback()
    {
        using var h=new H();h.Touch(TouchEventType.Down,0,0);h.Batch(4,S(4,10),S(4,20),S(4,8));h.Tick(12);
        Equal((8L,0L),h.Total,"last duplicate boundary wins");Equal(2L,h.M.DuplicateTimestamps,"duplicate count");
        h.Touch(TouchEventType.Move,16,12,stamp:2);h.Tick(24);Equal(12L,h.Total.X,"backward uses arrival+8");
        h.Touch(TouchEventType.Move,28,16,stamp:3);h.Tick(35);Check(h.Total.X<16,"fallback remains until DOWN");h.Tick(36);Equal(16L,h.Total.X,"fallback boundary");
        h.Touch(TouchEventType.Up,37,16,stamp:4);h.Tick(45);
        h.Touch(TouchEventType.Down,50,100,stamp:0,session:2);h.Touch(TouchEventType.Move,54,104,stamp:4,session:2);h.Tick(62);Equal(20L,h.Total.X,"fresh DOWN timestamp anchor");
        h.M.Reset();h.Touch(TouchEventType.Down,100,0);h.Batch(104,new TouchSample(ulong.MaxValue,7,0));h.Tick(112);Equal(27L,h.Total.X,"abnormal future falls back safely");
    }
    private static void Watermark()
    {
        using var h=new H();h.Touch(TouchEventType.Down,0,0);h.Touch(TouchEventType.Move,4,40);h.Tick(8);
        h.Touch(TouchEventType.Move,10,50,stamp:6);h.Tick(9);
        Equal(20d,h.M.Position.X,"wake sampled at9 cannot rewind admitted causal watermark10");Equal(Time(11),h.M.Schedule.Deadline,"phase after clamped wake");
        using var before=new H();using var after=new H();
        foreach(var x in new[]{before,after}){x.Touch(TouchEventType.Down,0,0);x.Touch(TouchEventType.Move,4,40);x.Tick(8);}
        before.Touch(TouchEventType.Move,12,80,stamp:4);before.Tick(12);
        after.Tick(12);after.Touch(TouchEventType.Move,12,80,stamp:4);after.Tick(13);
        Equal(80L,before.Total.X,"input-first duplicate seen by boundary tick");Equal(80L,after.Total.X,"tick-first replacement only next tick");
        Check(after.Native[0].X==40,"past boundary output immutable");
    }
    private static void QueueBound()
    {
        using var h=new H();h.Touch(TouchEventType.Down,0,0);
        h.Batch(1,Enumerable.Range(1,5000).Select(i=>S(i*.1,i*.001f)).ToArray());
        Check(h.M.BufferOverflows>0,"4096 bound reports lost intermediate path");h.Touch(TouchEventType.Up,6,5);h.Finish(7);
        Equal((5L,0L),h.Total,"last true target retained");
    }
    private static void HistoryBound()
    {
        var errors=new List<string>();
        foreach(var mode in new[]{MotionMode.M_R1,MotionModes.ProductionMode})
        {
            using var h=new H(mode);h.Touch(TouchEventType.Down,0,0);h.Batch(1,Enumerable.Range(1,5000).Select(i=>S(i*.001,i*.001f)).ToArray());
            try{for(int t=2;t<50;t++)h.Tick(t);}catch(InvalidOperationException e){errors.Add(e.Message);}
            Check(h.M.Schedule.Deadline is null,"history capacity abort fences pending output");
        }
        Check(errors.SequenceEqual(new[]{"Fixed finite critical history capacity exceeded.","Fixed finite critical history capacity exceeded."}),"unchanged history safety, not shortened support");
    }
    private static void Join(string order)
    {
        using var h=new H();h.Touch(TouchEventType.Down,0,0);h.Touch(TouchEventType.Move,4,2);h.Touch(TouchEventType.Up,5,2.75f);h.Tick(12);
        var phase=h.M.Schedule;int n=h.Native.Count;h.M.RequestProfile(MotionProfile.Cinematic);
        bool fresh=order is "same-after" or "after";double down=order=="before"?12.5:order=="after"?14:13;
        if(fresh)h.Tick(13);
        if(fresh)h.M.RequestProfile(MotionProfile.Normal);
        h.Touch(TouchEventType.Down,down,100,session:2);
        Equal(n,h.Native.Count,"DOWN creates no displacement");
        if(!fresh){Equal(phase,h.M.Schedule,"joining keeps generation/deadline");Equal(MotionProfile.Normal,h.M.ActiveMotionProfile,"Requested C deferred");}
        else Check(phase.Generation!=h.M.Schedule.Generation,"fresh chain new generation");
        Equal(8,h.M.ReconstructionDelayMs,"M8 held");
        h.M.RequestProfile(MotionProfile.Normal);h.Touch(TouchEventType.Up,down+1,100.5f,session:2);h.Finish((int)down+2);
        Equal(fresh?2L:3L,h.Total.X,"fraction ledger continues only pending chain; no completed-chain carry");
    }
    private static void Abort()
    {
        foreach(string kind in new[]{"run","reset","dispose"})
        {
            using var h=new H();h.Touch(TouchEventType.Down,0,0);h.Touch(TouchEventType.Up,4,100);var old=h.M.Schedule;
            if(kind=="run")h.Touch(TouchEventType.Down,5,500,session:2,run:2);else if(kind=="reset")h.M.Reset();else h.M.Dispose();
            h.Tick(100,old.Generation);Equal(0,h.Native.Count,"hard abort no old output");
            if(kind=="run"){h.Touch(TouchEventType.Up,6,502,session:2,run:2);h.Finish(7);Equal(2L,h.Total.X,"new run origin independent");}
        }
    }
    private static void Profiles()
    {
        using var h=new H();h.Touch(TouchEventType.Down,0,0);h.M.RequestProfile(MotionProfile.Cinematic);h.Tick(100);
        Equal(MotionProfile.Normal,h.M.ActiveMotionProfile,"stationary held still busy");
        h.Touch(TouchEventType.Up,101,0);h.Tick(108);Equal(MotionProfile.Normal,h.M.ActiveMotionProfile,"release pending still busy even zero delta");
        h.M.RequestProfile(MotionProfile.Normal);h.M.RequestProfile(MotionProfile.Cinematic);h.Tick(109);
        Equal(MotionProfile.Cinematic,h.M.ActiveMotionProfile,"latest Requested at completion");
        h.Touch(TouchEventType.Down,120,0,session:2);Equal(12,h.M.ReconstructionDelayMs,"C12");h.Touch(TouchEventType.Move,124,10000,session:2);
        for(int t=132;t<=172;t+=4)h.Tick(t);h.M.RequestProfile(MotionProfile.Normal);h.Touch(TouchEventType.Up,173,10000,session:2);
        h.Tick(184);Equal(MotionProfile.Cinematic,h.M.ActiveMotionProfile,"C release marker pending");h.Tick(188);
        Check(h.M.CinematicDynamics.Gliding,"C glide unchanged");Equal(MotionProfile.Cinematic,h.M.ActiveMotionProfile,"glide defers M");
        h.Finish(189);Equal(MotionProfile.Normal,h.M.ActiveMotionProfile,"natural C stop switches M");Equal(8,h.M.ReconstructionDelayMs,"M8 after natural boundary");
    }
    private static void CExact(string kind)
    {
        using var a=new H(MotionModes.ProductionMode,sx:2.75,sy:1.125);using var b=new H(sx:2.75,sy:1.125);
        foreach(var h in new[]{a,b})h.M.RequestProfile(MotionProfile.Cinematic);
        int stopped=-1;
        for(int t=0;t<=500;t++)
        {
            foreach(var h in new[]{a,b})
            {
                if(t==0)h.Touch(TouchEventType.Down,t,0);
                if(kind=="sensitivity" && t==24)h.Gain.Publish(6,3);
                if(t>0 && t<=80 && t%4==0)h.Touch(TouchEventType.Move,t,t<48?t*100:9600-t*100,-t*10,
                    stamp:kind=="late"?Math.Max(1,t-20):kind=="duplicate"&&t==44?40:kind=="fallback"&&t>=40?t-30:t);
                if(t==81)h.Touch(TouchEventType.Up,t,1600,-800);
                if(kind=="interrupt" && t==90)h.Touch(TouchEventType.Down,t,500,session:2);
                if(kind=="interrupt" && t==94)h.Touch(TouchEventType.Up,t,1000,session:2);
                if(kind=="interrupt-glide" && t==100)h.Touch(TouchEventType.Down,t,500,session:2);
                if(kind=="interrupt-glide" && t==104)h.Touch(TouchEventType.Up,t,1000,session:2);
                if(!(kind=="missed" && t is >=20 and <65))h.Tick(t);
            }
            var x=a.M.CinematicDynamics;var y=b.M.CinematicDynamics;
            Equal(x.Position,y.Position,"C position bits");Equal(x.Velocity,y.Velocity,"C velocity");Equal(x.Target,y.Target,"C target");
            Equal(x.LastSent,y.LastSent,"C ledger");Equal(x.CarryOver,y.CarryOver,"C natural carry");Equal(x.Gliding,y.Gliding,"C glide state");
            Equal(a.M.ReleaseMarker,b.M.ReleaseMarker,"C marker");Equal(a.M.Schedule,b.M.Schedule,"C deadline/generation");
            Equal(a.M.ReconstructedPosition,b.M.ReconstructedPosition,"C reconstruction");Equal(a.M.MissedTicks,b.M.MissedTicks,"C skip semantics");
            Check(a.Native.SequenceEqual(b.Native),"EVERY C native delta and fake submit time EXACT");
            if(t>100&&a.M.Schedule.Deadline is null&&stopped<0)stopped=t;
        }
        Check(stopped>0,"natural C stop observed");Check(a.Logical().SequenceEqual(b.Logical()),"C zero logical events EXACT");
        Console.WriteLine($"M-R1 C-Z1 isolation {kind}: EXACT native={a.Native.Count} naturalStopObserved={stopped}");
    }
    private static byte[] Profile(ulong run,uint sequence,MotionProfile profile)
    {
        byte[] b=new byte[16];b[0]=2;b[1]=7;BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(2),run);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(10),sequence);b[14]=(byte)profile;return b;
    }
    private static void Admission()
    {
        foreach(bool expire in new[]{false,true})
        {
            using var h=new H();using var r=new UdpReceiver(new(IPAddress.Loopback,0),TextWriter.Null,motion:h.M,detailedLogging:false);
            var source=new IPEndPoint(IPAddress.Loopback,1234);r.ProcessDatagram(PresenceTests.Heartbeat(1),source,h.Now);
            r.ProcessDatagram(PresenceTests.Touch(1,TouchEventType.Down,0),source,h.Now);
            h.Now=Time(4);r.ProcessDatagram(PresenceTests.Touch(1,TouchEventType.Up,1,40,4_000_000),source,h.Now);var old=h.M.Schedule;
            r.ProcessDatagram(Profile(1,0,MotionProfile.Cinematic),new(IPAddress.Parse("127.0.0.2"),1234),h.Now);
            Equal(MotionProfile.Normal,r.CurrentMotionProfile,"wrong source rejected");
            r.ProcessDatagram(Profile(1,0,MotionProfile.Cinematic),source,h.Now);Equal(MotionProfile.Cinematic,r.CurrentMotionProfile,"matching source accepted");
            Equal(MotionProfile.Normal,h.M.ActiveMotionProfile,"valid request still deferred");
            if(expire){h.Now+=3*System.Diagnostics.Stopwatch.Frequency;r.CheckTimeouts(h.Now);Check(!r.Presence.Connected,"presence expired");}
            else r.ProcessDatagram(PresenceTests.Heartbeat(2),source,h.Now);
            h.Tick(100,old.Generation);Equal(0,h.Native.Count,"invalidation fences pending motion");
        }
    }
    private static void Failure()
    {
        long now=Origin;int calls=0;using var m=new ResampledMotion((_,_)=>{calls++;throw new IOException("M-R1 injected native failure");},monotonicNow:()=>now,clockFrequency:Frequency,finiteCriticalMode:MotionMode.M_R1);
        m.Process(new(new(2,TouchEventType.Down,1,1,0,1),[S(0,0)]));now=Time(4);m.Process(new(new(2,TouchEventType.Up,1,1,1,1),[S(4,40)]));var old=m.Schedule;
        now=Time(12);Throws<IOException>(()=>m.Tick(now,old.Generation));Equal(0L,m.TotalDx,"no successful-submit total");m.Tick(Time(100),old.Generation);Equal(1,calls,"ambiguous failure never retried");Check(m.Schedule.Deadline is null,"failure clears");
    }
    private static async Task Save()
    {
        string dir=ArtifactDirectory("save");Directory.CreateDirectory(dir);
        string path=Path.Combine(dir,"settings.json");var initial=RuntimeSettings.Default with{SensitivityX=2,SensitivityY=3};
        var store=new RuntimeSettingsStore(initial);var file=new SettingsFileStore(path);var vm=new SettingsViewModel(store,file);
        using var h=new H(sx:2,sy:3,gain:store.Sensitivity);h.Touch(TouchEventType.Down,0,0);h.Touch(TouchEventType.Move,4,4,4);h.Tick(9);
        var pending=h.M.Pending;var total=h.Total;vm.SensitivityX.Text="4";vm.SensitivityY.Text="5";
        Equal(new SensitivitySnapshot(2,3),store.Sensitivity.Current,"draft inactive");Equal(pending,h.M.Pending,"draft does not rescale points");
        await vm.SaveAsync();Equal(new SensitivitySnapshot(4,5),store.Sensitivity.Current,"successful disk Save publishes pair");Check(File.Exists(path),"disk committed");
        Equal(total,h.Total,"Save no output");Equal(pending,h.M.Pending,"Save old pending untouched");h.Tick(12);Equal((8L,12L),h.Total,"old target keeps earned gain");
        h.Touch(TouchEventType.Move,16,5,5);h.Tick(24);Equal((12L,17L),h.Total,"only new delta gets new pair");
        string blocked=Path.Combine(dir,"blocked");Directory.CreateDirectory(blocked);var failedFile=new SettingsFileStore(blocked);var failed=new SettingsViewModel(store,failedFile);
        failed.SensitivityX.Text="7";await failed.SaveAsync();Equal(new SensitivitySnapshot(4,5),store.Sensitivity.Current,"failed Save never publishes");
        int n=h.Native.Count;h.Tick(100);Equal(n,h.Native.Count,"no new touch no output after failed Save");
        await file.FlushAsync();await failedFile.FlushAsync();
    }
    private static void Identity()
    {
        Equal(MotionMode.M_R1,Receiver.Program.ParseLaunchArguments(["--dev-motion-mode","M_R1"]).Motion,"explicit identity");
        Equal(MotionModes.ProductionMode,Receiver.Program.ResolveGuiMotionMode(Receiver.Program.ParseLaunchArguments([])),"production unchanged");
        Equal("Q0C",MotionModes.QuantizerName(MotionMode.M_R1),"quantizer capability");
        string dir=ArtifactDirectory("trace");
        using(var trace=new MotionTrace(dir,MotionMode.M_R1))
        using(var h=new H(trace:trace))
        {
            h.Touch(TouchEventType.Down,0,0);h.Touch(TouchEventType.Up,4,40);for(int t=8;t<=12;t++)h.Tick(t);
            Equal((0,0),h.Logical()[0],"H1 zero opportunity retained");Equal(5,h.Logical().Length,"H1 every valid tick");
            var a=h.M.ActiveAlgorithm;var vm=new RuntimeStatsViewModel();vm.Refresh(new(1,ReceiverState.Running,ActiveMotionAlgorithm:a.Algorithm,ActiveMotionTauMs:a.TauMs,ActiveMotionSupportMs:a.SupportMs),0);
            Equal(MotionModes.Mr1Algorithm,vm.MotionAlgorithm,"no Tau-gated blank algorithm");Equal("N/A",vm.ActiveTau,"active Tau N/A");Equal("N/A",vm.ActiveSupport,"active Support N/A");
            h.M.RequestProfile(MotionProfile.Cinematic);h.M.RequestProfile(MotionProfile.Normal);
        }
        var csv=File.ReadAllLines(Path.Combine(dir,"motion.csv"));
        Check(!csv.Any(s=>s.StartsWith("KernelPosition,")||s.StartsWith("KernelIntegration,")||s.StartsWith("SettleComplete,")),"no convolution or filter settlement evidence");
        var endpoint=csv.Last(s=>s.StartsWith("Position,")).Split(',');Equal(40d,double.Parse(endpoint[7],CultureInfo.InvariantCulture),"nonzero final R before Clear");
        Check(csv.Any(s=>s.StartsWith("ReconstructionComplete,")),"explicit completion identity");
        using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,"metadata.json")));var root=json.RootElement;
        Equal("M-R1",root.GetProperty("Mode").GetString(),"trace mode");Equal(JsonValueKind.Null,root.GetProperty("TauMs").ValueKind,"no Active Tau");
        Check(root.GetProperty("ProfileSegments").EnumerateArray().Select(p=>p.GetProperty("PlayoutDelayMs").GetInt32()).SequenceEqual(new[]{8,12,8}),"mixed M8/C12/M8 metadata");
        Console.WriteLine($"M-R1 trace evidence: {dir}");
    }
    private static async Task Runtime()
    {
        var native=new VirtualHidTests.FakeNative();var log=new StringWriter();var store=new RuntimeSettingsStore();
        ReceiverRuntime? runtime=null;
        runtime=new ReceiverRuntime(store,log,MouseBackend.VirtualHid,new(IPAddress.Loopback,0),()=>new LibVirtualHidMouseOutput(native,hitchTrace:runtime!.HitchTrace),motionMode:MotionMode.M_R1);
        try
        {
            await runtime.StartAsync();Check(log.ToString().Contains("playoutDelayMs=8"),"actual startup delay");Check(log.ToString().Contains("M-R1"),"startup identity");
            var snapshot=runtime.CaptureSnapshot();Equal(MotionModes.Mr1Algorithm,snapshot.ActiveMotionAlgorithm,"Runtime projects direct algorithm");Equal(0,snapshot.ActiveMotionTauMs,"Runtime active Tau absent");
            using var sender=new UdpClient();await sender.SendAsync(PresenceTests.Touch(1,TouchEventType.Down,0),runtime.LocalEndpoint!);
            await sender.SendAsync(PresenceTests.Touch(1,TouchEventType.Up,1,10,4_000_000),runtime.LocalEndpoint!);
            await RuntimeTests.Until(()=>runtime.CaptureSnapshot().MotionOutputEvents>0);
            var records=runtime.HitchTrace.Snapshot();foreach(var kind in new[]{HitchKind.Receive,HitchKind.Sample,HitchKind.Tick,HitchKind.Output,HitchKind.NativeSubmit})
                Check(records.Records.Take(records.Count).Any(r=>r.Kind==kind),"H1 preserved "+kind);
            native.FailMove=true;
            await sender.SendAsync(PresenceTests.Touch(1,TouchEventType.Down,2,0,20_000_000),runtime.LocalEndpoint!);
            await sender.SendAsync(PresenceTests.Touch(1,TouchEventType.Up,3,100,24_000_000),runtime.LocalEndpoint!);
            await RuntimeTests.Until(()=>runtime.CaptureSnapshot().RuntimeState==ReceiverState.Error);
            Check(runtime.CaptureSnapshot().LastError is not null,"ambiguous native failure visible in Runtime");
            records=runtime.HitchTrace.Snapshot();Check(records.Records.Take(records.Count).Any(r=>r.Kind==HitchKind.NativeSubmit&&r.Status==HitchStatus.Failure),"native failure bracket retained");
        }
        finally{await runtime.StopAsync();}
    }
}
