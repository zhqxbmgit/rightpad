using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using static Rightpad.Receiver.Tests.Program;
using static Rightpad.Receiver.Tests.RawMotionProcessorTests;

namespace Rightpad.Receiver.Tests;

internal static class VirtualGamepadTests
{
    public static readonly (string Name, Func<Task> Run)[] Cases =
    [
        Sync("immutable neutral", Neutral),
        Sync("A full state", () => Button(XboxGamepadButtons.A)),
        Sync("B full state", () => Button(XboxGamepadButtons.B)),
        Sync("Y full state", () => Button(XboxGamepadButtons.Y)),
        Sync("B to Y replacement and success stats", Replacement),
        Sync("ABI layout and all fields", NativeMapping),
        Sync("old ABI rejected before create", AbiMismatch),
        Sync("native create failure", NativeCreateFailure),
        Sync("native state and consuming destroy failures", NativeFailures),
        Sync("neutral before idempotent dispose", DisposeNeutral),
        Sync("report failure neutralizes and isolates", ReportFailure),
        Sync("neutral failure still destroys", NeutralFailure),
        Sync("destroy failure recorded", DestroyFailure),
        ("gamepad create failure preserves mouse pipeline", () => RuntimeFailure(true)),
        ("gamepad report failure preserves mouse pipeline", () => RuntimeFailure(false)),
        ("gamepad Stop neutral before mouse cleanup", RuntimeStop),
        ("gamepad Safe Restart fresh neutral", () => Restart(false)),
        ("gamepad fallback recovery fresh neutral", () => Restart(true)),
        ("gamepad Dispose failure preserves mouse cleanup", RuntimeDisposeFailure)
    ];
    private static (string, Func<Task>) Sync(string name, Action action) =>
        ("gamepad " + name, () => { action(); return Task.CompletedTask; });

    private sealed class FakeNative : IVirtualHidGamepad
    {
        public string DeviceIdentity => "fake-xbox360";
        public readonly ConcurrentQueue<XboxGamepadState> States = new();
        public bool FailState, FailDispose;
        public int Disposals;
        public Action? OnDispose;
        public void SetState(XboxGamepadState state)
        { States.Enqueue(state); if (FailState) throw new IOException("gamepad report failure"); }
        public void Dispose()
        { Disposals++; OnDispose?.Invoke(); if (FailDispose) throw new IOException("gamepad destroy failure"); }
    }
    private sealed class FakeApi : NativeVirtualHidGamepad.IApi
    {
        public int Version = 2, CreateStatus, SetStatus, DestroyStatus, Creates, Destroys;
        public NativeVirtualHidGamepad.NativeState State;
        public int AbiVersion() => Version;
        public int Create(out IntPtr handle, StringBuilder identity, int identitySize, StringBuilder error, int errorSize)
        { Creates++; handle = CreateStatus == 0 ? new IntPtr(123) : IntPtr.Zero; identity.Append("fake-api-node"); error.Append("create fault"); return CreateStatus; }
        public int SetState(IntPtr handle, in NativeVirtualHidGamepad.NativeState state, StringBuilder error, int size)
        { Equal(new IntPtr(123), handle, "independent handle"); State = state; error.Append("state fault"); return SetStatus; }
        public int Destroy(IntPtr handle, StringBuilder? error, int size)
        { Destroys++; error?.Append("destroy fault"); return DestroyStatus; }
    }
    private static void Neutral()
    {
        Equal(default(XboxGamepadState), XboxGamepadState.Neutral, "all fields default");
        var pressed = XboxGamepadState.Neutral with { Buttons = XboxGamepadButtons.B };
        Equal(XboxGamepadButtons.B, pressed.Buttons, "value copy");
        Equal(XboxGamepadButtons.None, XboxGamepadState.Neutral.Buttons, "neutral cannot mutate");
    }
    private static void Button(XboxGamepadButtons button)
    {
        var native = new FakeNative(); using var pad = new LibVirtualHidXboxGamepad(native);
        Check(pad.SetState(new(button)), "submission succeeds");
        Equal(new XboxGamepadState(button), native.States.Last(), "only requested button");
        Equal(XboxGamepadState.Neutral, native.States.First(), "starts neutral");
    }
    private static void Replacement()
    {
        var native = new FakeNative(); using var pad = new LibVirtualHidXboxGamepad(native);
        pad.SetState(new(XboxGamepadButtons.B, 255, 255, -32768, 32767, 123, -456));
        pad.SetState(new(XboxGamepadButtons.Y));
        Equal(3, native.States.Count, "one full submission per replacement");
        Equal(new XboxGamepadState(XboxGamepadButtons.Y), native.States.Last(), "B, triggers and axes cleared");
        Equal(new GamepadOutputStats(3, 0), pad.Stats, "separate gamepad counters");
        Check(pad.Available && pad.LastError is null, "healthy");
    }
    private static void NativeMapping()
    {
        Equal(12, Marshal.SizeOf<NativeVirtualHidGamepad.NativeState>(), "ABI size");
        Equal(4, Marshal.OffsetOf<NativeVirtualHidGamepad.NativeState>("LeftThumbX").ToInt32(), "axis offset");
        Equal(10, Marshal.OffsetOf<NativeVirtualHidGamepad.NativeState>("RightThumbY").ToInt32(), "last axis offset");
        var api = new FakeApi(); using var native = new NativeVirtualHidGamepad(api);
        native.SetState(new(XboxGamepadButtons.A | XboxGamepadButtons.B | XboxGamepadButtons.Y, 17, 255, -32768, 32767, -1234, 5678));
        Equal((ushort)11, api.State.Buttons, "logical ABI bits, not XInput masks");
        Equal((byte)17, api.State.LeftTrigger, "left trigger"); Equal((byte)255, api.State.RightTrigger, "right trigger");
        Equal(short.MinValue, api.State.LeftThumbX, "LX"); Equal(short.MaxValue, api.State.LeftThumbY, "LY");
        Equal((short)-1234, api.State.RightThumbX, "RX"); Equal((short)5678, api.State.RightThumbY, "RY");
        Equal("fake-api-node", native.DeviceIdentity, "native identity");
    }
    private static void AbiMismatch()
    {
        var api = new FakeApi { Version = 1 };
        Throws<IOException>(() => new NativeVirtualHidGamepad(api));
        Equal(0, api.Creates, "no ABI2 symbol called for ABI1");
        Throws<IOException>(() => NativeVirtualHidAbi.Validate(1)); // Same guard used by mouse.
        NativeVirtualHidAbi.Validate(2);
    }
    private static void NativeCreateFailure()
    {
        var api = new FakeApi { CreateStatus = 3 };
        Throws<IOException>(() => new NativeVirtualHidGamepad(api));
        Equal(1, api.Creates, "single create attempt"); Equal(0, api.Destroys, "failed create owns no handle");
    }
    private static void NativeFailures()
    {
        var api = new FakeApi { SetStatus = 3, DestroyStatus = 3 };
        var native = new NativeVirtualHidGamepad(api);
        Throws<IOException>(() => native.SetState(new(XboxGamepadButtons.B)));
        Throws<IOException>(native.Dispose); native.Dispose();
        Equal(1, api.Destroys, "destroy consumes handle even on error");
        Throws<ObjectDisposedException>(() => native.SetState(XboxGamepadState.Neutral));
    }
    private static void DisposeNeutral()
    {
        var native = new FakeNative(); var pad = new LibVirtualHidXboxGamepad(native);
        pad.SetState(new(XboxGamepadButtons.B));
        native.OnDispose = () => Equal(XboxGamepadState.Neutral, native.States.Last(), "neutral precedes native destruction");
        pad.Dispose(); pad.Dispose();
        Equal(1, native.Disposals, "idempotent"); Check(!pad.Available, "disposed unavailable");
        Check(!pad.SetState(new(XboxGamepadButtons.A)), "closed state rejected");
    }
    private static void ReportFailure()
    {
        var native = new FakeNative(); var pad = new LibVirtualHidXboxGamepad(native);
        native.FailState = true;
        Check(!pad.SetState(new(XboxGamepadButtons.B)), "failure returned");
        Check(!pad.Available && pad.LastError!.Contains("report failure"), "explicit unavailable/error");
        Equal(2L, pad.Stats.Failures, "report plus best effort neutral failures");
        Equal(1, native.Disposals, "failed device removed");
        Equal(XboxGamepadState.Neutral, native.States.Last(), "last attempted report neutral");
        pad.Dispose(); Equal(1, native.Disposals, "no second removal");
    }
    private static void NeutralFailure()
    {
        var native = new FakeNative(); var pad = new LibVirtualHidXboxGamepad(native);
        native.FailState = true; pad.Dispose();
        Equal(1, native.Disposals, "neutral failure cannot skip destroy"); Equal(1L, pad.Stats.Failures, "failure counted");
    }
    private static void DestroyFailure()
    {
        var native = new FakeNative { FailDispose = true }; var pad = new LibVirtualHidXboxGamepad(native);
        pad.Dispose(); pad.Dispose();
        Equal(1, native.Disposals, "destroy not retried"); Equal(1L, pad.Stats.Failures, "failure counted");
        Check(pad.LastError!.Contains("destroy failure"), "cause visible");
    }
    private static async Task RuntimeFailure(bool createFailure)
    {
        var mouse = new VirtualHidTests.FakeNative(); var native = new FakeNative();
        var runtime = new ReceiverRuntime(new(), TextWriter.Null, MouseBackend.VirtualHid, new(IPAddress.Loopback, 0),
            () => new LibVirtualHidMouseOutput(mouse), gamepadFactory: () => createFailure
                ? throw new IOException("gamepad license failure") : new LibVirtualHidXboxGamepad(native));
        try
        {
            await runtime.StartAsync();
            if (!createFailure) { native.FailState = true; Check(!runtime.SetGamepadState(new(XboxGamepadButtons.B)), "report failed"); }
            using var sender = new UdpClient();
            await sender.SendAsync(PacketDecoderTests.Encode(TouchEventType.Down, 1, 0, new TouchSample(0, 0, 0)), runtime.LocalEndpoint!);
            await sender.SendAsync(PacketDecoderTests.Encode(TouchEventType.Move, 1, 1, new TouchSample(1, 10, 0)), runtime.LocalEndpoint!);
            await sender.SendAsync(PacketDecoderTests.Encode(TouchEventType.Up, 1, 2, new TouchSample(2, 10, 0)), runtime.LocalEndpoint!);
            await RuntimeTests.Until(() => mouse.Events.Any(e => e.Kind == "move"));
            var s = runtime.CaptureSnapshot();
            Equal(ReceiverState.Running, s.RuntimeState, "mouse Runtime survives");
            Check(!s.GamepadAvailable && s.GamepadFailures > 0 && s.LastGamepadError is not null, "gamepad failure visible");
            Equal(0L, s.MouseOutputFailures, "mouse counters untouched"); Check(s.LastError is null, "no Runtime error");
        }
        finally { await runtime.StopAsync(); }
        Equal("dispose", mouse.Events.Last().Kind, "mouse cleanup survives");
    }
    private static async Task RuntimeStop()
    {
        var mouse = new VirtualHidTests.FakeNative(); var native = new FakeNative();
        native.OnDispose = () =>
        {
            Equal(XboxGamepadState.Neutral, native.States.Last(), "neutral on Stop");
            Check(!mouse.Events.Any(e => e.Kind == "dispose"), "gamepad before mouse");
        };
        var runtime = new ReceiverRuntime(new(), TextWriter.Null, MouseBackend.VirtualHid, new(IPAddress.Loopback, 0),
            () => new LibVirtualHidMouseOutput(mouse), gamepadFactory: () => new LibVirtualHidXboxGamepad(native));
        await runtime.StartAsync(); Check(runtime.SetGamepadState(new(XboxGamepadButtons.Y)), "pressed");
        await runtime.StopAsync(); await runtime.StopAsync();
        Equal(1, native.Disposals, "one destruction"); Equal("dispose", mouse.Events.Last().Kind, "mouse last");
        Check(!runtime.SetGamepadState(new(XboxGamepadButtons.A)), "stopped rejects reports");
        Check(!runtime.CaptureSnapshot().GamepadAvailable, "stopped unavailable");
    }
    private static async Task Restart(bool fallback)
    {
        int attempt = 0;
        var pads = new List<FakeNative>(); var mice = new List<VirtualHidTests.FakeNative>();
        var store = new RuntimeSettingsStore(RuntimeSettings.Default with { SmoothingTauMs = 24, SmoothingSupportMs = 120 });
        var runtime = new ReceiverRuntime(store, TextWriter.Null, MouseBackend.VirtualHid, new(IPAddress.Loopback, 0), () =>
        {
            attempt++;
            if (attempt > 1) Check(pads.All(p => p.Disposals == 1 && p.States.Last() == XboxGamepadState.Neutral), "previous run neutral/closed before restart");
            if (fallback && attempt is 2 or 3) throw new IOException("target start fault");
            var mouse = new VirtualHidTests.FakeNative(); mice.Add(mouse); return new LibVirtualHidMouseOutput(mouse);
        }, motionMode: MotionModes.ProductionMode, useProductMotionSettings: true, gamepadFactory: () =>
        { var pad = new FakeNative(); pads.Add(pad); return new LibVirtualHidXboxGamepad(pad); });
        try
        {
            await runtime.StartAsync(); runtime.SetGamepadState(new(XboxGamepadButtons.B));
            store.Publish(store.Current with { SmoothingTauMs = 18, SmoothingSupportMs = 90, SensitivityX = 3, SensitivityY = 3 });
            var result = await runtime.RestartAsync();
            Equal(!fallback, result.Succeeded, "target result"); Equal(fallback, result.Restored, "fallback result");
            Equal(fallback ? 4 : 2, attempt, "bounded attempts unchanged");
            Equal(2, pads.Count, "fresh pad on successful mouse creation only");
            Check(pads[1].States.All(s => s == XboxGamepadState.Neutral), "new run entirely neutral");
            Equal(fallback ? 24d : 18d, runtime.ActiveMotionConfiguration.FiniteCriticalTauMs, "active smoothing preserved");
            Equal(18, store.Current.SmoothingTauMs, "fallback does not republish settings");
            Equal(3d, runtime.ActiveSensitivity.X, "committed sensitivity preserved");
            Equal(ReceiverState.Running, runtime.CaptureSnapshot().RuntimeState, "new run healthy");
            Check(runtime.CaptureSnapshot().GamepadAvailable, "new pad available");
        }
        finally { await runtime.StopAsync(); }
        Check(pads.All(p => p.Disposals == 1 && p.States.Last() == XboxGamepadState.Neutral), "every pad neutral/disposed");
    }
    private static async Task RuntimeDisposeFailure()
    {
        var mouse = new VirtualHidTests.FakeNative(); var native = new FakeNative { FailDispose = true };
        var runtime = new ReceiverRuntime(new(), TextWriter.Null, MouseBackend.VirtualHid, new(IPAddress.Loopback, 0),
            () => new LibVirtualHidMouseOutput(mouse), gamepadFactory: () => new LibVirtualHidXboxGamepad(native));
        await runtime.StartAsync(); await runtime.StopAsync();
        var s = runtime.CaptureSnapshot();
        Equal(ReceiverState.Stopped, s.RuntimeState, "gamepad destroy cannot fail Runtime");
        Equal("dispose", mouse.Events.Last().Kind, "mouse still destroyed");
        Check(s.GamepadFailures == 1 && s.LastGamepadError!.Contains("destroy failure"), "separate error reported");
        Equal(0L, s.MouseOutputFailures, "no fake mouse failure");
    }
}
