using System.Diagnostics;
using System.IO;

namespace Rightpad.Receiver;

internal sealed class LibVirtualHidMouseOutput : IMouseOutput
{
    // Button timer and receiving loop share one native mouse. Serialize state and disposal.
    private readonly object gate = new();
    private readonly IVirtualHidMouse native;
    private readonly FlightRecorder? recorder;
    private MouseOutputStats stats;
    private bool disposed, held;
    public string BackendName => "libvirtualhid";
    public string DeviceIdentity { get; }
    public MouseOutputStats Stats { get { lock (gate) return stats; } }

    public LibVirtualHidMouseOutput(FlightRecorder? recorder = null) : this(new NativeVirtualHidMouse(), recorder) { }
    internal LibVirtualHidMouseOutput(IVirtualHidMouse native, FlightRecorder? recorder = null)
    {
        this.native = native;
        this.recorder = recorder;
        DeviceIdentity = native.DeviceIdentity;
    }

    public void Move(int dx, int dy)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (dx == 0 && dy == 0) return;
            stats = stats with { RelativeDx = stats.RelativeDx + dx, RelativeDy = stats.RelativeDy + dy,
                AbsDx = stats.AbsDx + Math.Abs((long)dx), AbsDy = stats.AbsDy + Math.Abs((long)dy) };
            try { native.Move(dx, dy); }
            catch (Exception e) { stats = stats with { MoveFailures = stats.MoveFailures + 1 }; Failed("move", e); throw; }
            stats = stats with { MoveSuccesses = stats.MoveSuccesses + 1 };
            Succeeded();
        }
    }
    public void LeftDown() => Button(true);
    public void LeftUp() => Button(false);
    private void Button(bool down)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (down) held = true; // Ambiguous submission still requires a cleanup UP.
            try { if (down) native.LeftDown(); else native.LeftUp(); }
            catch (Exception e) { stats = stats with { ButtonFailures = stats.ButtonFailures + 1 }; Failed("button", e); throw; }
            held = down;
            stats = down ? stats with { LeftDownSuccesses = stats.LeftDownSuccesses + 1 }
                : stats with { LeftUpSuccesses = stats.LeftUpSuccesses + 1 };
            Succeeded();
        }
    }
    private void Succeeded() => stats = stats with { Successes = stats.Successes + 1, LastSuccessAtTicks = Stopwatch.GetTimestamp() };
    private void Failed(string kind, Exception error)
    {
        stats = stats with { Failures = stats.Failures + 1, LastFailureAtTicks = Stopwatch.GetTimestamp() };
        recorder?.Event("mouse_output_failure", ("mouseBackend", BackendName), ("kind", kind), ("error", error.Message));
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            Exception? failure = null;
            try { if (held) LeftUp(); } catch (Exception e) { failure = e; }
            try { native.Dispose(); } catch (Exception e) { Failed("destroy", e); failure ??= e; }
            finally { disposed = true; }
            if (failure is not null) throw new IOException("libvirtualhid cleanup failed: " + failure.Message, failure);
        }
    }
}

// Fake implementations never load the DLL or inject operating-system input.
internal interface IVirtualHidMouse : IDisposable
{
    string DeviceIdentity { get; }
    void Move(int dx, int dy);
    void LeftDown();
    void LeftUp();
}
