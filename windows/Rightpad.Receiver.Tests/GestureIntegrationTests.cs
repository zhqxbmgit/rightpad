using System.Net;
using System.Net.Sockets;
using Rightpad.Receiver;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class GestureIntegrationTests
{
    private static TouchSample S(ulong ms, float x = 0, float y = 0) => new(ms * 1_000_000, x, y);
    private static byte[] P(TouchEventType type, uint id, uint seq, params TouchSample[] samples) =>
        PacketDecoderTests.Encode(type, id, seq, samples);

    public static async Task GateAndMotionIndependence()
    {
        byte[] down = P(TouchEventType.Down, 1, 1, S(100));
        byte[] up = P(TouchEventType.Up, 1, 3, S(200, .75f, -.75f));
        byte[][] packets = [
            down, down,
            P(TouchEventType.Move, 1, 2, S(120, .25f, -.25f), S(130, .5f, -.5f)),
            up, up, down, [1, 2],
            P(TouchEventType.Down, 2, 4, S(300)),
            P(TouchEventType.Move, 2, 5, S(320, 1), S(330, 9), S(340, 1)),
            P(TouchEventType.Up, 2, 6, S(400)),
            P(TouchEventType.Down, 3, 7, S(500)),
            P(TouchEventType.Move, 4, 8, S(520, 1000)),
            P(TouchEventType.Up, 4, 9, S(530, 1000)),
            P(TouchEventType.Up, 3, 10, S(550)),
            P(TouchEventType.Down, 5, 11, S(600)),
            P(TouchEventType.Up, 5, 12, S(901))
        ];
        async Task<(List<(int, int)> Moves, long Clicks)> Run(bool enabled)
        {
            var moves = new List<(int, int)>();
            var motion = new TouchSessionProcessor((x, y) => moves.Add((x, y)), 7, 7);
            var gesture = new GestureProcessor(() => { });
            using var output = new UdpReceiverTests.ObservedOutput();
            using var receiver = new UdpReceiver(new IPEndPoint(IPAddress.Loopback, 0), output,
                motion: motion, gesture: enabled ? gesture : null);
            using var sender = new UdpClient(AddressFamily.InterNetwork);
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Task running = receiver.RunAsync(cancel.Token);
            try
            {
                for (int i = 0; i < packets.Length; i++)
                {
                    await sender.SendAsync(packets[i], receiver.LocalEndpoint);
                    await output.WaitFor(s => s.StartsWith($"stats: receivedPackets={i + 1} "));
                }
            }
            finally { cancel.Cancel(); await running; }
            Equal(2L, receiver.Statistics.DuplicatePackets, "duplicate DOWN/UP filtered");
            Equal(1L, receiver.Statistics.OldPackets, "old DOWN filtered");
            Equal(1L, receiver.Statistics.InvalidPackets, "malformed filtered");
            if (enabled)
            {
                Equal(4L, gesture.TapCandidates, "no duplicate DOWN candidate");
                Equal(2L, gesture.ClicksTriggered, "one click per valid tap; no stale/cross-session click");
                Equal(1L, gesture.ConfirmedMoves, "middle sample latched");
            }
            return (moves, gesture.ClicksTriggered);
        }
        var baseline = await Run(false);
        var enabled = await Run(true);
        Check(baseline.Moves.Count > 0 && baseline.Moves.SequenceEqual(enabled.Moves), "exact RAW output sequence unchanged");
        Console.WriteLine($"MOTION_INDEPENDENCE events={enabled.Moves.Count} exactSequenceMatch=true clicks={enabled.Clicks}");
    }

    public static async Task NonblockingAndTimeout()
    {
        var up = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var button = new LeftButtonController(() => { }, () => up.TrySetResult(), _ => { }, cancel.Cancel, 1500);
        var gesture = new GestureProcessor(button.Click);
        var moves = new List<(int, int)>();
        var motion = new TouchSessionProcessor((x, y) => moves.Add((x, y)), 7, 7);
        using var output = new UdpReceiverTests.ObservedOutput();
        using var receiver = new UdpReceiver(new IPEndPoint(IPAddress.Loopback, 0), output,
            TimeSpan.FromMilliseconds(50), motion, gesture: gesture);
        using var sender = new UdpClient(AddressFamily.InterNetwork);
        Task running = receiver.RunAsync(cancel.Token);
        uint seq = 0;
        async Task Send(TouchEventType type, uint id, TouchSample sample)
        {
            await sender.SendAsync(P(type, id, ++seq, sample), receiver.LocalEndpoint);
            await output.WaitFor(s => s.StartsWith($"stats: receivedPackets={seq} "));
        }
        try
        {
            await Send(TouchEventType.Down, 1, S(0));
            await Send(TouchEventType.Up, 1, S(100));
            await Send(TouchEventType.Down, 2, S(200));
            await Send(TouchEventType.Move, 2, S(210, .25f));
            await output.WaitFor(s => s.StartsWith("receiver: status=input_timeout"));
            await Send(TouchEventType.Move, 2, S(220, .5f));
            Check(moves.SequenceEqual(new[] { (1, 0), (2, 0) }), "motion/accumulator continues during click hold and timeout");
            Check(!up.Task.IsCompleted, "Receiver processed packets before scheduled UP");
            await up.Task.WaitAsync(TimeSpan.FromSeconds(5)); // No more input is needed to release.
            await Send(TouchEventType.Up, 2, S(230, .5f));
            Equal(2L, gesture.ClicksTriggered, "timeout retains gesture candidate too");
        }
        finally { cancel.Cancel(); await running; }
    }

    public static async Task ShutdownCleanup()
    {
        foreach (bool failMotion in new[] { false, true })
        {
            int releases = 0;
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var button = new LeftButtonController(() => { }, () => releases++, _ => { }, cancel.Cancel, 5000);
            var gesture = new GestureProcessor(button.Click);
            var motion = new TouchSessionProcessor((_, _) => { if (failMotion) throw new IOException("motion failure"); });
            using var output = new UdpReceiverTests.ObservedOutput();
            using var receiver = new UdpReceiver(new IPEndPoint(IPAddress.Loopback, 0), output, motion: motion, gesture: gesture);
            using var sender = new UdpClient(AddressFamily.InterNetwork);
            var running = receiver.RunAsync(cancel.Token);
            try
            {
                await sender.SendAsync(P(TouchEventType.Down, 1, 1, S(0)), receiver.LocalEndpoint);
                await sender.SendAsync(P(TouchEventType.Up, 1, 2, S(10)), receiver.LocalEndpoint);
                await output.WaitFor(s => s.StartsWith("stats: receivedPackets=2 "));
                Equal(0, releases, "button still held before shutdown");
                if (failMotion)
                {
                    await sender.SendAsync(P(TouchEventType.Down, 2, 3, S(20)), receiver.LocalEndpoint);
                    await sender.SendAsync(P(TouchEventType.Move, 2, 4, S(30, 1)), receiver.LocalEndpoint);
                }
                else cancel.Cancel();
                try { await running; Check(!failMotion, "expected motion failure"); }
                catch (IOException) { Check(failMotion, "unexpected receiver failure"); }
            }
            finally { cancel.Cancel(); button.Dispose(); }
            Equal(1, releases, "normal/exception shutdown best-effort release");
        }
    }

    public static async Task AsyncFailureStopsIdleReceiver()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        int ups = 0;
        var logs = new List<string>();
        using var button = new LeftButtonController(() => { }, () =>
        {
            if (++ups == 1) throw new System.ComponentModel.Win32Exception(5, "test LEFT UP failure");
        }, logs.Add, cancel.Cancel, 30);
        var gesture = new GestureProcessor(button.Click);
        using var output = new UdpReceiverTests.ObservedOutput();
        using var receiver = new UdpReceiver(new IPEndPoint(IPAddress.Loopback, 0), output, gesture: gesture);
        using var sender = new UdpClient(AddressFamily.InterNetwork);
        Task running = receiver.RunAsync(cancel.Token);
        await sender.SendAsync(P(TouchEventType.Down, 1, 1, S(0)), receiver.LocalEndpoint);
        await sender.SendAsync(P(TouchEventType.Up, 1, 2, S(10)), receiver.LocalEndpoint);
        await running.WaitAsync(TimeSpan.FromSeconds(2));
        button.Dispose();
        Check(button.Failure is not null, "async failure available to Program exit status");
        Equal(2, ups, "failed timer UP followed by best-effort release");
        Equal(1, logs.Count, "async failure logged");
        Equal(2L, receiver.Statistics.ReceivedPackets, "no wakeup packet required");
        Equal("receiver: status=stopped", output.Lines[^1], "receiver stops on async button failure");
    }
}
