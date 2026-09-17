using System.Diagnostics;

namespace Rightpad.Receiver;

internal sealed class ResampledMotion : ITouchMotion, IDisposable
{
    public const int PlayoutDelayMs = 12, Capacity = 4096;
    public int PeriodMs { get; }
    private readonly object gate = new();
    private readonly Action<int, int> output;
    private readonly Func<long> now;
    private readonly long frequency, period, delay;
    private readonly double sensitivityX, sensitivityY;
    private readonly MotionTrace? trace;
    private readonly CausalBoxcar? boxcar;
    private readonly CausalFiniteCritical? finiteCritical;
    private readonly bool earnedSettle;
    private bool HasPositionFilter => boxcar is not null || finiteCritical is not null;
    private long SupportTicks => boxcar?.WindowTicks ?? finiteCritical!.SupportTicks;
    private long integratedAt;
    private double baseX, baseY, integratedX, integratedY;
    private readonly RawMotionProcessor quantizer = new(); // Already scaled Q: unit gain, original residual owner.
    private readonly CanonicalPositionQuantizer? canonicalQuantizer;
    private readonly record struct Point(long Time, double X, double Y);
    private readonly Point[] points = new Point[Capacity];
    private int head, count;
    private uint? session;
    private ulong run, androidOrigin, lastAndroid;
    private long origin, cadenceOrigin, generation, nextDeadline, lastPointTime, starvationAt;
    private bool fallback, disposed, settling;
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
    public uint? ActiveSessionId { get { lock (gate) return settling ? null : session; } }
    public long ProcessedMotionSamples => Interlocked.Read(ref processed);
    public long IgnoredSessionPackets => Interlocked.Read(ref ignored);
    public long OutputEvents => Interlocked.Read(ref outputEvents);
    public long LastOutputAtTicks => Interlocked.Read(ref lastOutput);
    public long TotalDx => Interlocked.Read(ref totalX);
    public long TotalDy => Interlocked.Read(ref totalY);
    public (long Generation, long? Deadline) Schedule { get { lock (gate) return (generation, nextDeadline == 0 ? null : nextDeadline); } }
    public (double X, double Y) Pending { get { lock (gate) return (targetX - playedX, targetY - playedY); } }
    public (double X, double Y) Position { get { lock (gate) return (playedX, playedY); } }
    public (double X, double Y) BasePending { get { lock (gate) return (targetX - baseX, targetY - baseY); } }
    public (double X, double Y) BoxcarPending { get { lock (gate) return (baseX - playedX, baseY - playedY); } }
    public int BoxcarWindowMs => boxcar?.WindowMs ?? 0;
    public (double X, double Y) KernelPending { get { lock (gate) return (baseX - playedX, baseY - playedY); } }
    public int KernelTauMs => finiteCritical?.TauMs ?? 0;
    public int KernelSupportMs => finiteCritical?.SupportMs ?? 0;
    public int KernelSegmentsIntegrated => finiteCritical?.LastIntegratedSegments ?? 0;

    public ResampledMotion(Action<int, int> output, double sensitivityX = 1, double sensitivityY = 1,
        Func<long>? monotonicNow = null, long? clockFrequency = null, MotionTrace? trace = null, int boxcarWindowMs = 0,
        MotionMode finiteCriticalMode = MotionMode.RESAMPLED_250HZ)
    {
        this.output = output; now = monotonicNow ?? Stopwatch.GetTimestamp;
        frequency = clockFrequency ?? Stopwatch.Frequency;
        PeriodMs = MotionModes.PeriodMs(finiteCriticalMode);
        if (PeriodMs == 0 || frequency < 1000 || frequency % (1000 / PeriodMs) != 0)
            throw new ArgumentOutOfRangeException(nameof(clockFrequency));
        _ = new RawMotionProcessor(sensitivityX, sensitivityY);
        this.sensitivityX = sensitivityX; this.sensitivityY = sensitivityY; this.trace = trace;
        earnedSettle = MotionModes.IsEarnedSettle(finiteCriticalMode);
        period = frequency / (1000 / PeriodMs);
        // Playout is a fixed duration, independent of the selected output cadence.
        delay = checked(frequency * PlayoutDelayMs) / 1000;
        boxcar = boxcarWindowMs == 0 ? null : new CausalBoxcar(boxcarWindowMs, frequency);
        if (finiteCriticalMode != MotionMode.RESAMPLED_250HZ)
        {
            if (boxcar is not null) throw new ArgumentException("Position filters cannot be combined.");
            finiteCritical = new CausalFiniteCritical(finiteCriticalMode, frequency);
            canonicalQuantizer = new();
        }
    }

    public void Process(TouchPacket packet) => Process(packet, sensitivityX, sensitivityY);
    public void Process(TouchPacket packet, double sx, double sy) => Enqueue(packet, now(), sx, sy);
    public void ProcessAt(TouchPacket packet, long receivedAt, RuntimeSettings? settings) =>
        Enqueue(packet, receivedAt, settings?.SensitivityX ?? sensitivityX, settings?.SensitivityY ?? sensitivityY);

    private void Enqueue(TouchPacket packet, long receivedAt, double sx, double sy)
    {
        // Each finite-kernel experiment keeps its startup sensitivity for the whole run.
        if (finiteCritical is not null) { sx = sensitivityX; sy = sensitivityY; }
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var h = packet.Header;
            if (h.EventType == TouchEventType.Down)
            {
                if (earnedSettle && settling && run == h.SenderRunId)
                {
                    try
                    {
                        // Continue the same earned ledger and realized history. DOWN itself
                        // creates no displacement, emits nothing and keeps the clock phase.
                        AdvanceHistory(receivedAt);
                        EndStarvation(receivedAt);
                        session = h.SessionId; settling = false; fallback = false;
                        var first = packet.Samples[0];
                        androidOrigin = lastAndroid = first.TimestampNs; origin = receivedAt;
                        rawX = first.X; rawY = first.Y;
                        lastPointTime = Math.Max(lastPointTime, origin + delay);
                        Append(new(lastPointTime, targetX, targetY));
                        trace?.Write(MotionEventKind.SettleContinue, receivedAt, run: run, session: h.SessionId,
                            x: targetX, y: targetY, a: playedX, b: playedY, count: generation);
                        Changed?.Invoke();
                        return;
                    }
                    catch { Clear(now(), aborted: true); throw; }
                }
                Clear(receivedAt, aborted: true);
                session = h.SessionId; run = h.SenderRunId;
                var s = packet.Samples[0]; androidOrigin = lastAndroid = s.TimestampNs;
                origin = cadenceOrigin = receivedAt; lastPointTime = origin + delay;
                rawX = s.X; rawY = s.Y; Append(new(lastPointTime, 0, 0));
                return;
            }
            // The UP endpoint is frozen. A late MOVE/UP cannot add to a released contact.
            if (settling) { ignored++; return; }
            if (session != h.SessionId || run != h.SenderRunId)
            {
                if (session is not null) Clear(receivedAt, aborted: true);
                ignored++; return;
            }
            try
            {
                EndStarvation(receivedAt);
                if (HasPositionFilter) AdvanceHistory(receivedAt);
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
                    if (earnedSettle)
                    {
                        settling = true;
                        trace?.Write(MotionEventKind.SettleStart, receivedAt, lastPointTime, run, h.SessionId,
                            x: targetX, y: targetY, a: targetX - playedX, b: targetY - playedY, count: generation);
                        ArmClock(receivedAt);
                        Changed?.Invoke();
                        return;
                    }
                    // This local motion lock covers the final write too. Gesture runs only after return.
                    generation++; nextDeadline = 0;
                    UpFlushCount++;
                    trace?.Write(MotionEventKind.UpFlush, now(), run: run, session: h.SessionId,
                        x: targetX - playedX, y: targetY - playedY,
                        a: targetX - baseX, b: targetY - baseY, count: generation);
                    if (boxcar is not null)
                        trace?.Write(MotionEventKind.BoxcarUpPending, now(), run: run, session: h.SessionId,
                            x: baseX - playedX, y: baseY - playedY, count: generation);
                    if (finiteCritical is not null)
                        trace?.Write(MotionEventKind.KernelUpPending, now(), run: run, session: h.SessionId,
                            x: baseX - playedX, y: baseY - playedY, count: generation);
                    Emit(targetX, targetY, now());
                    trace?.Write(MotionEventKind.Fence, now(), run: run, session: h.SessionId, count: generation);
                    Clear(now(), aborted: false);
                }
                else if (nextDeadline == 0)
                {
                    ArmClock(receivedAt);
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

    private void AdvanceHistory(long at)
    {
        // Admit packets only after recording the trajectory that was actually known before them.
        // A packet waiting on the motion gate cannot retroactively rewrite this history.
        if (at <= integratedAt) return;
        long cursor = Math.Max(integratedAt, at - SupportTicks);
        boxcar?.Trim(at - SupportTicks); finiteCritical?.Trim(at - SupportTicks);
        while (cursor < at)
        {
            while (count > 1 && At(1).Time <= cursor) { head = (head + 1) % Capacity; count--; }
            Point p = At(0);
            long end = at;
            double x0, y0, x1, y1;
            if (cursor < p.Time)
            {
                end = Math.Min(at, p.Time);
                x0 = x1 = integratedX; y0 = y1 = integratedY;
            }
            else if (count == 1) { x0 = x1 = p.X; y0 = y1 = p.Y; }
            else
            {
                var right = At(1); end = Math.Min(at, right.Time);
                double f0 = (cursor - p.Time) / (double)(right.Time - p.Time);
                double f1 = (end - p.Time) / (double)(right.Time - p.Time);
                x0 = p.X + (right.X - p.X) * f0; y0 = p.Y + (right.Y - p.Y) * f0;
                x1 = p.X + (right.X - p.X) * f1; y1 = p.Y + (right.Y - p.Y) * f1;
            }
            boxcar?.Add(cursor, end, x0, y0, x1, y1);
            if (finiteCritical is not null)
            {
                try { finiteCritical.Add(cursor, end, x0, y0, x1, y1); }
                catch
                {
                    trace?.Write(MotionEventKind.KernelHistoryError, at, run: run, session: session ?? 0,
                        count: finiteCritical.SegmentCount);
                    throw;
                }
            }
            cursor = end; integratedX = x1; integratedY = y1;
        }
        integratedAt = at;
    }

    private void ArmClock(long receivedAt)
    {
        if (nextDeadline != 0) return;
        long baseTime = cadenceOrigin + delay;
        nextDeadline = receivedAt < baseTime ? baseTime : baseTime + ((receivedAt - baseTime) / period + 1) * period;
    }
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
                // A wake sampled before another gate owner ran must not rewind F's causal history.
                if (HasPositionFilter) actualWake = Math.Max(actualWake, integratedAt);
                long deadline = nextDeadline, missed = (actualWake - deadline) / period;
                MissedTicks += missed; TickCount++;
                nextDeadline = deadline + (missed + 1) * period;
                trace?.Write(MotionEventKind.Tick, actualWake, deadline, run, session.Value, a: count,
                    b: (lastPointTime - actualWake) * 1000.0 / frequency, count: missed);
                long kernelStarted = finiteCritical is not null && trace is not null ? Stopwatch.GetTimestamp() : 0;
                if (HasPositionFilter) AdvanceHistory(actualWake);
                while (count > 1 && At(1).Time <= actualWake) { head = (head + 1) % Capacity; count--; }
                Point p = At(0);
                double x = p.X, y = p.Y;
                if (actualWake < p.Time) { x = HasPositionFilter ? integratedX : playedX; y = HasPositionFilter ? integratedY : playedY; }
                else if (count > 1)
                {
                    var right = At(1);
                    double f = (actualWake - p.Time) / (double)(right.Time - p.Time);
                    x += (right.X - x) * f; y += (right.Y - y) * f;
                }
                trace?.Write(MotionEventKind.Position, actualWake, lastPointTime, run, session.Value,
                    x: x, y: y, a: targetX - x, b: targetY - y, count: generation);
                baseX = x; baseY = y;
                if (boxcar is not null)
                {
                    (x, y) = boxcar.Average(actualWake, x, y);
                    trace?.Write(MotionEventKind.BoxcarPosition, actualWake, lastPointTime, run, session.Value,
                        x: x, y: y, a: baseX - x, b: baseY - y, count: generation);
                }
                if (finiteCritical is not null)
                {
                    (x, y) = finiteCritical.Position(actualWake, x, y);
                    if (trace is not null)
                    {
                        long finished = Stopwatch.GetTimestamp();
                        trace.Write(MotionEventKind.KernelPosition, actualWake, lastPointTime, run, session.Value,
                            x: x, y: y, a: baseX - x, b: baseY - y, count: generation);
                        trace.Write(MotionEventKind.KernelIntegration, finished, kernelStarted, run, session.Value,
                            count: finiteCritical.LastIntegratedSegments);
                    }
                }
                Emit(x, y, actualWake);
                if (count == 1 && actualWake >= p.Time)
                {
                    // No future segment: hold the known endpoint and park, never invent velocity.
                    // A jump exactly at this boundary has not occupied any integration time yet.
                    if (!HasPositionFilter || (integratedX == baseX && integratedY == baseY &&
                        (boxcar?.IsSettled(actualWake) ?? finiteCritical!.IsSettled(actualWake))))
                    {
                        nextDeadline = 0;
                        if (settling)
                        {
                            trace?.Write(MotionEventKind.SettleComplete, actualWake, run: run, session: session.Value,
                                x: targetX, y: targetY, count: generation);
                            Clear(actualWake, aborted: false);
                            return;
                        }
                    }
                    if (starvationAt == 0)
                    {
                        starvationAt = actualWake; Starvations++;
                        trace?.Write(MotionEventKind.StarvationStart, actualWake, run: run, session: session.Value);
                    }
                }
            }
            catch { Clear(now(), aborted: true); throw; }
        }
    }

    private void Emit(double x, double y, long at)
    {
        var delta = canonicalQuantizer is null
            ? quantizer.Process(x - playedX, y - playedY)
            : canonicalQuantizer.Submit(x, y, output);
        playedX = x; playedY = y;
        trace?.Write(MotionEventKind.Logical, at, run: run, session: session!.Value,
            x: delta.X, y: delta.Y,
            a: canonicalQuantizer is null ? quantizer.ResidualX : x - canonicalQuantizer.EmittedX,
            b: canonicalQuantizer is null ? quantizer.ResidualY : y - canonicalQuantizer.EmittedY, count: generation);
        if (delta.X == 0 && delta.Y == 0) return;
        if (canonicalQuantizer is null) output(delta.X, delta.Y);
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
        generation++; session = null; head = count = 0; nextDeadline = 0; fallback = settling = false;
        rawX = rawY = targetX = targetY = playedX = playedY = 0; quantizer.Reset();
        canonicalQuantizer?.Reset();
        baseX = baseY = integratedX = integratedY = 0; integratedAt = at; boxcar?.Reset(at); finiteCritical?.Reset(at);
        Changed?.Invoke();
    }
    public void Reset() { lock (gate) Clear(now(), aborted: true); }
    public void Dispose() { lock (gate) { if (disposed) return; Clear(now(), aborted: true); disposed = true; } }
}
