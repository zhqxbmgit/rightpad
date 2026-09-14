using System.Diagnostics;

namespace Rightpad.Receiver;

internal sealed class ResampledMotion : ITouchMotion, IDisposable
{
    public const int PeriodMs = 4, PlayoutDelayMs = 12, Capacity = 4096;
    private readonly object gate = new();
    private readonly Action<int, int> output;
    private readonly Func<long> now;
    private readonly long frequency, period, delay;
    private readonly double sensitivityX, sensitivityY;
    private readonly MotionTrace? trace;
    private readonly RawMotionProcessor quantizer = new(); // Already scaled Q: unit gain, original residual owner.
    private readonly record struct Point(long Time, double X, double Y);
    private readonly Point[] points = new Point[Capacity];
    private int head, count;
    private uint? session;
    private ulong run, androidOrigin, lastAndroid;
    private long origin, generation, nextDeadline, lastPointTime, starvationAt;
    private bool fallback, disposed;
    private double rawX, rawY, targetX, targetY, playedX, playedY;
    private long processed, ignored, outputEvents, lastOutput, totalX, totalY;
    public Action? Changed { get; set; }
    public long DuplicateTimestamps { get; private set; }
    public long NonMonotonicTimestamps { get; private set; }
    public long LateSamples { get; private set; }
    public long MissedTicks { get; private set; }
    public long TickCount { get; private set; }
    public long Starvations { get; private set; }
    public long StarvationTicks { get; private set; }
    public long UpFlushCount { get; private set; }
    public long BufferOverflows { get; private set; }
    public double LifecycleAbortDiscardedDistance { get; private set; }
    public uint? ActiveSessionId { get { lock (gate) return session; } }
    public long ProcessedMotionSamples => Interlocked.Read(ref processed);
    public long IgnoredSessionPackets => Interlocked.Read(ref ignored);
    public long OutputEvents => Interlocked.Read(ref outputEvents);
    public long LastOutputAtTicks => Interlocked.Read(ref lastOutput);
    public long TotalDx => Interlocked.Read(ref totalX);
    public long TotalDy => Interlocked.Read(ref totalY);
    public (long Generation, long? Deadline) Schedule { get { lock (gate) return (generation, nextDeadline == 0 ? null : nextDeadline); } }
    public (double X, double Y) Pending { get { lock (gate) return (targetX - playedX, targetY - playedY); } }

    public ResampledMotion(Action<int, int> output, double sensitivityX = 1, double sensitivityY = 1,
        Func<long>? monotonicNow = null, long? clockFrequency = null, MotionTrace? trace = null)
    {
        this.output = output; now = monotonicNow ?? Stopwatch.GetTimestamp;
        frequency = clockFrequency ?? Stopwatch.Frequency;
        if (frequency < 1000 || frequency % 250 != 0) throw new ArgumentOutOfRangeException(nameof(clockFrequency));
        _ = new RawMotionProcessor(sensitivityX, sensitivityY);
        this.sensitivityX = sensitivityX; this.sensitivityY = sensitivityY; this.trace = trace;
        period = frequency / 250; delay = period * 3;
    }

    public void Process(TouchPacket packet) => Process(packet, sensitivityX, sensitivityY);
    public void Process(TouchPacket packet, double sx, double sy) => Enqueue(packet, now(), sx, sy);
    public void ProcessAt(TouchPacket packet, long receivedAt, RuntimeSettings? settings) =>
        Enqueue(packet, receivedAt, settings?.SensitivityX ?? sensitivityX, settings?.SensitivityY ?? sensitivityY);

    private void Enqueue(TouchPacket packet, long receivedAt, double sx, double sy)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var h = packet.Header;
            if (h.EventType == TouchEventType.Down)
            {
                Clear(receivedAt, aborted: true);
                session = h.SessionId; run = h.SenderRunId;
                var s = packet.Samples[0]; androidOrigin = lastAndroid = s.TimestampNs;
                origin = receivedAt; lastPointTime = origin + delay;
                rawX = s.X; rawY = s.Y; Append(new(lastPointTime, 0, 0));
                return;
            }
            if (session != h.SessionId || run != h.SenderRunId)
            {
                if (session is not null) Clear(receivedAt, aborted: true);
                ignored++; return;
            }
            try
            {
                EndStarvation(receivedAt);
                foreach (var s in packet.Samples)
                {
                    targetX += ((double)s.X - rawX) * sx; targetY += ((double)s.Y - rawY) * sy;
                    rawX = s.X; rawY = s.Y; processed++;
                    if (!double.IsFinite(targetX) || !double.IsFinite(targetY)) throw new OverflowException("Motion target is not finite.");
                    long playTime = MapTime(s.TimestampNs, receivedAt);
                    if (playTime < receivedAt) { LateSamples++; trace?.Write(MotionEventKind.LateSample, receivedAt, playTime, run, h.SessionId); }
                    Append(new(playTime, targetX, targetY));
                    trace?.Write(MotionEventKind.Enqueue, now(), playTime, run, h.SessionId, h.Sequence, s.TimestampNs,
                        targetX, targetY, count: count);
                }
                if (h.EventType == TouchEventType.Up)
                {
                    // This local motion lock covers the final write too. Gesture runs only after return.
                    generation++; nextDeadline = 0;
                    UpFlushCount++;
                    trace?.Write(MotionEventKind.UpFlush, now(), run: run, session: h.SessionId,
                        x: targetX - playedX, y: targetY - playedY, count: generation);
                    Emit(targetX, targetY, now());
                    trace?.Write(MotionEventKind.Fence, now(), run: run, session: h.SessionId, count: generation);
                    Clear(now(), aborted: false);
                }
                else if (nextDeadline == 0)
                {
                    long baseTime = origin + delay;
                    nextDeadline = receivedAt < baseTime ? baseTime : baseTime + ((receivedAt - baseTime) / period + 1) * period;
                }
                Changed?.Invoke();
            }
            catch { Clear(now(), aborted: true); throw; }
        }
    }

    private long MapTime(ulong timestamp, long receivedAt)
    {
        if (timestamp == lastAndroid)
        {
            DuplicateTimestamps++;
            trace?.Write(MotionEventKind.DuplicateTimestamp, receivedAt, run: run, session: session!.Value);
            return lastPointTime;
        }
        if (timestamp < lastAndroid)
        {
            NonMonotonicTimestamps++; fallback = true;
            trace?.Write(MotionEventKind.BackwardTimestamp, receivedAt, run: run, session: session!.Value);
        }
        lastAndroid = timestamp;
        // An abnormal segment stays in arrival-order fallback until the next DOWN; no anchor chasing.
        // One second is a fixed malformed-timeline bound, not an adaptive delay or a motion cap.
        double offset = timestamp >= androidOrigin ? (timestamp - androidOrigin) * (frequency / 1e9) : -1;
        double mapped = origin + (double)delay + offset;
        if (offset < 0 || mapped > receivedAt + (double)frequency || mapped > long.MaxValue) fallback = true;
        long time = fallback ? receivedAt + delay : (long)Math.Round(mapped);
        lastPointTime = Math.Max(lastPointTime, time);
        return lastPointTime;
    }

    private Point At(int i) => points[(head + i) % Capacity];
    private void Append(Point point)
    {
        if (count > 0 && At(count - 1).Time == point.Time) { points[(head + count - 1) % Capacity] = point; return; }
        if (count == Capacity)
        {
            // Preserve the endpoint ledger under bounded overload; explicitly expose lost intermediate path.
            BufferOverflows++; trace?.Write(MotionEventKind.BufferOverflow, now(), count: BufferOverflows);
            head = (head + 1) % Capacity; count--;
        }
        points[(head + count++) % Capacity] = point;
    }

    public void Tick(long actualWake, long expectedGeneration)
    {
        lock (gate)
        {
            if (disposed || session is null || generation != expectedGeneration || nextDeadline == 0 || actualWake < nextDeadline) return;
            try
            {
                long deadline = nextDeadline, missed = (actualWake - deadline) / period;
                MissedTicks += missed; TickCount++;
                nextDeadline = deadline + (missed + 1) * period;
                trace?.Write(MotionEventKind.Tick, actualWake, deadline, run, session.Value, a: count,
                    b: (lastPointTime - actualWake) * 1000.0 / frequency, count: missed);
                while (count > 1 && At(1).Time <= actualWake) { head = (head + 1) % Capacity; count--; }
                Point p = At(0);
                double x = p.X, y = p.Y;
                if (actualWake < p.Time) { x = playedX; y = playedY; }
                else if (count > 1)
                {
                    var right = At(1);
                    double f = (actualWake - p.Time) / (double)(right.Time - p.Time);
                    x += (right.X - x) * f; y += (right.Y - y) * f;
                }
                trace?.Write(MotionEventKind.Position, actualWake, lastPointTime, run, session.Value,
                    x: x, y: y, a: targetX - x, b: targetY - y, count: generation);
                Emit(x, y, actualWake);
                if (count == 1 && actualWake >= p.Time)
                {
                    // No future segment: hold the known endpoint and park, never invent velocity.
                    nextDeadline = 0; starvationAt = actualWake; Starvations++;
                    trace?.Write(MotionEventKind.StarvationStart, actualWake, run: run, session: session.Value);
                }
            }
            catch { Clear(now(), aborted: true); throw; }
        }
    }

    private void Emit(double x, double y, long at)
    {
        var delta = quantizer.Process(x - playedX, y - playedY);
        playedX = x; playedY = y;
        trace?.Write(MotionEventKind.Logical, at, run: run, session: session!.Value,
            x: delta.X, y: delta.Y, a: quantizer.ResidualX, b: quantizer.ResidualY, count: generation);
        if (delta.X == 0 && delta.Y == 0) return;
        output(delta.X, delta.Y);
        outputEvents++; lastOutput = now(); totalX += delta.X; totalY += delta.Y;
    }

    private void EndStarvation(long at)
    {
        if (starvationAt == 0) return;
        long duration = Math.Max(0, at - starvationAt); StarvationTicks += duration;
        trace?.Write(MotionEventKind.StarvationEnd, at, starvationAt, run, session ?? 0, count: duration);
        starvationAt = 0;
    }

    private void Clear(long at, bool aborted)
    {
        EndStarvation(at);
        double distance = Math.Sqrt(Math.Pow(targetX - playedX, 2) + Math.Pow(targetY - playedY, 2));
        if (aborted) LifecycleAbortDiscardedDistance += distance;
        trace?.Write(MotionEventKind.Reset, at, run: run, session: session ?? 0, x: aborted ? distance : 0, count: generation);
        generation++; session = null; head = count = 0; nextDeadline = 0; fallback = false;
        rawX = rawY = targetX = targetY = playedX = playedY = 0; quantizer.Reset();
        Changed?.Invoke();
    }
    public void Reset() { lock (gate) Clear(now(), aborted: true); }
    public void Dispose() { lock (gate) { if (disposed) return; Clear(now(), aborted: true); disposed = true; } }
}
