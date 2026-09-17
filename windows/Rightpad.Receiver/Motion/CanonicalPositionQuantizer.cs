namespace Rightpad.Receiver;

// K-family Q0-C: exact classification of binary64 P minus the submitted integer I.
// P is already in mouse counts. No floating residual is authoritative state.
internal sealed class CanonicalPositionQuantizer
{
    public long EmittedX { get; private set; }
    public long EmittedY { get; private set; }

    public static int ExactTruncateDifference(double position, long emitted)
    {
        // The exclusive upper bound is exact; (double)long.MaxValue rounds to 2^63.
        if (!double.IsFinite(position) || position < -9223372036854775808.0 || position >= 9223372036854775808.0)
            throw new OverflowException("Q0C position is outside [-2^63, 2^63).");
        long integral = (long)position;
        Int128 difference = (Int128)integral - emitted;
        // A fractional binary64 has magnitude <2^52, making integral->double exact.
        // Truncate the integer difference plus that fraction without rounding P-I.
        if (position != (double)integral)
        {
            if (position > 0 && difference < 0) difference++;
            else if (position < 0 && difference > 0) difference--;
        }
        int delta = checked((int)difference);
        _ = checked(emitted + delta);
        return delta;
    }

    public (int X, int Y) Submit(double x, double y, Action<int, int> output)
    {
        int dx = ExactTruncateDifference(x, EmittedX), dy = ExactTruncateDifference(y, EmittedY);
        long nextX = checked(EmittedX + dx), nextY = checked(EmittedY + dy);
        // Validate both axes and complete the synchronous output before committing I.
        // On ambiguous native failure the owner aborts this contact and Runtime run.
        if (dx != 0 || dy != 0) output(dx, dy);
        EmittedX = nextX; EmittedY = nextY;
        return (dx, dy);
    }

    public void Reset() => (EmittedX, EmittedY) = (0, 0);
}
