namespace Rightpad.Receiver;

internal sealed class LibVirtualHidXboxGamepad : IVirtualGamepad
{
    internal const string Backend = "libvirtualhid Xbox360";
    private readonly object gate = new();
    private readonly IVirtualHidGamepad native;
    private readonly FlightRecorder? recorder;
    private GamepadOutputStats stats;
    private string? error;
    private bool disposed, unavailable;
    public string BackendName => Backend;
    public string DeviceIdentity { get; }
    public bool Available { get { lock (gate) return !disposed && !unavailable; } }
    public string? LastError { get { lock (gate) return error; } }
    public GamepadOutputStats Stats { get { lock (gate) return stats; } }

    public LibVirtualHidXboxGamepad(FlightRecorder? recorder = null) : this(new NativeVirtualHidGamepad(), recorder) { }
    internal LibVirtualHidXboxGamepad(IVirtualHidGamepad native, FlightRecorder? recorder = null)
    {
        this.native = native;
        this.recorder = recorder;
        DeviceIdentity = native.DeviceIdentity;
        SetState(XboxGamepadState.Neutral);
    }
    public bool SetState(XboxGamepadState state)
    {
        lock (gate)
        {
            if (disposed || unavailable) return false;
            if (Submit(state, "set_state")) return true;
            // Ambiguous failed report might have applied. Neutral and remove this gamepad immediately.
            unavailable = true;
            Dispose();
            return false;
        }
    }
    private bool Submit(XboxGamepadState state, string operation)
    {
        try { native.SetState(state); stats = stats with { Successes = stats.Successes + 1 }; return true; }
        catch (Exception e) { Failed(operation, e); return false; }
    }
    private void Failed(string operation, Exception failure)
    {
        stats = stats with { Failures = stats.Failures + 1 };
        error = error is null ? failure.Message : error + " | " + failure.Message;
        recorder?.Event("gamepad_output_failure", ("gamepadBackend", BackendName),
            ("kind", operation), ("error", failure.Message));
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            Submit(XboxGamepadState.Neutral, "neutralize");
            // Native destroy also neutralizes; it always consumes its handle even on failure.
            try { native.Dispose(); }
            catch (Exception e) { Failed("destroy", e); }
        }
    }
}
