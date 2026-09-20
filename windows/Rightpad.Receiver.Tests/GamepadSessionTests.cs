using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class GamepadSessionTests
{
    public static readonly (string Name, Func<Task> Run)[] Cases =
    [
        Sync("authority before admission and source correlation", Authority),
        Sync("Neutral/B/Y/A full replacements", Replacements),
        Sync("duplicate ignored without lease renewal", Duplicate),
        Sync("stale/out of order do not renew lease", Stale),
        Sync("uint32 wrap and ambiguous half range", Wrap),
        Sync("lease exact 300ms and expired replay rejection", Lease),
        Sync("valid held refresh extends lease", Refresh),
        Sync("sender change neutral/reset", SenderChange),
        Sync("presence disconnect neutral preserves sequence", Disconnect),
        Sync("same-run source change neutral preserves sequence", SourceChange),
        Sync("Stop neutral/idempotent/reject later input", Stop),
        Sync("output exception isolated", Failure),
        ("gamepad idle socket independent lease maintenance", IdleLease),
        ("gamepad real UDP held refresh above one second", HeldRefresh),
        ("gamepad transport malformed and Touch counters isolated", Transport),
        ("gamepad UDP output failure preserves mouse", RuntimeFailure),
        ("gamepad UDP Stop/Safe Restart neutral and new authority", RuntimeRestart)
    ];
    private static (string, Func<Task>) Sync(string name, Action action) =>
        ("gamepad session " + name, () => { action(); return Task.CompletedTask; });
    private static long T(double ms) => (long)(ms * Stopwatch.Frequency / 1000);
    private static readonly IPAddress Ip = IPAddress.Loopback;
    private static readonly XboxGamepadState B = new(XboxGamepadButtons.B), Y = new(XboxGamepadButtons.Y), A = new(XboxGamepadButtons.A);
    private sealed class Fixture
    {
        public readonly List<XboxGamepadState> Output = [];
        public readonly List<string> Log = [];
        public readonly GamepadSessionProcessor Processor;
        public Fixture(bool connected = true)
        {
            Processor = new(s => { Output.Add(s); return true; }, Log.Add);
            if (connected) Processor.UpdatePresence(new(1, 0, true, Ip.ToString()));
        }
        public bool Send(uint seq, XboxGamepadState state, double ms, ulong run = 1, IPAddress? ip = null) =>
            Processor.Process(new(run, seq, state), ip ?? Ip, T(ms));
    }
    private static void Authority()
    {
        var f = new Fixture(false); Check(!f.Send(0, B, 0), "type5 cannot establish run");
        f.Processor.UpdatePresence(new(1, 0, true, Ip.ToString()));
        Check(!f.Send(0, B, 0, 2), "other run rejected");
        Check(!f.Send(0, B, 0, ip: IPAddress.Parse("127.0.0.2")), "different source rejected");
        Check(f.Send(0, B, 0), "current sender accepted"); Equal(3L, f.Processor.Stats.Rejected, "authority rejections");
    }
    private static void Replacements()
    {
        var f = new Fixture(); f.Send(0, default, 0); f.Send(1, B, 1); f.Send(2, Y, 2); f.Send(3, A, 3); f.Send(4, default, 4);
        Check(f.Output.SequenceEqual(new[] {B, Y, A, XboxGamepadState.Neutral}), "one complete report per changed state");
    }
    private static void Duplicate()
    {
        var f = new Fixture(); f.Send(7, B, 0); Check(!f.Send(7, Y, 299), "same sequence even changed payload ignored");
        Equal(1, f.Output.Count, "no duplicate output"); f.Processor.CheckLease(T(300));
        Equal(XboxGamepadState.Neutral, f.Output.Last(), "duplicate did not renew");
    }
    private static void Stale()
    {
        var f = new Fixture(); f.Send(10, B, 0); f.Send(12, Y, 10);
        Check(!f.Send(11, B, 200) && !f.Send(9, A, 300), "reordered/old ignored");
        f.Processor.CheckLease(T(310)); Equal(default, f.Processor.State, "stale did not renew");
        Equal(2L, f.Processor.Stats.Stale, "stale counter independent");
    }
    private static void Wrap()
    {
        var f = new Fixture(); Check(f.Send(uint.MaxValue - 1, B, 0), "high baseline");
        Check(f.Send(uint.MaxValue, Y, 1) && f.Send(0, A, 2) && f.Send(1, B, 3), "wrap forward");
        Check(!f.Send(uint.MaxValue, Y, 4), "prewrap stale");
        Check(!f.Send(0x80000001, Y, 5), "half range rejected");
    }
    private static void Lease()
    {
        var f = new Fixture(); f.Send(5, B, 0); f.Processor.CheckLease(T(299.99)); Equal(B, f.Processor.State, "not early");
        f.Processor.CheckLease(T(300)); Equal(default, f.Processor.State, "deadline neutral");
        Check(!f.Send(5, B, 301) && !f.Send(4, Y, 302), "expired watermark retained");
        f.Processor.CheckLease(T(1000)); Equal(1L, f.Processor.Stats.LeaseExpirations, "once per lease");
        Check(f.Log.Any(l => l.StartsWith("gamepad_lease_expired")), "expiry logged");
    }
    private static void Refresh()
    {
        var f = new Fixture(); f.Send(0, B, 0); f.Send(1, B, 100); f.Send(2, B, 200);
        f.Processor.CheckLease(T(499)); Equal(B, f.Processor.State, "new sequences extend");
        Equal(1, f.Output.Count, "unchanged state does not duplicate HID output");
        f.Processor.CheckLease(T(500)); Equal(default, f.Processor.State, "expires from latest refresh");
    }
    private static void SenderChange()
    {
        var f = new Fixture(); f.Send(999, B, 0); f.Processor.UpdatePresence(new(2, 0, true, Ip.ToString()));
        Equal(default, f.Processor.State, "old neutral immediately"); Check(!f.Send(1000, B, 1), "old owner rejected");
        Check(f.Send(0, Y, 2, 2), "new sequence zero baseline");
    }
    private static void Disconnect()
    {
        var f = new Fixture(); f.Send(99, B, 0); f.Processor.UpdatePresence(new(1, 0, false, Ip.ToString()));
        Equal(default, f.Processor.State, "disconnect neutral"); Check(!f.Send(100, B, 1), "disconnected cannot reacquire with type5");
        f.Processor.UpdatePresence(new(1, 0, true, Ip.ToString()));
        Check(!f.Send(99, B, 2) && f.Send(100, Y, 3), "reconnect keeps watermark");
    }
    private static void SourceChange()
    {
        var f = new Fixture(); f.Send(10, B, 0); var ip = IPAddress.Parse("127.0.0.2");
        f.Processor.UpdatePresence(new(1, 0, true, ip.ToString()));
        Equal(default, f.Processor.State, "source transition clears held state");
        Check(!f.Send(11, B, 1), "prior source rejected");
        Check(!f.Send(10, B, 2, ip: ip) && f.Send(11, Y, 3, ip: ip), "new source keeps serial watermark");
    }
    private static void Stop()
    {
        var f = new Fixture(); f.Send(0, Y, 0); f.Processor.Stop(); f.Processor.Stop();
        Check(f.Output.SequenceEqual(new[] {Y, XboxGamepadState.Neutral}), "one release");
        Check(!f.Send(1, A, 1), "stopped rejects");
    }
    private static void Failure()
    {
        var processor = new GamepadSessionProcessor(_ => throw new IOException("injected"));
        processor.UpdatePresence(new(1, 0, true, Ip.ToString()));
        Check(!processor.Process(new(1, 0, B), Ip, 0), "exception contained");
        Check(!processor.Process(new(1, 1, Y), Ip, 1), "failed processor unavailable"); processor.Stop();
    }
    private static async Task IdleLease()
    {
        var state = XboxGamepadState.Neutral;
        var processor = new GamepadSessionProcessor(s => { state = s; return true; });
        using var receiver = new UdpReceiver(new(Ip, 0), TextWriter.Null, gamepad: processor);
        using var cancel = new CancellationTokenSource(); var run = receiver.RunAsync(cancel.Token);
        using var sender = new UdpClient();
        try
        {
            await sender.SendAsync(PresenceTests.Heartbeat(1), receiver.LocalEndpoint);
            await sender.SendAsync(GamepadPacketTests.Encode(1, 0, B), receiver.LocalEndpoint);
            await RuntimeTests.Until(() => processor.Stats.Accepted == 1);
            var time = Stopwatch.StartNew();
            await RuntimeTests.Until(() => processor.Stats.LeaseExpirations == 1);
            Check(time.ElapsedMilliseconds < 1000 && state == default, "idle timer releases without another UDP datagram");
            Check(receiver.Presence.Connected, "lease independent of 2-second presence");
        }
        finally { cancel.Cancel(); await run; }
    }
    private static async Task HeldRefresh()
    {
        var processor = new GamepadSessionProcessor(_ => true);
        using var receiver = new UdpReceiver(new(Ip, 0), TextWriter.Null, gamepad: processor);
        using var cancel = new CancellationTokenSource(); var run = receiver.RunAsync(cancel.Token);
        using var sender = new UdpClient();
        try
        {
            await sender.SendAsync(PresenceTests.Heartbeat(1), receiver.LocalEndpoint);
            for (uint seq = 0; seq < 13; seq++) {
                await sender.SendAsync(GamepadPacketTests.Encode(1, seq, Y), receiver.LocalEndpoint);
                await Task.Delay(100);
                Equal(Y, processor.State, "continuous held refresh");
            }
            Equal(0L, processor.Stats.LeaseExpirations, "no false lease");
        }
        finally { cancel.Cancel(); await run; }
        Equal(default, processor.State, "stop releases held state");
    }
    private static Task Transport()
    {
        var f = new Fixture(false); using var r = new UdpReceiver(new(Ip, 0), TextWriter.Null, gamepad: f.Processor);
        var remote = new IPEndPoint(Ip, 55555);
        r.ProcessDatagram(GamepadPacketTests.Encode(1, 0, B), remote, T(0));
        Equal(0L, f.Processor.Stats.Accepted, "type5 cannot admit run");
        r.ProcessDatagram(PresenceTests.Heartbeat(1), remote, T(1));
        r.ProcessDatagram(GamepadPacketTests.Encode(1, 0, B), remote, T(2));
        r.ProcessDatagram(GamepadPacketTests.Encode(1, 0, B), remote, T(3));
        r.ProcessDatagram(GamepadPacketTests.Encode(1, 1, Y)[..25], remote, T(4));
        Equal(1L, r.Statistics.InvalidPackets, "bad GP counted invalid");
        Equal(0L, r.Statistics.DuplicatePackets, "GP duplicates do not touch Touch counters");
        Equal(0L, r.Statistics.AcceptedPackets, "GP no Touch samples");
        r.ProcessDatagram(PresenceTests.Touch(1, TouchEventType.Down, 0, 1, 100), remote, T(5));
        Equal(0U, r.Statistics.LastSequence!.Value, "Touch baseline independent");
        r.CheckTimeouts(T(2005)); Equal(default, f.Processor.State, "presence disconnect neutral");
        Check(!r.Presence.Connected, "gamepad cannot renew presence");
        return Task.CompletedTask;
    }
    private sealed class Native : IVirtualHidGamepad
    {
        public string DeviceIdentity => "fake-transport";
        public readonly List<XboxGamepadState> States = [];
        public bool Fail;
        public int Disposals;
        public void SetState(XboxGamepadState state) { lock (States) States.Add(state); if (Fail) throw new IOException("gamepad transport failure"); }
        public void Dispose() { Disposals++; }
    }
    private static async Task RuntimeFailure()
    {
        var native = new Native(); var mouse = new VirtualHidTests.FakeNative();
        var runtime = new ReceiverRuntime(new(), TextWriter.Null, MouseBackend.VirtualHid, new(Ip, 0),
            () => new LibVirtualHidMouseOutput(mouse), gamepadFactory: () => new LibVirtualHidXboxGamepad(native));
        using var sender = new UdpClient();
        try
        {
            await runtime.StartAsync(); native.Fail = true;
            await sender.SendAsync(PresenceTests.Heartbeat(1), runtime.LocalEndpoint!);
            await sender.SendAsync(GamepadPacketTests.Encode(1, 0, B), runtime.LocalEndpoint!);
            await RuntimeTests.Until(() => runtime.CaptureSnapshot().GamepadFailures > 0);
            await sender.SendAsync(PresenceTests.Touch(1, TouchEventType.Down, 0, 0, 0), runtime.LocalEndpoint!);
            await sender.SendAsync(PresenceTests.Touch(1, TouchEventType.Move, 1, 10, 1000), runtime.LocalEndpoint!);
            await RuntimeTests.Until(() => mouse.Events.Any(e => e.Kind == "move"));
            var s = runtime.CaptureSnapshot(); Equal(ReceiverState.Running, s.RuntimeState, "mouse running");
            Equal(0L, s.MouseOutputFailures, "no mouse failures"); Check(s.LastError is null && !s.GamepadAvailable, "independent error");
        }
        finally { await runtime.StopAsync(); }
    }
    private static async Task RuntimeRestart()
    {
        var devices = new List<Native>();
        var runtime = new ReceiverRuntime(new(), TextWriter.Null, MouseBackend.VirtualHid, new(Ip, 0),
            () => new LibVirtualHidMouseOutput(new VirtualHidTests.FakeNative()), gamepadFactory: () =>
            { var native = new Native(); devices.Add(native); return new LibVirtualHidXboxGamepad(native); });
        using var sender = new UdpClient();
        try
        {
            await runtime.StartAsync();
            await sender.SendAsync(PresenceTests.Heartbeat(1), runtime.LocalEndpoint!);
            await sender.SendAsync(GamepadPacketTests.Encode(1, 100, B, 1000), runtime.LocalEndpoint!);
            await RuntimeTests.Until(() => { lock (devices[0].States) return devices[0].States.Contains(B); });
            await sender.SendAsync(GamepadPacketTests.Encode(1, 101, default), runtime.LocalEndpoint!);
            Check((await runtime.RestartAsync()).Succeeded, "restart succeeds");
            Equal(default, devices[0].States.Last(), "old neutral"); Equal(1, devices[0].Disposals, "old disposed");
            Check(devices[1].States.All(s => s == default), "fresh neutral");
            await sender.SendAsync(PresenceTests.Heartbeat(2), runtime.LocalEndpoint!);
            await sender.SendAsync(GamepadPacketTests.Encode(2, 0, Y), runtime.LocalEndpoint!);
            await RuntimeTests.Until(() => { lock (devices[1].States) return devices[1].States.Contains(Y); });
        }
        finally { await runtime.StopAsync(); }
        Equal(default, devices[1].States.Last(), "Stop neutral");
    }
}
