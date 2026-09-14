namespace Rightpad.Receiver;

// A fixed position integral, not a delta filter or another integer residual owner.
internal sealed class CausalBoxcar
{
    public const int Capacity = 4096;
    private readonly record struct Segment(long Start, long End, double X0, double Y0, double X1, double Y1);
    private readonly Segment[] segments = new Segment[Capacity];
    private int head, count;
    private long constantSince;
    private double lastX, lastY;
    public int WindowMs { get; }
    public long WindowTicks { get; }
    public int SegmentCount => count;

    public CausalBoxcar(int windowMs, long frequency)
    {
        if (windowMs is not (4 or 8)) throw new ArgumentOutOfRangeException(nameof(windowMs));
        WindowMs = windowMs;
        WindowTicks = frequency / 250 * (windowMs / 4);
    }

    public void Reset(long at)
    {
        head = count = 0; constantSince = at; lastX = lastY = 0;
    }

    private Segment At(int i) => segments[(head + i) % Capacity];
    public void Trim(long cutoff)
    {
        while (count > 0 && At(0).End <= cutoff) { head = (head + 1) % Capacity; count--; }
    }

    public void Add(long start, long end, double x0, double y0, double x1, double y1)
    {
        if (end <= start) return;
        Trim(end - WindowTicks);
        if (x0 != lastX || y0 != lastY) constantSince = start; // An admitted late sample can create a target jump.
        bool constant = x0 == x1 && y0 == y1;
        if (!constant) constantSince = end;
        lastX = x1; lastY = y1;
        if (constant && count > 0)
        {
            var previous = At(count - 1);
            if (previous.End == start && previous.X0 == x0 && previous.X1 == x0 && previous.Y0 == y0 && previous.Y1 == y0)
            {
                segments[(head + count - 1) % Capacity] = previous with { End = end };
                return;
            }
        }
        // Never silently shorten W or discard a still-needed piece of its integral.
        if (count == Capacity) throw new InvalidOperationException("Fixed boxcar history capacity exceeded.");
        segments[(head + count++) % Capacity] = new(start, end, x0, y0, x1, y1);
    }

    public (double X, double Y) Average(long at, double anchorX, double anchorY)
    {
        long cutoff = at - WindowTicks, covered = 0;
        Trim(cutoff);
        double areaX = 0, areaY = 0;
        for (int i = 0; i < count; i++)
        {
            var s = At(i);
            long left = Math.Max(cutoff, s.Start), right = Math.Min(at, s.End);
            if (right <= left) continue;
            double middle = left - s.Start + (right - left) * .5;
            double x = s.X0 + (s.X1 - s.X0) / (s.End - s.Start) * middle;
            double y = s.Y0 + (s.Y1 - s.Y0) / (s.End - s.Start) * middle;
            double weight = (right - left) / (double)WindowTicks;
            areaX += (x - anchorX) * weight; areaY += (y - anchorY) * weight;
            covered += right - left;
        }
        // Local differences avoid subtracting large cumulative session integrals.
        // Uncovered startup history is the specified zero extension, not a variable window.
        double missing = (WindowTicks - covered) / (double)WindowTicks;
        return (anchorX + areaX - anchorX * missing, anchorY + areaY - anchorY * missing);
    }

    public bool IsSettled(long at) => at - constantSince >= WindowTicks;
}
