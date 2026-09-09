namespace Rightpad.Receiver;

internal sealed class GestureProcessor
{
    private readonly Action<int> click;
    private readonly int defaultDuration;
    private readonly double defaultThreshold;
    private ulong maxDurationNs;
    private double movementThreshold;
    private uint? activeSessionId;
    private float downX, downY;
    private ulong downEventTimeNs;
    private bool confirmedMove;

    public long TapCandidates { get; private set; }
    public long ConfirmedMoves { get; private set; }
    public long ClicksTriggered { get; private set; }

    public GestureProcessor(Action click, int tapMaxDurationMs = 300, double tapMovementThresholdPx = 8)
        : this(_ => click(), tapMaxDurationMs, tapMovementThresholdPx) { }

    public GestureProcessor(Action<int> click, RuntimeSettings settings)
        : this(click, settings.TapMaxDurationMs, settings.TapMovementThresholdPx) { }

    private GestureProcessor(Action<int> click, int tapMaxDurationMs, double tapMovementThresholdPx)
    {
        if (tapMaxDurationMs <= 0) throw new ArgumentOutOfRangeException(nameof(tapMaxDurationMs));
        if (!double.IsFinite(tapMovementThresholdPx) || tapMovementThresholdPx <= 0)
            throw new ArgumentOutOfRangeException(nameof(tapMovementThresholdPx));
        this.click = click;
        defaultDuration = tapMaxDurationMs;
        defaultThreshold = tapMovementThresholdPx;
        maxDurationNs = (ulong)tapMaxDurationMs * 1_000_000;
        movementThreshold = tapMovementThresholdPx;
    }

    // Only decoded, sequence-accepted raw packets enter this independent path.
    public void Process(TouchPacket packet) => Process(packet, defaultDuration, defaultThreshold, 25);

    public void Process(TouchPacket packet, int tapMaxDurationMs, double tapMovementThresholdPx, int clickHoldMs)
    {
        if (packet.Header.EventType == TouchEventType.Down)
        {
            maxDurationNs = (ulong)tapMaxDurationMs * 1_000_000;
            movementThreshold = tapMovementThresholdPx;
            activeSessionId = packet.Header.SessionId;
            var down = packet.Samples[0];
            downX = down.X;
            downY = down.Y;
            downEventTimeNs = down.TimestampNs;
            confirmedMove = false;
            TapCandidates++;
            return;
        }
        if (activeSessionId != packet.Header.SessionId) return;

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
        Reset(); // Consume UP before output, including when output fails.
        if (tap)
        {
            click(clickHoldMs);
            ClicksTriggered++;
        }
    }

    public void Reset()
    {
        activeSessionId = null;
        downX = downY = 0;
        downEventTimeNs = 0;
        confirmedMove = false;
    }
}
