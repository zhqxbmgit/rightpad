namespace Rightpad.Receiver;

internal sealed record RuntimeSettings(double SensitivityX, double SensitivityY,
    int TapMaxDurationMs, double TapMovementThresholdPx, int ClickHoldMs,
    int DoubleTapIntervalMs = RuntimeSettings.DefaultDoubleTapIntervalMs,
    int SmoothingTauMs = RuntimeSettings.DefaultSmoothingTauMs,
    int SmoothingSupportMs = RuntimeSettings.DefaultSmoothingSupportMs)
{
    public TouchMode TouchMode { get; init; } = TouchMode.M_R1;
    public Cz1Parameters Cz1 { get; init; } = Cz1Parameters.Default;
    public VirtualControlsSettings Controls { get; init; } = VirtualControlsSettings.Default;
    public ModeBinding ModeControl { get; init; } = new();
    public const int DefaultDoubleTapIntervalMs = 130;
    public const int DefaultSmoothingTauMs = 24;
    public const int DefaultSmoothingSupportMs = 120;
    public static RuntimeSettings Default { get; } = new(7, 7, 300, 8, 25);

    public static bool InRange(double value, double min, double max) =>
        double.IsFinite(value) && value >= min && value <= max;

    public bool IsProductValid => InRange(SensitivityX, .1, 30) && InRange(SensitivityY, .1, 30) &&
        InRange(TapMaxDurationMs, 50, 1500) && InRange(TapMovementThresholdPx, .5, 100) &&
        InRange(ClickHoldMs, 1, 200) && InRange(DoubleTapIntervalMs, 50, 1000) &&
        InRange(SmoothingTauMs, 8, 60) && InRange(SmoothingSupportMs, 40, 300) && Controls?.IsValid == true && ModeControl?.IsValid == true && TouchModes.IsValid(TouchMode) && Cz1?.IsValid == true;

    public void ValidateCore()
    {
        if (!double.IsFinite(SensitivityX) || SensitivityX <= 0 ||
            !double.IsFinite(SensitivityY) || SensitivityY <= 0 || TapMaxDurationMs <= 0 ||
            !double.IsFinite(TapMovementThresholdPx) || TapMovementThresholdPx <= 0 || ClickHoldMs <= 0 ||
            !InRange(DoubleTapIntervalMs, 50, 1000) || !InRange(SmoothingTauMs, 8, 60) ||
            !InRange(SmoothingSupportMs, 40, 300) || Controls?.IsValid != true || ModeControl?.IsValid != true || !TouchModes.IsValid(TouchMode) || Cz1?.IsValid != true)
            throw new ArgumentOutOfRangeException(nameof(RuntimeSettings));
    }
}
