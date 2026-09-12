using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class RuntimeTests
{
    internal static async Task Until(Func<bool> predicate)
    {
        var clock = Stopwatch.StartNew();
        while (!predicate())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Test condition not reached.");
            await Task.Delay(5);
        }
    }
    private sealed class Harness : IAsyncDisposable
    {
        public readonly RuntimeSettingsStore Settings = new();
        public readonly ConcurrentQueue<(uint Flags, int X, int Y, long Time)> Events = new();
        public readonly UdpClient Sender = new(AddressFamily.InterNetwork);
        public readonly ReceiverRuntime Runtime;
        public bool FailNextMove;
        public Harness(IPEndPoint? endpoint = null)
        {
            Runtime = new(Settings, TextWriter.Null, MouseBackend.SendInput, endpoint ?? new(IPAddress.Loopback, 0), () => new WindowsMouseOutput(
                (uint count, ref WindowsMouseOutput.NativeInput input, int size) =>
                {
                    if (input.Data.Mouse.Flags == WindowsMouseOutput.MouseMove && FailNextMove)
                    { FailNextMove = false; return 0; }
                    Events.Enqueue((input.Data.Mouse.Flags, input.Data.Mouse.Dx, input.Data.Mouse.Dy, Stopwatch.GetTimestamp()));
                    return 1;
                }, () => 5));
        }
        public async Task Send(TouchEventType type, uint sequence, params TouchSample[] samples)
        {
            await Sender.SendAsync(PacketDecoderTests.Encode(type, 1, sequence, samples), Runtime.LocalEndpoint!);
        }
        public async ValueTask DisposeAsync() { await Runtime.StopAsync(); Sender.Dispose(); }
    }
    public static async Task StartStop()
    {
        await using var h = new Harness();
        await h.Runtime.StartAsync();
        Equal(ReceiverState.Running, h.Runtime.CaptureSnapshot().RuntimeState, "started");
        var endpoint = h.Runtime.LocalEndpoint!;
        await h.Runtime.StopAsync();
        Equal(ReceiverState.Stopped, h.Runtime.CaptureSnapshot().RuntimeState, "stopped");
        using var rebound = new UdpClient(endpoint);
    }
    public static async Task Repeated()
    {
        await using var h = new Harness();
        await Task.WhenAll(h.Runtime.StartAsync(), h.Runtime.StartAsync());
        Equal(1L, h.Runtime.CaptureSnapshot().RunId, "repeated Start is one run");
        await Task.WhenAll(h.Runtime.StopAsync(), h.Runtime.StopAsync());
        await h.Runtime.StartAsync();
        Equal(2L, h.Runtime.CaptureSnapshot().RunId, "fresh start");
    }
    public static async Task Occupied()
    {
        using var occupied = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var endpoint = (IPEndPoint)occupied.Client.LocalEndPoint!;
        await using var h = new Harness(endpoint);
        await h.Runtime.StartAsync();
        Equal(ReceiverState.Error, h.Runtime.CaptureSnapshot().RuntimeState, "bind error");
        occupied.Dispose();
        await h.Runtime.StartAsync();
        Equal(ReceiverState.Running, h.Runtime.CaptureSnapshot().RuntimeState, "retry bind");
    }
    public static async Task ErrorRestart()
    {
        await using var h = new Harness();
        h.FailNextMove = true;
        await h.Runtime.StartAsync();
        await h.Send(TouchEventType.Down, 10, new TouchSample(0, 0, 0));
        await h.Send(TouchEventType.Move, 11, new TouchSample(1, 1, 0));
        await Until(() => h.Runtime.Completion.IsCompleted);
        Check(h.Runtime.CaptureSnapshot().LastError is not null, "real output error");
        await h.Runtime.StartAsync();
        await h.Send(TouchEventType.Down, 0, new TouchSample(0, 0, 0));
        await h.Send(TouchEventType.Move, 1, new TouchSample(1, 2, 0));
        await Until(() => h.Events.Any(e => e.X == 14));
        Equal(0L, h.Runtime.CaptureSnapshot().OldCount, "fresh sequence baseline");
        Check(h.Runtime.CaptureSnapshot().LastError is null, "old error isolated");
    }
    public static async Task CountersAndRuns()
    {
        await using var h = new Harness();
        await h.Runtime.StartAsync();
        await h.Sender.SendAsync(new byte[] { 1 }, h.Runtime.LocalEndpoint!);
        await h.Send(TouchEventType.Down, 10, new TouchSample(0, 0, 0));
        await h.Send(TouchEventType.Move, 13, new TouchSample(1, 1, 0), new TouchSample(2, 2, 0));
        await h.Send(TouchEventType.Move, 13, new TouchSample(1, 1, 0));
        await h.Send(TouchEventType.Move, 12, new TouchSample(1, 1, 0));
        await Until(() => h.Runtime.CaptureSnapshot().OldCount == 1);
        var s = h.Runtime.CaptureSnapshot();
        Equal(5L, s.ReceivedPackets, "all datagrams"); Equal(2L, s.AcceptedPackets, "accepted");
        Equal(3L, s.AcceptedSamples, "samples"); Equal(2L, s.GapCount, "gap");
        Equal(1L, s.InvalidCount, "invalid"); Equal(1L, s.DuplicateCount, "duplicate");
        Equal("127.0.0.1", s.LastRemoteIp, "accepted remote");
        await h.Runtime.StopAsync(); await h.Runtime.StartAsync();
        s = h.Runtime.CaptureSnapshot();
        Equal(0L, s.ReceivedPackets, "run counters reset"); Equal(0L, s.LastAcceptedAtTicks, "run activity reset");
        Equal(-1L, s.ActiveTouchSessionId, "session reset");
    }
    public static async Task NoUiReads()
    {
        await using var h = new Harness();
        await h.Runtime.StartAsync();
        await h.Send(TouchEventType.Down, 0, new TouchSample(0, 0, 0));
        await h.Send(TouchEventType.Move, 1, new TouchSample(10_000_000, 1, 0));
        await h.Send(TouchEventType.Up, 2, new TouchSample(20_000_000, 1, 0));
        // No CaptureSnapshot / Dispatcher / UI reads until native output has completed.
        await Until(() => h.Events.Count(e => e.Flags == WindowsMouseOutput.MouseLeftUp) == 1);
        Check(h.Events.Any(e => e.X == 7), "motion continued without UI");
        Equal(1, h.Events.Count(e => e.Flags == WindowsMouseOutput.MouseLeftDown), "gesture continued");
        Equal(3L, h.Runtime.CaptureSnapshot().AcceptedPackets, "UDP continued");
    }
    public static async Task OldTimerIsolation()
    {
        await using var h = new Harness();
        h.Settings.Publish(RuntimeSettings.Default with { ClickHoldMs = 200 });
        await h.Runtime.StartAsync();
        await h.Send(TouchEventType.Down, 0, new TouchSample(0, 0, 0));
        await h.Send(TouchEventType.Up, 1, new TouchSample(1, 0, 0));
        await Until(() => h.Events.Any(e => e.Flags == WindowsMouseOutput.MouseLeftDown));
        await h.Runtime.StopAsync();
        h.Settings.Publish(RuntimeSettings.Default);
        await h.Runtime.StartAsync();
        await h.Send(TouchEventType.Down, 0, new TouchSample(0, 0, 0));
        await h.Send(TouchEventType.Up, 1, new TouchSample(1, 0, 0));
        await Until(() => h.Events.Count(e => e.Flags == WindowsMouseOutput.MouseLeftUp) == 2);
        await Task.Delay(250);
        Equal(2, h.Events.Count(e => e.Flags == WindowsMouseOutput.MouseLeftUp), "no stale UP callback");
        Equal(2L, h.Runtime.CaptureSnapshot().RunId, "new run alive");
    }
    public static void ActivityAndRates()
    {
        long t = Stopwatch.Frequency * 10;
        var s = new RuntimeStatsSnapshot(1, ReceiverState.Running);
        Equal("Waiting for Android", RuntimeStatsViewModel.Activity(s, t), "waiting");
        s = s with { LastAcceptedAtTicks = t, Presence = new(1, t, true, "127.0.0.1") };
        Equal("Connected", RuntimeStatsViewModel.Activity(s, t + Stopwatch.Frequency / 2), "connected");
        Equal("Connected", RuntimeStatsViewModel.Activity(s with { InputTimeoutCount = 1 }, t + Stopwatch.Frequency), "touch timeout not disconnect");
        Equal("Disconnected", RuntimeStatsViewModel.Activity(s, t + 2 * Stopwatch.Frequency), "presence expires");
        var vm = new RuntimeStatsViewModel();
        vm.Refresh(s, t);
        vm.Refresh(s with { AcceptedSamples = 100, ReceivedPackets = 50 }, t + Stopwatch.Frequency / 2);
        Equal("200.0", vm.SamplesHz, "real elapsed samples"); Equal("100.0", vm.PacketsHz, "real elapsed packets");
        vm.Refresh(s with { AcceptedSamples = 400, ReceivedPackets = 200 }, t + 2 * Stopwatch.Frequency);
        Equal("200.0", vm.SamplesHz, "late UI tick uses real interval");
        vm.Refresh(new(2, ReceiverState.Running), t + 3 * Stopwatch.Frequency);
        Equal("0.0", vm.SamplesHz, "fresh run rate");
    }
}
