namespace Rightpad.Receiver;

internal interface IMouseOutput : IDisposable
{
    string BackendName { get; }
    string? DeviceIdentity => null;
    MouseOutputStats Stats { get; }
    void Move(int dx, int dy);
    void LeftDown();
    void LeftUp();
}

// Counts API calls, not HID report chunks or proof that a foreground app consumed input.
internal readonly record struct MouseOutputStats(
    long MoveSuccesses = 0, long MoveFailures = 0,
    long LeftDownSuccesses = 0, long LeftUpSuccesses = 0, long ButtonFailures = 0,
    long Successes = 0, long Failures = 0, long LastSuccessAtTicks = 0, long LastFailureAtTicks = 0,
    long RelativeDx = 0, long RelativeDy = 0, long AbsDx = 0, long AbsDy = 0);

internal enum MouseBackend { SendInput, VirtualHid }

internal static class MouseOutputFactory
{
    public static string Name(MouseBackend backend) => backend switch
    {
        MouseBackend.SendInput => "SendInput",
        MouseBackend.VirtualHid => "libvirtualhid",
        _ => throw new ArgumentOutOfRangeException(nameof(backend))
    };

    public static IMouseOutput Create(MouseBackend backend, FlightRecorder? recorder = null) => backend switch
    {
        MouseBackend.SendInput => new WindowsMouseOutput(recorder),
        MouseBackend.VirtualHid => new LibVirtualHidMouseOutput(recorder),
        _ => throw new ArgumentOutOfRangeException(nameof(backend))
    };
}
