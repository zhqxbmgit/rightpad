using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class GamepadDwellTests
{
    public static readonly (string Name, Func<Task> Run)[] Cases =
    [
        Sync("Neutral received at 10ms waits until 25", () => Early(10)),
        Sync("Neutral received at 24ms waits until 25", () => Early(24)),
        Sync("Neutral received at 30ms immediate", Late),
        Sync("generic analog state also observes dwell", Analog),
        Sync("Y/A replace immediately and cancel old pending", Replacement),
        Sync("FORCE_NEUTRAL cancels pending", Force),
        Sync("disconnect/run/source/stop/dispose bypass", Safety),
        Sync("lease overrides long dwell", Lease),
        Sync("duplicate/stale/wrap preserve pending", Ordering),
        Sync("zero-dwell refresh preserves minimum", Refresh),
        Sync("backend submission time cannot consume dwell", BackendTime),
        Sync("deferred output failure clears pending", Failure),
        ("gamepad dwell real UDP idle deadline release", IdleDeadline)
    ];
    private static (string, Func<Task>) Sync(string name, Action action) =>
        ("gamepad dwell " + name, () => { action(); return Task.CompletedTask; });
    private static readonly IPAddress Ip = IPAddress.Loopback;
    private static readonly XboxGamepadState B = new(XboxGamepadButtons.B), Y = new(XboxGamepadButtons.Y), A = new(XboxGamepadButtons.A);
    private static long T(double ms) => (long)(ms * Stopwatch.Frequency / 1000);
    private sealed class Fixture
    {
        public long Now;
        public bool Fail;
        public int BackendDelayMs;
        public readonly List<(XboxGamepadState State, long At)> Output = [];
        public readonly GamepadSessionProcessor P;
        public Fixture()
        {
            P = new(s => { Output.Add((s, Now)); Now += T(BackendDelayMs); return !Fail; }, timestamp: () => Now);
            P.UpdatePresence(new(1, 0, true, Ip.ToString()));
        }
        public bool Send(uint seq, XboxGamepadState state, double ms, ushort dwell = 0, bool force = false)
        { Now = T(ms); return P.Process(new(1, seq, state, dwell, force), Ip, Now); }
        public void Tick(double ms) { Now = T(ms); P.CheckDeadlines(Now); }
    }
    private static void Early(double arrival)
    {
        var f = new Fixture(); f.Send(0, B, 0, 25); f.Send(1, default, arrival);
        f.Tick(24.999); Equal(B, f.P.State, "must not release early"); Equal(1, f.Output.Count, "pending only");
        f.Tick(25); Equal(default, f.P.State, "deadline release"); Equal(T(25), f.Output.Last().At, "exact deterministic backend timestamp");
        f.Tick(100); Equal(2, f.Output.Count, "no repeated release");
    }
    private static void Late()
    {
        var f = new Fixture(); f.Send(0, B, 0, 25); f.Send(1, default, 30);
        Equal(default, f.P.State, "late Neutral immediate"); Equal(T(30), f.Output.Last().At, "no extra dwell on Neutral");
    }
    private static void Analog()
    {
        var f = new Fixture(); var analog = new XboxGamepadState(XboxGamepadButtons.None, 123, 0, 7, -4);
        f.Send(0, analog, 0, 37); f.Send(1, default, 1); f.Tick(36); Equal(analog, f.P.State, "no button-specific logic");
        f.Tick(37); Equal(default, f.P.State, "generic analog release");
    }
    private static void Replacement()
    {
        foreach (var next in new[] {Y, A}) {
            var f = new Fixture(); f.Send(0, B, 0, 25); f.Send(1, default, 5); f.Send(2, next, 10);
            Equal(next, f.P.State, "replacement immediate"); f.Tick(25); Equal(next, f.P.State, "old pending canceled");
            f.Send(3, default, 26); Equal(default, f.P.State, "replacement uses own zero dwell");
        }
        var g = new Fixture(); g.Send(0, B, 0, 25); g.Send(1, Y, 10, 40); g.Send(2, default, 11);
        g.Tick(49); Equal(Y, g.P.State, "replacement own positive dwell"); g.Tick(50); Equal(default, g.P.State, "new deadline");
    }
    private static void Force()
    {
        var f = new Fixture(); f.Send(0, B, 0, 25); f.Send(1, default, 5); f.Send(2, default, 10, force: true);
        Equal(default, f.P.State, "force immediate"); f.Tick(25); Equal(2, f.Output.Count, "no stale timer output");
    }
    private static void Safety()
    {
        for (int action = 0; action < 5; action++) {
            var f = new Fixture(); f.Send(0, B, 0, 1000); f.Send(1, default, 10);
            switch (action) {
                case 0: f.P.UpdatePresence(new(1, 0, false, Ip.ToString())); break;
                case 1: f.P.UpdatePresence(new(2, 0, true, Ip.ToString())); break;
                case 2: f.P.UpdatePresence(new(1, 0, true, "127.0.0.2")); break;
                case 3: f.P.Stop(); break;
                case 4: using (var r = new UdpReceiver(new(Ip, 0), TextWriter.Null, gamepad: f.P)) { } break;
            }
            Equal(default, f.P.State, "safety " + action); f.Tick(1000); Equal(2, f.Output.Count, "canceled deadline " + action);
        }
    }
    private static void Lease()
    {
        var f = new Fixture(); f.Send(0, B, 0, 1000); f.Send(1, default, 10);
        f.Tick(309); Equal(B, f.P.State, "lease from newest valid packet"); f.Tick(310);
        Equal(default, f.P.State, "lease overrides dwell"); Equal(1L, f.P.Stats.LeaseExpirations, "lease recorded");
    }
    private static void Ordering()
    {
        var f = new Fixture(); f.Send(uint.MaxValue, B, 0, 25); f.Send(0, default, 10);
        Check(!f.Send(0, Y, 11) && !f.Send(uint.MaxValue, default, 12, force: true), "duplicate/stale cannot replace pending even force");
        Check(!f.Send(0x80000000, A, 13), "half range rejected"); f.Tick(24); Equal(B, f.P.State, "pending intact");
        f.Tick(25); Equal(default, f.P.State, "pending releases"); Check(!f.Send(0, B, 26), "watermark retained after release");
    }
    private static void Refresh()
    {
        var f = new Fixture(); f.Send(0, B, 0, 250); f.Send(1, B, 100); f.Send(2, default, 110);
        f.Tick(249); Equal(B, f.P.State, "refresh neither erases nor restarts dwell"); f.Tick(250); Equal(default, f.P.State, "original deadline");
    }
    private static void BackendTime()
    {
        var f = new Fixture { BackendDelayMs = 5 }; f.Send(0, B, 0, 25); f.Send(1, default, 10);
        f.Tick(29.999); Equal(B, f.P.State, "25ms after successful backend acceptance"); f.Tick(30); Equal(default, f.P.State, "backend time protected");
    }
    private static void Failure()
    {
        var f = new Fixture(); f.Send(0, B, 0, 25); f.Send(1, default, 10); f.Fail = true; f.Tick(25);
        f.Tick(100); Equal(2, f.Output.Count, "failed deferred output not retried/replayed"); Check(!f.Send(2, Y, 101), "failure isolated");
    }
    private static async Task IdleDeadline()
    {
        var reports = new ConcurrentQueue<(XboxGamepadState State, long At)>();
        var p = new GamepadSessionProcessor(s => { reports.Enqueue((s, Stopwatch.GetTimestamp())); return true; });
        using var r = new UdpReceiver(new(Ip, 0), TextWriter.Null, gamepad: p);
        using var cancel = new CancellationTokenSource(); var run = r.RunAsync(cancel.Token);
        using var sender = new UdpClient();
        try {
            await sender.SendAsync(PresenceTests.Heartbeat(1), r.LocalEndpoint);
            await sender.SendAsync(GamepadPacketTests.Encode(1, 0, B, 25), r.LocalEndpoint);
            await sender.SendAsync(GamepadPacketTests.Encode(1, 1, default), r.LocalEndpoint);
            await RuntimeTests.Until(() => reports.Count == 2);
            var output = reports.ToArray(); var duration = Stopwatch.GetElapsedTime(output[0].At, output[1].At).TotalMilliseconds;
            Check(duration >= 25 && duration < 200, "idle deadline without more UDP, duration=" + duration);
            Equal(0L, p.Stats.LeaseExpirations, "dwell release not lease");
        } finally { cancel.Cancel(); await run; }
    }
}
