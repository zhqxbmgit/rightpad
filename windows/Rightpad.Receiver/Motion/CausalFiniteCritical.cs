namespace Rightpad.Receiver;

// Fixed causal position convolution. A bounded history records only already-realized R.
internal sealed class CausalFiniteCritical
{
    // 4096 segments across at most 140 ms: >29k distinct boundaries/s including ticks
    // and arrivals. Pathological accepted bursts fail visibly; support is never shortened.
    public const int Capacity = 4096;
    private readonly record struct Segment(long Start, long End, double X0, double Y0, double X1, double Y1);
    private readonly Segment[] segments = new Segment[Capacity];
    private readonly double frequency, tau;
    private int head, count;
    private long constantSince;
    private double lastX, lastY;
    public int TauMs { get; }
    public int SupportMs { get; }
    public long SupportTicks { get; }
    public double Normalization { get; }
    public int SegmentCount => count;
    public int LastIntegratedSegments { get; private set; }

    public CausalFiniteCritical(MotionMode mode, long frequency)
    {
        (TauMs, SupportMs) = mode switch
        {
            MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5 or MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5_SETTLE or
            MotionMode.RESAMPLED_500HZ_FINITE_CRITICAL_K24_R5_SETTLE or MotionMode.RESAMPLED_1000HZ_FINITE_CRITICAL_K24_R5_SETTLE => (24, 120),
            MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K35_R4 => (35, 140),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        if (frequency < 1000 || frequency % 250 != 0) throw new ArgumentOutOfRangeException(nameof(frequency));
        this.frequency = frequency; tau = TauMs / 1000.0;
        SupportTicks = checked(frequency * SupportMs) / 1000;
        double r = SupportMs / (double)TauMs;
        Normalization = 1 - Math.Exp(-r) * (1 + r);
    }

    public void Reset(long at)
    {
        head = count = LastIntegratedSegments = 0; constantSince = at; lastX = lastY = 0;
    }
    private Segment At(int i) => segments[(head + i) % Capacity];
    public void Trim(long cutoff)
    {
        while (count > 0 && At(0).End <= cutoff) { head = (head + 1) % Capacity; count--; }
    }
    public void Add(long start, long end, double x0, double y0, double x1, double y1)
    {
        if (end <= start) return;
        if (!double.IsFinite(x0) || !double.IsFinite(y0) || !double.IsFinite(x1) || !double.IsFinite(y1))
            throw new InvalidOperationException("Finite critical history contains a nonfinite position.");
        if (count > 0 && start != At(count - 1).End)
            throw new InvalidOperationException("Finite critical history is not contiguous.");
        Trim(end - SupportTicks);
        if (x0 != lastX || y0 != lastY) constantSince = start;
        if (x0 != x1 || y0 != y1) constantSince = end;
        lastX = x1; lastY = y1;
        if (count == Capacity) throw new InvalidOperationException("Fixed finite critical history capacity exceeded.");
        segments[(head + count++) % Capacity] = new(start, end, x0, y0, x1, y1);
    }

    // Analytic exponential moments. Near zero, convergent power series evaluate the
    // same closed forms without subtracting nearly equal numbers; no quadrature.
    private static void ExponentialMoments(double d, out double a0, out double a1, out double a2)
    {
        double decay = Math.Exp(-d);
        if (d < 0.5)
        {
            double term = d, sum0 = term;
            for (int n = 2; n <= 18; n++) { term *= -d / n; sum0 += term; }
            a0 = sum0;
            double term1 = 1, sum1 = 1, term2 = 1, sum2 = 1;
            for (int n = 1; n <= 18; n++)
            {
                term1 *= d / (n + 2); sum1 += term1;
                term2 *= d / (n + 3); sum2 += term2;
            }
            a1 = decay * d * d * 0.5 * sum1;
            a2 = decay * d * d * d / 3 * sum2;
        }
        else
        {
            a0 = 1 - decay;
            a1 = 1 - (1 + d) * decay;
            a2 = 2 - (d * d + 2 * d + 2) * decay;
        }
    }

    public (double X, double Y) Position(long at, double anchorX, double anchorY)
    {
        long cutoff = at - SupportTicks;
        Trim(cutoff); LastIntegratedSegments = 0;
        double areaX = 0, areaY = 0;
        for (int i = 0; i < count; i++)
        {
            var s = At(i); long left = Math.Max(cutoff, s.Start), right = Math.Min(at, s.End);
            if (right <= left) continue;
            double x = (at - right) / frequency / tau, d = (right - left) / frequency / tau;
            ExponentialMoments(d, out double a0, out double a1, out double a2);
            double decay = Math.Exp(-x);
            double mass = decay * (x * a0 + a1) / Normalization;
            double moment = tau * decay * (x * a1 + a2) / Normalization;
            double sx = (s.X1 - s.X0) / ((s.End - s.Start) / frequency);
            double sy = (s.Y1 - s.Y0) / ((s.End - s.Start) / frequency);
            double latestX = s.X0 + sx * ((right - s.Start) / frequency);
            double latestY = s.Y0 + sy * ((right - s.Start) / frequency);
            areaX += (latestX - anchorX) * mass - sx * moment;
            areaY += (latestY - anchorY) * mass - sy * moment;
            LastIntegratedSegments++;
        }
        // The only uncovered history is the specified pre-DOWN zero extension.
        // Use its analytic mass, never divide by a shortened startup support.
        double missing = 0;
        if (count > 0 && At(0).Start > cutoff)
        {
            double lower = Math.Clamp((at - At(0).Start) / frequency / tau, 0, SupportMs / (double)TauMs);
            double r = SupportMs / (double)TauMs;
            missing = (Math.Exp(-lower) * (1 + lower) - Math.Exp(-r) * (1 + r)) / Normalization;
        }
        return (anchorX + areaX - anchorX * missing, anchorY + areaY - anchorY * missing);
    }
    public bool IsSettled(long at) => at - constantSince >= SupportTicks;
}
