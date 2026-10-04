using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class GamepadStartupRecoveryTests
{
    private const string TransientText = "Polar validation is unavailable; the one-device fallback is already in use.";
    public static readonly (string Name, Func<Task> Run)[] Cases =
    [
        ("gamepad startup typed native classifier", () => { Classifier(); return Task.CompletedTask; }),
        ("gamepad startup first success has no retry", FirstSuccess),
        ("gamepad startup transient recovery and mouse isolation", Recovery),
        ("gamepad startup permanent failure does not retry", Permanent),
        ("gamepad startup permanent failure ends recovery", PermanentDuringRecovery),
        ("gamepad startup fixed deadline and bounded attempts", Timeout),
        ("gamepad startup independent hard attempt cap", AttemptCap),
        ("gamepad startup late wake skips expired create", LateWake),
        ("gamepad startup Stop cancels delay immediately", StopWaiting),
        ("gamepad startup cancellation versus completed delay", CancelledWake),
        ("gamepad startup Stop/create ownership race", StopCreate),
        ("gamepad startup serial creates and live mouse during create", SerialCreates),
        ("gamepad startup recovered transport latch and serial fence", TransportRecovery),
        ("gamepad startup runtime SetState failure never reopens", RuntimeFailure),
        ("gamepad startup failed candidate disposed without retry", CandidateFailure),
        ("gamepad startup unavailable candidate retains actual output error", UnavailableCandidate),
        ("gamepad startup flight events preserve history", FlightEvents)
    ];

    private static NativeVirtualHidGamepadException Transient() => new(8, TransientText, "create");
    private sealed class Api : NativeVirtualHidGamepad.IApi
    {
        public int Status = 8;
        public string Error = TransientText;
        public int AbiVersion() => 2;
        public int Create(out IntPtr handle, StringBuilder identity, int identitySize, StringBuilder error, int errorSize)
        { handle = IntPtr.Zero; error.Append(Error); return Status; }
        public int SetState(IntPtr handle, in NativeVirtualHidGamepad.NativeState state, StringBuilder error, int size) => 0;
        public int Destroy(IntPtr handle, StringBuilder? error, int size) => 0;
    }
    private static void Classifier()
    {
        try { _ = new NativeVirtualHidGamepad(new Api()); throw new Exception("Expected typed create error"); }
        catch (NativeVirtualHidGamepadException e)
        {
            Equal(8, e.Status, "native status"); Equal(TransientText, e.NativeError, "native error unformatted");
            Equal("create", e.Operation, "operation"); Check(e.IsStartupReadinessTransient, "known transient");
            Equal("libvirtualhid gamepad status=8: " + TransientText, e.Message, "original diagnostic preserved");
        }
        Check(!new NativeVirtualHidGamepadException(7, TransientText, "create").IsStartupReadinessTransient, "other status");
        Check(!new NativeVirtualHidGamepadException(8, "Permanent license unavailable", "create").IsStartupReadinessTransient, "other status=8");
        Check(!new NativeVirtualHidGamepadException(8, TransientText, "set_state").IsStartupReadinessTransient, "not output retry");
        Check(!new NativeVirtualHidGamepadException(8, TransientText, "destroy").IsStartupReadinessTransient, "not destroy retry");
    }
    private sealed class Pad : IVirtualGamepad
    {
        public string BackendName => LibVirtualHidXboxGamepad.Backend;
        public string DeviceIdentity => "startup-fake-owned-pad";
        public bool Available => Volatile.Read(ref Disposals) == 0 && Error is null;
        public string? LastError => Error;
        public string? Error;
        public GamepadOutputStats Stats => new(States.Count, 0);
        public readonly ConcurrentQueue<XboxGamepadState> States = new();
        public int Disposals;
        public bool FailState;
        public bool SetState(XboxGamepadState state)
        {
            if (FailState) throw new IOException("runtime output failure");
            States.Enqueue(state); return true;
        }
        public void Dispose() => Interlocked.Increment(ref Disposals);
    }
    private sealed record Wait(TimeSpan Duration, TaskCompletionSource<bool> Completion);
    private sealed class Scheduler
    {
        public readonly Channel<Wait> Waits = Channel.CreateUnbounded<Wait>();
        public long Ticks;
        public int Delays;
        public bool IgnoreCancellation;
        public Task Delay(TimeSpan duration, CancellationToken token)
        {
            Interlocked.Increment(ref Delays);
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Waits.Writer.TryWrite(new(duration, done));
            return IgnoreCancellation ? done.Task : done.Task.WaitAsync(token);
        }
        public Task<Wait> Next() => Waits.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        public void Complete(Wait wait, TimeSpan? elapsed = null)
        {
            Interlocked.Add(ref Ticks, (long)((elapsed ?? wait.Duration).TotalSeconds * Stopwatch.Frequency));
            wait.Completion.TrySetResult(true);
        }
    }
    private sealed class Harness : IAsyncDisposable
    {
        public readonly Scheduler Clock = new();
        public readonly VirtualHidTests.FakeNative Mouse = new();
        public readonly Pad Pad = new();
        public readonly ConcurrentQueue<string> Log = new();
        public readonly ReceiverRuntime Runtime;
        public int Calls, InCreate, MaxParallel;
        public Harness(Func<int, IVirtualGamepad> factory, FlightRecorder? recorder = null, bool discovery = false)
        {
            Runtime = new(new(), new QueueWriter(Log), MouseBackend.VirtualHid, new(IPAddress.Loopback, 0),
                () => new LibVirtualHidMouseOutput(Mouse), gamepadFactory: () =>
                {
                    int active = Interlocked.Increment(ref InCreate);
                    MaxParallel = Math.Max(MaxParallel, active);
                    try { return factory(Interlocked.Increment(ref Calls)); }
                    finally { Interlocked.Decrement(ref InCreate); }
                }, flightRecorder: recorder,
                discoveryEndpoint: discovery ? new(IPAddress.Loopback, 0) : null,
                identityFactory: () => Enumerable.Range(0, 16).Select(i => (byte)i).ToArray(),
                gamepadStartupDelay: Clock.Delay,
                gamepadStartupTimestamp: () => Volatile.Read(ref Clock.Ticks));
        }
        public async Task MouseInput()
        {
            using var sender = new UdpClient();
            await sender.SendAsync(PacketDecoderTests.Encode(TouchEventType.Down, 3, 0, new TouchSample(0, 0, 0)), Runtime.LocalEndpoint!);
            await sender.SendAsync(PacketDecoderTests.Encode(TouchEventType.Move, 3, 1, new TouchSample(1, 10, 0)), Runtime.LocalEndpoint!);
            await RuntimeTests.Until(() => Mouse.Events.Any(e => e.Kind == "move"));
            var s = Runtime.CaptureSnapshot();
            Equal(ReceiverState.Running, s.RuntimeState, "mouse runtime running");
            Check(s.MouseOutputSuccesses > 0 && s.MouseOutputFailures == 0 && s.LastError is null, "mouse works independently");
        }
        public async ValueTask DisposeAsync() => await Runtime.StopAsync();
    }
    private sealed class QueueWriter(ConcurrentQueue<string> lines) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string? value) => lines.Enqueue(value ?? "");
    }
    private static async Task FirstSuccess()
    {
        var pad = new Pad(); await using var h = new Harness(_ => pad);
        await h.Runtime.StartAsync(); Check(h.Runtime.CaptureSnapshot().GamepadAvailable, "first success");
        Equal(1, h.Calls, "one factory"); Equal(0, h.Clock.Delays, "no retry task/delay");
        await h.Runtime.StopAsync(); Equal(1, pad.Disposals, "one owner disposed once");
    }
    private static async Task Recovery()
    {
        var pad = new Pad(); await using var h = new Harness(n => n <= 2 ? throw Transient() : pad, discovery: true);
        await h.Runtime.StartAsync(); var first = await h.Clock.Next();
        Equal(TimeSpan.FromSeconds(2), first.Duration, "fixed delay");
        Check(!h.Runtime.CaptureSnapshot().GamepadAvailable && h.Runtime.CaptureSnapshot().LastGamepadError is not null, "unavailable during recovery");
        Check(h.Runtime.DiscoveryEndpoint is not null, "discovery started before license recovery");
        using (var probe = new UdpClient())
        {
            await probe.SendAsync(DiscoveryCodec.Discover(1), h.Runtime.DiscoveryEndpoint!);
            var offer = await probe.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Check(DiscoveryCodec.TryOffer(offer.Buffer, 1, out _), "discovery works while unavailable");
        }
        await h.MouseInput(); h.Clock.Complete(first); var second = await h.Clock.Next(); h.Clock.Complete(second);
        await RuntimeTests.Until(() => h.Runtime.CaptureSnapshot().GamepadAvailable);
        var s = h.Runtime.CaptureSnapshot();
        Equal(1L, s.RunId, "same runtime"); Equal(3, h.Calls, "fail/fail/success");
        Equal(0L, s.GamepadFailures, "recovered startup failures not current health errors");
        Check(s.LastGamepadError is null, "startup error cleared"); Equal(1, h.MaxParallel, "single live creation");
        Check(h.Runtime.SetGamepadState(new(XboxGamepadButtons.B)), "normal state after recovery");
        Equal(XboxGamepadButtons.B, pad.States.Last().Buttons, "state reaches pad");
        await h.Runtime.StopAsync(); Equal(1, pad.Disposals, "single owner");
    }
    private static async Task Permanent()
    {
        foreach (Exception error in new Exception[] { new IOException("DLL/ABI failure"),
            new NativeVirtualHidGamepadException(8, "Permanent license unavailable", "create"),
            new NativeVirtualHidGamepadException(8, TransientText, "set_state"), new ArgumentException("invalid argument") })
        {
            await using var h = new Harness(_ => throw error); await h.Runtime.StartAsync();
            Equal(1, h.Calls, "permanent one shot"); Equal(0, h.Clock.Delays, "no permanent retry"); await h.MouseInput();
        }
    }
    private static async Task PermanentDuringRecovery()
    {
        await using var h = new Harness(n => n == 1 ? throw Transient() : throw new IOException("permanent second failure"));
        await h.Runtime.StartAsync(); h.Clock.Complete(await h.Clock.Next());
        await RuntimeTests.Until(() => h.Log.Any(l => l.Contains("permanent second failure")));
        await h.Runtime.StopAsync(); Equal(2, h.Calls, "stops on permanent"); Equal(1, h.Clock.Delays, "no further delay");
    }
    private static async Task Timeout()
    {
        Equal(TimeSpan.FromSeconds(2), ReceiverRuntime.GamepadStartupRetryInterval, "production interval");
        Equal(TimeSpan.FromSeconds(120), ReceiverRuntime.GamepadStartupRetryWindow, "production window");
        Equal(60, ReceiverRuntime.GamepadStartupMaxRecoveryAttempts, "hard cap");
        await using var h = new Harness(_ => throw Transient()); await h.Runtime.StartAsync();
        for (int i = 0; i < 60; i++) h.Clock.Complete(await h.Clock.Next());
        await RuntimeTests.Until(() => h.Log.Any(l => l.StartsWith("gamepad_startup_retry_timeout:")));
        Equal(60, h.Calls, "initial + 59 before exclusive 120s deadline");
        Check(h.Runtime.CaptureSnapshot().LastGamepadError!.Contains(TransientText), "last real error retained");
        await h.MouseInput(); await h.Runtime.StopAsync(); Equal(60, h.Calls, "no post-timeout factory");
    }
    private static async Task LateWake()
    {
        await using var h = new Harness(_ => throw Transient()); await h.Runtime.StartAsync();
        h.Clock.Complete(await h.Clock.Next(), TimeSpan.FromSeconds(121));
        await RuntimeTests.Until(() => h.Log.Any(l => l.StartsWith("gamepad_startup_retry_timeout:")));
        Equal(1, h.Calls, "expired wake cannot create");
    }
    private static async Task AttemptCap()
    {
        await using var h = new Harness(_ => throw Transient()); await h.Runtime.StartAsync();
        for (int i = 0; i < 60; i++) h.Clock.Complete(await h.Clock.Next(), TimeSpan.Zero);
        await RuntimeTests.Until(() => h.Log.Any(l => l.StartsWith("gamepad_startup_retry_timeout:")));
        Equal(61, h.Calls, "initial plus at most 60 recoveries even with nonadvancing injected clock");
    }
    private static async Task StopWaiting()
    {
        await using var h = new Harness(_ => throw Transient()); await h.Runtime.StartAsync();
        _ = await h.Clock.Next(); await h.Runtime.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Equal(1, h.Calls, "cancellation without completing delay");
    }
    private static async Task CancelledWake()
    {
        await using var h = new Harness(_ => throw Transient()); h.Clock.IgnoreCancellation = true;
        await h.Runtime.StartAsync(); var wait = await h.Clock.Next(); var stop = h.Runtime.StopAsync();
        h.Clock.Complete(wait); await stop; Equal(1, h.Calls, "pre-create fence despite completed delay");
    }
    private static async Task StopCreate()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim(); var pad = new Pad();
        await using var h = new Harness(n => { if (n == 1) throw Transient(); entered.TrySetResult(true);
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); return n == 2 ? pad : new Pad(); });
        await h.Runtime.StartAsync(); h.Clock.Complete(await h.Clock.Next()); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stop = h.Runtime.StopAsync(); release.Set(); await stop;
        Equal(1, pad.Disposals, "rejected stale candidate disposed"); Check(!h.Runtime.CaptureSnapshot().GamepadAvailable, "not installed");
        await h.Runtime.StartAsync(); Check(h.Runtime.CaptureSnapshot().RunId == 2, "new run only after old creation joined");
    }
    private static async Task SerialCreates()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim(); var pad = new Pad();
        await using var h = new Harness(n => { if (n == 1) throw Transient(); entered.TrySetResult(true);
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); return pad; });
        await h.Runtime.StartAsync(); h.Clock.Complete(await h.Clock.Next()); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try { await Task.WhenAll(h.Runtime.StartAsync(), h.Runtime.StartAsync()); await h.MouseInput();
            Equal(2, h.Calls, "no parallel attempts while blocked native create"); Equal(1, h.MaxParallel, "single create"); }
        finally { release.Set(); }
        await RuntimeTests.Until(() => h.Runtime.CaptureSnapshot().GamepadAvailable);
    }
    private static async Task TransportRecovery()
    {
        var pad = new Pad(); await using var h = new Harness(n => n == 1 ? throw Transient() : pad);
        await h.Runtime.StartAsync(); var wait = await h.Clock.Next(); using var sender = new UdpClient();
        await sender.SendAsync(PresenceTests.Heartbeat(2), h.Runtime.LocalEndpoint!);
        await sender.SendAsync(GamepadPacketTests.Encode(2, 10, new(XboxGamepadButtons.B)), h.Runtime.LocalEndpoint!);
        await RuntimeTests.Until(() => h.Log.Contains("gamepad_processor_unavailable"));
        h.Clock.Complete(wait); await RuntimeTests.Until(() => h.Log.Any(l => l.StartsWith("gamepad_startup_recovered:")));
        Check(pad.States.All(s => s == XboxGamepadState.Neutral), "old held B never replayed");
        await sender.SendAsync(GamepadPacketTests.Encode(2, 10, new(XboxGamepadButtons.B)), h.Runtime.LocalEndpoint!);
        await sender.SendAsync(GamepadPacketTests.Encode(2, 11, new(XboxGamepadButtons.Y)), h.Runtime.LocalEndpoint!);
        await RuntimeTests.Until(() => pad.States.Any(s => s.Buttons == XboxGamepadButtons.Y));
        Check(!pad.States.Any(s => s.Buttons == XboxGamepadButtons.B), "serial watermark preserved");
        var delivery = StatusProtocol.Capture(h.Runtime.CaptureSnapshot(), 1, 1, Stopwatch.GetTimestamp());
        Check(delivery is not null && delivery.Snapshot.Flags.HasFlag(StatusFlags.GamepadAvailable) &&
            !delivery.Snapshot.Flags.HasFlag(StatusFlags.GamepadFailurePresent), "RPST XBOX healthy after startup recovery");
    }
    private static async Task RuntimeFailure()
    {
        var pad = new Pad(); await using var h = new Harness(n => n == 1 ? throw Transient() : pad);
        await h.Runtime.StartAsync(); h.Clock.Complete(await h.Clock.Next());
        await RuntimeTests.Until(() => h.Runtime.CaptureSnapshot().GamepadAvailable); pad.FailState = true;
        Check(!h.Runtime.SetGamepadState(new(XboxGamepadButtons.B)), "runtime failure");
        Equal(1, pad.Disposals, "backend closed"); Check(!h.Runtime.CaptureSnapshot().GamepadAvailable, "unavailable");
        await h.MouseInput(); Equal(2, h.Calls, "no new create for runtime failure"); Equal(1, h.Clock.Delays, "retry stays ended");
    }
    private static async Task CandidateFailure()
    {
        var pad = new Pad { FailState = true }; await using var h = new Harness(_ => pad);
        await h.Runtime.StartAsync(); Equal(1, pad.Disposals, "initial neutral exception disposes candidate");
        Equal(0, h.Clock.Delays, "SetState candidate failure not readiness retry"); await h.MouseInput();
    }
    private static async Task UnavailableCandidate()
    {
        var pad = new Pad { Error = "native initial output failure" };
        await using var h = new Harness(n => n == 1 ? throw Transient() : pad);
        await h.Runtime.StartAsync(); h.Clock.Complete(await h.Clock.Next());
        await RuntimeTests.Until(() => h.Runtime.CaptureSnapshot().GamepadDeviceIdentity is not null);
        Equal(pad.Error, h.Runtime.CaptureSnapshot().LastGamepadError, "latest real output error replaces old readiness error");
        Check(!h.Runtime.CaptureSnapshot().GamepadAvailable, "unavailable output candidate not recovered");
        await h.Runtime.StopAsync(); Equal(1, pad.Disposals, "unavailable candidate still has one owner");
        Equal(2, h.Calls, "does not retry initial output failure");
    }
    private sealed class Storage : IFlightRecordStorage
    {
        public readonly ConcurrentQueue<string> Lines = new();
        public void WriteLine(string line) => Lines.Enqueue(line);
        public void Dispose() { }
    }
    private static async Task FlightEvents()
    {
        var storage = new Storage(); var recorder = new FlightRecorder(storage); var pad = new Pad();
        await using var h = new Harness(n => n == 1 ? throw Transient() : pad, recorder);
        await h.Runtime.StartAsync(); h.Clock.Complete(await h.Clock.Next());
        await RuntimeTests.Until(() => h.Log.Any(l => l.StartsWith("gamepad_startup_recovered:")));
        await h.Runtime.StopAsync(); recorder.Dispose();
        foreach (string name in new[] { "gamepad_unavailable", "gamepad_backend", "gamepad_startup_retry", "gamepad_startup_recovered" })
            Check(storage.Lines.Any(l => l.Contains('"' + name + '"')), "flight event " + name);
        Check(storage.Lines.Any(l => l.Contains("\"startupFailures\":1")), "historical startup failure preserved");
    }
}
