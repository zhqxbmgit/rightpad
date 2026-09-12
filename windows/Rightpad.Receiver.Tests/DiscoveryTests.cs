using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class DiscoveryTests
{
    private sealed class MemoryStorage : IFlightRecordStorage
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<string> Lines = new();
        public void WriteLine(string line) => Lines.Enqueue(line);
        public void Dispose() { }
    }
    private static readonly byte[] Id = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
    public static void Codec()
    {
        const ulong nonce = 0xFEDCBA9876543210;
        byte[] discover = DiscoveryCodec.Discover(nonce), offer = DiscoveryCodec.Offer(nonce, Id);
        Equal(16, discover.Length, "DISCOVER exact size");
        Equal(40, offer.Length, "OFFER exact size");
        Equal("52504144010100001032547698BADCFE", Convert.ToHexString(discover), "golden LE request");
        Check(DiscoveryCodec.TryDiscover(discover, out ulong decoded) && decoded == nonce, "nonce round trip");
        Check(DiscoveryCodec.TryOffer(offer, nonce, out var id) && id.SequenceEqual(Id), "opaque ID, port, protocol round trip");
        Equal((ushort)50000, BinaryPrimitives.ReadUInt16LittleEndian(offer.AsSpan(32)), "touch port");
        Check(!DiscoveryCodec.TryOffer(offer, nonce + 1, out _), "nonce mismatch");
        for (int size = 0; size <= 80; size++)
        {
            if (size != 16) Check(!DiscoveryCodec.TryDiscover(discover.Concat(new byte[80]).Take(size).ToArray(), out _), "bad request size");
            if (size != 40) Check(!DiscoveryCodec.TryOffer(offer.Concat(new byte[80]).Take(size).ToArray(), nonce, out _), "bad offer size");
        }
        foreach (int offset in new[] { 0, 1, 2, 3, 4, 5, 6, 7 })
        {
            var invalid = (byte[])discover.Clone(); invalid[offset] ^= 0x80;
            Check(!DiscoveryCodec.TryDiscover(invalid, out _), "invalid request field " + offset);
        }
        foreach (int offset in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 32, 33, 34, 35 })
        {
            var invalid = (byte[])offer.Clone(); invalid[offset] ^= 0x80;
            Check(!DiscoveryCodec.TryOffer(invalid, nonce, out _), "invalid offer field " + offset);
        }
        offer[39] = 128;
        Check(DiscoveryCodec.TryOffer(offer, nonce, out _), "unknown capabilities informational");
    }
    public static async Task Identity()
    {
        string directory = Path.Combine(Path.GetTempPath(), "rightpad-discovery-tests-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "receiver-id");
        var diagnostics = new List<string>();
        try
        {
            var first = ReceiverIdentityStore.Load(path, diagnostics.Add);
            Equal(16, first.Length, "first create");
            Equal(32, File.ReadAllText(path).Length, "exact file format");
            Check(first.SequenceEqual(ReceiverIdentityStore.Load(path, diagnostics.Add)), "restart persistence");
            File.WriteAllText(path, Convert.ToHexString(Id).ToLowerInvariant());
            Check(Id.SequenceEqual(ReceiverIdentityStore.Load(path, diagnostics.Add)), "valid exact reload");
            File.WriteAllText(path, "bad identity");
            var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => ReceiverIdentityStore.Load(path, _ => { }))));
            Check(results.All(value => value.SequenceEqual(results[0])), "atomic race repair");
            File.WriteAllText(path, new string('z', 32));
            ReceiverIdentityStore.Load(path, diagnostics.Add);
            Check(diagnostics.Contains("receiver_id_corrupt_replaced"), "diagnostic repair");
            File.Delete(path);
            results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => ReceiverIdentityStore.Load(path, _ => { }))));
            Check(results.All(value => value.SequenceEqual(results[0])), "atomic race first create");
            Equal(1, Directory.GetFiles(directory).Length, "no temp remnants");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    public static async Task Responder()
    {
        var storage = new MemoryStorage();
        using var recorder = new FlightRecorder(storage);
        using var responder = new DiscoveryResponder(new(IPAddress.Loopback, 0), Id, TextWriter.Null, recorder);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var task = responder.RunAsync(cancel.Token);
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        await client.SendAsync(new byte[100], responder.LocalEndpoint);
        await RuntimeTests.Until(() => responder.InvalidPackets == 1);
        for (ulong nonce = 0; nonce < 5; nonce++)
        {
            await client.SendAsync(DiscoveryCodec.Discover(nonce), responder.LocalEndpoint);
            var received = await client.ReceiveAsync(cancel.Token);
            Check(DiscoveryCodec.TryOffer(received.Buffer, nonce, out var id) && id.SequenceEqual(Id), "valid response / nonce / stable identity");
            Equal(responder.LocalEndpoint, received.RemoteEndPoint, "unicast source");
        }
        Equal(5L, responder.OffersSent, "invalid request ignored");
        using (var transient = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            await transient.SendAsync(DiscoveryCodec.Discover(900), responder.LocalEndpoint);
        await Task.Delay(100); // The phone may close its reply port on pause before an OFFER arrives.
        await client.SendAsync(DiscoveryCodec.Discover(901), responder.LocalEndpoint);
        var afterClosedClient = await client.ReceiveAsync(cancel.Token);
        Check(DiscoveryCodec.TryOffer(afterClosedClient.Buffer, 901, out _), "closed probe source does not stop responder");
        cancel.Cancel();
        await task;
        using var rebound = new UdpClient(responder.LocalEndpoint);
        await RuntimeTests.Until(() => storage.Lines.Any(line => line.Contains("discovery_stopped")));
        foreach (string name in new[] { "discovery_started", "discovery_offer_sent", "invalid_discovery_packet", "discovery_stopped" })
            Check(storage.Lines.Any(line => line.Contains(name)), "production Flight Recorder event " + name);
        Equal(1, storage.Lines.Count(line => line.Contains("discovery_offer_sent")), "normal probes are rate-limited in production diagnostics");
    }
    public static async Task RuntimeLifecycle()
    {
        using var reserve = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var discoveryEndpoint = (IPEndPoint)reserve.Client.LocalEndPoint!;
        reserve.Dispose();
        var runtime = new ReceiverRuntime(new(), TextWriter.Null, new(IPAddress.Loopback, 0), rawMouse: false,
            discoveryEndpoint: discoveryEndpoint, identityFactory: () => Id);
        Check(runtime.DiscoveryEndpoint is null, "not advertised before startup");
        using (var before = new UdpClient(discoveryEndpoint)) { }
        try
        {
            await runtime.StartAsync();
            Equal(ReceiverState.Running, runtime.CaptureSnapshot().RuntimeState, "ready");
            using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            using var deadline = new CancellationTokenSource(3000);
            await client.SendAsync(DiscoveryCodec.Discover(42), runtime.DiscoveryEndpoint!);
            var offer = await client.ReceiveAsync(deadline.Token);
            Check(DiscoveryCodec.TryOffer(offer.Buffer, 42, out _), "advertised only with touch bound");
            await client.SendAsync(PresenceTests.Heartbeat(22), runtime.LocalEndpoint!);
            await RuntimeTests.Until(() => runtime.CaptureSnapshot().HeartbeatPackets == 1);
            Equal(0L, runtime.CaptureSnapshot().InvalidCount, "discovery does not enter touch decoder");
            var touchEndpoint = runtime.LocalEndpoint!;
            await runtime.StopAsync();
            using var touchRebound = new UdpClient(touchEndpoint);
            using var discoveryRebound = new UdpClient(discoveryEndpoint);
        }
        finally { await runtime.StopAsync(); }
        // Failed touch bind must never acquire the discovery port.
        using var occupied = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var failed = new ReceiverRuntime(new(), TextWriter.Null, (IPEndPoint)occupied.Client.LocalEndPoint!, rawMouse: false,
            discoveryEndpoint: discoveryEndpoint, identityFactory: () => Id);
        await failed.StartAsync();
        Equal(ReceiverState.Error, failed.CaptureSnapshot().RuntimeState, "touch bind fails");
        Check(failed.DiscoveryEndpoint is null, "no advertising on failed touch bind");
        using var free = new UdpClient(discoveryEndpoint);
        // Failed discovery bind must also release touch and leave a truthful Error.
        var discoveryFailed = new ReceiverRuntime(new(), TextWriter.Null, new(IPAddress.Loopback, 0), rawMouse: false,
            discoveryEndpoint: discoveryEndpoint, identityFactory: () => Id);
        await discoveryFailed.StartAsync();
        Equal(ReceiverState.Error, discoveryFailed.CaptureSnapshot().RuntimeState, "discovery bind error");
        using var released = new UdpClient(discoveryFailed.LocalEndpoint!);
    }

    public static async Task RuntimeErrorCleanup()
    {
        var runtime = new ReceiverRuntime(new(), TextWriter.Null, new(IPAddress.Loopback, 0),
            mouseFactory: () => new WindowsMouseOutput((uint count, ref WindowsMouseOutput.NativeInput input, int size) => 0, () => 5),
            discoveryEndpoint: new(IPAddress.Loopback, 0), identityFactory: () => Id);
        try
        {
            await runtime.StartAsync();
            var touch = runtime.LocalEndpoint!;
            var discovery = runtime.DiscoveryEndpoint!;
            using var sender = new UdpClient();
            await sender.SendAsync(PacketDecoderTests.Encode(TouchEventType.Down, 1, 0, new TouchSample(0, 0, 0)), touch);
            await sender.SendAsync(PacketDecoderTests.Encode(TouchEventType.Move, 1, 1, new TouchSample(1, 1, 0)), touch);
            await RuntimeTests.Until(() => runtime.Completion.IsCompleted);
            Equal(ReceiverState.Error, runtime.CaptureSnapshot().RuntimeState, "output failure is Runtime Error");
            using var touchReleased = new UdpClient(touch);
            using var discoveryReleased = new UdpClient(discovery);
        }
        finally { await runtime.StopAsync(); }
    }
}
