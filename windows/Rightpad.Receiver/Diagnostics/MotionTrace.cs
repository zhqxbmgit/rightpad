using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Rightpad.Receiver;

internal enum MotionEventKind
{
    Sample, Enqueue, Tick, Position, Logical, ManagedBegin, ManagedEnd, NativeBegin, NativeEnd,
    UpFlush, Fence, Reset, StarvationStart, StarvationEnd, DuplicateTimestamp, BackwardTimestamp, LateSample, BufferOverflow,
    BoxcarPosition, BoxcarUpPending, KernelPosition, KernelUpPending, KernelIntegration, KernelHistoryError,
    SettleStart, SettleComplete, SettleContinue
}

internal readonly record struct MotionTraceEvent(MotionEventKind Kind, long Qpc, long ReferenceQpc,
    ulong Run, uint Session, uint Sequence, ulong AndroidNs, double X, double Y, double A, double B, long Count);

// Optional experiment instrumentation. No file I/O or formatting on the input/ticker paths.
// Freeze stops recording first, then exports on its own low-frequency timer callback.
internal sealed class MotionTrace : IDisposable
{
    private readonly object gate = new();
    private readonly MotionTraceEvent[] events;
    private readonly string directory;
    private readonly System.Threading.Timer requests;
    private MotionMode mode;
    private long runtimeRunId;
    private long start, allocationStart;
    private TimeSpan cpuStart;
    private readonly int[] gcStart = new int[3];
    private int head, length;
    private long overwritten;
    private bool frozen;
    private Task? export;

    public MotionTrace(string directory, MotionMode mode, int capacity = 262144)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.directory = directory; this.mode = mode;
        Directory.CreateDirectory(directory);
        events = new MotionTraceEvent[capacity];
        ResetAccounting();
        requests = new(_ => { if (File.Exists(Path.Combine(directory, "freeze.request"))) Freeze(); }, null, 1000, 1000);
    }

    // A trace export is one Runtime run. Switching cadence starts a fresh segment so
    // events from different fixed configurations can never share ambiguous metadata.
    public void BeginRuntimeRun(long id, MotionMode activeMode)
    {
        lock (gate)
        {
            if (frozen) return;
            runtimeRunId = id;
            mode = activeMode;
            head = length = 0;
            overwritten = 0;
            ResetAccounting();
        }
    }

    private void ResetAccounting()
    {
        start = Stopwatch.GetTimestamp();
        allocationStart = GC.GetTotalAllocatedBytes();
        cpuStart = Process.GetCurrentProcess().TotalProcessorTime;
        gcStart[0] = GC.CollectionCount(0);
        gcStart[1] = GC.CollectionCount(1);
        gcStart[2] = GC.CollectionCount(2);
    }

    public void Write(MotionEventKind kind, long at = 0, long reference = 0, ulong run = 0,
        uint session = 0, uint sequence = 0, ulong android = 0, double x = 0, double y = 0,
        double a = 0, double b = 0, long count = 0)
    {
        if (Volatile.Read(ref frozen)) return;
        var item = new MotionTraceEvent(kind, at == 0 ? Stopwatch.GetTimestamp() : at, reference,
            run, session, sequence, android, x, y, a, b, count);
        lock (gate)
        {
            if (frozen) return;
            events[(head + length) % events.Length] = item;
            if (length == events.Length) { head = (head + 1) % events.Length; overwritten++; }
            else length++;
        }
    }

    public void Accepted(TouchPacket packet, long receivedAt)
    {
        foreach (var s in packet.Samples)
            Write(MotionEventKind.Sample, receivedAt, run: packet.Header.SenderRunId,
                session: packet.Header.SessionId, sequence: packet.Header.Sequence, android: s.TimestampNs,
                x: s.X, y: s.Y, count: (long)packet.Header.EventType);
    }

    public void Move(Action<int, int> output, int x, int y)
    {
        Write(MotionEventKind.ManagedBegin, x: x, y: y);
        try { output(x, y); }
        finally { Write(MotionEventKind.ManagedEnd, x: x, y: y); }
    }

    public void Freeze()
    {
        lock (gate)
        {
            if (frozen) return;
            frozen = true;
            long end = Stopwatch.GetTimestamp(), allocated = GC.GetTotalAllocatedBytes() - allocationStart;
            double cpuMs = (Process.GetCurrentProcess().TotalProcessorTime - cpuStart).TotalMilliseconds;
            int[] collections = [GC.CollectionCount(0) - gcStart[0], GC.CollectionCount(1) - gcStart[1], GC.CollectionCount(2) - gcStart[2]];
            MotionMode exportedMode = mode;
            long exportedRuntimeRunId = runtimeRunId, exportedStart = start;
            export = Task.Run(() =>
            {
                using var writer = new StreamWriter(Path.Combine(directory, "motion.csv"));
                writer.WriteLine("kind,qpc,referenceQpc,run,session,sequence,androidNs,x,y,a,b,count");
                for (int i = 0; i < length; i++)
                {
                    var e = events[(head + i) % events.Length];
                    writer.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"{e.Kind},{e.Qpc},{e.ReferenceQpc},{e.Run},{e.Session},{e.Sequence},{e.AndroidNs},{e.X:R},{e.Y:R},{e.A:R},{e.B:R},{e.Count}"));
                }
                File.WriteAllText(Path.Combine(directory, "metadata.json"), JsonSerializer.Serialize(new
                {
                    Mode = exportedMode.ToString(), RuntimeRunId = exportedRuntimeRunId,
                    Frequency = Stopwatch.Frequency, StartQpc = exportedStart, EndQpc = end,
                    Quantizer = MotionModes.QuantizerName(exportedMode),
                    Events = length, Overwritten = overwritten, AllocatedBytes = allocated, ProcessCpuMs = cpuMs,
                    PeriodMs = MotionModes.PeriodMs(exportedMode), PlayoutDelayMs = exportedMode == MotionMode.RAW ? 0 : 12,
                    BoxcarWindowMs = MotionModes.BoxcarWindowMs(exportedMode), Pid = Environment.ProcessId,
                    GcCollections = collections, LogicalProcessors = Environment.ProcessorCount,
                    TauMs = MotionModes.FiniteCriticalParameters(exportedMode).TauMs,
                    SupportMs = MotionModes.FiniteCriticalParameters(exportedMode).SupportMs,
                    KernelNormalization = MotionModes.FiniteCriticalParameters(exportedMode).Normalization,
                    Scope = "Managed and native-call boundaries; no VHF submit or hardware timestamp. CPU/allocation are whole-process including instrumentation."
                }, new JsonSerializerOptions { WriteIndented = true }));
            });
        }
    }

    public void Dispose()
    {
        requests.Dispose(); Freeze();
        Task? pending; lock (gate) pending = export;
        pending?.GetAwaiter().GetResult();
    }
}
