using System.Globalization;

namespace Rightpad.Receiver;

// Counts are Receiver logical displacement, before the existing integer HID output.
// At DT=.004 and tau>=.020, r=DT/tau<=.2. With damping<=2 the
// semi-implicit Euler stability condition r*r+4*damping*r<4 holds.
// V<=30000 bounds normal steps to 120 counts; glide>=10000 bounds a
// full-speed glide to 3 seconds. These are conservative engineering limits,
// not a claim that every allowed configuration has been accepted in games.
// A>=20000 also excludes the long cap-limited oscillations observed in the
// A1000/tau20/damping.25/V30000 offline corner test. Allowed corners settle
// the tested 1000/-500 count step while retaining the exact production defaults.
internal sealed record Cz1Parameters(double MaxAcceleration = 80000,
    double MaxVelocity = 15000, double TauMs = 35, double DampingRatio = 1,
    double GlideDeceleration = 120000, double PositionThreshold = .5,
    double VelocityThreshold = 2)
{
    public static Cz1Parameters Default { get; } = new();
    public static readonly (double Min, double Max) AccelerationRange = (20000, 320000),
        VelocityRange = (100, 30000), TauRange = (20, 100), DampingRange = (.25, 2),
        GlideRange = (10000, 240000), PositionRange = (.01, 2), StopVelocityRange = (.1, 10);
    public double TauSeconds => TauMs / 1000.0;
    public bool IsValid => Valid(MaxAcceleration, AccelerationRange) && Valid(MaxVelocity, VelocityRange) &&
        Valid(TauMs, TauRange) && Valid(DampingRatio, DampingRange) && Valid(GlideDeceleration, GlideRange) &&
        Valid(PositionThreshold, PositionRange) && Valid(VelocityThreshold, StopVelocityRange);
    internal static bool Valid(double value, (double Min, double Max) range) =>
        RuntimeSettings.InRange(value, range.Min, range.Max);
    public void Validate()
    {
        if (!IsValid) throw new ArgumentOutOfRangeException(nameof(Cz1Parameters), "Invalid C-Z1 parameter snapshot.");
    }
    public string Description => string.Create(CultureInfo.InvariantCulture,
        $"Amax {MaxAcceleration:G} counts/s² · Vmax {MaxVelocity:G} counts/s · Tau {TauMs:G} ms · 阻尼比 {DampingRatio:G} · Glide {GlideDeceleration:G} counts/s² · 位置阈值 {PositionThreshold:G} counts · 速度阈值 {VelocityThreshold:G} counts/s");
}
