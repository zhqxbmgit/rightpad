namespace Rightpad.Receiver;

// Phase Z1: fixed reference dynamics only. The caller owns scheduling, release
// playout, lifecycle fences and native submission. No earned-distance correction.
internal sealed class ZhqTrackpadDynamics
{
    public const double Dt = 0.004, Tau = 0.035, DampingRatio = 1.0;
    public const double MaxVelocity = 15000, MaxAcceleration = 80000, GlideDeceleration = 120000;
    public const double PositionThreshold = 0.5, VelocityThreshold = 2.0;
    public (double X, double Y) Target { get; private set; }
    public (double X, double Y) Position { get; private set; }
    public (double X, double Y) Velocity { get; private set; }
    public (double X, double Y) LastSent { get; private set; }
    public (double X, double Y) CarryOver { get; private set; }
    public bool Gliding { get; private set; }

    public void Down()
    {
        Target = Position = CarryOver;
        Velocity = LastSent = default;
        Gliding = false;
    }
    public void SetTarget(double x, double y) => Target = (x, y);
    public void Release() => Gliding = true;

    // Return true only at reference natural shouldStop. Emission still follows
    // this step, then the caller saves the remainder after successful submission.
    public bool Step()
    {
        double px = Position.X, py = Position.Y, vx = Velocity.X, vy = Velocity.Y;
        bool shouldStop = false;
        if (Gliding)
        {
            double speed = Math.Sqrt(vx * vx + vy * vy);
            double decel = GlideDeceleration * Dt;
            if (speed <= decel + VelocityThreshold) { vx = vy = 0; shouldStop = true; }
            else
            {
                double scale = (speed - decel) / speed;
                vx *= scale; vy *= scale;
                px += vx * Dt; py += vy * Dt;
            }
        }
        else
        {
            double dx = Target.X - px, dy = Target.Y - py;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            double speed = Math.Sqrt(vx * vx + vy * vy);
            if (distance < PositionThreshold && speed < VelocityThreshold)
            { px = Target.X; py = Target.Y; vx = vy = 0; }
            else
            {
                double omega = 1.0 / Tau;
                double ax = dx * omega * omega - vx * 2.0 * DampingRatio * omega;
                double ay = dy * omega * omega - vy * 2.0 * DampingRatio * omega;
                double acceleration = Math.Sqrt(ax * ax + ay * ay);
                if (acceleration > MaxAcceleration)
                { double scale = MaxAcceleration / acceleration; ax *= scale; ay *= scale; }
                vx += ax * Dt; vy += ay * Dt;
                double velocity = Math.Sqrt(vx * vx + vy * vy);
                if (velocity > MaxVelocity)
                { double scale = MaxVelocity / velocity; vx *= scale; vy *= scale; }
                px += vx * Dt; py += vy * Dt;
            }
        }
        Position = (px, py); Velocity = (vx, vy);
        return shouldStop;
    }

    public (int X, int Y) IntegerDelta() =>
        (JavaRound(Position.X - LastSent.X), JavaRound(Position.Y - LastSent.Y));
    public void Commit(int x, int y) => LastSent = (LastSent.X + x, LastSent.Y + y);
    public void SaveNaturalCarry() => CarryOver = (Position.X - LastSent.X, Position.Y - LastSent.Y);
    public void Reset()
    { Target = Position = Velocity = LastSent = CarryOver = default; Gliding = false; }

    internal static int JavaRound(double value)
    {
        if (!double.IsFinite(value)) throw new OverflowException("C motion delta is not finite.");
        // Compare the fraction directly: adding 0.5 would round the binary64
        // value immediately below +0.5 up to 1 before floor can classify it.
        double floor = Math.Floor(value);
        double rounded = value - floor >= 0.5 ? floor + 1 : floor;
        return checked((int)rounded);
    }
}
