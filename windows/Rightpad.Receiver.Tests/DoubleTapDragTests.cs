using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using static Rightpad.Receiver.Tests.Program;
using static Rightpad.Receiver.Tests.RawMotionProcessorTests;
using static Rightpad.Receiver.Tests.TouchSessionProcessorTests;

namespace Rightpad.Receiver.Tests;

internal static class DoubleTapDragTests
{
    private static TouchSample S(ulong ns, float x = 0, float y = 0) => new(ns, x, y);
    private sealed class Gesture
    {
        public int Clicks, Starts, Ends;
        public readonly GestureProcessor G;
        public Gesture() => G = new(() => Clicks++, () => Starts++, () => Ends++);
        public void Down(uint id, ulong ns, float x = 0, float y = 0) => G.Process(Packet(TouchEventType.Down, id, S(ns, x, y)));
        public void Up(uint id, ulong ns, float x = 0, float y = 0) => G.Process(Packet(TouchEventType.Up, id, S(ns, x, y)));
        public void Tap() { Down(1, 100_000_000); Up(1, 200_000_000); }
    }

    public static void ImmediateTap()
    {
        var h = new Gesture(); h.Tap();
        Equal(1, h.Clicks, "click synchronous at UP without double tap wait");
        Check(h.G.DoubleTapArmed && !h.G.IsDragging, "valid UP arms only");
    }
    private static void Interval(ulong ns, bool expected)
    {
        var h = new Gesture(); h.Tap(); h.Down(2, ns, 500, 900);
        Equal(expected, h.G.IsDragging, "event-time interval boundary and unrestricted placement");
        Equal(expected ? 1 : 0, h.Starts, "DOWN starts immediately without MOVE");
        Check(!h.G.DoubleTapArmed, "qualification consumed even when expired/backward");
        h.Down(3, 300_000_000);
        Equal(expected ? 1 : 0, h.Starts, "consumed qualification cannot be reused");
    }
    public static void IntervalInclusive() => Interval(330_000_000, true);
    public static void IntervalOneNsOver() => Interval(330_000_001, false);
    public static void IntervalBackward() => Interval(199_999_999, false);
    public static void IntervalZero() => Interval(200_000_000, true);
    public static void TimestampOverflow()
    {
        var h = new Gesture(); h.Down(1, ulong.MaxValue - 200); h.Up(1, ulong.MaxValue - 100);
        h.Down(2, ulong.MaxValue); Equal(1, h.Starts, "uint64 subtraction does not overflow");
    }
    private static void FirstTap(ulong upNs, float x, float y, bool expected, bool excursion = false)
    {
        var h = new Gesture(); h.Down(1, 100_000_000);
        if (excursion) h.G.Process(Packet(TouchEventType.Move, 1, S(110_000_000, 1), S(120_000_000, 9), S(130_000_000)));
        h.Up(1, upNs, x, y);
        Equal(expected, h.G.DoubleTapArmed, "only valid first tap arms");
        h.Down(2, upNs + 100_000_000);
        Equal(expected ? 1 : 0, h.Starts, "invalid first contact never starts drag");
    }
    public static void DurationInclusive() => FirstTap(400_000_000, 0, 0, true);
    public static void DurationOneNsOver() => FirstTap(400_000_001, 0, 0, false);
    public static void MovementInclusive() => FirstTap(200_000_000, 8, -8, true);
    public static void MovementXOver() => FirstTap(200_000_000, MathF.BitIncrement(8), 0, false);
    public static void MovementYOver() => FirstTap(200_000_000, 0, -MathF.BitIncrement(8), false);
    public static void HistoricalExcursion() => FirstTap(200_000_000, 0, 0, false, true);
    public static void UnlimitedDrag()
    {
        var h = new Gesture(); h.Tap(); h.Down(2, 300_000_000, 500, 900);
        h.G.Process(Packet(TouchEventType.Move, 2, S(5_000_000_000, 9999, -9999)));
        Check(h.G.IsDragging, "duration and movement limits no longer apply");
        h.Up(99, 5_100_000_000); Check(h.G.IsDragging, "foreign UP ignored");
        h.Up(1, 200_000_000); Check(h.G.IsDragging, "prior session UP ignored");
        h.Up(2, 5_200_000_000); h.Up(2, 5_200_000_000);
        Equal(1, h.Ends, "matching UP releases exactly once");
        Equal(1L, h.G.DragEnds, "end diagnostic");
        Equal(1, h.Clicks, "drag UP never clicks");
        Check(!h.G.DoubleTapArmed && !h.G.IsDragging, "drag UP does not rearm");
        h.Down(3, 5_300_000_000); Equal(1, h.Starts, "third DOWN cannot chain drag");
        h.Up(3, 5_400_000_000); h.Down(4, 5_500_000_000);
        Equal(2, h.Starts, "new valid tap permits another drag");
    }
    public static void ExpiredContactRearms()
    {
        var h = new Gesture(); h.Tap(); h.Down(2, 400_000_000); h.Up(2, 500_000_000);
        Equal(2, h.Clicks, "expired second contact remains a normal tap");
        h.Down(3, 600_000_000); Equal(1, h.Starts, "expired second tap becomes next first tap");
    }
    public static void Reset()
    {
        var h = new Gesture(); h.Tap(); h.G.Reset(); h.Down(2, 300_000_000);
        Equal(0, h.Starts, "Reset clears qualification");
        h.Up(2, 350_000_000); h.Down(3, 400_000_000); h.G.Reset();
        Check(!h.G.IsDragging && !h.G.DoubleTapArmed, "Reset clears drag and qualification; outer layer releases");
    }
    public static void HotInterval()
    {
        foreach (int interval in new[] { 50, 200 })
        {
            var h = new Gesture(); h.Tap();
            h.G.Process(Packet(TouchEventType.Down, 2, S(400_000_000)), 300, 8, 25, interval);
            Equal(interval == 200, h.G.IsDragging, "interval snapshot at second DOWN");
            h.G.Process(Packet(TouchEventType.Move, 2, S(5_000_000_000, 900)), 50, .5, 1, 50);
            Equal(interval == 200, h.G.IsDragging, "live settings cannot terminate held drag");
        }
    }
    public static async Task ButtonLifecycle()
    {
        foreach (string end in new[] { "up", "cancel", "dispose" })
        {
            var events = new List<string>();
            using var b = new LeftButtonController(() => events.Add("D"), () => events.Add("U"), _ => { }, () => { });
            b.BeginDrag(); b.BeginDrag(); Check(events.SequenceEqual(new[] { "D" }), "one immediate drag DOWN");
            if (end == "up") { b.EndDrag(); b.EndDrag(); }
            else if (end == "cancel") { b.CancelPendingAndRelease(); b.CancelPendingAndRelease(); b.EndDrag(); }
            else { b.Dispose(); b.Dispose(); b.EndDrag(); }
            await Task.Delay(40);
            Check(events.SequenceEqual(new[] { "D", "U" }), "exactly one release: " + end);
        }
    }
    public static async Task OverlappingClick()
    {
        var events = new List<string>();
        using var b = new LeftButtonController(() => events.Add("D"), () => events.Add("U"), _ => { }, () => { }, 100);
        b.Click(); b.BeginDrag();
        await Task.Delay(160);
        Check(events.SequenceEqual(new[] { "D", "U", "D" }), "old click timer cannot release drag");
        b.EndDrag(); Check(events.SequenceEqual(new[] { "D", "U", "D", "U" }), "overlap safely neutral");
    }
    public static void ButtonFailures()
    {
        foreach (bool failDown in new[] { true, false })
        {
            bool held = false, fail = true; int wakeups = 0; var log = new List<string>();
            using var b = new LeftButtonController(() => { held = true; if (failDown) throw new IOException("DOWN failure"); },
                () => { if (!failDown && fail) { fail = false; throw new IOException("UP failure"); } held = false; },
                log.Add, () => wakeups++);
            if (failDown) Throws<IOException>(b.BeginDrag);
            else { b.BeginDrag(); Throws<IOException>(b.EndDrag); }
            Check(!held && b.Failure is IOException && wakeups == 1 && log.Count == 1, "failure propagated, logged and released");
            Throws<IOException>(b.BeginDrag);
        }
    }

    private sealed class Pipeline : IDisposable
    {
        public bool Held, FailMove;
        public readonly List<string> Buttons = new();
        public readonly List<(int, int)> Moves = new();
        public readonly LeftButtonController B;
        public readonly GestureProcessor G;
        public readonly UdpReceiver R;
        public readonly UdpReceiverTests.ObservedOutput Log = new();
        public long Now = Stopwatch.Frequency * 10;
        public uint Sequence;
        public ulong Run = 1;
        public Pipeline()
        {
            B = new(() => { Held = true; Buttons.Add("D"); }, () => { Held = false; Buttons.Add("U"); }, _ => { }, () => { });
            G = new(B.Click, B.BeginDrag, B.EndDrag);
            var motion = new TouchSessionProcessor((x, y) => { if (FailMove) throw new IOException("move failed"); Moves.Add((x, y)); }, 7, 7);
            R = new(new(IPAddress.Loopback, 0), Log, motion: motion, gesture: G, cancelButtons: B.CancelPendingAndRelease);
        }
        public byte[] Bytes(TouchEventType type, uint id, ulong ns, float x = 0, float y = 0)
        {
            var bytes = PacketDecoderTests.Encode(type, id, Sequence++, S(ns, x, y));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(2), Run);
            return bytes;
        }
        public void Send(byte[] bytes) => R.ProcessDatagram(bytes, new(IPAddress.Loopback, 1234), Now);
        public void StartDrag()
        {
            Send(Bytes(TouchEventType.Down, 1, 100_000_000)); Send(Bytes(TouchEventType.Up, 1, 200_000_000));
            Send(Bytes(TouchEventType.Down, 2, 300_000_000, 500, 900));
            Check(Held && G.IsDragging, "active drag");
        }
        public void Dispose() { R.Dispose(); B.Dispose(); Log.Dispose(); }
    }
    public static void InputGate()
    {
        using var h = new Pipeline(); h.StartDrag();
        var move = h.Bytes(TouchEventType.Move, 2, 400_000_000, 501, 900); h.Send(move);
        h.Send(move); h.Send([1, 2]);
        var staleUp = PacketDecoderTests.Encode(TouchEventType.Up, 2, 0, S(310_000_000));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(staleUp.AsSpan(2), h.Run);
        h.Send(staleUp); h.Send(h.Bytes(TouchEventType.Up, 99, 450_000_000));
        Check(h.Held && h.G.IsDragging, "duplicate/invalid/stale/foreign cannot release");
        var up = h.Bytes(TouchEventType.Up, 2, 500_000_000, 501, 900); h.Send(up); h.Send(up);
        Equal(1L, h.G.DragEnds, "one accepted drag UP");
        Equal(2L, h.R.Statistics.DuplicatePackets, "duplicates excluded");
        Equal(1L, h.R.Statistics.OldPackets, "stale excluded");
        Equal(1L, h.R.Statistics.InvalidPackets, "invalid excluded");
        Check(!h.Held && !h.G.DoubleTapArmed, "neutral, not rearmed");
    }
    public static void StationaryThreeSeconds()
    {
        using var h = new Pipeline(); h.StartDrag();
        for (int i = 0; i < 6; i++)
        {
            h.Now += Stopwatch.Frequency / 2; h.Send(PresenceTests.Heartbeat(1));
            Check(h.Held && h.G.IsDragging, "heartbeat preserves stationary drag");
        }
        Equal(1L, h.R.InputTimeouts, "three seconds passes production input timeout");
        Equal(0L, h.R.PresenceTimeouts, "healthy presence");
        h.Send(h.Bytes(TouchEventType.Move, 2, 3_400_000_000, 500.25f, 899.75f));
        h.Send(h.Bytes(TouchEventType.Up, 2, 3_500_000_000, 500.5f, 899.5f));
        Check(h.Moves.SequenceEqual(new[] { (1, -1), (2, -2) }), "motion and fraction continue after silence including UP");
        Check(!h.Held && h.G.DragEnds == 1, "neutral at final UP");
    }
    public static void LifecycleCleanup()
    {
        foreach (bool activeDrag in new[] { false, true })
        foreach (string reason in new[] { "sender", "presence", "dispose" })
        {
            using var h = new Pipeline();
            if (activeDrag) h.StartDrag();
            else { h.Send(h.Bytes(TouchEventType.Down, 1, 100_000_000)); h.Send(h.Bytes(TouchEventType.Up, 1, 200_000_000)); }
            if (reason == "sender") { h.Run = 2; h.Sequence = 0; h.Send(PresenceTests.Heartbeat(2)); }
            else if (reason == "presence") h.R.CheckTimeouts(h.Now + 2 * Stopwatch.Frequency);
            else h.R.Dispose();
            Check(!h.Held && !h.G.IsDragging && !h.G.DoubleTapArmed, "clear all state: " + reason);
            if (reason != "dispose")
            {
                h.Send(h.Bytes(TouchEventType.Down, 3, 310_000_000));
                Check(!h.G.IsDragging, "cleanup cannot transfer old qualification");
            }
        }
    }
    public static async Task Loopback()
    {
        foreach (bool expired in new[] { false, true })
        {
            using var h = new Pipeline(); using var sender = new UdpClient(); using var cancel = new CancellationTokenSource();
            Task loop = h.R.RunAsync(cancel.Token); int received = 0;
            async Task PacketSend(byte[] b)
            {
                int expected = ++received; await sender.SendAsync(b, h.R.LocalEndpoint);
                await h.Log.WaitFor(s => s.StartsWith($"stats: receivedPackets={expected} "));
            }
            try
            {
                await PacketSend(h.Bytes(TouchEventType.Down, 1, 100_000_000));
                await PacketSend(h.Bytes(TouchEventType.Up, 1, 200_000_000));
                await RuntimeTests.Until(() => h.Buttons.Count == 2);
                // Arrival delay exceeds 130 ms while Android event gap may still be 100 ms.
                await Task.Delay(180);
                await PacketSend(h.Bytes(TouchEventType.Down, 2, expired ? 400_000_000UL : 300_000_000UL, 500, 900));
                Equal(!expired, h.Held, "only event time determines immediate hold");
                if (!expired)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        await Task.Delay(500);
                        await PacketSend(PresenceTests.Heartbeat(h.Run));
                        Check(h.Held && h.G.IsDragging, "actual three-second stationary UDP drag remains held");
                    }
                    Equal(1L, h.R.InputTimeouts, "real input timeout reached");
                    Equal(0L, h.R.PresenceTimeouts, "real heartbeat keeps presence");
                }
                await PacketSend(h.Bytes(TouchEventType.Move, 2, 500_000_000, 500.25f, 899.75f));
                await PacketSend(h.Bytes(TouchEventType.Move, 2, 600_000_000, 502.5f, 898.5f));
                Equal(!expired, h.Held, "MOVE preserves button state");
                await PacketSend(h.Bytes(TouchEventType.Up, 2, 800_000_000, 503, 898));
                Check(h.Moves.SequenceEqual(new[] { (1, -1), (16, -9), (4, -4) }), "exact RAW sequence with residual and final UP");
                Check(h.Buttons.SequenceEqual(expired ? new[] { "D", "U" } : new[] { "D", "U", "D", "U" }), "full native button sequence");
                Check(!h.Held, "final neutral");
            }
            finally { cancel.Cancel(); await loop; }
        }
    }
    public static async Task StopAndOutputFailure()
    {
        foreach (bool failure in new[] { false, true })
        {
            using var h = new Pipeline(); h.Now = Stopwatch.GetTimestamp(); h.StartDrag(); h.FailMove = failure;
            using var cancel = new CancellationTokenSource(); using var sender = new UdpClient();
            Task loop = h.R.RunAsync(cancel.Token);
            if (failure) await sender.SendAsync(h.Bytes(TouchEventType.Move, 2, 400_000_000, 501, 900), h.R.LocalEndpoint);
            else cancel.Cancel();
            try { await loop.WaitAsync(TimeSpan.FromSeconds(5)); Check(!failure, "failure must propagate"); }
            catch (IOException) { Check(failure, "expected output error"); }
            Check(!h.Held && !h.G.IsDragging && !h.G.DoubleTapArmed, "stop/error neutralizes complete gesture");
            Equal(2, h.Buttons.Count(x => x == "U"), "one click UP plus one drag cleanup UP");
        }
    }
}
