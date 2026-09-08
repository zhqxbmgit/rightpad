using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Rightpad.Receiver;

internal sealed class UdpReceiver : IDisposable
{
    public const int Port = 50000;
    public static readonly TimeSpan InputTimeout = TimeSpan.FromSeconds(2);

    private readonly UdpClient socket;
    private readonly RawSampleLogger logger;
    private readonly TimeSpan inputTimeout;
    private readonly TouchSessionProcessor? motion;
    private readonly bool detailedLogging;

    public IPEndPoint LocalEndpoint { get; }
    public PacketStatistics Statistics { get; } = new();
    public long InputTimeouts { get; private set; }

    // Endpoint and timeout injection are for loopback tests, not user configuration.
    public UdpReceiver(IPEndPoint endpoint, TextWriter output, TimeSpan? timeout = null,
        TouchSessionProcessor? motion = null, bool detailedLogging = true)
    {
        inputTimeout = timeout ?? InputTimeout;
        if (inputTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        socket = new UdpClient(endpoint);
        LocalEndpoint = (IPEndPoint)socket.Client.LocalEndPoint!;
        this.motion = motion;
        this.detailedLogging = detailedLogging;
        logger = new RawSampleLogger(output, detailedLogging);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        TimeSpan? lastAccepted = null;
        try
        {
            logger.Listening(LocalEndpoint);
            while (!cancellationToken.IsCancellationRequested)
            {
                using var receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (lastAccepted.HasValue)
                {
                    TimeSpan remaining = inputTimeout - (clock.Elapsed - lastAccepted.Value);
                    if (remaining <= TimeSpan.Zero)
                    {
                        RecordTimeout(clock.Elapsed.TotalMilliseconds);
                        lastAccepted = null;
                        continue;
                    }
                    receiveCancellation.CancelAfter(remaining);
                }

                UdpReceiveResult received;
                try
                {
                    received = await socket.ReceiveAsync(receiveCancellation.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    RecordTimeout(clock.Elapsed.TotalMilliseconds);
                    lastAccepted = null;
                    continue;
                }

                TimeSpan receiveTime = clock.Elapsed;
                Statistics.RecordReceived();
                if (!PacketDecoder.TryDecode(received.Buffer, out var packet, out string error))
                {
                    Statistics.RecordInvalid();
                    logger.Invalid(receiveTime.TotalMilliseconds, received.RemoteEndPoint, received.Buffer.Length, error);
                }
                else
                {
                    var observation = Statistics.Observe(packet!.Header);
                    if (observation.Accepted)
                    {
                        lastAccepted = receiveTime;
                        motion?.Process(packet);
                    }
                    logger.Packet(receiveTime.TotalMilliseconds, received.RemoteEndPoint, packet, observation);
                }
                if (detailedLogging) logger.Stats(Statistics);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ctrl+C cancels a pending receive without requiring another datagram.
        }
        finally
        {
            motion?.Reset();
            socket.Dispose();
            logger.Stats(Statistics);
            logger.Summary(InputTimeouts, motion);
            logger.Stopped();
        }
    }

    private void RecordTimeout(double elapsedMs)
    {
        // Silence is indistinguishable from a stationary held finger. Preserve all motion state.
        InputTimeouts++;
        logger.Timeout(elapsedMs);
    }

    public void Dispose()
    {
        socket.Dispose();
        motion?.Reset();
    }
}
