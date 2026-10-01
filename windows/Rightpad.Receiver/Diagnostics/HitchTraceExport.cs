using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Rightpad.Receiver;

internal sealed record HitchExportResult(string? Directory, string? Error);

internal static class HitchTraceExport
{
    internal readonly record struct Interval(string Series, long From, long To, double Milliseconds);

    internal static string DefaultRoot()
    {
        // Resolve from the executable, never the launcher's working directory.
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Rightpad.Receiver", "Rightpad.Receiver.csproj")))
                return Path.Combine(d.FullName, "test-results", "hitch");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "rightpad", "diagnostics", "hitch");
    }

    public static HitchExportResult Save(HitchSnapshot snapshot, string root)
    {
        string directory = Path.Combine(root, snapshot.FrozenUtc.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(directory);
            using (var csv = Writer(Path.Combine(directory, "trace.csv")))
            {
                csv.WriteLine("ordinal,kind,recordedQpc,atQpc,endQpc,androidTimestampNs,runtimeRun,senderRun,session,sequence,event,status,sampleCount,sampleIndex,dx,dy,queueDepth,deadlineQpc,generation,missedTicks");
                for (int i = 0; i < snapshot.Count; i++)
                {
                    var r = snapshot.Records[i];
                    csv.WriteLine(FormattableString.Invariant($"{snapshot.FirstOrdinal + i},{r.Kind},{r.RecordedAt},{r.At},{r.End},{r.AndroidNs},{r.RuntimeRun},{r.SenderRun},{r.Session},{r.Sequence},{r.Event},{r.Status},{r.SampleCount},{r.SampleIndex},{r.Dx},{r.Dy},{r.QueueDepth},{r.Deadline},{r.Generation},{r.Missed}"));
                }
            }
            var intervals = Intervals(snapshot);
            using (var gaps = Writer(Path.Combine(directory, "gaps.csv")))
            {
                gaps.WriteLine("series,fromOrdinal,toOrdinal,intervalMs,gt5ms,gt10ms,gt12ms,gt20ms,gt50ms");
                foreach (var v in intervals.Where(v => v.Milliseconds > 5))
                    gaps.WriteLine(FormattableString.Invariant($"{v.Series},{v.From},{v.To},{v.Milliseconds:R},{v.Milliseconds > 5},{v.Milliseconds > 10},{v.Milliseconds > 12},{v.Milliseconds > 20},{v.Milliseconds > 50}"));
            }
            File.WriteAllText(Path.Combine(directory, "summary.txt"), Summary(snapshot, intervals), new UTF8Encoding(false));
            return new(directory, null);
        }
        catch (Exception e) { return new(directory, e.Message); }
    }

    private static StreamWriter Writer(string path) => new(path, false, new UTF8Encoding(false));

    internal static List<Interval> Intervals(HitchSnapshot s)
    {
        var result = new List<Interval>();
        // Receiver ordering is retained. Do not sort Android timestamps and hide backwards samples.
        var samples = new Dictionary<(long, ulong, uint), (ulong Time, long Ordinal)>();
        var previous = new Dictionary<(string, long), (long Time, long Ordinal)>();
        for (int i = 0; i < s.Count; i++)
        {
            var r = s.Records[i];
            long ordinal = s.FirstOrdinal + i;
            if (r.Kind == HitchKind.Sample && r.Status == HitchStatus.Accepted)
            {
                var key = (r.RuntimeRun, r.SenderRun, r.Session);
                if (samples.TryGetValue(key, out var p) && r.AndroidNs >= p.Time)
                    result.Add(new("AndroidSample", p.Ordinal, ordinal, (r.AndroidNs - p.Time) / 1e6));
                samples[key] = (r.AndroidNs, ordinal);
            }
        }
        // Completion records may be appended after another thread's receive. Order by measured time.
        foreach (var item in s.Records.Take(s.Count).Select((r, i) => (Record: r, Ordinal: s.FirstOrdinal + i)).OrderBy(v => v.Record.At))
        {
            var r = item.Record;
            string? series = r.Kind switch
            {
                HitchKind.Receive => "WindowsUdpReceive",
                HitchKind.Tick => "PlayoutTick",
                HitchKind.Output => "MotionOutputOpportunity",
                HitchKind.NativeSubmit => "NativeSubmit",
                _ => null
            };
            if (series is null) continue;
            var key = (series, r.RuntimeRun);
            if (previous.TryGetValue(key, out var p))
                result.Add(new(series, p.Ordinal, item.Ordinal, (r.At - p.Time) * 1000.0 / s.Frequency));
            previous[key] = (r.At, item.Ordinal);
        }
        return result;
    }

    internal static string Summary(HitchSnapshot s, List<Interval> intervals)
    {
        var b = new StringBuilder();
        b.AppendLine("RIGHTPAD MANUAL HITCH FLIGHT RECORDER H1");
        b.AppendLine(FormattableString.Invariant($"Frozen UTC: {s.FrozenUtc:O}\nFrozen QPC: {s.FrozenAt}\nStopwatch frequency: {s.Frequency}\nRecords: {s.Count}\nRing capacity: {s.Capacity}\nRecord bytes: {Marshal.SizeOf<HitchRecord>()}\nLifetime capacity overwrites before snapshot: {s.Overwritten}"));
        double coverage = s.Count == 0 ? 0 : (s.FrozenAt - s.Records[0].RecordedAt) * 1000.0 / s.Frequency;
        b.AppendLine(FormattableString.Invariant($"Oldest retained record age ms: {coverage:R}\nRetention: last 60 seconds by Windows recording time, bounded by ring capacity."));
        b.AppendLine("At more than 4369 records/second sustained, capacity can shorten the 60-second window. Lifetime overwrites include normal rolling replacement; they do not by themselves indicate loss within the requested window.");
        b.AppendLine("AUTO HITCH DETECTION: NOT IMPLEMENTED\nMOTION TUNING: UNCHANGED");
        b.AppendLine("Android and Windows clocks are unsynchronized. NO Android-to-Windows one-way latency is computed.");
        b.AppendLine("AndroidSample: accepted samples only, within runtime/sender/session; duplicates remain zero, backwards intervals omitted (raw values remain in trace.csv). Rejected samples remain in trace.csv.");
        b.AppendLine("WindowsUdpReceive: all Touch-path datagrams, including heartbeat, malformed and rejected; B/X and config datagrams excluded.");
        b.AppendLine("Windows series do not cross Runtime runs. Idle/contact gaps ARE included; native submit occurs only for nonzero dx/dy. Gaps are observations, not root-cause findings.");
        b.AppendLine("PlayoutTick atQpc is actual observation, deadlineQpc is scheduled deadline; Output includes zero deltas. NativeSubmit atQpc/endQpc bracket the managed native adapter call, not game consumption or individual HID reports.");
        b.AppendLine("Snapshot ordinal is append order. recordedQpc defines retention; atQpc defines Windows intervals. Native status is API success/failure. Event 0 and unused fields mean not applicable; Invalid datagram identity fields are untrusted raw header bytes when present.");
        foreach (string series in new[] { "AndroidSample", "WindowsUdpReceive", "PlayoutTick", "MotionOutputOpportunity", "NativeSubmit" })
        {
            double[] values = intervals.Where(v => v.Series == series).Select(v => v.Milliseconds).Order().ToArray();
            b.AppendLine($"\n{series} interval (ms), n={values.Length}");
            if (values.Length == 0) b.AppendLine("p50=N/A p95=N/A p99=N/A max=N/A");
            else b.AppendLine(FormattableString.Invariant($"p50={Percentile(values, .50):R} p95={Percentile(values, .95):R} p99={Percentile(values, .99):R} max={values[^1]:R}"));
            foreach (int threshold in new[] { 5, 10, 12, 20, 50 })
                b.AppendLine($">{threshold}ms: {values.Count(v => v > threshold)}");
        }
        b.AppendLine("\nPercentiles: nearest rank. All intervals >5ms and threshold flags are listed in gaps.csv with trace ordinals.");
        return b.ToString();
    }

    private static double Percentile(double[] sorted, double fraction) => sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * fraction) - 1)];
}
