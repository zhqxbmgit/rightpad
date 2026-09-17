using System.Globalization;
using System.Numerics;
using static Rightpad.Receiver.Tests.Program;
using static Rightpad.Receiver.Tests.RawMotionProcessorTests;

namespace Rightpad.Receiver.Tests;

internal static class CanonicalPositionQuantizerTests
{
    private static BigInteger Exact(double p, long i)
    {
        ulong bits = BitConverter.DoubleToUInt64Bits(p);
        int raw = (int)((bits >> 52) & 2047);
        BigInteger significand = bits & 0xFFFFFFFFFFFFFUL;
        if (raw != 0) significand += BigInteger.One << 52;
        if (bits >> 63 != 0) significand = -significand;
        int exponent = raw == 0 ? -1074 : raw - 1075;
        return exponent >= 0 ? (significand << exponent) - i :
            (significand - (new BigInteger(i) << -exponent)) / (BigInteger.One << -exponent);
    }

    public static IEnumerable<(string Name, Action Run)> Cases
    {
        get
        {
            yield return ("Q0C native K24 session 13653 bitstream / held / UP / reproducibility", NativeFailure);
            yield return ("Q0C bit-decoded reference / signed zero / subnormal / boundaries / large state", Boundaries);
            yield return ("Q0C exact zero crossing / symmetric integer discrepancies", Crossing);
            yield return ("Q0C fractional endpoint preserves directional history", Fractional);
            yield return ("Q0C invalid/overflow and failed submission never commit either axis", Atomic);
            yield return ("Q0C successful submit commits after output / reset", Commit);
            yield return ("Q0C deterministic random exact classification", RandomStates);
            yield return ("B/F legacy Q0-I retained with explicit quantizer identity", Legacy);
            foreach(var mode in new[]{MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5,MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K35_R4})
            {
                yield return ($"{mode} Q0C overflow propagates through tick and UP to Runtime Error",()=>RuntimeOverflow(mode).GetAwaiter().GetResult());
                yield return ($"{mode} Q0C nonzero I cleared by sender/presence invalidation",()=>Lifecycle(mode));
            }
        }
    }
    private static void NativeFailure()
    {
        string[] lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory,"Fixtures","k24-session-13653-px.txt"));
        foreach (int sign in new[] { 1, -1 })
        for (int repeat = 0; repeat < 2; repeat++)
        {
            var q = new CanonicalPositionQuantizer(); var legacy = new RawMotionProcessor();
            long native = 0, old = 0; double previous = 0;
            for (int index = 0; index < lines.Length; index++)
            {
                double p = sign * BitConverter.UInt64BitsToDouble(ulong.Parse(lines[index],NumberStyles.HexNumber));
                old += legacy.Process(p-previous,0).X; previous=p;
                if (index == 374) Equal((long)-sign,q.EmittedX,"terminal previous integer");
                var d=q.Submit(p,0,(x,_)=>native+=x);
                if (index == 374) { Equal(sign,d.X,"normal tick exact missing count"); Equal(0L,native,"native terminal endpoint"); }
            }
            Equal((long)-sign,old,"fixture reproduces original Q0-I failure");
            Equal(0L,q.EmittedX,"UP endpoint");
            for(int tick=0;tick<1000;tick++)Equal((0,0),q.Submit(0,0,(_,_)=>throw new Exception("spurious output")),"held zero");
        }
    }
    private static void Boundaries()
    {
        foreach(double p in new[]{0.0,-0.0,double.Epsilon,-double.Epsilon,Math.ScaleB(1,-54),-Math.ScaleB(1,-54),Math.BitDecrement(1),1,Math.BitIncrement(1),Math.BitDecrement(-1),-1,Math.BitIncrement(-1),Math.BitDecrement(2),2,Math.BitIncrement(2),-2,1e9+.5,-1e9-.5})
        foreach(long i in new long[]{-3,-2,-1,0,1,2,3,1000000000,-1000000000})
            Equal(checked((int)Exact(p,i)),CanonicalPositionQuantizer.ExactTruncateDifference(p,i),"exact dyadic state");
        double large=Math.BitDecrement(Math.ScaleB(1,63));
        Equal(0,CanonicalPositionQuantizer.ExactTruncateDifference(large,(long)large),"large exact state");
        Equal(-1,CanonicalPositionQuantizer.ExactTruncateDifference(large,(long)large+1),"large I not representable in double");
        Equal(0,CanonicalPositionQuantizer.ExactTruncateDifference(-Math.ScaleB(1,63),long.MinValue),"inclusive lower limit");
        Equal(int.MinValue,CanonicalPositionQuantizer.ExactTruncateDifference(int.MinValue,0),"int32 lower delta");
        Equal(int.MaxValue,CanonicalPositionQuantizer.ExactTruncateDifference(int.MaxValue,0),"int32 upper delta");
    }
    private static void Crossing()
    {
        foreach(int sign in new[]{-1,1})
        {
            var q=new CanonicalPositionQuantizer();q.Submit(sign,0,(_,_)=>{});
            Equal((0,0),q.Submit(sign*Math.ScaleB(1,-54),0,(_,_)=>throw new Exception("early integer")),"exact side of threshold");
            Equal((-sign,0),q.Submit(0,0,(_,_)=>{}),"zero completes normally");
            foreach(int n in new[]{1,2,3,10,1000000000}) {q.Reset();q.Submit(sign*n,0,(_,_)=>{});Equal((-sign*n,0),q.Submit(0,0,(_,_)=>{}),"lawful larger discrepancy");}
        }
    }
    private static void Fractional()
    {
        var q=new CanonicalPositionQuantizer();q.Submit(2,0,(_,_)=>{});q.Submit(.75,0,(_,_)=>{});Equal(1L,q.EmittedX,"approached from above");
        q.Reset();q.Submit(.75,0,(_,_)=>{});Equal(0L,q.EmittedX,"approached from below");
    }
    private static void Atomic()
    {
        var q=new CanonicalPositionQuantizer();q.Submit(1,-1,(_,_)=>{});
        foreach(double p in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity,double.MaxValue,Math.ScaleB(1,63),Math.BitDecrement(-Math.ScaleB(1,63)),(double)int.MaxValue+10,(double)int.MinValue-10})
        {Throws<OverflowException>(()=>q.Submit(2,p,(_,_)=>throw new Exception("must validate before output")));Equal(1L,q.EmittedX,"X unchanged");Equal(-1L,q.EmittedY,"Y unchanged");}
        Throws<InvalidOperationException>(()=>q.Submit(20,30,(_,_)=>throw new InvalidOperationException("native failed")));
        Equal(1L,q.EmittedX,"failed output X uncommitted");Equal(-1L,q.EmittedY,"failed output Y uncommitted");
        Throws<OverflowException>(()=>CanonicalPositionQuantizer.ExactTruncateDifference(-Math.ScaleB(1,63),long.MaxValue));
    }
    private static void Commit()
    {
        var q=new CanonicalPositionQuantizer();q.Submit(2,-3,(x,y)=>{Equal(0L,q.EmittedX,"not committed during output");Equal((2,-3),(x,y),"delta");});
        Equal(2L,q.EmittedX,"committed X");Equal(-3L,q.EmittedY,"committed Y");q.Reset();Equal(0L,q.EmittedX,"reset X");Equal(0L,q.EmittedY,"reset Y");
    }
    private static void RandomStates()
    {
        var random=new Random(13653);
        for(int n=0;n<100000;n++)
        {
            double p=Math.ScaleB(random.NextDouble()*2-1,random.Next(-1074,51));long i=(long)p+random.Next(-100,101);
            Equal((int)Exact(p,i),CanonicalPositionQuantizer.ExactTruncateDifference(p,i),"random exact classification");
        }
    }
    private static void Legacy()
    {
        foreach(var mode in new[]{MotionMode.RAW,MotionMode.RESAMPLED_250HZ,MotionMode.RESAMPLED_250HZ_BOXCAR_4MS,MotionMode.RESAMPLED_250HZ_BOXCAR_8MS})Equal("Q0I",MotionModes.QuantizerName(mode),"legacy identity");
        var q=new RawMotionProcessor();q.Process(-1,0);Equal((1,0),q.Process(1-Math.ScaleB(1,-54),0),"legacy binary64 early event preserved");
    }
    private static async Task RuntimeOverflow(MotionMode mode)
    {
        foreach(var type in new[]{TouchEventType.Move,TouchEventType.Up})
        {
            var native=new VirtualHidTests.FakeNative();
            var runtime=new ReceiverRuntime(new(),TextWriter.Null,MouseBackend.VirtualHid,new(System.Net.IPAddress.Loopback,0),()=>new LibVirtualHidMouseOutput(native),motionMode:mode);
            await runtime.StartAsync();using var sender=new System.Net.Sockets.UdpClient();
            await sender.SendAsync(PresenceTests.Touch(1,TouchEventType.Down,0),runtime.LocalEndpoint!);
            await sender.SendAsync(PresenceTests.Touch(1,type,1,float.MaxValue,4_000_000),runtime.LocalEndpoint!);
            await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            var snapshot=runtime.CaptureSnapshot();Equal(ReceiverState.Error,snapshot.RuntimeState,"explicit runtime error");
            Check(snapshot.LastError?.Contains("Q0C")==true,"quantizer error visible");
            Check(!native.Events.Any(e=>e.Kind=="move"),"no saturated/wrapped movement submitted");
            Check(native.Events.Any(e=>e.Kind=="dispose"),"native cleanup");await runtime.StopAsync();
        }
    }
    private static void Lifecycle(MotionMode mode)
    {
        long frequency=System.Diagnostics.Stopwatch.Frequency,now=10*frequency;int net=0;
        using var motion=new ResampledMotion((x,_)=>net+=x,monotonicNow:()=>now,clockFrequency:frequency,finiteCriticalMode:mode);
        using var receiver=new UdpReceiver(new(System.Net.IPAddress.Loopback,0),TextWriter.Null,motion:motion,detailedLogging:false);
        void Send(byte[] data)=>receiver.ProcessDatagram(data,new(System.Net.IPAddress.Loopback,1234),now);
        void Complete(){now+=frequency;motion.Tick(now,motion.Schedule.Generation);}
        Send(PresenceTests.Touch(1,TouchEventType.Down,0));Send(PresenceTests.Touch(1,TouchEventType.Move,1,10,4_000_000));Complete();Equal(10,net,"first contact earned I");
        Send(PresenceTests.Heartbeat(2));Send(PresenceTests.Touch(2,TouchEventType.Down,0,100));Send(PresenceTests.Touch(2,TouchEventType.Move,1,101,4_000_000));Complete();Equal(11,net,"new sender starts at contact origin");
        now+=2*frequency;receiver.CheckTimeouts(now);Send(PresenceTests.Touch(2,TouchEventType.Down,2,200));Send(PresenceTests.Touch(2,TouchEventType.Move,3,201,8_000_000));Complete();Equal(12,net,"presence reset starts at contact origin");
    }
}
