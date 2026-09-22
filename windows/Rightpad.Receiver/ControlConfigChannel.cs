using System.Diagnostics;
using System.Net;
namespace Rightpad.Receiver;

internal sealed class ControlConfigChannel : IDisposable
{
    private readonly object gate = new();
    private readonly RuntimeSettingsStore store;
    private readonly Func<SenderPresence> presence;
    private readonly Action<IPAddress, byte[]> send;
    private readonly TextWriter log;
    private VirtualControlsSettings current;
    private bool disposed;
    private long? lastReply;
    public ulong Epoch { get; } = (ulong)Random.Shared.NextInt64(1, long.MaxValue);
    public ulong Revision { get; private set; } = 1;
    public (ulong Epoch, ulong Revision) CaptureVersion() { lock (gate) return (Epoch, Revision); }
    public ControlConfigChannel(RuntimeSettingsStore store, Func<SenderPresence> presence,
        Action<IPAddress, byte[]> send, TextWriter log)
    {
        this.store = store; this.presence = presence; this.send = send; this.log = log;
        lock (gate) { current = store.Current.Controls; store.Published += Published; current = store.Current.Controls; }
    }
    private void Published()
    {
        lock (gate)
        {
            if (disposed || current == store.Current.Controls) return;
            current = store.Current.Controls;
            Revision = checked(Revision + 1);
            Push(presence(), "save");
        }
    }
    private void Push(SenderPresence p, string reason)
    {
        if (!p.Connected || p.RunId is not ulong run || !IPAddress.TryParse(p.RemoteIp, out var address)) return;
        // Sending is a bounded, nonblocking enqueue into the existing 50002 sender worker.
        try { send(address, ControlConfigProtocol.Encode(run, Epoch, Revision, current));
            log.WriteLine($"controls_config_queued: reason={reason} senderRunId={run:X16} epoch={Epoch:X16} revision={Revision}"); }
        catch (Exception e) { log.WriteLine($"controls_config_send_error: {e.Message}"); }
    }
    public bool Request(ControlConfigRequest request, IPAddress source, long now)
    {
        lock (gate)
        {
            var p = presence();
            if (disposed || !p.Connected || request.SenderRunId != p.RunId || source.ToString() != p.RemoteIp) return false;
            // At most one requested snapshot/second, including idempotent confirmations.
            if (lastReply.HasValue && Stopwatch.GetElapsedTime(lastReply.Value, now) < TimeSpan.FromSeconds(1)) return true;
            lastReply = now;
            Push(p, request.Epoch == Epoch && request.Revision == Revision ? "confirm" : "request");
            return true;
        }
    }
    public void Dispose() { lock (gate) { disposed = true; store.Published -= Published; } }
}
