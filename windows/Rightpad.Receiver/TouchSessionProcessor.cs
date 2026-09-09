namespace Rightpad.Receiver;

internal sealed class TouchSessionProcessor(Action<int, int> output, double sensitivityX = 1, double sensitivityY = 1)
{
    private readonly RawMotionProcessor motion = new(sensitivityX, sensitivityY);
    private double previousX;
    private double previousY;

    public uint? ActiveSessionId { get; private set; }
    public long ProcessedMotionSamples { get; private set; }
    public long IgnoredSessionPackets { get; private set; }
    public long OutputEvents { get; private set; }
    public long TotalDx { get; private set; }
    public long TotalDy { get; private set; }

    // Only fully decoded, sequence-accepted packets enter here.
    public void Process(TouchPacket packet) => Process(packet, sensitivityX, sensitivityY);

    public void Process(TouchPacket packet, double packetSensitivityX, double packetSensitivityY)
    {
        if (packet.Header.EventType == TouchEventType.Down)
        {
            Reset();
            ActiveSessionId = packet.Header.SessionId;
            previousX = packet.Samples[0].X;
            previousY = packet.Samples[0].Y;
            return;
        }

        if (ActiveSessionId != packet.Header.SessionId)
        {
            IgnoredSessionPackets++;
            return;
        }

        try
        {
            foreach (var sample in packet.Samples)
            {
                // Widen before subtraction; timestamps intentionally play no role.
                double dx = (double)sample.X - previousX;
                double dy = (double)sample.Y - previousY;
                previousX = sample.X;
                previousY = sample.Y;
                var movement = motion.Process(dx, dy, packetSensitivityX, packetSensitivityY);
                ProcessedMotionSamples++;
                if (movement.X == 0 && movement.Y == 0) continue;
                output(movement.X, movement.Y);
                OutputEvents++;
                TotalDx += movement.X;
                TotalDy += movement.Y;
            }
            // UP's real final delta has already been processed.
            if (packet.Header.EventType == TouchEventType.Up) Reset();
        }
        catch
        {
            Reset();
            throw;
        }
    }

    public void Reset()
    {
        ActiveSessionId = null;
        previousX = previousY = 0;
        motion.Reset();
    }
}
