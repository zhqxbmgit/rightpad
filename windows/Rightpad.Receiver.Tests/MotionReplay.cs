using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Rightpad.Receiver.Tests;

// Explicit offline experiment entry. The same accepted packets and arrival times feed both implementations.
internal static class MotionReplay
{
    private sealed record Input(long At, TouchPacket Packet);
    public static void Run(string inputPath, string directory, double gain)
    {
        Directory.CreateDirectory(directory);
        bool actualReceive = File.ReadLines(inputPath).First().StartsWith("kind,");
        long frequency = actualReceive ? Stopwatch.Frequency : 1_000_000;
        var input = actualReceive ? ReadTrace(inputPath) : ReadTouchCsv(inputPath);
        foreach (var mode in new[] { MotionMode.RAW, MotionMode.RESAMPLED_250HZ })
        {
            long now = frequency * 10; uint session = 0; string cause = "packet";
            using var output = new StreamWriter(Path.Combine(directory, mode + "-output.csv"));
            using var contacts = new StreamWriter(Path.Combine(directory, mode + "-contacts.csv"));
            output.WriteLine("qpc,session,x,y,cause");
            contacts.WriteLine("session,endpointX,endpointY,sentX,sentY,inputPath,outputPath,upPendingX,upPendingY,maxBacklog,stopFromLastMoveMs");
            double sentX = 0, sentY = 0, outputPath = 0, inputLength = 0, maxBacklog = 0;
            double originX = 0, originY = 0, rawX = 0, rawY = 0; long lastOutput = 0, lastMove = 0;
            void Move(int x, int y)
            {
                sentX += x; sentY += y; outputPath += Math.Sqrt((double)x*x + (double)y*y); lastOutput = now;
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,$"{now},{session},{x},{y},{cause}"));
            }
            ITouchMotion motion = mode == MotionMode.RAW ? new TouchSessionProcessor(Move,gain,gain,()=>now)
                : new ResampledMotion(Move,gain,gain,()=>now,frequency);
            var b = motion as ResampledMotion;
            void TickUntil(long at)
            {
                while (b?.Schedule.Deadline is long next && next <= at)
                {
                    now = next; cause = "tick"; b.Tick(now,b.Schedule.Generation);
                    maxBacklog = Math.Max(maxBacklog, Math.Sqrt(Math.Pow((rawX-originX)*gain-sentX,2)+Math.Pow((rawY-originY)*gain-sentY,2)));
                }
            }
            foreach (var item in input)
            {
                TickUntil(item.At); now = item.At; cause = item.Packet.Header.EventType.ToString();
                var p = item.Packet; session = p.Header.SessionId;
                double beforeRawX = rawX, beforeRawY = rawY;
                if (p.Header.EventType == TouchEventType.Down)
                {
                    originX=rawX=p.Samples[0].X;originY=rawY=p.Samples[0].Y;
                    sentX=sentY=outputPath=inputLength=maxBacklog=0;lastOutput=lastMove=now;
                }
                else foreach(var s in p.Samples)
                {
                    double dx=(s.X-rawX)*gain,dy=(s.Y-rawY)*gain;
                    inputLength+=Math.Sqrt(dx*dx+dy*dy);
                    if(dx!=0||dy!=0)lastMove=now;
                    rawX=s.X;rawY=s.Y;
                }
                var pending = b?.Pending ?? (0.0, 0.0);
                // UP final coordinate is also owed; the core performs this same step before its fence.
                if(p.Header.EventType==TouchEventType.Up && b is not null)
                {
                    var up=p.Samples[0]; pending=(pending.Item1+(up.X-beforeRawX)*gain,pending.Item2+(up.Y-beforeRawY)*gain);
                }
                motion.ProcessAt(p,now,new RuntimeSettings(gain,gain,300,8,25));
                double ex=(rawX-originX)*gain,ey=(rawY-originY)*gain;
                maxBacklog=Math.Max(maxBacklog,Math.Sqrt(Math.Pow(ex-sentX,2)+Math.Pow(ey-sentY,2)));
                if(p.Header.EventType==TouchEventType.Up)
                    contacts.WriteLine(string.Create(CultureInfo.InvariantCulture,$"{session},{ex:R},{ey:R},{sentX},{sentY},{inputLength:R},{outputPath:R},{pending.Item1:R},{pending.Item2:R},{maxBacklog:R},{Math.Max(0,lastOutput-lastMove)*1000.0/frequency:R}"));
            }
            TickUntil(now+frequency*2);
            File.WriteAllText(Path.Combine(directory,mode+"-summary.json"),JsonSerializer.Serialize(new
            {
                Mode=mode.ToString(),Frequency=frequency,Packets=input.Count,Gain=gain,
                ArrivalModel=actualReceive?"Recorded Windows receive QPC":"MotionEvent current-sample time as arrival proxy; no measured callback/network delay",
                Scheduler="Ideal offline deadlines, not Windows scheduler measurements",
                motion.OutputEvents,motion.ProcessedMotionSamples,
                DuplicateTimestamp=b?.DuplicateTimestamps,BackwardTimestamp=b?.NonMonotonicTimestamps,LateSamples=b?.LateSamples,
                MissedTicks=b?.MissedTicks,TickOpportunities=b?.TickCount,Starvations=b?.Starvations,
                StarvationMs=b?.StarvationTicks*1000.0/frequency,UpFlushCount=b?.UpFlushCount,
                BufferOverflows=b?.BufferOverflows,AbortDiscardedDistance=b?.LifecycleAbortDiscardedDistance
            },new JsonSerializerOptions{WriteIndented=true}));
            (motion as IDisposable)?.Dispose();
        }
    }
    private static List<Input> ReadTouchCsv(string path)
    {
        var result=new List<Input>();var samples=new List<TouchSample>();uint seq=0;ulong? first=null;
        foreach(string line in File.ReadLines(path).Skip(1))
        {
            var c=line.Split(',');if(c.Length!=7)continue;
            if(!Enum.TryParse<TouchEventType>(c[2],true,out var type))continue;
            ulong time=ulong.Parse(c[6],CultureInfo.InvariantCulture);first??=time;
            samples.Add(new(time,float.Parse(c[4],CultureInfo.InvariantCulture),float.Parse(c[5],CultureInfo.InvariantCulture)));
            if(c[3]!="current")continue;
            long at=10_000_000+(long)((time-first.Value)/1000);
            result.Add(new(at,new(new(2,type,(ushort)samples.Count,uint.Parse(c[0]),seq++,1),samples.ToArray())));samples.Clear();
        }
        return result;
    }
    private static List<Input> ReadTrace(string path)
    {
        var groups=File.ReadLines(path).Skip(1).Select(l=>l.Split(',')).Where(c=>c[0]=="Sample")
            .GroupBy(c=>(At:long.Parse(c[1]),Run:ulong.Parse(c[3]),Session:uint.Parse(c[4]),Sequence:uint.Parse(c[5]),Type:(TouchEventType)int.Parse(c[11])));
        return groups.Select(g=>new Input(g.Key.At,new(new(2,g.Key.Type,(ushort)g.Count(),g.Key.Session,g.Key.Sequence,g.Key.Run),
            g.Select(c=>new TouchSample(ulong.Parse(c[6]),float.Parse(c[7],CultureInfo.InvariantCulture),float.Parse(c[8],CultureInfo.InvariantCulture))).ToArray()))).OrderBy(x=>x.At).ToList();
    }
}
