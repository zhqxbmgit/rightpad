namespace Rightpad.Receiver;

internal enum MotionMode
{
    RAW, RESAMPLED_250HZ, RESAMPLED_250HZ_BOXCAR_4MS, RESAMPLED_250HZ_BOXCAR_8MS,
    RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5, RESAMPLED_250HZ_FINITE_CRITICAL_K35_R4,
    RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5_SETTLE,
    RESAMPLED_500HZ_FINITE_CRITICAL_K24_R5_SETTLE,
    RESAMPLED_1000HZ_FINITE_CRITICAL_K24_R5_SETTLE
}

internal static class MotionModes
{
    public const MotionMode ProductionMode = MotionMode.RESAMPLED_1000HZ_FINITE_CRITICAL_K24_R5_SETTLE;
    public static int PeriodMs(MotionMode mode) => mode switch
    {
        MotionMode.RAW => 0,
        MotionMode.RESAMPLED_500HZ_FINITE_CRITICAL_K24_R5_SETTLE => 2,
        MotionMode.RESAMPLED_1000HZ_FINITE_CRITICAL_K24_R5_SETTLE => 1,
        _ when Enum.IsDefined(mode) => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
    public static string QuantizerName(MotionMode mode) => IsFiniteCritical(mode) ? "Q0C" : "Q0I";
    public static int BoxcarWindowMs(MotionMode mode) => mode switch
    {
        MotionMode.RAW or MotionMode.RESAMPLED_250HZ or
        MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5 or MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K35_R4 or
        MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5_SETTLE or
        MotionMode.RESAMPLED_500HZ_FINITE_CRITICAL_K24_R5_SETTLE or
        MotionMode.RESAMPLED_1000HZ_FINITE_CRITICAL_K24_R5_SETTLE => 0,
        MotionMode.RESAMPLED_250HZ_BOXCAR_4MS => 4,
        MotionMode.RESAMPLED_250HZ_BOXCAR_8MS => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
    public static bool IsFiniteCritical(MotionMode mode) => mode is
        MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5 or MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K35_R4 or
        MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5_SETTLE or
        MotionMode.RESAMPLED_500HZ_FINITE_CRITICAL_K24_R5_SETTLE or
        MotionMode.RESAMPLED_1000HZ_FINITE_CRITICAL_K24_R5_SETTLE;
    public static bool IsEarnedSettle(MotionMode mode) => mode is
        MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5_SETTLE or
        MotionMode.RESAMPLED_500HZ_FINITE_CRITICAL_K24_R5_SETTLE or
        MotionMode.RESAMPLED_1000HZ_FINITE_CRITICAL_K24_R5_SETTLE;
    public static (int TauMs, int SupportMs, double Normalization) FiniteCriticalParameters(MotionMode mode)
    {
        (int tau, int support) = mode switch
        {
            MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5 or MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5_SETTLE or
            MotionMode.RESAMPLED_500HZ_FINITE_CRITICAL_K24_R5_SETTLE or MotionMode.RESAMPLED_1000HZ_FINITE_CRITICAL_K24_R5_SETTLE => (24, 120),
            MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K35_R4 => (35, 140),
            _ => (0, 0)
        };
        double r = tau == 0 ? 0 : support / (double)tau;
        return (tau, support, tau == 0 ? 0 : 1 - Math.Exp(-r) * (1 + r));
    }

    public static MotionConfiguration FixedConfiguration(MotionMode mode)
    {
        var kernel = FiniteCriticalParameters(mode);
        return new(mode, kernel.TauMs, kernel.SupportMs);
    }
}

// Immutable configuration captured once at Receiver run construction. Development modes use
// their fixed mode parameters; only the ordinary production path substitutes saved Tau/Support.
internal readonly record struct MotionConfiguration(MotionMode Mode, int FiniteCriticalTauMs, int FiniteCriticalSupportMs)
{
    public int PeriodMs => MotionModes.PeriodMs(Mode);
    public bool IsFiniteCritical => MotionModes.IsFiniteCritical(Mode);
    public bool IsEarnedSettle => MotionModes.IsEarnedSettle(Mode);
    public double KernelNormalization
    {
        get
        {
            if (!IsFiniteCritical) return 0;
            double r = FiniteCriticalSupportMs / (double)FiniteCriticalTauMs;
            return 1 - Math.Exp(-r) * (1 + r);
        }
    }

    public static MotionConfiguration Product(RuntimeSettings settings) =>
        new(MotionModes.ProductionMode, settings.SmoothingTauMs, settings.SmoothingSupportMs);
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
