using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using static Rightpad.Receiver.Tests.Program;
using static Rightpad.Receiver.Tests.RawMotionProcessorTests;

namespace Rightpad.Receiver.Tests;

internal static class VirtualHidTests
{
    private sealed class FakeNative : IVirtualHidMouse
    {
        public string DeviceIdentity => "fake-owned-node";
        public readonly ConcurrentQueue<(string Kind, int X, int Y)> Events = new();
        public bool FailMove, FailDown, FailUp, FailDispose;
        public void Move(int dx, int dy) { Events.Enqueue(("move", dx, dy)); if (FailMove) throw new IOException("report submission failed"); }
        public void LeftDown() { Events.Enqueue(("down", 0, 0)); if (FailDown) throw new IOException("DOWN failed"); }
        public void LeftUp() { Events.Enqueue(("up", 0, 0)); if (FailUp) throw new IOException("UP failed"); }
        public void Dispose() { Events.Enqueue(("dispose", 0, 0)); if (FailDispose) throw new IOException("destroy failed"); }
    }
    public static void Arguments()
    {
        Equal(MouseBackend.SendInput, Receiver.Program.ParseLaunchArguments([]).Backend, "default remains SendInput");
        Equal(MouseBackend.SendInput, Receiver.Program.ParseLaunchArguments(["--dev-mouse-backend", "sendinput"]).Backend, "explicit SendInput");
        var selected = Receiver.Program.ParseLaunchArguments(["--dev-mouse-backend", "virtualhid"]);
        Equal(MouseBackend.VirtualHid, selected.Backend, "explicit Virtual HID");
        Equal(Receiver.Program.LaunchMode.Gui, selected.Mode, "GUI retained");
        foreach (var args in new[] { new[] { "--dev-mouse-backend" }, new[] { "--dev-mouse-backend", "auto" },
            new[] { "--dev-mouse-backend", "virtualhid", "--dev-mouse-backend", "sendinput" },
            new[] { "--diagnostics", "--dev-mouse-backend", "virtualhid" } })
            Throws<ArgumentException>(() => Receiver.Program.ParseLaunchArguments(args));
    }
    public static void Mapping()
    {
        var native = new FakeNative();
        using var mouse = new LibVirtualHidMouseOutput(native);
        mouse.Move(0, 0);
        mouse.Move(int.MinValue, int.MaxValue);
        mouse.Move(65536, -65537);
        mouse.LeftDown(); mouse.LeftUp();
        Check(native.Events.ToArray().SequenceEqual(new[] { ("move", int.MinValue, int.MaxValue), ("move", 65536, -65537),
            ("down", 0, 0), ("up", 0, 0) }), "int32 unchanged, no clamp, exact left mapping");
        Equal("libvirtualhid", mouse.BackendName, "backend identity");
        Equal("fake-owned-node", mouse.DeviceIdentity, "device identity");
        Equal(4L, mouse.Stats.Successes, "API operation count");
        Equal(2L, mouse.Stats.MoveSuccesses, "movement count");
        Equal(2147549184L, mouse.Stats.AbsDx, "int.MinValue absolute count widened");
    }
    public static void Cleanup()
    {
        var native = new FakeNative(); var mouse = new LibVirtualHidMouseOutput(native);
        mouse.LeftDown(); mouse.Dispose(); mouse.Dispose();
        Check(native.Events.Select(e => e.Kind).SequenceEqual(new[] { "down", "up", "dispose" }), "release before destroy, idempotent");
        Throws<ObjectDisposedException>(() => mouse.Move(1, 0));
        foreach (bool downFailure in new[] { false, true })
        {
            native = new() { FailDown = downFailure, FailUp = true };
            mouse = new(native);
            if (downFailure) Throws<IOException>(mouse.LeftDown); else mouse.LeftDown();
            Throws<IOException>(mouse.Dispose);
            Equal("dispose", native.Events.Last().Kind, "destroy despite release failure");
            Check(mouse.Stats.Failures > 0, "failures counted");
        }
        native = new() { FailDispose = true }; mouse = new(native);
        Throws<IOException>(mouse.Dispose);
        Equal(1L, mouse.Stats.Failures, "destroy failure reported");
    }
    public static async Task InitializationFailure()
    {
        int attempts = 0;
        var runtime = new ReceiverRuntime(new(), TextWriter.Null, new(IPAddress.Loopback, 0),
            () => { attempts++; throw new IOException("license unavailable"); }, backend: MouseBackend.VirtualHid);
        Equal("libvirtualhid", runtime.CaptureSnapshot().MouseBackend, "selected identity before start");
        await runtime.StartAsync();
        Equal(ReceiverState.Error, runtime.CaptureSnapshot().RuntimeState, "selected failure is Error");
        Equal("libvirtualhid", runtime.CaptureSnapshot().MouseBackend, "failed backend identity");
        Equal(1, attempts, "no retry/fallback");
        Check(runtime.LocalEndpoint is null && runtime.Completion.IsCompleted, "no UDP admission after failed initialization");
        Check(runtime.CaptureSnapshot().LastError!.Contains("license unavailable"), "actionable error");
        await runtime.StopAsync();
    }
    public static async Task RuntimeLifecycle()
    {
        var instances = new List<FakeNative>();
        var settings = new RuntimeSettingsStore(RuntimeSettings.Default with { ClickHoldMs = 5000 });
        var runtime = new ReceiverRuntime(settings, TextWriter.Null, new(IPAddress.Loopback, 0),
            () => { var n = new FakeNative(); instances.Add(n); return new LibVirtualHidMouseOutput(n); }, backend: MouseBackend.VirtualHid);
        using var sender = new UdpClient();
        try
        {
            await runtime.StartAsync();
            var endpoint = runtime.LocalEndpoint!;
            await sender.SendAsync(PacketDecoderTests.Encode(TouchEventType.Down, 1, 0, new TouchSample(0, 0, 0)), endpoint);
            await sender.SendAsync(PacketDecoderTests.Encode(TouchEventType.Up, 1, 1, new TouchSample(1, 1, 0)), endpoint);
            await RuntimeTests.Until(() => instances[0].Events.Any(e => e.Kind == "down"));
            var s = runtime.CaptureSnapshot();
            Equal("libvirtualhid", s.MouseBackend, "actual injected backend");
            Equal(0L, s.SendInputSuccesses, "never label HID as SendInput");
            Check(s.MouseOutputSuccesses >= 2 && s.LastSuccessfulMouseOutputAtTicks > 0, "generic counters active");
            await runtime.StopAsync();
            Check(instances[0].Events.Select(e => e.Kind).SequenceEqual(new[] { "move", "down", "up", "dispose" }), "Runtime releases held click before disposing output");
            using var rebound = new UdpClient(endpoint);
            await runtime.StartAsync();
            Equal(2, instances.Count, "fresh output for each Runtime Run");
            Equal(0L, runtime.CaptureSnapshot().MouseOutputSuccesses, "fresh counters");
        }
        finally { await runtime.StopAsync(); }
    }
    public static async Task RuntimeReportFailure()
    {
        var native = new FakeNative { FailMove = true };
        var runtime = new ReceiverRuntime(new(), TextWriter.Null, new(IPAddress.Loopback, 0),
            () => new LibVirtualHidMouseOutput(native), backend: MouseBackend.VirtualHid);
        using var sender = new UdpClient();
        try
        {
            await runtime.StartAsync();
            await sender.SendAsync(PacketDecoderTests.Encode(TouchEventType.Down, 1, 0, new TouchSample(0, 0, 0)), runtime.LocalEndpoint!);
            await sender.SendAsync(PacketDecoderTests.Encode(TouchEventType.Move, 1, 1, new TouchSample(1, 1, 0)), runtime.LocalEndpoint!);
            await RuntimeTests.Until(() => runtime.Completion.IsCompleted);
            var s = runtime.CaptureSnapshot();
            Equal(ReceiverState.Error, s.RuntimeState, "report failure stops Runtime");
            Equal(1L, s.MouseOutputFailures, "HID failure count");
            Equal(0L, s.SendInputFailures, "no mislabelled failure");
            Check(native.Events.Select(e => e.Kind).SequenceEqual(new[] { "move", "dispose" }), "single attempt, destroyed on Error");
        }
        finally { await runtime.StopAsync(); }
    }
    public static async Task BindFailureCleanup()
    {
        using var occupied = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var native = new FakeNative();
        var runtime = new ReceiverRuntime(new(), TextWriter.Null, (IPEndPoint)occupied.Client.LocalEndPoint!,
            () => new LibVirtualHidMouseOutput(native), backend: MouseBackend.VirtualHid);
        await runtime.StartAsync();
        Equal(ReceiverState.Error, runtime.CaptureSnapshot().RuntimeState, "bind error");
        Equal("dispose", native.Events.Single().Kind, "creation followed by bind failure destroys device");
        await runtime.StopAsync();
    }
}
