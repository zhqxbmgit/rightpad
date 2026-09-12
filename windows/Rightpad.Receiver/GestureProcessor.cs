namespace Rightpad.Receiver;

internal sealed class GestureProcessor
{
    private readonly Action<int> click;
    private readonly Action beginDrag, endDrag;
    private readonly int defaultDoubleTapInterval;
    private readonly int defaultClickHold;
    private readonly int defaultDuration;
    private readonly double defaultThreshold;
    private ulong maxDurationNs;
    private double movementThreshold;
    private uint? activeSessionId;
    private float downX, downY;
    private ulong downEventTimeNs;
    private bool confirmedMove;
    private ulong lastTapUpTimestampNs;
    public bool DoubleTapArmed { get; private set; }
    public bool IsDragging { get; private set; }
    public ulong? LastDoubleTapDeltaNs { get; private set; }

    public long TapCandidates { get; private set; }
    public long ConfirmedMoves { get; private set; }
    public long ClicksTriggered { get; private set; }
    public long DragStarts { get; private set; }
    public long DragEnds { get; private set; }

    public GestureProcessor(Action click, Action beginDrag, Action endDrag,
        int tapMaxDurationMs = 300, double tapMovementThresholdPx = 8)
        : this(_ => click(), new RuntimeSettings(7, 7, tapMaxDurationMs, tapMovementThresholdPx, 25), beginDrag, endDrag) { }

    public GestureProcessor(Action<int> click, RuntimeSettings settings, Action beginDrag, Action endDrag)
    {
        settings.ValidateCore();
        this.click = click;
        this.beginDrag = beginDrag;
        this.endDrag = endDrag;
        defaultDuration = settings.TapMaxDurationMs;
        defaultThreshold = settings.TapMovementThresholdPx;
        defaultDoubleTapInterval = settings.DoubleTapIntervalMs;
        defaultClickHold = settings.ClickHoldMs;
    }

    // Only decoded, sequence-accepted raw packets enter this independent path.
    public void Process(TouchPacket packet) => Process(packet, defaultDuration, defaultThreshold, defaultClickHold, defaultDoubleTapInterval);

    public void Process(TouchPacket packet, int tapMaxDurationMs, double tapMovementThresholdPx, int clickHoldMs,
        int doubleTapIntervalMs = RuntimeSettings.DefaultDoubleTapIntervalMs)
    {
        if (packet.Header.EventType == TouchEventType.Down)
        {
            // An accepted replacement DOWN ends a lost-UP contact before establishing a new one.
            if (IsDragging) FinishDrag();
            var down = packet.Samples[0];
            LastDoubleTapDeltaNs = DoubleTapArmed && down.TimestampNs >= lastTapUpTimestampNs
                ? down.TimestampNs - lastTapUpTimestampNs : null;
            bool drag = LastDoubleTapDeltaNs is ulong delta && delta <= (ulong)doubleTapIntervalMs * 1_000_000;
            DoubleTapArmed = false; // Every following DOWN consumes or expires the one-shot qualification.
            lastTapUpTimestampNs = 0;
            maxDurationNs = (ulong)tapMaxDurationMs * 1_000_000;
            movementThreshold = tapMovementThresholdPx;
            activeSessionId = packet.Header.SessionId;
            downX = down.X;
            downY = down.Y;
            downEventTimeNs = down.TimestampNs;
            confirmedMove = false;
            if (drag)
            {
                beginDrag();
                IsDragging = true;
                DragStarts++;
                return;
            }
            TapCandidates++;
            return;
        }
        if (activeSessionId != packet.Header.SessionId) return;
        if (IsDragging)
        {
            if (packet.Header.EventType == TouchEventType.Up) FinishDrag(packet.Samples[0].TimestampNs);
            return;
        }

        foreach (var sample in packet.Samples)
        {
            if (!confirmedMove && (Math.Abs((double)sample.X - downX) > movementThreshold ||
                                   Math.Abs((double)sample.Y - downY) > movementThreshold))
            {
                confirmedMove = true;
                ConfirmedMoves++;
            }
        }
        if (packet.Header.EventType != TouchEventType.Up) return;
        ulong upTime = packet.Samples[0].TimestampNs;
        bool tap = !confirmedMove && upTime >= downEventTimeNs && upTime - downEventTimeNs <= maxDurationNs;
        FinishCurrentContact(); // Consume UP before output, including when output fails.
        if (tap)
        {
            click(clickHoldMs);
            ClicksTriggered++;
            lastTapUpTimestampNs = upTime;
            DoubleTapArmed = true;
        }
    }

    public void Reset()
    {
        FinishCurrentContact();
        DoubleTapArmed = false;
        lastTapUpTimestampNs = 0;
        LastDoubleTapDeltaNs = null;
        // Outer lifecycle cleanup owns button release, including output-failure recovery.
        IsDragging = false;
    }

    private void FinishDrag(ulong? upTimestampNs = null)
    {
        IsDragging = false;
        FinishCurrentContact();
        endDrag();
        DragEnds++;
        if (upTimestampNs is ulong timestampNs)
        {
            lastTapUpTimestampNs = timestampNs;
            DoubleTapArmed = true;
        }
    }

    private void FinishCurrentContact()
    {
        activeSessionId = null;
        downX = downY = 0;
        downEventTimeNs = 0;
        confirmedMove = false;
    }
}
