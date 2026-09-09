using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class RuntimeSettingsTests
{
    public static async Task PacketBoundary()
    {
        var settings = new RuntimeSettingsStore();
        var movements = new ConcurrentQueue<int>();
        var motion = new TouchSessionProcessor((x, _) =>
        {
            movements.Enqueue(x);
            settings.Publish(RuntimeSettings.Default with { SensitivityX = 3 });
        });
        using var receiver = new UdpReceiver(new(IPAddress.Loopback, 0), TextWriter.Null,
            motion: motion, detailedLogging: false, settings: settings);
        using var cancellation = new CancellationTokenSource();
        var loop = receiver.RunAsync(cancellation.Token);
        using var sender = new UdpClient();
        try
        {
            await sender.SendAsync(PacketDecoderTests.Encode(TouchEventType.Down, 1, 0, new TouchSample(0, 0, 0)), receiver.LocalEndpoint);
            await sender.SendAsync(PacketDecoderTests.Encode(TouchEventType.Move, 1, 1, new TouchSample(1, 1, 0), new TouchSample(2, 2, 0)), receiver.LocalEndpoint);
            await RuntimeTests.Until(() => movements.Count == 2);
            await sender.SendAsync(PacketDecoderTests.Encode(TouchEventType.Move, 1, 2, new TouchSample(3, 3, 0)), receiver.LocalEndpoint);
            await RuntimeTests.Until(() => movements.Count == 3);
            Check(movements.ToArray().SequenceEqual(new[] { 7, 7, 3 }), "same packet snapshot, next packet new gain");
        }
        finally { cancellation.Cancel(); await loop; }
    }
    public static void Residual()
    {
        var output = new List<int>();
        var session = new TouchSessionProcessor((x, _) => output.Add(x));
        TouchPacket Packet(TouchEventType type, float x) => new(new(1, type, 1, 1, 0), [new(0, x, 0)]);
        session.Process(Packet(TouchEventType.Down, 0), .6, 1);
        session.Process(Packet(TouchEventType.Move, 1), .6, 1);
        Equal(0, output.Count, "residual only");
        session.Process(Packet(TouchEventType.Move, 2), .5, 1);
        Check(output.SequenceEqual(new[] { 1 }), ".6 old residual + .5 new delta");
        Equal((uint?)1, session.ActiveSessionId, "session retained");
    }
    public static void TapDownSnapshot()
    {
        var holds = new List<int>();
        var gesture = new GestureProcessor(holds.Add, RuntimeSettings.Default);
        TouchPacket Packet(TouchEventType type, uint session, ulong time, float x) => new(new(1, type, 1, session, 0), [new(time, x, 0)]);
        gesture.Process(Packet(TouchEventType.Down, 1, 0, 0), 300, 8, 25);
        gesture.Process(Packet(TouchEventType.Up, 1, 200_000_000, 7), 50, .5, 31);
        Check(holds.SequenceEqual(new[] { 31 }), "DOWN duration/threshold retained; request hold current");
        gesture.Process(Packet(TouchEventType.Down, 2, 0, 0), 50, .5, 25);
        gesture.Process(Packet(TouchEventType.Up, 2, 200_000_000, 7), 300, 8, 25);
        Equal(1, holds.Count, "new DOWN takes new rules");
    }
    public static async Task QueuedHold()
    {
        var events = new ConcurrentQueue<(bool Down, long Time)>();
        using var buttons = new LeftButtonController(() => events.Enqueue((true, Stopwatch.GetTimestamp())),
            () => events.Enqueue((false, Stopwatch.GetTimestamp())), _ => { }, () => { });
        buttons.Click(40);
        buttons.Click(120);
        buttons.Click(25);
        await RuntimeTests.Until(() => events.Count == 6);
        var e = events.ToArray();
        Check(e.Select(x => x.Down).SequenceEqual(new[] { true, false, true, false, true, false }), "button ordering");
        Check(Stopwatch.GetElapsedTime(e[0].Time, e[1].Time).TotalMilliseconds >= 30, "first retains 40ms");
        Check(Stopwatch.GetElapsedTime(e[2].Time, e[3].Time).TotalMilliseconds >= 105, "queued retains 120ms");
        Check(Stopwatch.GetElapsedTime(e[4].Time, e[5].Time).TotalMilliseconds >= 15, "third retains 25ms");
    }
}
