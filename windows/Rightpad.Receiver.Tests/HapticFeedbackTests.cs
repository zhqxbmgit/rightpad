using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class HapticFeedbackTests
{
    public static void Codec()
    {
        var click = new ClickFeedback(0xFEDCBA9876543210, 0x89ABCDEF, 0xFEDCBA98);
        byte[] golden = Convert.FromHexString("52504846010100001032547698BADCFEEFCDAB8998BADCFE");
        Check(HapticFeedbackCodec.Encode(click).SequenceEqual(golden), "cross-language golden bytes");
        Check(HapticFeedbackCodec.TryDecode(golden, out var decoded), "valid CLICK");
        Equal(click, decoded, "full unsigned identities");
        for (int length = 0; length < golden.Length; length++)
            Check(!HapticFeedbackCodec.TryDecode(golden.AsSpan(0, length), out _), $"reject length {length}");
        Check(!HapticFeedbackCodec.TryDecode([.. golden, 0], out _), "reject oversized");
        for (int offset = 0; offset < 8; offset++)
        {
            byte[] invalid = (byte[])golden.Clone(); invalid[offset] ^= 0x80;
            Check(!HapticFeedbackCodec.TryDecode(invalid, out _), $"strict header {offset}");
        }
        Check(HapticFeedbackCodec.TryDecode(HapticFeedbackCodec.Encode(new(0, 0, 0)), out decoded), "zero identities legal");
        Equal(new ClickFeedback(0, 0, 0), decoded, "zero round trip");
    }

    private static byte[] P(TouchEventType type, uint id, uint seq, ulong ms, float x = 0, ulong run = 7)
    {
        byte[] bytes = PacketDecoderTests.Encode(type, id, seq, new TouchSample(ms * 1_000_000, x, 0));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(2), run);
        return bytes;
    }

    public static void InputGate()
    {
        var feedback = new List<ClickFeedback>();
        int downs = 0, ups = 0;
        using var button = new LeftButtonController(() => downs++, () => ups++, _ => { }, () => { }, 5000);
        var gesture = new GestureProcessor((hold, packet) => button.Click(hold, () =>
            feedback.Add(new(packet.Header.SenderRunId, packet.Header.SessionId, packet.Header.Sequence))),
            RuntimeSettings.Default with { ClickHoldMs = 5000 }, button.BeginDrag, button.EndDrag);
        using var receiver = new UdpReceiver(new(IPAddress.Loopback, 0), TextWriter.Null,
            gesture: gesture, cancelButtons: button.CancelPendingAndRelease);
        long now = Stopwatch.GetTimestamp();
        var remote = new IPEndPoint(IPAddress.Loopback, 40000);
        void Send(byte[] bytes) => receiver.ProcessDatagram(bytes, remote, now++);
        Send(P(TouchEventType.Down, 1, 0, 0));
        var up = P(TouchEventType.Up, 1, 1, 10); Send(up); Send(up);
        Send(P(TouchEventType.Down, 1, 0, 0)); Send([2, 3]);
        Equal(new ClickFeedback(7, 1, 1), feedback.Single(), "accepted NormalClick once with UP identity");
        Send(P(TouchEventType.Down, 2, 2, 20)); // NoMoveDrag
        Send(P(TouchEventType.Up, 2, 3, 30));
        Send(P(TouchEventType.Down, 3, 4, 40)); // RearmDrag + movement
        Send(P(TouchEventType.Move, 3, 5, 2000, 100));
        Send(P(TouchEventType.Up, 3, 6, 3000, 100));
        Equal(2L, gesture.DragStarts, "both drag paths exercised");
        Equal(1, feedback.Count, "drag DOWN/UP never normal feedback");
        Send(P(TouchEventType.Down, 4, 7, 4000));
        Send(P(TouchEventType.Move, 4, 8, 4010, 9));
        Send(P(TouchEventType.Up, 4, 9, 4020));
        Send(P(TouchEventType.Down, 5, 10, 5000));
        Send(P(TouchEventType.Up, 5, 11, 5301));
        Send(P(TouchEventType.Down, 6, 12, 6000));
        Send(P(TouchEventType.Up, 99, 13, 6010));
        Send(P(TouchEventType.Up, 6, 14, 6020, 9));
        Equal(1, feedback.Count, "move, duration, wrong session, UP threshold zero feedback");
        Send(P(TouchEventType.Down, 7, 15, 7000));
        receiver.CheckTimeouts(now + Stopwatch.Frequency * 3);
        now += Stopwatch.Frequency * 3;
        Send(P(TouchEventType.Up, 7, 16, 7010));
        Send(P(TouchEventType.Down, 8, 0, 8000, run: 8));
        Send(P(TouchEventType.Up, 8, 1, 8010, run: 7));
        gesture.Reset(); button.CancelPendingAndRelease();
        Equal(1, feedback.Count, "presence/run/reset cleanup never feedback");
        Check(downs == 3 && ups == 3, "normal click plus two drags preserve output order/count");
    }

    public static async Task Buttons()
    {
        var order = new ConcurrentQueue<string>();
        using (var buttons = new LeftButtonController(() => order.Enqueue("down"), () => order.Enqueue("up"), _ => { }, () => { }))
        {
            buttons.Click(40, () => order.Enqueue("feedback1"));
            buttons.Click(40, () => order.Enqueue("feedback2"));
            Check(order.SequenceEqual(new[] { "down", "feedback1" }), "queued click has no premature feedback");
            await RuntimeTests.Until(() => order.Count == 6);
            Check(order.SequenceEqual(new[] { "down", "feedback1", "up", "down", "feedback2", "up" }), "feedback after each actual DOWN");
            buttons.Click(5000, () => order.Enqueue("feedback3"));
            buttons.Click(5000, () => order.Enqueue("cancelled"));
            buttons.BeginDrag(); buttons.EndDrag();
            Check(!order.Contains("cancelled"), "drag takeover cancels queued callback");
            buttons.Click(5000);
            buttons.Click(5000, () => order.Enqueue("cancelled"));
            buttons.CancelPendingAndRelease();
            Check(!order.Contains("cancelled"), "lifecycle cancels queued callback");
        }
        int feedback = 0;
        using var failed = new LeftButtonController(() => throw new IOException("down failed"), () => { }, _ => { }, () => { });
        try { failed.Click(25, () => feedback++); throw new Exception("missing output failure"); }
        catch (IOException) { }
        Equal(0, feedback, "failed DOWN never feedback");
    }

    public static async Task SenderIsolation()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        await using (var sender = new HapticFeedbackSender(TextWriter.Null, sendForTest: async (_, _, token) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                started.SetResult(); await release.Task.WaitAsync(token);
                throw new SocketException((int)SocketError.ConnectionReset);
            }
        }))
        {
            var up = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var button = new LeftButtonController(() => { }, () => up.TrySetResult(), _ => { }, () => { });
            button.Click(25, () => sender.TryEnqueue(IPAddress.Loopback, new(7, 1, 1)));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            for (uint i = 2; i < 1000; i++) sender.TryEnqueue(IPAddress.Loopback, new(7, i, i));
            await up.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(!release.Task.IsCompleted && button.Failure is null, "blocked/full feedback cannot delay LEFT UP or fail input");
            release.SetResult();
            await RuntimeTests.Until(() => Volatile.Read(ref calls) >= 2);
            Check(button.Failure is null, "ICMP error independent from mouse output");
        }
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopping = new HapticFeedbackSender(TextWriter.Null, sendForTest: async (_, _, token) =>
        { cancelled.SetResult(); await Task.Delay(Timeout.Infinite, token); });
        stopping.TryEnqueue(IPAddress.Loopback, new(7, 1, 1));
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await stopping.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    }

    public static async Task RuntimeLoopback()
    {
        using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, HapticFeedbackCodec.Port));
        var native = new VirtualHidTests.FakeNative();
        var runtime = new ReceiverRuntime(new(), TextWriter.Null, MouseBackendDefaults.Production,
            new(IPAddress.Loopback, 0), () => new LibVirtualHidMouseOutput(native));
        using var touch = new UdpClient();
        try
        {
            await runtime.StartAsync();
            await touch.SendAsync(P(TouchEventType.Down, 123, 0, 0), runtime.LocalEndpoint!);
            await touch.SendAsync(P(TouchEventType.Up, 123, 1, 10), runtime.LocalEndpoint!);
            var received = await listener.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Check(HapticFeedbackCodec.TryDecode(received.Buffer, out var click), "actual Runtime UDP codec");
            Equal(new ClickFeedback(7, 123, 1), click, "accepted source and packet identity wired through Runtime");
            Check(native.Events.Any(e => e.Kind == "down"), "native output already succeeded");
            await touch.SendAsync(P(TouchEventType.Up, 123, 1, 10), runtime.LocalEndpoint!);
            await touch.SendAsync(P(TouchEventType.Down, 124, 2, 20), runtime.LocalEndpoint!);
            await touch.SendAsync(P(TouchEventType.Up, 124, 3, 30), runtime.LocalEndpoint!);
            await RuntimeTests.Until(() => runtime.CaptureSnapshot().AcceptedPackets == 4);
            await runtime.StopAsync();
            Equal(0, listener.Available, "duplicate UP and NoMoveDrag add no packet");
            Equal(ReceiverState.Stopped, runtime.CaptureSnapshot().RuntimeState, "feedback worker clean stop");
        }
        finally { await runtime.StopAsync(); }
    }
}
