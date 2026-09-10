using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Rightpad.Receiver;

internal sealed class WindowsMouseOutput
{
    internal const uint InputMouse = 0;
    internal const uint MouseMove = 0x0001;
    internal const uint MouseLeftDown = 0x0002;
    internal const uint MouseLeftUp = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    // MOUSEINPUT is the largest member of Win32 INPUT's native union.
    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeInput
    {
        public uint Type;
        public InputUnion Data;
    }

    [DllImport("user32.dll", EntryPoint = "SendInput", ExactSpelling = true, SetLastError = true)]
    private static extern uint SendInput(uint count, ref NativeInput input, int size);

    internal delegate uint SendInputCall(uint count, ref NativeInput input, int size);
    private readonly SendInputCall send;
    private readonly Func<int> getError;
    private static readonly int InputSize = Marshal.SizeOf<NativeInput>();

    private long successfulEvents, failedCalls, successfulCalls, allFailedCalls, lastSuccessfulAtTicks, lastFailedAtTicks;
    public long SuccessfulEvents => Interlocked.Read(ref successfulEvents);
    public long FailedCalls => Interlocked.Read(ref failedCalls);
    public long SuccessfulCalls => Interlocked.Read(ref successfulCalls);
    public long AllFailedCalls => Interlocked.Read(ref allFailedCalls);
    public long LastSuccessfulAtTicks => Interlocked.Read(ref lastSuccessfulAtTicks);
    public long LastFailedAtTicks => Interlocked.Read(ref lastFailedAtTicks);
    private readonly Func<long> monotonicNow;
    private readonly FlightRecorder? flightRecorder;
    private long leftDownSuccess, leftUpSuccess, leftButtonFailures;
    public long LeftDownSuccess => Interlocked.Read(ref leftDownSuccess);
    public long LeftUpSuccess => Interlocked.Read(ref leftUpSuccess);
    public long LeftButtonFailures => Interlocked.Read(ref leftButtonFailures);

    public void LeftDown() => Button(MouseLeftDown);
    public void LeftUp() => Button(MouseLeftUp);

    private void Button(uint flags)
    {
        var input = new NativeInput
        {
            Type = InputMouse,
            Data = new InputUnion { Mouse = new MouseInput { Flags = flags } }
        };
        uint inserted = send(1, ref input, InputSize);
        if (inserted != 1)
        {
            int error = getError();
            Interlocked.Increment(ref leftButtonFailures);
            RecordFailure("button", inserted, error);
            throw new Win32Exception(error,
                $"SendInput button={(flags == MouseLeftDown ? "LEFT_DOWN" : "LEFT_UP")} inserted={inserted} expected=1 win32Error={error}. Cause may not be reported by Windows (including UIPI).");
        }
        RecordSuccess();
        if (flags == MouseLeftDown) Interlocked.Increment(ref leftDownSuccess);
        else Interlocked.Increment(ref leftUpSuccess);
    }

    public WindowsMouseOutput(FlightRecorder? flightRecorder = null) : this(SendInput, Marshal.GetLastPInvokeError, null, flightRecorder) { }

    // Small native-call seam for verifying failure handling without injecting input.
    internal WindowsMouseOutput(SendInputCall send, Func<int> getError, Func<long>? monotonicNow = null, FlightRecorder? flightRecorder = null)
    {
        this.send = send;
        this.getError = getError;
        this.monotonicNow = monotonicNow ?? Stopwatch.GetTimestamp;
        this.flightRecorder = flightRecorder;
    }

    public void Move(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        var input = new NativeInput
        {
            Type = InputMouse,
            Data = new InputUnion { Mouse = new MouseInput { Dx = dx, Dy = dy, Flags = MouseMove } }
        };
        uint inserted = send(1, ref input, InputSize);
        if (inserted != 1)
        {
            int error = getError();
            Interlocked.Increment(ref failedCalls);
            RecordFailure("move", inserted, error);
            throw new Win32Exception(error,
                $"SendInput inserted={inserted} expected=1 win32Error={error}. Cause may not be reported by Windows (including UIPI).");
        }
        Interlocked.Increment(ref successfulEvents);
        RecordSuccess();
    }
    private void RecordSuccess() { Interlocked.Increment(ref successfulCalls); Interlocked.Exchange(ref lastSuccessfulAtTicks, monotonicNow()); }
    private void RecordFailure(string kind, uint inserted, int error)
    {
        Interlocked.Increment(ref allFailedCalls); Interlocked.Exchange(ref lastFailedAtTicks, monotonicNow());
        flightRecorder?.Event("send_input_failure", ("kind", kind), ("inserted", inserted), ("win32Error", error));
    }
}
