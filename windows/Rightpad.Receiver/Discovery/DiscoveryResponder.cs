using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Rightpad.Receiver;

internal sealed class DiscoveryResponder : IDisposable
{
    private readonly UdpClient socket;
    private readonly byte[] identity;
    private readonly TextWriter output;
    private readonly FlightRecorder? flightRecorder;
    private long invalid, offers;
    public long InvalidPackets => Interlocked.Read(ref invalid);
    public long OffersSent => Interlocked.Read(ref offers);
    public IPEndPoint LocalEndpoint { get; }

    public DiscoveryResponder(IPEndPoint endpoint, byte[] identity, TextWriter output, FlightRecorder? flightRecorder = null)
    {
        if (identity.Length != 16) throw new ArgumentException("Invalid receiver identity.");
        this.identity = (byte[])identity.Clone();
        this.output = output;
        this.flightRecorder = flightRecorder;
        socket = new UdpClient(endpoint);
        LocalEndpoint = (IPEndPoint)socket.Client.LocalEndPoint!;
    }

    public async Task RunAsync(CancellationToken token)
    {
        output.WriteLine($"discovery_started: endpoint={LocalEndpoint} receiverId={Convert.ToHexString(identity)}");
        flightRecorder?.Event("discovery_started", ("endpoint", LocalEndpoint.ToString()), ("receiverId", Convert.ToHexString(identity)));
        try
        {
            while (!token.IsCancellationRequested)
            {
                var request = await socket.ReceiveAsync(token).ConfigureAwait(false);
                if (!DiscoveryCodec.TryDiscover(request.Buffer, out ulong nonce))
                {
                    long count = Interlocked.Increment(ref invalid);
                    if (count == 1 || count % 1024 == 0)
                    {
                        output.WriteLine($"invalid_discovery_packet: count={count}");
                        flightRecorder?.Event("invalid_discovery_packet", ("count", count));
                    }
                    continue;
                }
                token.ThrowIfCancellationRequested();
                await socket.SendAsync(DiscoveryCodec.Offer(nonce, identity), request.RemoteEndPoint, token).ConfigureAwait(false);
                long sent = Interlocked.Increment(ref offers);
                if (sent == 1 || sent % 60 == 0)
                {
                    output.WriteLine($"discovery_offer_sent: count={sent} remote={request.RemoteEndPoint} receiverId={Convert.ToHexString(identity)}");
                    flightRecorder?.Event("discovery_offer_sent", ("count", sent), ("remote", request.RemoteEndPoint.ToString()),
                        ("receiverId", Convert.ToHexString(identity)));
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            socket.Dispose();
            output.WriteLine($"discovery_stopped: offers={OffersSent} invalid_discovery_packet={InvalidPackets}");
            flightRecorder?.Event("discovery_stopped", ("offers", OffersSent), ("invalidDiscoveryPackets", InvalidPackets));
        }
    }
    public void Dispose() => socket.Dispose();
}
