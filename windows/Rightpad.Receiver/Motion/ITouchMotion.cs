namespace Rightpad.Receiver;

internal enum MotionMode { RAW, RESAMPLED_250HZ }

// Shared accepted-input boundary; RAW's calculations and lifecycle remain in its original class.
internal interface ITouchMotion
{
    uint? ActiveSessionId { get; }
    long ProcessedMotionSamples { get; }
    long IgnoredSessionPackets { get; }
    long OutputEvents { get; }
    long LastOutputAtTicks { get; }
    long TotalDx { get; }
    long TotalDy { get; }
    void Process(TouchPacket packet);
    void Process(TouchPacket packet, double sensitivityX, double sensitivityY);
    void Reset();
    void ProcessAt(TouchPacket packet, long receivedAt, RuntimeSettings? settings)
    {
        if (settings is null) Process(packet);
        else Process(packet, settings.SensitivityX, settings.SensitivityY);
    }
}
