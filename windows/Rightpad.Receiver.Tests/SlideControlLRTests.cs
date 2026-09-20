using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class SlideControlLRTests
{
    private static readonly XboxGamepadButtons[] Buttons = [XboxGamepadButtons.X, XboxGamepadButtons.DpadLeft,
        XboxGamepadButtons.DpadRight, XboxGamepadButtons.DpadUp];
    private static readonly IPAddress Ip = IPAddress.Loopback;
    private static long T(double ms) => (long)(ms * Stopwatch.Frequency / 1000);
    public static readonly (string Name, Func<Task> Run)[] Cases =
    [
        Sync("logical bits / 30-byte decoder / native ABI", Mapping),
        Sync("X minimum dwell / Dpad replacement", Replacement),
        Sync("duplicate stale FORCE_NEUTRAL and lease", Ordering),
        Sync("disconnect run replacement Stop Dispose", Safety),
        Sync("backend failure isolation", Failure),
        ("LR real UDP Runtime Restart and Stop", RuntimeRestart)
    ];
    private static (string, Func<Task>) Sync(string name, Action action) =>
        ("LR " + name, () => { action(); return Task.CompletedTask; });
    private static void Mapping()
    {
        ushort[] expected = [0x0004, 0x2000, 0x4000, 0x0800];
        for (int i = 0; i < Buttons.Length; i++) {
            var state = new XboxGamepadState(Buttons[i]);
            var wire = GamepadPacketTests.Encode(1, 0, state);
            Equal(30, wire.Length, "unchanged exact packet size");
            Equal(expected[i], (ushort)Buttons[i], "pre-existing logical bits");
            Check(PacketDecoder.TryDecodeGamepad(wire, out var packet, out var error), error);
            Equal(state, packet.State, "Dpad decoded as buttons, analog zero");
            var native = new NativeVirtualHidGamepad.NativeState(packet.State);
            Equal(expected[i], native.Buttons, "native ABI keeps logical bits");
            Equal((short)0, native.LeftThumbX, "not left stick"); Equal((short)0, native.LeftThumbY, "not left stick");
        }
    }
    private sealed class Fixture
    {
        public long Now;
        public readonly List<XboxGamepadState> Output = [];
        public readonly GamepadSessionProcessor Processor;
        public Fixture() {
            Processor = new(s => { Output.Add(s); return true; }, timestamp: () => Now);
            Processor.UpdatePresence(new(1, 0, true, Ip.ToString()));
        }
        public bool Send(uint seq, XboxGamepadButtons buttons, double ms, ushort dwell = 0, bool force = false) {
            Now = T(ms);
            return Processor.Process(new(1, seq, new(buttons), dwell, force), Ip, Now);
        }
        public void Tick(double ms) { Now = T(ms); Processor.CheckDeadlines(Now); }
    }
    private static void Replacement()
    {
        var tap = new Fixture(); tap.Send(0, XboxGamepadButtons.X, 0, 25); tap.Send(1, 0, 1);
        tap.Tick(24.999); Equal(new(XboxGamepadButtons.X), tap.Processor.State, "X not released early");
        tap.Tick(25); Equal(default, tap.Processor.State, "X minimum dwell release");
        foreach (var direction in Buttons.Skip(1)) {
            var f = new Fixture(); f.Send(0, XboxGamepadButtons.X, 0, 25); f.Send(1, 0, 1);
            f.Send(2, direction, 2); Equal(new(direction), f.Processor.State, "Dpad replaces X immediately");
            Check(f.Output.SequenceEqual(new[] {new XboxGamepadState(XboxGamepadButtons.X), new XboxGamepadState(direction)}),
                "one complete replacement, no X+Dpad or intermediate Neutral");
            f.Tick(25); Equal(new(direction), f.Processor.State, "old pending Neutral canceled");
            f.Send(3, 0, 26); Equal(default, f.Processor.State, "zero-dwell direction release immediate");
        }
    }
    private static void Ordering()
    {
        foreach (var button in Buttons) {
            var f = new Fixture(); f.Send(10, button, 0, 1000);
            Check(!f.Send(10, 0, 20, force: true) && !f.Send(9, 0, 21, force: true), "duplicate/stale safety rejected");
            f.Send(11, 0, 22, force: true); Equal(default, f.Processor.State, "new FORCE_NEUTRAL bypasses dwell");
            f.Send(12, button, 30, 1000); f.Tick(329); Equal(new(button), f.Processor.State, "lease not early");
            f.Tick(330); Equal(default, f.Processor.State, "lease overrides long dwell for X/Dpad");
        }
    }
    private static void Safety()
    {
        foreach (var button in Buttons) for (int kind = 0; kind < 4; kind++) {
            var f = new Fixture(); f.Send(0, button, 0, 1000); f.Send(1, 0, 1);
            switch (kind) {
                case 0: f.Processor.UpdatePresence(new(1, 0, false, Ip.ToString())); break;
                case 1: f.Processor.UpdatePresence(new(2, 0, true, Ip.ToString())); break;
                case 2: f.Processor.Stop(); break;
                case 3: using (var receiver = new UdpReceiver(new(Ip, 0), TextWriter.Null, gamepad: f.Processor)) { } break;
            }
            Equal(default, f.Processor.State, "safety releases " + button + " / " + kind);
            f.Tick(1000); Equal(2, f.Output.Count, "stale dwell callback cannot revive output");
        }
    }
    private sealed class Native : IVirtualHidGamepad
    {
        public string DeviceIdentity => "fake-lr";
        public readonly System.Collections.Concurrent.ConcurrentQueue<XboxGamepadState> States = new();
        public bool Fail; public int Disposals;
        public void SetState(XboxGamepadState state) { States.Enqueue(state); if (Fail) throw new IOException("LR backend failure"); }
        public void Dispose() { Disposals++; }
    }
    private static void Failure()
    {
        foreach (var button in Buttons) {
            var native = new Native(); using var pad = new LibVirtualHidXboxGamepad(native);
            Check(pad.SetState(new(button)), "LR output initially works"); native.Fail = true;
            Check(!pad.SetState(new(XboxGamepadButtons.B)), "failure isolated");
            Equal(default, native.States.Last(), "best-effort safety Neutral");
            Equal(1, native.Disposals, "failed device disposed");
        }
    }
    private static async Task RuntimeRestart()
    {
        var devices = new List<Native>();
        var runtime = new ReceiverRuntime(new(), TextWriter.Null, MouseBackend.VirtualHid, new(Ip, 0),
            () => new LibVirtualHidMouseOutput(new VirtualHidTests.FakeNative()), gamepadFactory: () => {
                var native = new Native(); devices.Add(native); return new LibVirtualHidXboxGamepad(native);
            });
        using var sender = new UdpClient();
        try {
            await runtime.StartAsync();
            for (int i = 0; i < Buttons.Length; i++) {
                var device = devices.Last(); ulong run = (ulong)i + 1;
                await sender.SendAsync(PresenceTests.Heartbeat(run), runtime.LocalEndpoint!);
                await sender.SendAsync(GamepadPacketTests.Encode(run, 0, new(Buttons[i]), 1000), runtime.LocalEndpoint!);
                await RuntimeTests.Until(() => device.States.Contains(new(Buttons[i])));
                Check((await runtime.RestartAsync()).Succeeded, "LR Runtime restart");
                Equal(default, device.States.Last(), "restart clears held X/Dpad"); Equal(1, device.Disposals, "one device disposed");
                Check(devices.Last().States.All(s => s == default), "new run starts Neutral");
            }
            await sender.SendAsync(PresenceTests.Heartbeat(99), runtime.LocalEndpoint!);
            await sender.SendAsync(GamepadPacketTests.Encode(99, 0, new(XboxGamepadButtons.DpadUp)), runtime.LocalEndpoint!);
            await RuntimeTests.Until(() => devices.Last().States.Contains(new(XboxGamepadButtons.DpadUp)));
        } finally { await runtime.StopAsync(); }
        Check(devices.All(d => d.States.Last() == default && d.Disposals == 1), "Stop cleans final held direction");
    }
}
