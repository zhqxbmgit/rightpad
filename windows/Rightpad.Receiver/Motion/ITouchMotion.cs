namespace Rightpad.Receiver;

internal enum MotionMode
{
    RAW, RESAMPLED_250HZ, RESAMPLED_250HZ_BOXCAR_4MS, RESAMPLED_250HZ_BOXCAR_8MS,
    RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5, RESAMPLED_250HZ_FINITE_CRITICAL_K35_R4
}

internal static class MotionModes
{
    public static int BoxcarWindowMs(MotionMode mode) => mode switch
    {
        MotionMode.RAW or MotionMode.RESAMPLED_250HZ or
        MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5 or MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K35_R4 => 0,
        MotionMode.RESAMPLED_250HZ_BOXCAR_4MS => 4,
        MotionMode.RESAMPLED_250HZ_BOXCAR_8MS => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
    public static bool IsFiniteCritical(MotionMode mode) => mode is
        MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5 or MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K35_R4;
    public static (int TauMs, int SupportMs, double Normalization) FiniteCriticalParameters(MotionMode mode)
    {
        (int tau, int support) = mode switch
        {
            MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5 => (24, 120),
            MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K35_R4 => (35, 140),
            _ => (0, 0)
        };
        double r = tau == 0 ? 0 : support / (double)tau;
        return (tau, support, tau == 0 ? 0 : 1 - Math.Exp(-r) * (1 + r));
    }
}

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
