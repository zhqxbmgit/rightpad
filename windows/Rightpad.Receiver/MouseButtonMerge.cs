namespace Rightpad.Receiver;

// The two independent producers of the one production mouse device: the
// ordinary touchpad Gesture path and the Mode single binding.
internal enum MouseButtonSource { Gesture, Mode }

// Mode and the ordinary touchpad each own their own button contribution; the
// shared Virtual HID mouse receives the merged effective state. An edge is only
// submitted when that merged state changes, so one source releasing or being
// cancelled can never release a button another source still requires.
internal sealed class MouseButtonMerge(IMouseOutput output)
{
    private readonly object gate = new();
    private int gesture, mode; // committed per-source contributions
    private int device;        // buttons this router believes the device holds

    public void Set(MouseButtonSource source, ModeMouseButton button, bool down)
    {
        lock (gate)
        {
            int bit = 1 << (int)button;
            if (!down) Release(source, button, bit);
            else Press(source, button, bit);
        }
    }

    private void Press(MouseButtonSource source, ModeMouseButton button, int bit)
    {
        if ((device & bit) != 0)
        {
            // Already held on the device (including an ambiguous earlier press):
            // record the contribution without creating a second press edge.
            Commit(source, bit, true);
            return;
        }
        // Pessimistic: an ambiguous submission must still be releasable.
        device |= bit;
        output.SetButton(button, true);
        Commit(source, bit, true);
    }

    private void Release(MouseButtonSource source, ModeMouseButton button, int bit)
    {
        Commit(source, bit, false);
        if (((gesture | mode) & bit) != 0) return; // another source still requires it
        if ((device & bit) == 0) return;
        // A failed release keeps the device bit so Dispose or a retry releases it.
        output.SetButton(button, false);
        device &= ~bit;
    }

    private void Commit(MouseButtonSource source, int bit, bool down)
    {
        if (source == MouseButtonSource.Gesture) gesture = down ? gesture | bit : gesture & ~bit;
        else mode = down ? mode | bit : mode & ~bit;
    }

    // Read-only diagnostics for tests and status; never drives output.
    internal int GestureMask { get { lock (gate) return gesture; } }
    internal int ModeMask { get { lock (gate) return mode; } }
    internal int DeviceMask { get { lock (gate) return device; } }
}
