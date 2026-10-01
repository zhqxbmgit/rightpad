using System.Diagnostics;

namespace Rightpad.Receiver;

internal enum HitchKind : byte { Receive, Sample, Tick, Output, NativeSubmit }
internal enum HitchStatus : byte { None, Accepted, Invalid, RetiredRun, UnknownRun, Duplicate, Old, Success, Failure }

// Scalars only: no strings, object references, formatting or allocation per record.
internal readonly record struct HitchRecord(
    HitchKind Kind, long At, long End = 0, ulong AndroidNs = 0, long RuntimeRun = 0,
    ulong SenderRun = 0, uint Session = 0, uint Sequence = 0, TouchEventType Event = 0,
    HitchStatus Status = HitchStatus.None, int SampleCount = 0, int SampleIndex = 0,
    int Dx = 0, int Dy = 0, int QueueDepth = 0, long Deadline = 0, long Generation = 0,
    long Missed = 0, long RecordedAt = 0);

internal sealed record HitchSnapshot(HitchRecord[] Records, int Count, long FirstOrdinal,
    long FrozenAt, long Frequency, int Capacity, long Overwritten, DateTimeOffset FrozenUtc);

internal sealed class HitchTraceRecorder
{
    public const int DefaultCapacity = 262144;
    public const int RetentionSeconds = 60;
    private const int CopyBatch = 64;
    private readonly object gate = new();
    private readonly HitchRecord[] ring;
    private readonly Func<long> now;
    private readonly long frequency;
    private long written;
    private Capture? capture;
    private int exporting;
    public int Capacity => ring.Length;

    internal sealed class Capture(HitchRecord[] records, long first, int count, long at, DateTimeOffset utc)
    {
        public readonly HitchRecord[] Records = records;
        public readonly long First = first, At = at;
        public readonly int Count = count;
        public readonly DateTimeOffset Utc = utc;
        public int Copied;
    }

    public HitchTraceRecorder(int capacity = DefaultCapacity, Func<long>? clock = null, long? clockFrequency = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        frequency = clockFrequency ?? Stopwatch.Frequency;
        if (frequency <= 0 || frequency > long.MaxValue / RetentionSeconds) throw new ArgumentOutOfRangeException(nameof(clockFrequency));
        ring = new HitchRecord[capacity];
        now = clock ?? Stopwatch.GetTimestamp;
    }

    public void Write(in HitchRecord record)
    {
        lock (gate)
        {
            // A manual snapshot owns the old value even if its export thread is descheduled.
            // Preserve at most one overwritten slot; the writer never copies a whole trace.
            var c = capture;
            if (c is not null && c.Copied < c.Count && written - ring.Length == c.First + c.Copied)
                CopyNext(c);
            ring[(int)(written % ring.Length)] = record with { RecordedAt = now() };
            written++;
        }
    }

    public void Packet(TouchPacket packet, long receivedAt, long runtimeRun, HitchStatus status)
    {
        var h = packet.Header;
        Write(new(HitchKind.Receive, receivedAt, RuntimeRun: runtimeRun, SenderRun: h.SenderRunId,
            Session: h.SessionId, Sequence: h.Sequence, Event: h.EventType, Status: status, SampleCount: h.SampleCount));
        for (int i = 0; i < packet.Samples.Length; i++)
            Write(new(HitchKind.Sample, receivedAt, AndroidNs: packet.Samples[i].TimestampNs,
                RuntimeRun: runtimeRun, SenderRun: h.SenderRunId, Session: h.SessionId,
                Sequence: h.Sequence, Event: h.EventType, Status: status, SampleCount: h.SampleCount, SampleIndex: i));
    }

    // Allocation happens before the linearization point. No input lock or Motion state is touched.
    internal Capture BeginSnapshot()
    {
        var records = new HitchRecord[ring.Length];
        lock (gate)
        {
            if (capture is not null) throw new InvalidOperationException("Hitch snapshot already in progress.");
            int count = (int)Math.Min(written, ring.Length);
            return capture = new(records, written - count, count, now(), DateTimeOffset.UtcNow);
        }
    }

    private void CopyNext(Capture c)
    {
        c.Records[c.Copied] = ring[(int)((c.First + c.Copied) % ring.Length)];
        c.Copied++;
    }

    internal HitchSnapshot CompleteSnapshot(Capture c)
    {
        try
        {
            while (true)
            {
                lock (gate)
                {
                    if (!ReferenceEquals(capture, c)) throw new InvalidOperationException("Unknown hitch snapshot.");
                    int end = Math.Min(c.Count, c.Copied + CopyBatch);
                    while (c.Copied < end) CopyNext(c);
                    if (c.Copied == c.Count) break;
                }
            }
            // Retention uses Windows recording time only, never an Android-to-Windows subtraction.
            long cutoff = c.At - frequency * RetentionSeconds;
            int first = 0;
            while (first < c.Count && c.Records[first].RecordedAt < cutoff) first++;
            int count = c.Count - first;
            Array.Copy(c.Records, first, c.Records, 0, count);
            Array.Clear(c.Records, count, c.Records.Length - count);
            return new(c.Records, count, c.First + first, c.At, frequency, ring.Length,
                Math.Max(0, c.First), c.Utc);
        }
        finally { lock (gate) { if (ReferenceEquals(capture, c)) capture = null; } }
    }

    internal HitchSnapshot Snapshot() => CompleteSnapshot(BeginSnapshot());

    // Only the manual UI action calls this. There is no timer, threshold or file watcher.
    public async Task<HitchExportResult> FreezeAsync(string? root = null)
    {
        if (Interlocked.CompareExchange(ref exporting, 1, 0) != 0)
            return new(null, "A hitch export is already in progress.");
        try
        {
            return await Task.Run(() =>
            {
                try { return HitchTraceExport.Save(Snapshot(), root ?? HitchTraceExport.DefaultRoot()); }
                catch (Exception e) { return new HitchExportResult(null, e.Message); }
            }).ConfigureAwait(false);
        }
        finally { Volatile.Write(ref exporting, 0); }
    }
}
