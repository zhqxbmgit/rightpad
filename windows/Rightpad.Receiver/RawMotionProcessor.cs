namespace Rightpad.Receiver;

internal sealed class RawMotionProcessor
{
    public double SensitivityX { get; }
    public double SensitivityY { get; }
    public double ResidualX { get; private set; }
    public double ResidualY { get; private set; }

    public RawMotionProcessor(double sensitivityX = 1, double sensitivityY = 1)
    {
        if (!double.IsFinite(sensitivityX) || sensitivityX <= 0)
            throw new ArgumentOutOfRangeException(nameof(sensitivityX));
        if (!double.IsFinite(sensitivityY) || sensitivityY <= 0)
            throw new ArgumentOutOfRangeException(nameof(sensitivityY));
        SensitivityX = sensitivityX;
        SensitivityY = sensitivityY;
    }

    public (int X, int Y) Process(double dx, double dy)
    {
        double totalX = ResidualX + dx * SensitivityX;
        double totalY = ResidualY + dy * SensitivityY;
        double integerX = Math.Truncate(totalX);
        double integerY = Math.Truncate(totalY);
        // Validate both axes before committing residuals; never wrap or clamp movement.
        if (!double.IsFinite(integerX) || !double.IsFinite(integerY) ||
            integerX < int.MinValue || integerX > int.MaxValue ||
            integerY < int.MinValue || integerY > int.MaxValue)
            throw new OverflowException("RAW movement cannot be represented by SendInput int32 deltas.");

        ResidualX = totalX - integerX;
        ResidualY = totalY - integerY;
        return ((int)integerX, (int)integerY);
    }

    public void Reset() => (ResidualX, ResidualY) = (0, 0);
}
