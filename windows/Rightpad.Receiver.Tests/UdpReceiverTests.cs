using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Rightpad.Receiver;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class UdpReceiverTests
{
    public static async Task Loopback()
    {
        using var output = new ObservedOutput();
        using var receiver = new UdpReceiver(new IPEndPoint(IPAddress.Loopback, 0), output);
        using var sender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var otherSender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task running = receiver.RunAsync(cancellation.Token);
        byte[][] packets =
        [
            [0x01, 0x02],
            PacketDecoderTests.Encode(TouchEventType.Move, 100, 999, new TouchSample(1_000_000_000UL, float.NaN, 2)),
            PacketDecoderTests.Encode(TouchEventType.Down, 100, 10, new TouchSample(1_000_000_000UL, 1.5f, -2.25f)),
            PacketDecoderTests.Encode(TouchEventType.Move, 100, 13,
                new(1_008_000_001UL, 2.5f, 3), new(1_008_000_001UL, 4.5f, 5)),
            PacketDecoderTests.Encode(TouchEventType.Move, 100, 13, new TouchSample(1_008_000_001UL, 4.5f, 5)),
            PacketDecoderTests.Encode(TouchEventType.Move, 100, 12, new TouchSample(1_004_000_001UL, 2, 3)),
            PacketDecoderTests.Encode(TouchEventType.Up, 100, 14, new TouchSample(1_016_000_001UL, 4.5f, 5))
        ];
        try
        {
            for (int i = 0; i < packets.Length; i++)
            {
                // The forward MOVE arrives from a different port: no source locking.
                await (i == 3 ? otherSender : sender).SendAsync(packets[i], receiver.LocalEndpoint);
                await output.WaitFor(line => line.StartsWith($"stats: receivedPackets={i + 1} "));
            }
        }
        finally
        {
            cancellation.Cancel();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
        var stats = receiver.Statistics;
        Equal(7L, stats.ReceivedPackets, "UDP datagrams");
        Equal(2L, stats.InvalidPackets, "malformed packets");
        Equal(3L, stats.AcceptedPackets, "accepted packets");
        Equal(4L, stats.AcceptedSamples, "raw samples");
        Equal(2L, stats.SequenceGapEstimate, "gap estimate");
        Equal(1L, stats.DuplicatePackets, "duplicate");
        Equal(1L, stats.OldPackets, "old");
        string[] lines = output.Lines;
        string[] samples = lines.Where(line => line.StartsWith("sample:")).ToArray();
        string[] expected =
        [
            "sample: sessionId=100 sequence=10 sampleIndex=0 timestampNs=1000000000 x=1.5 y=-2.25",
            "sample: sessionId=100 sequence=13 sampleIndex=0 timestampNs=1008000001 x=2.5 y=3",
            "sample: sessionId=100 sequence=13 sampleIndex=1 timestampNs=1008000001 x=4.5 y=5",
            "sample: sessionId=100 sequence=14 sampleIndex=0 timestampNs=1016000001 x=4.5 y=5"
        ];
        Check(samples.SequenceEqual(expected), "raw sample values/order/count mismatch");
        double previousReceiveTime = -1;
        foreach (string line in lines.Where(line => line.StartsWith("packet:")))
        {
            string time = line.Split(' ').Single(field => field.StartsWith("receiveElapsedMs=")).Split('=')[1];
            double current = double.Parse(time, CultureInfo.InvariantCulture);
            Check(current >= previousReceiveTime, "receive clock regressed");
            previousReceiveTime = current;
        }
        Check(lines.Any(line => line.Contains("eventType=DOWN")), "DOWN log missing");
        Check(lines.Any(line => line.Contains("eventType=MOVE")), "MOVE log missing");
        Check(lines.Any(line => line.Contains("eventType=UP")), "UP log missing");
        Equal("receiver: status=stopped", lines[^1], "shutdown diagnostic");
        Console.WriteLine($"LOOPBACK endpoint={receiver.LocalEndpoint} sent=7 received=7 invalid=2 accepted=3 samples=4 gap=2 duplicate=1 old=1");
        foreach (string line in lines.Where(line => line.StartsWith("packet:") || line.StartsWith("sample:")))
            Console.WriteLine(line);
    }

    public static async Task CancelIdle()
    {
        using var output = new ObservedOutput();
        using var receiver = new UdpReceiver(new IPEndPoint(IPAddress.Loopback, 0), output);
        using var cancellation = new CancellationTokenSource();
        var endpoint = receiver.LocalEndpoint;
        Task running = receiver.RunAsync(cancellation.Token);
        await output.WaitFor(line => line.StartsWith("listening:"));
        cancellation.Cancel();
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        Equal(0L, receiver.Statistics.ReceivedPackets, "unexpected datagrams");
        Equal("receiver: status=stopped", output.Lines[^1], "idle shutdown");
        using var rebound = new UdpClient(endpoint);
        Console.WriteLine($"CANCELLATION pending_receive_cancelled=true port_rebound={endpoint.Port}");
    }

    public static async Task TimeoutAndResume()
    {
        using var output = new ObservedOutput();
        using var receiver = new UdpReceiver(new IPEndPoint(IPAddress.Loopback, 0), output, TimeSpan.FromMilliseconds(100));
        using var sender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task running = receiver.RunAsync(cancellation.Token);
        try
        {
            byte[] first = PacketDecoderTests.Encode(TouchEventType.Down, 1, 1, new TouchSample(5_000_000_000UL, 0, 0));
            await sender.SendAsync(first, receiver.LocalEndpoint);
            await output.WaitFor(line => line.StartsWith("stats: receivedPackets=1 "));
            await output.WaitFor(line => line.StartsWith("receiver: status=input_timeout"));
            await sender.SendAsync(first, receiver.LocalEndpoint);
            await output.WaitFor(line => line.Contains("status=duplicate"));
            await sender.SendAsync(PacketDecoderTests.Encode(TouchEventType.Up, 1, 2,
                new TouchSample(5_100_000_000UL, 0, 0)), receiver.LocalEndpoint);
            await output.WaitFor(line => line.Contains("status=in_order"));
            await output.WaitFor(line => line.StartsWith("receiver: status=input_timeout"));
        }
        finally
        {
            cancellation.Cancel();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Equal(2L, receiver.Statistics.AcceptedPackets, "timeout reset sequence");
        Equal(1L, receiver.Statistics.DuplicatePackets, "duplicate after timeout");
        Equal(2, output.Lines.Count(line => line.StartsWith("receiver: status=input_timeout")), "timeout notifications");
    }

    public static void OccupiedPort()
    {
        using var first = new UdpClient(AddressFamily.InterNetwork);
        first.ExclusiveAddressUse = true;
        first.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        bool failed = false;
        try { using var second = new UdpReceiver((IPEndPoint)first.Client.LocalEndPoint!, TextWriter.Null); }
        catch (SocketException) { failed = true; }
        Check(failed, "occupied UDP port did not fail");
    }

    public static async Task MotionGate()
    {
        var moves = new List<(int, int)>();
        var motion = new TouchSessionProcessor((x, y) => moves.Add((x, y)));
        using var output = new ObservedOutput();
        using var receiver = new UdpReceiver(new IPEndPoint(IPAddress.Loopback, 0), output, motion: motion);
        using var sender = new UdpClient(AddressFamily.InterNetwork);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task running = receiver.RunAsync(cancellation.Token);
        byte[] Packet(TouchEventType type, uint id, uint seq, params TouchSample[] samples) =>
            PacketDecoderTests.Encode(type, id, seq, samples);
        byte[] down = Packet(TouchEventType.Down, 1, 1, new TouchSample(0, 0, 0));
        byte[] move = Packet(TouchEventType.Move, 1, 2, new TouchSample(3, 1.75f, 0), new TouchSample(2, 2.25f, 0));
        byte[][] packets = [
            Packet(TouchEventType.Move, 9, 0, new TouchSample(0, 999, 999)),
            down, down, move, move, down,
            Packet(TouchEventType.Move, 1, 3, new TouchSample(0, 888, 0), new TouchSample(0, float.NaN, 0)),
            Packet(TouchEventType.Move, 2, 3, new TouchSample(0, 1000, 1000)),
            Packet(TouchEventType.Up, 2, 4, new TouchSample(0, 1000, 1000)),
            Packet(TouchEventType.Move, 1, 6, new TouchSample(0, 2.75f, 0)),
            Packet(TouchEventType.Up, 1, 7, new TouchSample(0, 3.5f, 0)),
            Packet(TouchEventType.Down, 2, 8, new TouchSample(0, 100, 0)),
            Packet(TouchEventType.Move, 2, 9, new TouchSample(0, 100.75f, 0))
        ];
        try
        {
            for (int i = 0; i < packets.Length; i++)
            {
                await sender.SendAsync(packets[i], receiver.LocalEndpoint);
                await output.WaitFor(line => line.StartsWith($"stats: receivedPackets={i + 1} "));
            }
        }
        finally
        {
            cancellation.Cancel();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Check(moves.SequenceEqual(new[] { (1, 0), (1, 0), (1, 0) }), "accepted-only per-sample output");
        Equal(3L, motion.IgnoredSessionPackets, "orphan MOVE and foreign MOVE/UP");
        Equal(2L, receiver.Statistics.DuplicatePackets, "duplicate DOWN/MOVE");
        Equal(1L, receiver.Statistics.OldPackets, "old DOWN");
        Equal(1L, receiver.Statistics.InvalidPackets, "malformed whole packet excluded");
        Equal(1L, receiver.Statistics.SequenceGapEstimate, "gap accepted without synthetic samples");
        Check(motion.ActiveSessionId is null, "shutdown clears active session");
        motion.Process(TouchSessionProcessorTests.Packet(TouchEventType.Move, 2, new TouchSample(0, 101.5f, 0)));
        Equal(3, moves.Count, "no movement after shutdown reset");
    }

    public static async Task MotionSurvivesTimeout()
    {
        var moves = new List<(int, int)>();
        var motion = new TouchSessionProcessor((x, y) => moves.Add((x, y)));
        using var output = new ObservedOutput();
        using var receiver = new UdpReceiver(new IPEndPoint(IPAddress.Loopback, 0), output,
            TimeSpan.FromMilliseconds(100), motion);
        using var sender = new UdpClient(AddressFamily.InterNetwork);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task running = receiver.RunAsync(cancellation.Token);
        async Task Send(TouchEventType type, uint seq, float x, float y)
        {
            await sender.SendAsync(PacketDecoderTests.Encode(type, 1, seq, new TouchSample(0, x, y)), receiver.LocalEndpoint);
            await output.WaitFor(line => line.StartsWith($"stats: receivedPackets={seq + 1} "));
        }
        try
        {
            await Send(TouchEventType.Down, 0, 100, 100);
            await Send(TouchEventType.Move, 1, 100.75f, 99.25f);
            await output.WaitFor(line => line.StartsWith("receiver: status=input_timeout"));
            Equal((uint?)1, motion.ActiveSessionId, "timeout retains session");
            await Send(TouchEventType.Move, 2, 101.25f, 98.75f);
            Check(moves.SequenceEqual(new[] { (1, -1) }), "timeout retains both position and residual");
            await Send(TouchEventType.Up, 3, 102, 98);
            Check(moves.SequenceEqual(new[] { (1, -1), (1, -1) }), "UP still outputs tail after timeout");
            Equal(1L, receiver.InputTimeouts, "timeout counted");
        }
        finally
        {
            cancellation.Cancel();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    public static async Task QuietLogging()
    {
        using var output = new ObservedOutput();
        var motion = new TouchSessionProcessor((_, _) => { });
        using var receiver = new UdpReceiver(new IPEndPoint(IPAddress.Loopback, 0), output,
            TimeSpan.FromMilliseconds(100), motion, detailedLogging: false);
        using var sender = new UdpClient(AddressFamily.InterNetwork);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task running = receiver.RunAsync(cancellation.Token);
        byte[] down = PacketDecoderTests.Encode(TouchEventType.Down, 1, 1, new TouchSample(0, 0, 0));
        try
        {
            foreach (byte[] packet in new byte[][] { down, down,
                PacketDecoderTests.Encode(TouchEventType.Move, 1, 3, new TouchSample(0, 2, 0)), down, [1, 2] })
                await sender.SendAsync(packet, receiver.LocalEndpoint);
            await output.WaitFor(line => line.Contains("status=invalid"));
            await output.WaitFor(line => line.StartsWith("receiver: status=input_timeout"));
        }
        finally
        {
            cancellation.Cancel();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
        string[] lines = output.Lines;
        Equal(0, lines.Count(line => line.StartsWith("sample:")), "no sample logs");
        Equal(1, lines.Count(line => line.StartsWith("stats:")), "only final stats");
        Equal(3, lines.Count(line => line.StartsWith("packet:")), "only gap/old/invalid packet logs");
        Check(!lines.Any(line => line.Contains("status=duplicate") || line.Contains("status=baseline")), "normal packets quiet");
        Check(lines.Any(line => line.StartsWith("motion_stats:") && line.Contains("outputEvents=1")), "motion summary");
        Check(lines.Any(line => line == "receiver_stats: inputTimeouts=1"), "timeout summary");
        Equal(5L, receiver.Statistics.ReceivedPackets, "quiet mode still counts packets");
    }

    public static async Task MotionFailure()
    {
        var motion = new TouchSessionProcessor((_, _) => throw new System.ComponentModel.Win32Exception(5, "test failure"));
        using var output = new ObservedOutput();
        using var receiver = new UdpReceiver(new IPEndPoint(IPAddress.Loopback, 0), output, motion: motion);
        using var sender = new UdpClient(AddressFamily.InterNetwork);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task running = receiver.RunAsync(cancellation.Token);
        await sender.SendAsync(PacketDecoderTests.Encode(TouchEventType.Down, 1, 0, new TouchSample(0, 0, 0)), receiver.LocalEndpoint);
        await output.WaitFor(line => line.StartsWith("stats: receivedPackets=1 "));
        await sender.SendAsync(PacketDecoderTests.Encode(TouchEventType.Move, 1, 1, new TouchSample(0, 1, 0)), receiver.LocalEndpoint);
        bool failed = false;
        try { await running.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (System.ComponentModel.Win32Exception) { failed = true; }
        Check(failed, "native output failure must terminate receiver");
        Check(motion.ActiveSessionId is null, "failed receive loop clears state");
        Equal("receiver: status=stopped", output.Lines[^1], "failure shutdown log");
        using var rebound = new UdpClient(receiver.LocalEndpoint);
    }

    private sealed class ObservedOutput : TextWriter
    {
        private readonly List<string> lines = [];
        private readonly Channel<string> pending = Channel.CreateUnbounded<string>();
        public override Encoding Encoding => Encoding.UTF8;
        public string[] Lines { get { lock (lines) return lines.ToArray(); } }

        public override void WriteLine(string? value)
        {
            lock (lines) lines.Add(value ?? "");
            pending.Writer.TryWrite(value ?? "");
        }

        public async Task WaitFor(Func<string, bool> predicate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!predicate(await pending.Reader.ReadAsync(timeout.Token))) { }
        }
    }
}
