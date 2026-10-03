using System.Net;
using System.Reflection;
using System.Numerics;
using System.Net.Sockets;
using System.Text.Json;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class ZhqTrackpadDynamicsTests
{
    public static IEnumerable<(string Name, Action Run)> Cases =>
    [
        .. new[] { "step", "ramp", "stationary", "micro", "diagonal", "reversal", "acceleration cap", "velocity cap" }
            .Select(s => ($"Z1 independent servo oracle {s}", (Action)(() => Servo(s)))),
        ("Z1 acceleration cap 80000 discriminates from Z2 160000", AccelerationCap),
        ("Z1 strict position/speed thresholds and operation order", Thresholds),
        ("Z1 Java half/adjacent/cross-zero/negative-zero rounding", Rounding),
        ("Z1 glide threshold 482 and deceleration before integration", GlideThresholds),
        ("Z1 glide endpoints above/below target and ignores target", Endpoints),
        ("Z1 natural carry interrupted carry DOWN and hard reset", Carry),
        ("Z1 integer ledger commits only after successful submit", Ledger),
        .. new[] { "ordinary", "live sensitivity", "one missed", "multiple missed", "duplicate", "late", "backward" }
            .Select(s => ($"Z1 reconstruction/servo/native oracle {s}", (Action)(() => Pipeline(s)))),
        .. new[] { 15, 16, 17 }.Select(t => ($"Z1 UP arrival around deadline {t}", (Action)(() => Release(t)))),
        ("Z1 C touching stationary deferred and latest request wins", StationarySwitch),
        ("Z1 C release pending and gliding defer switch through natural stop", Deferred),
        ("Z1 new DOWN interrupts pending release and glide with stale fence", Interrupted),
        ("Z1 M touching and Earned-Settle defer C until safe boundary", FromM),
        ("Z1 carry retained C to C cleared C to M", ProfileCarry),
        ("Z1 reset Dispose native failure stale wakes zero output", Failure),
        ("Z1 isolated fake Runtime Stop joins C writer and native failure enters Error", () => RuntimeLifecycle().GetAwaiter().GetResult()),
        ("Z1 current source authority run invalidation presence expiry abort", Admission),
        ("Z1 missed glide steps extend duration without compensation", MissedGlide),
        ("Z1 active Diagnostics and mixed MotionTrace metadata", Diagnostics)
    ];

    // Independent Z1 oracle: literal constants, no production dynamics,
    // quantizer or configuration calls in the expected-value calculation.
    private sealed class Oracle
    {
        public double X, Y, Vx, Vy, Tx, Ty, Ix, Iy, Cx, Cy;
        public bool Glide;
        public void Down() { X = Tx = Cx; Y = Ty = Cy; Vx = Vy = Ix = Iy = 0; Glide = false; }
        public bool Step()
        {
            if (Glide)
            {
                double s = Math.Sqrt(Vx * Vx + Vy * Vy);
                if (s <= 482) { Vx = Vy = 0; return true; }
                double k = (s - 480) / s;
                Vx *= k; Vy *= k; X += Vx * .004; Y += Vy * .004;
                return false;
            }
            double dx = Tx - X, dy = Ty - Y;
            double d = Math.Sqrt(dx * dx + dy * dy), v = Math.Sqrt(Vx * Vx + Vy * Vy);
            if (d < .5 && v < 2) { X = Tx; Y = Ty; Vx = Vy = 0; return false; }
            double w = 1 / .035;
            double ax = dx * w * w - Vx * 2 * 1 * w;
            double ay = dy * w * w - Vy * 2 * 1 * w;
            double a = Math.Sqrt(ax * ax + ay * ay);
            if (a > 80000) { double k = 80000 / a; ax *= k; ay *= k; }
            Vx += ax * .004; Vy += ay * .004;
            v = Math.Sqrt(Vx * Vx + Vy * Vy);
            if (v > 15000) { double k = 15000 / v; Vx *= k; Vy *= k; }
            X += Vx * .004; Y += Vy * .004;
            return false;
        }
        // Exact rational oracle over the binary64 bits. It does not call the
        // production fraction classifier, Math.Round or a rounded x+0.5.
        public static int Round(double value)
        {
            ulong bits=BitConverter.DoubleToUInt64Bits(value);
            int exponent=(int)((bits>>52)&2047);
            BigInteger significand=bits&0xfffffffffffffUL;
            if(exponent!=0)significand+=BigInteger.One<<52;
            int power=exponent==0?-1074:exponent-1075;
            if((bits>>63)!=0)significand=-significand;
            BigInteger numerator=power>=0?significand<<power:significand;
            BigInteger denominator=power>=0?BigInteger.One:BigInteger.One<<-power;
            BigInteger result=BigInteger.DivRem(2*numerator+denominator,2*denominator,out var remainder);
            if(remainder.Sign<0)result--;
            return checked((int)result);
        }
        public (int X, int Y) Emit()
        {
            int dx = Round(X - Ix);
            int dy = Round(Y - Iy);
            Ix += dx; Iy += dy; return (dx, dy);
        }
        public void Stop() { Cx = X - Ix; Cy = Y - Iy; }
    }
    private static void Compare(Oracle o, ZhqTrackpadDynamics d)
    {
        Equal((o.X, o.Y), d.Position, "binary64 reference position operation order");
        Equal((o.Vx, o.Vy), d.Velocity, "binary64 reference velocity vector caps");
        Equal((o.Ix, o.Iy), d.LastSent, "independent integer ledger");
        Equal((o.Cx, o.Cy), d.CarryOver, "natural carry");
    }
    private static void Seed(ZhqTrackpadDynamics d, string property, (double, double) value) =>
        typeof(ZhqTrackpadDynamics).GetField($"<{property}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(d, value);
    private static void Servo(string kind)
    {
        var d = new ZhqTrackpadDynamics(); var o = new Oracle(); d.Down(); o.Down();
        for (int i = 0; i < 600; i++)
        {
            o.Tx = kind switch
            { "ramp" => i * 8, "stationary" => 0, "micro" => i * .001, "reversal" => i < 150 ? 1200 : -800,
              "acceleration cap" => 1e6, "velocity cap" => 1e8, _ => 1000 };
            o.Ty = kind is "diagonal" or "acceleration cap" or "velocity cap" ? o.Tx * .7 : 0;
            d.SetTarget(o.Tx, o.Ty); Equal(o.Step(), d.Step(), "shouldStop touching never parks");
            var expected = o.Emit(); Equal(expected, d.IntegerDelta(), "oracle integer emission");
            d.Commit(expected.X, expected.Y); Compare(o, d);
        }
        if (kind == "velocity cap") Check(Math.Abs(Math.Sqrt(o.Vx * o.Vx + o.Vy * o.Vy) - 15000) < 1e-8, "velocity cap exercised");
    }
    private static void AccelerationCap()
    {
        Equal(.004, ZhqTrackpadDynamics.Dt, "fixed DT");
        Equal(.035, ZhqTrackpadDynamics.Tau, "fixed tau");
        Equal(1.0, ZhqTrackpadDynamics.DampingRatio, "fixed damping");
        Equal(15000.0, ZhqTrackpadDynamics.MaxVelocity, "fixed Vmax");
        Equal(80000.0, ZhqTrackpadDynamics.MaxAcceleration, "Z1 fixed Amax");
        Equal(120000.0, ZhqTrackpadDynamics.GlideDeceleration, "fixed glide deceleration");
        Equal(.5, ZhqTrackpadDynamics.PositionThreshold, "fixed position threshold");
        Equal(2.0, ZhqTrackpadDynamics.VelocityThreshold, "fixed velocity threshold");
        var d = new ZhqTrackpadDynamics(); var o = new Oracle { Tx = 1e6 };
        d.Down(); d.SetTarget(1e6, 0);
        for (int i = 0; i < 3; i++)
        {
            Equal(o.Step(), d.Step(), "large step must use capped Z1 dynamics");
            var expected = o.Emit(); Equal(expected, d.IntegerDelta(), "independent capped integer output");
            d.Commit(expected.X, expected.Y); Compare(o, d);
            if (i == 0)
            {
                // Independent first-step results: A*DT, then velocity*DT.
                // Z1's 80000 gives velocity 320, position 1.28, integer output 1.
                Equal((320.0, 0.0), d.Velocity, "80000 * .004");
                Equal((1.28, 0.0), d.Position, "320 * .004");
                Equal((1, 0), expected, "Z1 first native delta differs from Z2 (3,0)");
                Check(d.Velocity.X != 640 && d.Position.X != 2.56, "Z2 160000 result discriminated");
                Console.WriteLine("Z1 acceleration-cap discrimination: target=1000000 Z1 v=320 p=1.28 delta=1; Z2 v=640 p=2.56 delta=3");
            }
        }
    }
    private static void Thresholds()
    {
        foreach (double distance in new[] { Math.BitDecrement(.5), .5, Math.BitIncrement(.5) })
        foreach (double speed in new[] { Math.BitDecrement(2.0), 2.0, Math.BitIncrement(2.0) })
        {
            var d = new ZhqTrackpadDynamics(); d.Down(); d.SetTarget(distance, 0); Seed(d, "Velocity", (speed, 0));
            var o = new Oracle { Tx = distance, Vx = speed }; o.Step(); d.Step(); Compare(o, d);
            if (distance < .5 && speed < 2) Equal((distance, 0.0), d.Position, "strict snap");
            else Check(d.Position.X != distance, "equal thresholds do not snap");
        }
    }
    private static void Rounding()
    {
        foreach (var (value, expected) in new (double, int)[]
        {
            (.5,1),(-.5,0),(1.5,2),(-1.5,-1),(2.5,3),(-2.5,-2),
            (Math.BitDecrement(.5),0),(Math.BitIncrement(.5),1),
            (Math.BitDecrement(-.5),-1),(Math.BitIncrement(-.5),0),
            (Math.BitDecrement(1.5),1),(Math.BitIncrement(1.5),2),
            (Math.BitDecrement(-1.5),-2),(Math.BitIncrement(-1.5),-1),
            (-0.0,0),(double.Epsilon,0),(-double.Epsilon,0),(-.49,0),(.49,0)
        }) Equal(expected, ZhqTrackpadDynamics.JavaRound(value), $"Java round {value:R}");
        for(int i=-2000;i<=2000;i++)
        foreach(double value in new[]{Math.BitDecrement(i+.5),i+.5,Math.BitIncrement(i+.5)})
            Equal(Oracle.Round(value),ZhqTrackpadDynamics.JavaRound(value),"bit-decoded half-adjacent oracle");
        var d = new ZhqTrackpadDynamics(); d.Down();
        foreach (double p in new[] { .5, -.5, -1.5, .5, 0.0 })
        {
            Seed(d,"Position",(p,0)); var delta=d.IntegerDelta();
            Check(delta.Y==0,"independent axis"); d.Commit(delta.X,delta.Y);
            Check(Math.Abs(d.Position.X-d.LastSent.X)<=.5,"cross-zero residual bounded");
        }
    }
    private static void GlideThresholds()
    {
        foreach (double speed in new[] { Math.BitDecrement(482.0),482.0,Math.BitIncrement(482.0),1000.0 })
        {
            var d=new ZhqTrackpadDynamics();d.Down();Seed(d,"Position",(3,4));Seed(d,"Velocity",(speed,0));d.Release();
            bool stop=d.Step();Equal(speed<=482,stop,"glide threshold inclusive");
            Equal(speed<=482?3:3+(speed-480)*.004,d.Position.X,"deceleration precedes position; stop skips integration");
        }
    }
    private static void Endpoints()
    {
        foreach(double target in new[]{-100.0,10000.0})
        {
            var d=new ZhqTrackpadDynamics();d.Down();d.SetTarget(target,0);Seed(d,"Velocity",(2000,0));d.Release();
            int n=0;while(!d.Step()){var q=d.IntegerDelta();d.Commit(q.X,q.Y);n++;}
            Check(n==4,"fixed glide number of steps");Equal(12.8,d.Position.X,"no target attraction or endpoint clamp");
            Check(target<0?d.Position.X>target:d.Position.X<target,"endpoint exceeds or undershoots target");
        }
    }
    private static void Carry()
    {
        var d=new ZhqTrackpadDynamics();d.Down();Seed(d,"Position",(.3,-.2));d.Release();Check(d.Step(),"natural stop");
        var q=d.IntegerDelta();d.Commit(q.X,q.Y);d.SaveNaturalCarry();Equal((.3,-.2),d.CarryOver,"saved remainder");
        d.Down();Equal(d.CarryOver,d.Position,"DOWN uses natural carry");Equal((0.0,0.0),d.LastSent,"new ledger");
        Seed(d,"Position",(23.4,-7.1));Seed(d,"Velocity",(3000,1000));d.Release();d.Step();d.Down();
        Equal((.3,-.2),d.CarryOver,"interrupt never snapshots new fraction");Equal((.3,-.2),d.Position,"interrupt initializes from old carry");
        Equal((0,0),d.IntegerDelta(),"DOWN never repeats old integer output");
        d.Reset();Equal((0.0,0.0),d.CarryOver,"hard reset");Equal((0.0,0.0),d.Velocity,"hard reset velocity");
    }
    private static void Ledger()
    {
        var d=new ZhqTrackpadDynamics();d.Down();Seed(d,"Position",(12.6,-8.5));
        Equal((13,-8),d.IntegerDelta(),"rounding");Equal((0.0,0.0),d.LastSent,"classifying does not commit");
        Equal((13,-8),d.IntegerDelta(),"no retry implied by classification");d.Commit(13,-8);Equal((0,0),d.IntegerDelta(),"one successful commit");
    }

    private const long Origin=100_000;
    private sealed class H : IDisposable
    {
        public long Now=Origin;public readonly List<(long At,int X,int Y)> Native=[];
        public readonly LiveSensitivity Sensitivity=new(1,1);
        public readonly ResampledMotion M;
        public bool Fail;public int Attempts;private uint sequence;
        public H(MotionProfile p=MotionProfile.Cinematic)
        {
            M=new((x,y)=>{Attempts++;if(Fail)throw new InvalidOperationException("Z1 injected native failure");Native.Add((Now,x,y));},
                monotonicNow:()=>Now,clockFrequency:1_000_000,finiteCriticalMode:MotionModes.ProductionMode,liveSensitivity:Sensitivity);
            M.RequestProfile(p);
        }
        public void Touch(TouchEventType e,int arrival,float x,float y=0,uint session=1,int? timestamp=null,ulong run=1)
        {Now=Origin+arrival*1000L;M.Process(new(new(2,e,1,session,sequence++,run),[new((ulong)(timestamp??arrival)*1_000_000,x,y)]));}
        public void Tick(int t,long? generation=null){Now=Origin+t*1000L;M.Tick(Now,generation??M.Schedule.Generation);}
        public void Finish(int from=0){for(int t=from;t<3000 && M.Schedule.Deadline is not null;t++)Tick(t);Check(M.Schedule.Deadline is null,"finite natural stop");}
        public void Dispose()=>M.Dispose();
    }
    private static void Pipeline(string kind)
    {
        using var h=new H();h.Touch(TouchEventType.Down,0,0);var o=new Oracle();o.Down();
        var points=new List<(int T,double X,double Y)>{(12,0,0)};double tx=0,ty=0;float raw=0;
        var expected=new List<(long,int,int)>();long? marker=null;long deadline=Origin+12000;long missed=0;int steps=0;
        for(int t=0;t<1200;t++)
        {
            if(t==40 && kind=="live sensitivity")h.Sensitivity.Publish(3,2);
            if(t>0 && t<=100 && t%4==0 || t==101)
            {
                float x=t<=60?t*50:(120-t)*50;
                int stamp=kind=="duplicate"&&t==44?40:kind=="backward"&&t==44?36:kind=="late"?Math.Max(0,t-20):t;
                h.Touch(t==101?TouchEventType.Up:TouchEventType.Move,t,x,x*.5f,timestamp:stamp);
                tx+=((double)x-raw)*(kind=="live sensitivity"&&t>=40?3:1);
                ty+=((double)(x*.5f)-(double)(raw*.5f))*(kind=="live sensitivity"&&t>=40?2:1);raw=x;
                int mapped=Math.Max(points[^1].T,kind=="backward"&&t>=44?t+12:stamp+12);
                if(points[^1].T==mapped)points[^1]=(mapped,tx,ty);else points.Add((mapped,tx,ty));
                if(t==101){marker=mapped;Equal(Origin+mapped*1000L,h.M.ReleaseMarker,"UP uses final mapped boundary once");}
            }
            if(kind=="one missed" && t is >=28 and <32 || kind=="multiple missed" && t is >=40 and <76)continue;
            if(Origin+t*1000L<deadline || h.M.Schedule.Deadline is null)continue;
            long skipped=(Origin+t*1000L-deadline)/4000;missed+=skipped;deadline+=(skipped+1)*4000;
            var left=points.Last(p=>p.T<=t);var right=points.FirstOrDefault(p=>p.T>t);
            double f=right.T==0?0:(t-left.T)/(double)(right.T-left.T);
            o.Tx=right.T==0?left.X:left.X+(right.X-left.X)*f;o.Ty=right.T==0?left.Y:left.Y+(right.Y-left.Y)*f;
            if(marker is long release && t>=release)o.Glide=true;
            bool stop=o.Step();var delta=o.Emit();if(delta!=(0,0))expected.Add((Origin+t*1000L,delta.X,delta.Y));steps++;
            h.Tick(t);Equal((long)steps,h.M.TickCount,"one fixed step per due wake");Equal(missed,h.M.MissedTicks,"skipped count");
            if(stop){o.Stop();Equal((o.Cx,o.Cy),h.M.CinematicDynamics.CarryOver,"natural carry after emission");break;}
            Compare(o,h.M.CinematicDynamics);Equal(deadline,h.M.Schedule.Deadline!.Value,"absolute fixed phase next deadline");
        }
        Check(expected.SequenceEqual(h.Native),"every native delta and fake-clock submit time from independent oracle");
        Check(h.M.Schedule.Deadline is null,"natural stop parks");Equal(0L,h.M.UpFlushCount,"never Earned-Settle or UP flush");
        if(kind=="duplicate")Check(h.M.DuplicateTimestamps>0,"duplicate exercised");
        if(kind=="late")Check(h.M.LateSamples>0,"late samples exercised");
        if(kind=="backward")Check(h.M.NonMonotonicTimestamps>0,"arrival-order fallback exercised");
        Console.WriteLine($"Z1 oracle {kind}: steps={steps} native={expected.Count} missed={missed}");
    }
    private static void Release(int arrival)
    {
        using var h=new H();h.Touch(TouchEventType.Down,0,0);h.Touch(TouchEventType.Move,4,400);h.Tick(12);
        if(arrival>16)h.Tick(16);
        h.Touch(TouchEventType.Up,arrival,500);Equal(Origin+(arrival+12)*1000L,h.M.ReleaseMarker,"no second delay");
        int n=h.Native.Count;Check(!h.M.CinematicDynamics.Gliding,"packet arrival never enters glide");Equal(n,h.Native.Count,"UP emits nothing");
        for(int t=20;t<=arrival+16;t+=4)
        {h.Tick(t);if(t<arrival+12)Check(!h.M.CinematicDynamics.Gliding,"touching until marker");else Check(h.M.CinematicDynamics.Gliding,"first due tick at/after marker glides");}
    }
    private static void StationarySwitch()
    {
        using var h=new H();h.Touch(TouchEventType.Down,0,0);h.M.RequestProfile(MotionProfile.Normal);
        for(int t=12;t<=200;t+=4)h.Tick(t);
        Equal(MotionProfile.Cinematic,h.M.ActiveMotionProfile,"stationary contact remains busy");Check(h.M.Schedule.Deadline is not null,"stationary ticks retain cadence");
        h.M.RequestProfile(MotionProfile.Cinematic);h.Touch(TouchEventType.Up,201,0);h.Finish(202);
        Equal(MotionProfile.Cinematic,h.M.ActiveMotionProfile,"latest C wins at natural stop");
    }
    private static void Deferred()
    {
        foreach(bool pending in new[]{true,false})
        {
            using var h=new H();h.Touch(TouchEventType.Down,0,0);h.Touch(TouchEventType.Move,4,10000);
            for(int t=12;t<=60;t+=4)h.Tick(t);h.Touch(TouchEventType.Up,61,10000);
            if(!pending)for(int t=64;t<=76;t+=4)h.Tick(t);
            h.M.RequestProfile(MotionProfile.Normal);Equal(MotionProfile.Cinematic,h.M.ActiveMotionProfile,"pending/glide defers M");
            h.Finish(pending?62:77);Equal(MotionProfile.Normal,h.M.ActiveMotionProfile,"only natural stop applies M");
            Equal((0.0,0.0),h.M.CinematicDynamics.CarryOver,"real C to M clears carry");Equal(1,h.M.PeriodMs,"M cadence restored");
        }
    }
    private static void Interrupted()
    {
        foreach(int down in new[]{63,79})
        {
            using var h=new H();h.Touch(TouchEventType.Down,0,0);h.Touch(TouchEventType.Move,4,10000);
            for(int t=12;t<=60;t+=4)h.Tick(t);h.Touch(TouchEventType.Up,61,10000);h.M.RequestProfile(MotionProfile.Normal);
            if(down>73)for(int t=64;t<=76;t+=4)h.Tick(t);
            var old=h.M.Schedule;int n=h.Native.Count;Seed(h.M.CinematicDynamics,"CarryOver",(.3,-.2));
            h.Touch(TouchEventType.Down,down,500,session:2);
            Equal(MotionProfile.Cinematic,h.M.ActiveMotionProfile,"new DOWN retains unfinished C chain");
            Equal((.3,-.2),h.M.CinematicDynamics.Position,"old natural carry only");Equal((0.0,0.0),h.M.CinematicDynamics.Velocity,"velocity reset");
            Check(h.M.ReleaseMarker is null,"old marker removed");Equal(Origin+(down+12)*1000L,h.M.Schedule.Deadline,"new phase");
            h.Tick(down+20,old.Generation);Equal(n,h.Native.Count,"old generation emits zero");
            h.Tick(down+24);Check(!h.M.CinematicDynamics.Gliding,"old UP marker cannot affect new contact");
            Equal(n,h.Native.Count,"old targets and integer outputs never replay");
        }
    }
    private static void FromM()
    {
        using var h=new H(MotionProfile.Normal);h.Touch(TouchEventType.Down,0,0);h.M.RequestProfile(MotionProfile.Cinematic);
        h.Tick(12);Equal(MotionProfile.Normal,h.M.ActiveMotionProfile,"stationary M touching defers C");
        h.Touch(TouchEventType.Move,16,100);h.Touch(TouchEventType.Up,17,100);h.Tick(40);
        Equal(MotionProfile.Normal,h.M.ActiveMotionProfile,"M settlement defers C");h.Finish(41);
        Equal(MotionProfile.Cinematic,h.M.ActiveMotionProfile,"M earned boundary applies C");Equal(100L,h.M.TotalDx,"M final earned endpoint exact");
    }
    private static void ProfileCarry()
    {
        using var h=new H();h.Touch(TouchEventType.Down,0,0);h.Touch(TouchEventType.Move,4,.3f);h.Tick(16);
        h.Touch(TouchEventType.Up,17,.3f);h.Finish(18);Check(h.M.CinematicDynamics.CarryOver.X!=0,"nonvacuous natural carry");
        var carry=h.M.CinematicDynamics.CarryOver;h.M.RequestProfile(MotionProfile.Cinematic);h.Touch(TouchEventType.Down,100,100,session:2);
        Equal(carry,h.M.CinematicDynamics.Position,"C to C preserves natural carry");int n=h.Native.Count;h.Tick(112);Equal(n,h.Native.Count,"no duplicate integer output on DOWN");
        h.Touch(TouchEventType.Up,113,100,session:2);h.Finish(114);h.M.RequestProfile(MotionProfile.Normal);
        Equal((0.0,0.0),h.M.CinematicDynamics.CarryOver,"profile switch clears");
        h.Touch(TouchEventType.Down,200,0,session:3);Equal((0.0,0.0),h.M.Position,"M starts clean");
    }
    private static void Failure()
    {
        foreach(string action in new[]{"reset","dispose","native"})
        {
            using var h=new H();h.Touch(TouchEventType.Down,0,0);h.Touch(TouchEventType.Move,4,10000);long old=h.M.Schedule.Generation;
            if(action=="native"){h.Fail=true;bool threw=false;try{h.Tick(16);}catch(InvalidOperationException){threw=true;}Check(threw,"native failure propagated");Equal(1,h.Attempts,"no ambiguous retry");}
            else if(action=="dispose")h.M.Dispose();else h.M.Reset();
            int n=h.Native.Count;h.Tick(500,old);Equal(n,h.Native.Count,"stale failure wake zero output");
            Check(h.M.Schedule.Deadline is null && h.M.ReleaseMarker is null,"schedule/marker cleared");Equal((0.0,0.0),h.M.CinematicDynamics.CarryOver,"carry aborted");
            Equal((0.0,0.0),h.M.CinematicDynamics.Velocity,"velocity aborted");Equal((0.0,0.0),h.M.CinematicDynamics.Target,"target aborted");
        }
    }
    private static void Admission()
    {
        foreach(string invalidation in new[]{"run","source","expiry","dispose"})
        {
            using var h=new H();using var r=new UdpReceiver(new(IPAddress.Loopback,0),TextWriter.Null,motion:h.M,detailedLogging:false);
            void Send(byte[] b,string source="127.0.0.1")=>r.ProcessDatagram(b,new(IPAddress.Parse(source),1234),h.Now);
            Send(PresenceTests.Heartbeat(1));
            byte[] profile=new byte[16];profile[0]=2;profile[1]=7;profile[2]=1;profile[14]=1;Send(profile);
            var down=PresenceTests.Touch(1,TouchEventType.Down,0);Send(down);h.Touch(TouchEventType.Move,4,10000);h.Tick(16);
            long old=h.M.Schedule.Generation;int n=h.Native.Count;
            if(invalidation=="run")Send(PresenceTests.Heartbeat(2));
            else if(invalidation=="source")
            {
                // Same-run source correlation changes type7 authority, not Touch
                // admission. Preserve that existing contract; a new source/run
                // transition then exercises the real input invalidation hook.
                Send(PresenceTests.Heartbeat(1),"127.0.0.2");
                profile[10]=1;profile[14]=0;Send(profile);
                Equal(MotionProfile.Cinematic,r.CurrentMotionProfile,"old source cannot request M");
                Equal(old,h.M.Schedule.Generation,"source authority update alone preserves Touch contract");
                Send(PresenceTests.Heartbeat(2),"127.0.0.2");
            }
            else if(invalidation=="dispose")r.Dispose();
            else {h.Now+=System.Diagnostics.Stopwatch.Frequency*3;Send(profile);}
            h.M.Tick(h.Now+1_000_000,old);Equal(n,h.Native.Count,"invalidation emits no tail");
            Check(h.M.Schedule.Deadline is null,"invalidation clears schedule");Equal((0.0,0.0),h.M.CinematicDynamics.CarryOver,"invalidation clears carry");
            if(invalidation is "run" or "source")Equal(MotionProfile.Normal,h.M.ActiveMotionProfile,"new run M baseline");
        }
    }
    private static async Task RuntimeLifecycle()
    {
        foreach(string reason in new[]{"touching","pending","glide","failure"})
        {
            var native=new VirtualHidTests.FakeNative {FailMove=reason=="failure"};
            var runtime=new ReceiverRuntime(new(),TextWriter.Null,MouseBackend.VirtualHid,new(IPAddress.Loopback,0),
                ()=>new LibVirtualHidMouseOutput(native), motionMode:MotionModes.ProductionMode, useProductMotionSettings:true);
            using var sender=new UdpClient();
            try
            {
                await runtime.StartAsync();var endpoint=runtime.LocalEndpoint!;
                await sender.SendAsync(PresenceTests.Heartbeat(1),endpoint);
                await RuntimeTests.Until(()=>runtime.CaptureSnapshot().Presence?.Connected==true);
                byte[] profile=new byte[16];profile[0]=2;profile[1]=7;profile[2]=1;profile[14]=1;
                await sender.SendAsync(profile,endpoint);
                await RuntimeTests.Until(()=>runtime.CaptureSnapshot().ActiveMotionProfile==MotionProfile.Cinematic);
                await sender.SendAsync(PacketDecoderTests.Encode(TouchEventType.Down,1,0,new TouchSample(0,0,0)),endpoint);
                await sender.SendAsync(PacketDecoderTests.Encode(TouchEventType.Move,1,1,new TouchSample(4_000_000,1e6f,0)),endpoint);
                if(reason=="failure")
                {
                    await RuntimeTests.Until(()=>runtime.Completion.IsCompleted);
                    Equal(ReceiverState.Error,runtime.CaptureSnapshot().RuntimeState,"C native failure enters Runtime Error");
                    Equal(1,native.Events.Count(e=>e.Kind=="move"),"ambiguous report is never retried");
                    Check(native.Events.Last().Kind=="dispose","C error destroys device");
                }
                else
                {
                    await RuntimeTests.Until(()=>native.Events.Count(e=>e.Kind=="move")>=20);
                    if(reason is "pending" or "glide")
                    {
                        await sender.SendAsync(PacketDecoderTests.Encode(TouchEventType.Up,1,2,
                            new TouchSample(reason=="pending"?500_000_000UL:4_000_000UL,1e6f,0)),endpoint);
                        await RuntimeTests.Until(()=>runtime.CaptureSnapshot().AcceptedPackets>=3);
                        if(reason=="glide")
                        {long ticks=runtime.CaptureSnapshot().MotionClockTicks;await RuntimeTests.Until(()=>runtime.CaptureSnapshot().MotionClockTicks>ticks);}
                    }
                    await runtime.StopAsync();
                    Equal(ReceiverState.Stopped,runtime.CaptureSnapshot().RuntimeState,"C Stop complete");
                    Check(runtime.Completion.IsCompleted && native.Events.Last().Kind=="dispose","Stop joins writer before disposal");
                    Check(native.Events.Count(e=>e.Kind=="dispose")==1,"single native cleanup");
                }
            }
            finally{await runtime.StopAsync();}
        }
    }
    private static void MissedGlide()
    {
        int Run(bool miss)
        {
            using var h=new H();h.Touch(TouchEventType.Down,0,0);h.Touch(TouchEventType.Move,4,1e6f);
            for(int t=12;t<=100;t+=4)h.Tick(t);h.Touch(TouchEventType.Up,101,1e6f);
            int done=0;for(int t=104;t<1000;t+=4){if(miss&&t is >=120 and <220)continue;h.Tick(t);if(h.M.Schedule.Deadline is null){done=t;break;}}
            Check(done>0,"glide stopped");return done;
        }
        int regular=Run(false),missed=Run(true);Equal(100,missed-regular,"missed 25 steps extend wall clock by 100ms");
    }
    private static void Diagnostics()
    {
        using var h=new H();var a=h.M.ActiveAlgorithm;var vm=new RuntimeStatsViewModel();
        vm.Refresh(new(1,ReceiverState.Running,MotionProfile:MotionProfile.Normal,ActiveMotionProfile:a.Profile,
            ActiveMotionAlgorithm:a.Algorithm,ActiveMotionTauMs:a.TauMs,ActiveMotionSupportMs:a.SupportMs,NativeOutputCadence:h.M.NativeOutputCadence),0);
        Equal("M",vm.MotionProfile,"requested separate");Equal("C",vm.ActiveMotionProfile,"active separate");
        Check(vm.ActiveMotionKernel.Contains("4 ms")&&vm.MotionAlgorithm.Contains("Java-compatible")&&vm.MotionAlgorithm.Contains("true glide"),"actual C identity");
        Check(vm.ActiveMotionKernel.Contains("zhq-derived servo")&&vm.ActiveMotionKernel.Contains("Amax 80000")&&vm.ActiveMotionKernel.Contains("Vmax 15000"),"Z1 derived identity and fixed caps");
        string dir=Path.Combine(Path.GetTempPath(),"rightpad-z2-"+Guid.NewGuid());
        using(var trace=new MotionTrace(dir,MotionModes.ProductionMode,64))
        {trace.BeginRuntimeRun(1,MotionModes.ProductionMode);trace.Profile(MotionProfile.Cinematic);trace.Profile(MotionProfile.Normal);trace.Profile(MotionProfile.Cinematic);}
        using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,"metadata.json")));var root=json.RootElement;
        Equal(4,root.GetProperty("PeriodMs").GetInt32(),"trace C cadence");Equal(35,root.GetProperty("TauMs").GetInt32(),"trace C tau");
        Equal("Java-compatible rounding",root.GetProperty("Quantizer").GetString(),"trace C quantizer");Equal(4,root.GetProperty("ProfileSegments").GetArrayLength(),"initial M and mixed profile interpretation");
        Equal("C-Z1",root.GetProperty("Mode").GetString(),"trace Z1 identity");
        Check(root.GetProperty("Algorithm").GetString()!.Contains("zhq-derived servo / Amax 80000 / Vmax 15000"),"trace fixed Z1 caps");
    }
}
