using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Rightpad.Receiver;

internal sealed record SenderPresence(ulong? RunId = null, long LastSeenAtTicks = 0,
    bool Connected = false, string? RemoteIp = null);

internal sealed class UdpReceiver : IDisposable
{
    public const int Port = 50000;
    public static readonly TimeSpan InputTimeout = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan PresenceTimeout = TimeSpan.FromSeconds(2);

    private readonly UdpClient socket;
    private readonly RawSampleLogger logger;
    private readonly TimeSpan inputTimeout;
    private readonly TouchSessionProcessor? motion;
    private readonly GestureProcessor? gesture;
    private readonly bool detailedLogging;
    private readonly RuntimeSettingsStore? settings;
    private readonly Action? cancelButtons;
    private readonly TextWriter output;
    private readonly HashSet<ulong> retiredRunIds = new();
    private SenderPresence presence = new();
    private long presenceTimeouts;
    private long? touchDeadline;
    private long clockOrigin;
    public SenderPresence Presence => Volatile.Read(ref presence);
    public long PresenceTimeouts => Interlocked.Read(ref presenceTimeouts);
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
        RuntimeSettingsStore? settings = null, Action? cancelButtons = null)
    {
        inputTimeout = timeout ?? InputTimeout;
        if (inputTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        socket = new UdpClient(endpoint);
        LocalEndpoint = (IPEndPoint)socket.Client.LocalEndPoint!;
        this.motion = motion;
        this.gesture = gesture;
        this.detailedLogging = detailedLogging;
        this.settings = settings;
        this.cancelButtons = cancelButtons;
        this.output = output;
        logger = new RawSampleLogger(output, detailedLogging);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        clockOrigin = Stopwatch.GetTimestamp();
        try
        {
            logger.Listening(LocalEndpoint);
            while (!cancellationToken.IsCancellationRequested)
            {
                long now = Stopwatch.GetTimestamp();
                CheckTimeouts(now);
                using var receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                long? deadline = touchDeadline;
                var p = Presence;
                if (p.Connected)
                {
                    long presenceDeadline = p.LastSeenAtTicks + Ticks(PresenceTimeout);
                    deadline = deadline.HasValue ? Math.Min(deadline.Value, presenceDeadline) : presenceDeadline;
                }
                if (deadline.HasValue)
                    receiveCancellation.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1,
                        (deadline.Value - now) * 1000.0 / Stopwatch.Frequency)));

                UdpReceiveResult received;
                try
                {
                    received = await socket.ReceiveAsync(receiveCancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    continue;
                }

                ProcessDatagram(received.Buffer, received.RemoteEndPoint, Stopwatch.GetTimestamp(), clock.Elapsed.TotalMilliseconds);
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
            output.WriteLine($"presence_stats: heartbeatPackets={Statistics.HeartbeatPackets} outdatedRunPackets={Statistics.OutdatedRunPackets} presenceTimeouts={PresenceTimeouts}");
            logger.Stopped();
        }
    }

    private void RecordTimeout(double elapsedMs)
    {
        // Silence is indistinguishable from a stationary held finger. Preserve all motion state.
        Interlocked.Increment(ref inputTimeouts);
        logger.Timeout(elapsedMs);
    }

    // These methods run only on the sequential input loop. Explicit time supports deterministic tests.
    internal void CheckTimeouts(long now)
    {
        if (touchDeadline is long deadline && now >= deadline)
        {
            touchDeadline = null;
            RecordTimeout((now - clockOrigin) * 1000.0 / Stopwatch.Frequency);
        }
        var p = Presence;
        if (p.Connected && Stopwatch.GetElapsedTime(p.LastSeenAtTicks, now) >= PresenceTimeout)
        {
            Volatile.Write(ref presence, p with { Connected = false });
            Interlocked.Increment(ref presenceTimeouts);
            ClearInput();
            output.WriteLine($"presence: status=disconnected senderRunId={p.RunId:X16} timeouts={PresenceTimeouts}");
        }
    }

    internal void ProcessDatagram(byte[] bytes, IPEndPoint remote, long now, double elapsedMs = 0)
    {
        CheckTimeouts(now); // Invalid/old traffic must not postpone expiry, even at the recovery boundary.
        Statistics.RecordReceived();
        if (!PacketDecoder.TryDecode(bytes, out var packet, out string error))
        {
            Statistics.RecordInvalid();
            logger.Invalid(elapsedMs, remote, bytes.Length, error);
        }
        else ProcessValid(packet!, remote, now, elapsedMs);
        if (detailedLogging) logger.Stats(Statistics);
    }

    private void ProcessValid(TouchPacket packet, IPEndPoint remote, long now, double elapsedMs)
    {
        var h = packet.Header;
        var p = Presence;
        if (h.SenderRunId != p.RunId)
        {
            if (retiredRunIds.Contains(h.SenderRunId)) { Statistics.RecordOutdatedRun(); return; }
            if (h.EventType is not (TouchEventType.Down or TouchEventType.Heartbeat)) return;
            if (p.RunId is ulong old) retiredRunIds.Add(old);
            ClearInput();
            Statistics.ResetSequence();
            touchDeadline = null;
            Interlocked.Exchange(ref lastAcceptedAtTicks, 0);
            Volatile.Write(ref presence, new(h.SenderRunId));
            output.WriteLine($"sender_run: senderRunId={h.SenderRunId:X16} trigger={h.EventType}");
        }
        if (h.EventType == TouchEventType.Heartbeat)
        {
            Statistics.RecordHeartbeat();
            RecordPresence(remote, now);
            return;
        }
        var observation = Statistics.Observe(h);
        if (observation.Accepted)
        {
            RecordPresence(remote, now);
            touchDeadline = now + Ticks(inputTimeout);
            Interlocked.Exchange(ref lastAcceptedAtTicks, now);
            var snapshot = settings?.Current;
            if (snapshot is null) { motion?.Process(packet); gesture?.Process(packet); }
            else
            {
                motion?.Process(packet, snapshot.SensitivityX, snapshot.SensitivityY);
                gesture?.Process(packet, snapshot.TapMaxDurationMs, snapshot.TapMovementThresholdPx, snapshot.ClickHoldMs);
            }
            Interlocked.Exchange(ref activeSession, motion?.ActiveSessionId is uint id ? id : -1);
        }
        logger.Packet(elapsedMs, remote, packet, observation);
        if (observation.Accepted && h.EventType == TouchEventType.Down)
            output.WriteLine($"touch_start: senderRunId={h.SenderRunId:X16} sequence={h.Sequence} sessionId={h.SessionId}");
        if (observation.Accepted && h.EventType == TouchEventType.Up && motion is not null)
            output.WriteLine($"touch_end: senderRunId={h.SenderRunId:X16} sequence={h.Sequence} outputEvents={motion.OutputEvents} totalDx={motion.TotalDx} totalDy={motion.TotalDy} clicksTriggered={gesture?.ClicksTriggered ?? 0}");
    }

    private void RecordPresence(IPEndPoint remote, long now)
    {
        var p = Presence;
        if (!remote.Address.Equals(lastRemoteIp)) Volatile.Write(ref lastRemoteIp, remote.Address);
        string ip = LastRemoteIp!;
        Volatile.Write(ref presence, new(p.RunId, now, true, ip));
        if (!p.Connected) output.WriteLine($"presence: status=connected senderRunId={p.RunId:X16} remote={ip}");
    }

    private void ClearInput()
    {
        motion?.Reset();
        gesture?.Reset();
        Interlocked.Exchange(ref activeSession, -1);
        cancelButtons?.Invoke();
    }
    private static long Ticks(TimeSpan time) => (long)(time.TotalSeconds * Stopwatch.Frequency);

    public void Dispose()
    {
        gesture?.Reset();
        socket.Dispose();
        motion?.Reset();
    }
}
