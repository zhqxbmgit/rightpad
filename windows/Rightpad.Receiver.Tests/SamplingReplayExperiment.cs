using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rightpad.Receiver.Tests;

// Explicit offline entry only. No Runtime, sockets, native devices or real MotionClock.
internal static class SamplingReplayExperiment
{
    private const long Frequency = 1_000_000_000, Origin = 10_000_000_000, Ms = 1_000_000;
    private const double Gain = 12;
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;
    private sealed record P(long Time, float X, float Y, TouchEventType Action);
    private sealed record Row(double X, double Y, long Ix, long Iy, int Dx, int Dy);
    private sealed record Replay(Row[] Rows, long Late, long Missed, long Overflow, long Processed, long Outputs, double EndpointResidual);
    private sealed record Distribution(double Rms, double P95, double Max);
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    public static void Run(string evidence)
    {
        Check(Directory.Exists(evidence), "Create a fresh task evidence directory first.");
        Check(!File.Exists(Path.Combine(evidence,"replay-summary.json")), "Do not overwrite evidence.");
        SelfCheck();
        var inputRoot = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(evidence)!)!,
            "20261104-sampling-stability-r1", "evidence");
        var summaries = new List<object>();
        foreach (string name in new[] { "on-circles", "on-slow-pause", "on-retest" })
        {
            string input = Path.Combine(inputRoot,name,"getevent.txt");
            var all = Parse(File.ReadLines(input));
            string destination = Path.Combine(evidence,name); Directory.CreateDirectory(destination);
            using (var writer = new StreamWriter(Path.Combine(destination,"input.csv")))
            {
                writer.WriteLine("time_ms,x_assumed_px,y_assumed_px,action");
                foreach(var p in all) writer.WriteLine(FormattableString.Invariant($"{p.Time/1e6:R},{p.X:R},{p.Y:R},{p.Action}"));
            }
            var variants = new[] { ("full",all), ("half-even",Thin(all,0)), ("half-odd",Thin(all,1)) };
            foreach(int burst in new[]{0,8,16})
            {
                var reference = Simulate(all,burst);
                var oracleError = OracleError(all,reference,burst);
                Check(oracleError < 1e-5, $"Independent causal interpolation oracle differs: {name}/{burst}: {oracleError}");
                foreach(var (variant,points) in variants)
                {
                    var replay = variant=="full" ? reference : Simulate(points,burst);
                    Check(replay.Overflow==0 && replay.Missed==0, "Unexpected offline capacity/cadence failure");
                    Check(replay.EndpointResidual < 1.000001, "Final real endpoint not quantized within one count per axis");
                    Check(replay.Processed==points.Length-1, "Every MOVE/UP must enter production sample loop");
                    var position=Stats(replay.Rows.Zip(reference.Rows,(a,b)=>Distance(a.X-b.X,a.Y-b.Y)));
                    var emitted=Stats(replay.Rows.Zip(reference.Rows,(a,b)=>Distance(a.Ix-b.Ix,a.Iy-b.Iy)));
                    var delta=Stats(replay.Rows.Zip(reference.Rows,(a,b)=>Distance(a.Dx-b.Dx,a.Dy-b.Dy)));
                    int changed=replay.Rows.Zip(reference.Rows,(a,b)=>a.Dx!=b.Dx||a.Dy!=b.Dy).Count(x=>x);
                    string stem=$"{variant}-burst{burst}";
                    using(var writer=new StreamWriter(Path.Combine(destination,stem+".csv")))
                    {
                        writer.WriteLine("time_ms,reconstructed_x_counts,reconstructed_y_counts,emitted_x_counts,emitted_y_counts,delta_x,delta_y");
                        for(int t=0;t<replay.Rows.Length;t++) { var r=replay.Rows[t];writer.WriteLine(FormattableString.Invariant($"{t},{r.X:R},{r.Y:R},{r.Ix},{r.Iy},{r.Dx},{r.Dy}")); }
                    }
                    var summary=new { trace=name,variant,burstMs=burst,samples=points.Length,fullSamples=all.Length,
                        spanMs=all[^1].Time/1e6,gainX=Gain,gainY=Gain,rawCoordinateScale=.01,
                        reconstructionDelayMs=8,tickMs=1,late=replay.Late,missed=replay.Missed,overflow=replay.Overflow,
                        processed=replay.Processed,nonzeroOutputs=replay.Outputs,endpointResidual=replay.EndpointResidual,
                        positionVsFullCounts=position,emittedPositionVsFullCounts=emitted,deltaVsFullCounts=delta,
                        changedOutputTickPercent=100.0*changed/replay.Rows.Length,
                        finalX=replay.Rows[^1].Ix,finalY=replay.Rows[^1].Iy,independentOracleMaxErrorCounts=oracleError,
                        inputSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))) };
                    summaries.Add(summary);
                    Console.WriteLine(FormattableString.Invariant($"{name} {stem}: n={points.Length} posRMS={position.Rms:F4} count posMax={position.Max:F4} changed={100.0*changed/replay.Rows.Length:F2}% late={replay.Late}"));
                }
            }
        }
        File.WriteAllText(Path.Combine(evidence,"replay-summary.json"),JsonSerializer.Serialize(new {
            createdUtc=DateTimeOffset.UtcNow,selfChecks="PASS: parsing, thinning phases, straight line equivalence, reversal sensitivity, production/independent causal oracle",
            assumptions=new[]{"Driver XY scaled by 0.01 to approximate portrait screen pixels; not a recorded MotionEvent stream.",
                "Fixed sensitivity 12/12, M-R1 M8 Q0-C, ideal 1ms scheduler; fake output only.",
                "Arrival=sample time for burst0; ceil(sample/burst)*burst otherwise, preserving DOWN at zero.",
                "8/16ms bursts are synthetic arrival models, not measured network jitter.",
                "Full recorded causal polyline is comparison reference, not physical finger ground truth.",
                "Both thinning phases retain DOWN and UP, including final real coordinates; all recorded gaps retained."},results=summaries
        },new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS offline replay completed without native output or device access.");
    }

    private static P[] Parse(IEnumerable<string> lines)
    {
        var output=new List<P>(); bool active=false,down=false,up=false,changed=false; int x=0,y=0,slot=0,contacts=0;
        foreach(string line in lines)
        {
            var m=Regex.Match(line,@"^\[\s*([0-9.]+)\]\s+(EV_\w+)\s+(\w+)\s+(\S+)");
            if(!m.Success)continue;
            long time=checked((long)(decimal.Parse(m.Groups[1].Value,C)*1_000_000_000m));
            string code=m.Groups[3].Value,value=m.Groups[4].Value;
            Check(code!="SYN_DROPPED","Cannot replay an overflowed input stream");
            if(code=="ABS_MT_SLOT")slot=Convert.ToInt32(value,16);
            else if(code=="ABS_MT_TRACKING_ID")
            {
                if(value=="ffffffff"){Check(active&&contacts==1,"Unmatched UP");active=false;contacts--;up=true;}
                else{Check(!active&&contacts==0&&output.Count==0,"Only one complete single-finger contact per trace is supported");active=true;contacts++;down=true;}
            }
            else if(code is "ABS_MT_POSITION_X" or "ABS_MT_POSITION_Y")
            {
                Check(slot==0,"Unexpected contact slot");
                if(code=="ABS_MT_POSITION_X")x=Convert.ToInt32(value,16);else y=Convert.ToInt32(value,16);
                changed=true;
            }
            else if(code=="SYN_REPORT")
            {
                if(down||up||(active&&changed))output.Add(new(time,x/100f,y/100f,down?TouchEventType.Down:up?TouchEventType.Up:TouchEventType.Move));
                down=up=changed=false;
            }
        }
        Check(output.Count>=4&&output[0].Action==TouchEventType.Down&&output[^1].Action==TouchEventType.Up&&!active,"Incomplete contact");
        long origin=output[0].Time;
        var result=output.Select(p=>p with{Time=p.Time-origin}).ToArray();
        Check(result.Zip(result.Skip(1),(a,b)=>b.Time>=a.Time).All(x=>x),"Backward timestamps");
        return result;
    }
    private static P[] Thin(P[] all,int phase)=>all.Where((p,i)=>p.Action!=TouchEventType.Move||(i-1)%2==phase).ToArray();
    private static long Arrival(P p,int burst)=>burst==0?p.Time:((p.Time+burst*Ms-1)/(burst*Ms))*(burst*Ms);
    private static Replay Simulate(P[] points,int burst)
    {
        long now=Origin,ix=0,iy=0,oldIx=0,oldIy=0;
        using var motion=new ResampledMotion((x,y)=>{ix+=x;iy+=y;},Gain,Gain,()=>now,Frequency,
            finiteCriticalMode:MotionMode.M_R1,configuration:new(MotionMode.M_R1,24,120),liveSensitivity:new(Gain,Gain));
        int end=checked((int)Math.Ceiling((Arrival(points[^1],burst)+10*Ms)/(double)Ms));
        var rows=new Row[end+1];int next=0;uint sequence=0;
        double targetX=((double)points[^1].X-points[0].X)*Gain,targetY=((double)points[^1].Y-points[0].Y)*Gain;
        for(int t=0;t<=end;t++)
        {
            long tick=t*Ms;
            while(next<points.Length&&Arrival(points[next],burst)<=tick)
            {
                var p=points[next++];now=Origin+Arrival(p,burst);
                motion.Process(new(new(2,p.Action,1,1,sequence++,1),[new((ulong)p.Time,p.X,p.Y)]));
            }
            now=Origin+tick;
            if(motion.Schedule.Deadline is long d&&d<=now)motion.Tick(now,motion.Schedule.Generation);
            var pos=motion.Position;
            if(next==points.Length&&motion.Schedule.Deadline is null)pos=(targetX,targetY);
            rows[t]=new(pos.X,pos.Y,ix,iy,checked((int)(ix-oldIx)),checked((int)(iy-oldIy)));oldIx=ix;oldIy=iy;
        }
        Check(motion.Schedule.Deadline is null,"UP reconstruction failed to finish");
        return new(rows,motion.LateSamples,motion.MissedTicks,motion.BufferOverflows,motion.ProcessedMotionSamples,
            motion.OutputEvents,Math.Max(Math.Abs(targetX-ix),Math.Abs(targetY-iy)));
    }
    private static double OracleError(P[] points,Replay replay,int burst)
    {
        int admitted=0,left=0;double max=0,x=0,y=0;long? deadline=null;
        for(int t=0;t<replay.Rows.Length;t++)
        {
            while(admitted<points.Length&&Arrival(points[admitted],burst)<=t*Ms)
            {
                var point=points[admitted++];long arrival=Arrival(point,burst);
                if(point.Action!=TouchEventType.Down&&deadline is null)
                    deadline=arrival<8*Ms?8*Ms:(arrival/Ms+1)*Ms;
            }
            long time=(t-8)*Ms;
            if(deadline is long due&&t*Ms>=due&&time>=0&&admitted>0)
            {
                while(left+1<admitted&&points[left+1].Time<=time)left++;
                var a=points[left];x=a.X;y=a.Y;
                if(left+1<admitted&&time>=a.Time){var b=points[left+1];double f=(time-a.Time)/(double)(b.Time-a.Time);x+=((double)b.X-a.X)*f;y+=((double)b.Y-a.Y)*f;}
                x=(x-points[0].X)*Gain;y=(y-points[0].Y)*Gain;
                deadline=points[admitted-1].Time+8*Ms<=t*Ms?null:(t+1)*Ms;
            }
            var r=replay.Rows[t];max=Math.Max(max,Distance(r.X-x,r.Y-y));
        }
        return max;
    }
    private static double Distance(double x,double y)=>Math.Sqrt(x*x+y*y);
    private static Distribution Stats(IEnumerable<double> input)
    {var v=input.Order().ToArray();return new(Math.Sqrt(v.Sum(x=>x*x)/v.Length),v[(int)Math.Ceiling(.95*v.Length)-1],v[^1]);}
    private static void SelfCheck()
    {
        var line=Enumerable.Range(0,6).Select(i=>new P(i*4*Ms,i*4,0,i==0?TouchEventType.Down:i==5?TouchEventType.Up:TouchEventType.Move)).ToArray();
        var full=Simulate(line,0);
        foreach(int phase in new[]{0,1})
        {
            var half=Thin(line,phase);Check(half[0]==line[0]&&half[^1]==line[^1]&&half.Length<line.Length,"Thinning boundaries");
            Check(Simulate(half,0).Rows.SequenceEqual(full.Rows),"Collinear extra points must not change output");
        }
        var reversal=new[]{new P(0,0,0,TouchEventType.Down),new P(4*Ms,4,0,TouchEventType.Move),new P(8*Ms,0,0,TouchEventType.Move),new P(12*Ms,4,0,TouchEventType.Move),new P(16*Ms,0,0,TouchEventType.Up)};
        Check(!Simulate(reversal,0).Rows.SequenceEqual(Simulate(Thin(reversal,1),0).Rows),"Dropping a turn must be observable");
        var fixture=new[]{"[ 1.000000] EV_ABS ABS_MT_TRACKING_ID 00000001","[ 1.000000] EV_ABS ABS_MT_POSITION_X 00000064","[ 1.000000] EV_ABS ABS_MT_POSITION_Y 000000c8","[ 1.000000] EV_SYN SYN_REPORT 00000000","[ 1.004000] EV_ABS ABS_MT_POSITION_X 000000c8","[ 1.004000] EV_SYN SYN_REPORT 00000000","[ 1.008000] EV_ABS ABS_MT_POSITION_Y 0000012c","[ 1.008000] EV_SYN SYN_REPORT 00000000","[ 1.012000] EV_ABS ABS_MT_TRACKING_ID ffffffff","[ 1.012000] EV_SYN SYN_REPORT 00000000"};
        var parsed=Parse(fixture);Check(parsed[0].X==1&&parsed[0].Y==2&&parsed[^1].X==2&&parsed[^1].Y==3&&parsed[^1].Time==12*Ms,"Raw parsing/scaling/held axes/final UP");
        Check(OracleError(reversal,Simulate(reversal,0),0)<1e-9,"Independent oracle reversal");
        Console.WriteLine("PASS offline replay self checks");
    }
}
