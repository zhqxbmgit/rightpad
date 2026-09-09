namespace Rightpad.Receiver;

internal sealed record RuntimeSettings(double SensitivityX, double SensitivityY,
    int TapMaxDurationMs, double TapMovementThresholdPx, int ClickHoldMs)
{
    public static RuntimeSettings Default { get; } = new(7, 7, 300, 8, 25);

    public static bool InRange(double value, double min, double max) =>
        double.IsFinite(value) && value >= min && value <= max;

    public bool IsProductValid => InRange(SensitivityX, .1, 30) && InRange(SensitivityY, .1, 30) &&
        InRange(TapMaxDurationMs, 50, 1500) && InRange(TapMovementThresholdPx, .5, 100) &&
        InRange(ClickHoldMs, 1, 200);

    public void ValidateCore()
    {
        if (!double.IsFinite(SensitivityX) || SensitivityX <= 0 ||
            !double.IsFinite(SensitivityY) || SensitivityY <= 0 || TapMaxDurationMs <= 0 ||
            !double.IsFinite(TapMovementThresholdPx) || TapMovementThresholdPx <= 0 || ClickHoldMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(RuntimeSettings));
    }
}
