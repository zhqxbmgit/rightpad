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
    private readonly GestureProcessor? gesture;
    private readonly bool detailedLogging;
    private readonly RuntimeSettingsStore? settings;
    private long inputTimeouts, lastAcceptedAtTicks, activeSession = -1;
    private IPAddress? lastRemoteIp;

    public IPEndPoint LocalEndpoint { get; }
    public PacketStatistics Statistics { get; } = new();
    public long InputTimeouts => Interlocked.Read(ref inputTimeouts);
    public long LastAcceptedAtTicks => Interlocked.Read(ref lastAcceptedAtTicks);
    public long ActiveTouchSessionId => Interlocked.Read(ref activeSession);
    public string? LastRemoteIp => Volatile.Read(ref lastRemoteIp)?.ToString();

    // Endpoint and timeout injection are for loopback tests, not user configuration.
    public UdpReceiver(IPEndPoint endpoint, TextWriter output, TimeSpan? timeout = null,
        TouchSessionProcessor? motion = null, bool detailedLogging = true, GestureProcessor? gesture = null,
        RuntimeSettingsStore? settings = null)
    {
        inputTimeout = timeout ?? InputTimeout;
        if (inputTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        socket = new UdpClient(endpoint);
        LocalEndpoint = (IPEndPoint)socket.Client.LocalEndPoint!;
        this.motion = motion;
        this.gesture = gesture;
        this.detailedLogging = detailedLogging;
        this.settings = settings;
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
                    received = await socket.ReceiveAsync(receiveCancellation.Token).ConfigureAwait(false);
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
                        Interlocked.Exchange(ref lastAcceptedAtTicks, Stopwatch.GetTimestamp());
                        if (!received.RemoteEndPoint.Address.Equals(lastRemoteIp))
                            Volatile.Write(ref lastRemoteIp, received.RemoteEndPoint.Address);
                        // One publication boundary for every historical/current sample in this packet.
                        var snapshot = settings?.Current;
                        if (snapshot is null)
                        {
                            motion?.Process(packet);
                            gesture?.Process(packet);
                        }
                        else
                        {
                            motion?.Process(packet, snapshot.SensitivityX, snapshot.SensitivityY);
                            gesture?.Process(packet, snapshot.TapMaxDurationMs,
                                snapshot.TapMovementThresholdPx, snapshot.ClickHoldMs);
                        }
                        Interlocked.Exchange(ref activeSession, motion?.ActiveSessionId is uint id ? id : -1);
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
            gesture?.Reset();
            motion?.Reset();
            Interlocked.Exchange(ref activeSession, -1);
            socket.Dispose();
            logger.Stats(Statistics);
            logger.Summary(InputTimeouts, motion);
            logger.Stopped();
        }
    }

    private void RecordTimeout(double elapsedMs)
    {
        // Silence is indistinguishable from a stationary held finger. Preserve all motion state.
        Interlocked.Increment(ref inputTimeouts);
        logger.Timeout(elapsedMs);
    }

    public void Dispose()
    {
        gesture?.Reset();
        socket.Dispose();
        motion?.Reset();
    }
}
