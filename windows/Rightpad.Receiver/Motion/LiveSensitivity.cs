namespace Rightpad.Receiver;

internal sealed record SensitivitySnapshot(double X, double Y);

// Published only after the settings transaction commits. Readers take the pair
// once per real input sample; the output clock never consults this source.
internal sealed class LiveSensitivity(double x, double y)
{
    private SensitivitySnapshot current = new(x, y);
    public SensitivitySnapshot Current => Volatile.Read(ref current);
    public void Publish(double x, double y) => Volatile.Write(ref current, new(x, y));
}
