using System.Diagnostics;
using System.Globalization;

namespace Rightpad.Receiver;

internal sealed class RuntimeStatsViewModel : ObservableModel
{
    private readonly Queue<(long Time, long Samples, long Packets)> history = new();
    private long runId = -1;
    private string status = "Stopped", samplesHz = "—", packetsHz = "—", runtimeState = "Stopped";
    private string remoteIp = "—", endpoint = "Not listening", session = "None", lastAccepted = "Never";
    private string mouseBackend = "Not selected", error = "";
    private long gap, old, duplicate, invalid, timeouts;
    private string lastSeen = "Never";
    private long heartbeats, presenceTimeouts, outdatedRuns;
    public string LastSeen { get => lastSeen; private set => Set(ref lastSeen, value); }
    public long Heartbeats { get => heartbeats; private set => Set(ref heartbeats, value); }
    public long PresenceTimeouts { get => presenceTimeouts; private set => Set(ref presenceTimeouts, value); }
    public long OutdatedRuns { get => outdatedRuns; private set => Set(ref outdatedRuns, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public string SamplesHz { get => samplesHz; private set => Set(ref samplesHz, value); }
    public string PacketsHz { get => packetsHz; private set => Set(ref packetsHz, value); }
    public string RuntimeState { get => runtimeState; private set => Set(ref runtimeState, value); }
    public string RemoteIp { get => remoteIp; private set => Set(ref remoteIp, value); }
    public string Endpoint { get => endpoint; private set => Set(ref endpoint, value); }
    public string Session { get => session; private set => Set(ref session, value); }
    public string LastAccepted { get => lastAccepted; private set => Set(ref lastAccepted, value); }
    public string MouseBackend { get => mouseBackend; private set => Set(ref mouseBackend, value); }
    public string Error { get => error; private set => Set(ref error, value); }
    public long Gap { get => gap; private set => Set(ref gap, value); }
    public long Old { get => old; private set => Set(ref old, value); }
    public long Duplicate { get => duplicate; private set => Set(ref duplicate, value); }
    public long Invalid { get => invalid; private set => Set(ref invalid, value); }
    public long Timeouts { get => timeouts; private set => Set(ref timeouts, value); }

    public static string Activity(RuntimeStatsSnapshot s, long now) => s.RuntimeState switch
    {
        ReceiverState.Starting => "Starting…",
        ReceiverState.Stopping => "Stopping…",
        ReceiverState.Error => "Error",
        ReceiverState.Stopped => "Receiver Stopped",
        _ when s.Presence?.RunId is null => "Waiting for Android",
        _ when s.Presence.Connected && Stopwatch.GetElapsedTime(s.Presence.LastSeenAtTicks, now) < UdpReceiver.PresenceTimeout => "Connected",
        _ => "Disconnected"
    };

    public void Refresh(RuntimeStatsSnapshot s, long now)
    {
        Status = Activity(s, now);
        RuntimeState = s.RuntimeState.ToString();
        RemoteIp = s.Presence?.RemoteIp ?? "—";
        LastSeen = s.Presence?.RunId is null ? "Never" :
            FormatAge(Math.Max(0, Stopwatch.GetElapsedTime(s.Presence.LastSeenAtTicks, now).TotalSeconds));
        Heartbeats = s.HeartbeatPackets;
        PresenceTimeouts = s.PresenceTimeouts;
        OutdatedRuns = s.OutdatedRunPackets;
        Endpoint = s.RuntimeState == ReceiverState.Running ? "0.0.0.0:50000" : "Not listening";
        Session = s.ActiveTouchSessionId < 0 ? "None" : $"Active · {s.ActiveTouchSessionId}";
        LastAccepted = s.LastAcceptedAtTicks == 0 ? "Never" :
            FormatAge(Math.Max(0, Stopwatch.GetElapsedTime(s.LastAcceptedAtTicks, now).TotalSeconds));
        MouseBackend = s.MouseBackend;
        Error = s.LastError ?? "";
        Gap = s.GapCount; Old = s.OldCount; Duplicate = s.DuplicateCount;
        Invalid = s.InvalidCount; Timeouts = s.InputTimeoutCount;
        if (runId != s.RunId) { history.Clear(); runId = s.RunId; }
        if (s.RuntimeState != ReceiverState.Running)
        { history.Clear(); SamplesHz = PacketsHz = "—"; return; }
        while (history.Count > 1 && Stopwatch.GetElapsedTime(history.Peek().Time, now).TotalSeconds > 1)
            history.Dequeue();
        double samples = 0, packets = 0;
        if (history.TryPeek(out var previous) && now > previous.Time)
        {
            double seconds = Stopwatch.GetElapsedTime(previous.Time, now).TotalSeconds;
            samples = Math.Max(0, s.AcceptedSamples - previous.Samples) / seconds;
            packets = Math.Max(0, s.ReceivedPackets - previous.Packets) / seconds;
        }
        history.Enqueue((now, s.AcceptedSamples, s.ReceivedPackets));
        SamplesHz = samples.ToString("F1", CultureInfo.InvariantCulture);
        PacketsHz = packets.ToString("F1", CultureInfo.InvariantCulture);
    }

    private static string FormatAge(double totalSeconds)
    {
        if (totalSeconds < 60)
            return $"{totalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s ago";

        int totalWholeSeconds = (int)Math.Floor(totalSeconds);
        int minutes = totalWholeSeconds / 60;
        int seconds = totalWholeSeconds % 60;
        return seconds == 0 ? $"{minutes}m ago" : $"{minutes}m {seconds}s ago";
    }
}
