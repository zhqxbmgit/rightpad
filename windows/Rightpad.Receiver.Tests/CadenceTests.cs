using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class CadenceTests
{
    private static readonly MotionMode[] Modes = [MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5_SETTLE,
        MotionMode.RESAMPLED_500HZ_FINITE_CRITICAL_K24_R5_SETTLE, MotionMode.RESAMPLED_1000HZ_FINITE_CRITICAL_K24_R5_SETTLE];
    private const long Origin = 100_000, Frequency = 1_000_000;
    private sealed class Harness(MotionMode mode) : IDisposable
    {
        public long Now = Origin;
        public readonly List<(int X,int Y)> Moves = [];
        private ResampledMotion? motion;
        public ResampledMotion Motion => motion ??= new((x,y)=>Moves.Add((x,y)),9,9,()=>Now,Frequency,finiteCriticalMode:mode);
        public void Advance(int ms)
        {
            long end=Origin+ms*1000L;
            while(Motion.Schedule.Deadline is long d && d<=end){Now=d;Motion.Tick(d,Motion.Schedule.Generation);}
            Now=end;
        }
        public void Send(TouchEventType type,int ms,float x,float y=0,uint session=1)
        {
            Advance(ms);
            Motion.Process(new(new(2,type,1,session,(uint)ms,1),[new((ulong)ms*1_000_000,x,y)]));
        }
        public void Dispose()=>motion?.Dispose();
    }
    public static IEnumerable<(string Name,Action Run)> Cases
    {
        get
        {
            foreach(var mode in Modes)
            {
                yield return ($"Cadence {mode}: fixed period, CLI, Q0C and 12ms playout",()=>Configuration(mode));
                yield return ($"Cadence {mode}: earned endpoint, settle completion and silent UP",()=>Endpoint(mode));
                yield return ($"Cadence {mode}: hard reset cancels old tail",()=>Reset(mode));
                yield return ($"Cadence {mode}: skip overdue opportunities, no catch-up output",()=>Skip(mode));
                yield return ($"Cadence {mode}: next contact preserves cadence and earned ledger",()=>Continue(mode));
            }
            yield return ("Cadence continuous K24 positions agree at common actual times",SameContinuousPosition);
            yield return ("Cadence kernel actual non-grid time and support remain 120ms",ActualTime);
        }
    }
    private static void Configuration(MotionMode mode)
    {
        int period=mode==Modes[0]?4:mode==Modes[1]?2:1;
        Equal(period,MotionModes.PeriodMs(mode),"explicit fixed period");
        Equal(mode,Receiver.Program.ParseLaunchArguments(["--dev-motion-mode",mode.ToString()]).Motion,"CLI");
        Equal("Q0C",MotionModes.QuantizerName(mode),"unchanged quantizer");
        using var h=new Harness(mode);h.Send(TouchEventType.Down,0,0);h.Send(TouchEventType.Move,1,1);
        Equal(period,h.Motion.PeriodMs,"runtime period");
        Equal((long?)(Origin+12000),h.Motion.Schedule.Deadline,"12ms playout independent of period");
        h.Advance(12);Equal((long?)(Origin+12000+period*1000),h.Motion.Schedule.Deadline,"next opportunity");
        Equal((24,120),(h.Motion.KernelTauMs,h.Motion.KernelSupportMs),"fixed K24");
    }
    private static void Endpoint(MotionMode mode)
    {
        using var h=new Harness(mode);h.Send(TouchEventType.Down,0,0);
        h.Send(TouchEventType.Move,5,12,-4);h.Advance(9);int before=h.Moves.Count;
        h.Send(TouchEventType.Up,9,37,-11);Equal(before,h.Moves.Count,"no UP flush");
        h.Advance(300);Equal(333,h.Moves.Sum(v=>v.X),"same earned X");Equal(-99,h.Moves.Sum(v=>v.Y),"same earned Y");
        Check(h.Moves.All(v=>v.X>=0&&v.Y<=0),"no extra path");
        Equal(0L,h.Motion.UpFlushCount,"no instant flush regression");Check(h.Motion.Schedule.Deadline is null,"settle completes");
        before=h.Moves.Count;h.Advance(1000);Equal(before,h.Moves.Count,"parked, no new distance");
    }
    private static void Reset(MotionMode mode)
    {
        using var h=new Harness(mode);h.Send(TouchEventType.Down,0,0);h.Send(TouchEventType.Up,4,100);
        long generation=h.Motion.Schedule.Generation;h.Motion.Reset();h.Motion.Tick(Origin+500000,generation);
        Equal(0,h.Moves.Count,"old tail cancelled");Check(h.Motion.Schedule.Deadline is null,"parked");
        h.Send(TouchEventType.Down,20,1000,session:2);h.Send(TouchEventType.Up,24,1001,session:2);h.Advance(200);
        Equal(9,h.Moves.Sum(v=>v.X),"no old earned backlog after reset");
    }
    private static void Skip(MotionMode mode)
    {
        using var h=new Harness(mode);h.Send(TouchEventType.Down,0,0);h.Send(TouchEventType.Move,1,100);
        long first=h.Motion.Schedule.Deadline!.Value,period=h.Motion.PeriodMs*1000;
        h.Now=first+5*period+period/2;long generation=h.Motion.Schedule.Generation;
        h.Motion.Tick(h.Now,generation);Equal(1L,h.Motion.TickCount,"one evaluation per wake");Equal(5L,h.Motion.MissedTicks,"five skipped opportunities");
        Equal((long?)(first+6*period),h.Motion.Schedule.Deadline,"absolute phase retained");
        int count=h.Moves.Count;h.Motion.Tick(h.Now,generation);Equal(count,h.Moves.Count,"no replay/catch-up emission");Equal(1L,h.Motion.TickCount,"no catch-up ticks");
    }
    private static void Continue(MotionMode mode)
    {
        using var h=new Harness(mode);h.Send(TouchEventType.Down,0,0);h.Send(TouchEventType.Up,4,10);h.Advance(25);
        var schedule=h.Motion.Schedule;var pos=h.Motion.Position;int n=h.Moves.Count;
        h.Send(TouchEventType.Down,25,1000,session:2);Equal(schedule,h.Motion.Schedule,"same phase");Equal(pos,h.Motion.Position,"no liquidation");Equal(n,h.Moves.Count,"DOWN silent");
        h.Send(TouchEventType.Up,29,1010,session:2);h.Advance(250);Equal(180,h.Moves.Sum(v=>v.X),"both contacts earned");
    }
    private static void SameContinuousPosition()
    {
        using var a=new Harness(Modes[0]);using var b=new Harness(Modes[1]);using var c=new Harness(Modes[2]);
        Harness[] all=[a,b,c];foreach(var h in all)h.Send(TouchEventType.Down,0,0);
        for(int t=4;t<=100;t+=4)
        {
            foreach(var h in all)h.Send(TouchEventType.Move,t,t/4f);
            foreach(var h in all)Check(Math.Abs(h.Motion.Position.X-a.Motion.Position.X)<1e-8,"same continuous target at shared QPC, independent of segmentation");
        }
        foreach(var h in all){h.Send(TouchEventType.Up,101,25);h.Advance(300);Equal(225,h.Moves.Sum(m=>m.X),"same final integer endpoint");}
    }
    private static void ActualTime()
    {
        var kernels=Modes.Select(m=>new CausalFiniteCritical(m,Frequency)).ToArray();
        foreach(var k in kernels){k.Reset(Origin);k.Add(Origin,Origin+17321,0,0,17.321,0);Equal(120000L,k.SupportTicks,"support independent of output period");}
        var expected=kernels[0].Position(Origin+17321,17.321,0);
        foreach(var k in kernels)Equal(expected,k.Position(Origin+17321,17.321,0),"same actual non-grid evaluation time");
    }
}
