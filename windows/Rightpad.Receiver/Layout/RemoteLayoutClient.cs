using System.Diagnostics;
using System.Net;

namespace Rightpad.Receiver;

/** One bounded operation, three attempts within three seconds, then reconciliation. */
internal sealed class RemoteLayoutClient(Func<SenderPresence> presence, Action<IPAddress, byte[]> send)
{
    private readonly object gate = new();
    private LayoutMessage? latest, operation;
    private LayoutMessage? reconciliation;
    private string? address;
    private ulong? run;
    private ulong request;
    internal static readonly long OperationLifetime = 3 * Stopwatch.Frequency;
    private long nextSend, receivedAt, deadline, operationStartedAt, operationExpiresAt, operationSentAt;
    private int attempts;
    private bool uncertain;
    internal Func<LayoutRect,string?> ValidateModeLayout { get; set; } = _ => null;
    internal string? ValidationError { get; private set; }
    private readonly Dictionary<ulong, long> queries = new();
    public (LayoutMessage? Snapshot, bool Connected, bool Uncertain, int Remaining, long At) Capture(long now)
    {
        lock (gate)
        {
            ExpireOperation(now);
            var p = presence();
            bool connected = p.Connected && p.RunId == run && p.RemoteIp == address;
            bool fresh = connected && latest is not null && Stopwatch.GetElapsedTime(receivedAt, now).TotalSeconds < 4;
            return (fresh ? latest : null, connected, uncertain,
                latest?.State == 2 ? (int)Math.Max(0, (deadline - now) * 1000.0 / Stopwatch.Frequency) : 0, receivedAt);
        }
    }
    public void Poll(long now, bool force = false)
    {
        lock (gate)
        {
            var p = presence();
            if (!p.Connected || p.RunId is null || p.RemoteIp is null)
            {
                // Preserve the original identity across temporary absence. Reconnect can only
                // reconcile it, never resume a mutation whose result was lost.
                ReconcileOperation(); latest = null; queries.Clear(); nextSend = 0; return;
            }
            if (p.RunId != run || p.RemoteIp != address)
            {
                run = p.RunId; address = p.RemoteIp; operation = latest = null;
                reconciliation = null;
                queries.Clear(); attempts = 0; nextSend = 0; uncertain = false;
            }
            ExpireOperation(now); // force and a stopped UI timer cannot renew the lifetime.
            if (now < nextSend && (!force || operation is not null)) return;
            nextSend = now + Stopwatch.Frequency;
            if (operation is not null && attempts < 3)
            {
                operationSentAt = now;
                send(IPAddress.Parse(address!), RemoteLayoutProtocol.Encode(operation)); attempts++;
            }
            else
            {
                ReconcileOperation();
                nextSend = now + Stopwatch.Frequency;
                ulong id = ++request;
                if (queries.Count >= 8) queries.Remove(queries.Keys.First());
                queries[id] = now;
                send(IPAddress.Parse(address!), RemoteLayoutProtocol.Encode(new(RemoteLayoutProtocol.Query,
                    0, 0, run!.Value, reconciliation?.Epoch ?? 0, reconciliation?.Revision ?? 0,
                    reconciliation?.Geometry ?? 0, reconciliation?.Transaction ?? 0, id, 0, 0, 0, 0, 0, [])));
            }
        }
    }
    private void ExpireOperation(long now)
    {
        if (operation is not null && now >= operationExpiresAt) ReconcileOperation();
    }
    private void ReconcileOperation()
    {
        if (operation is null) return;
        reconciliation = operation;
        operation = null;
        uncertain = true;
        // A Query sent before this transition cannot prove that an unused token was sealed.
        queries.Clear();
        nextSend = 0;
    }
    internal void Suspend(long now)
    {
        lock (gate)
        {
            ExpireOperation(now);
            ReconcileOperation(); // Change state before any lifecycle-triggered Poll can send.
        }
    }
    public bool Begin(byte kind, LayoutMessage basis, LayoutItem[]? draft, long now)
    {
        ValidationError=null;
        if(kind==RemoteLayoutProtocol.Try && draft?.FirstOrDefault(i=>i.Id=="rightpad.input.mode") is { } mode &&
            ValidateModeLayout(mode.Rect) is { } error) {ValidationError=error;return false;}
        lock (gate)
        {
            ExpireOperation(now);
            var p = presence();
            if (!p.Connected || p.RunId != basis.Run || p.RemoteIp != address || latest is null ||
                latest.Epoch != basis.Epoch || latest.Revision != basis.Revision || latest.Geometry != basis.Geometry ||
                operation is not null || reconciliation is not null ||
                Stopwatch.GetElapsedTime(receivedAt, now).TotalSeconds >= 4) return false;
            if (kind == RemoteLayoutProtocol.Try && latest.State is 1 or 2) return false;
            if (kind == RemoteLayoutProtocol.Keep && (latest.State != 2 || now >= deadline)) return false;
            ulong txn = kind == RemoteLayoutProtocol.Try ? latest.Next : latest.Transaction;
            operation = basis with { Kind = kind, State = 0, Error = 0, Transaction = txn, Request = ++request,
                Remaining = 0, Serial = 0, Items = kind == RemoteLayoutProtocol.Try ? draft! : [] };
            attempts = 0; uncertain = true; nextSend = 0;
            operationStartedAt = now;
            operationExpiresAt = operationStartedAt + OperationLifetime;
            queries.Clear();
        }
        Poll(now); return true;
    }
    public void Accept(byte[] bytes, IPAddress source, long now)
    {
        var m = RemoteLayoutProtocol.Decode(bytes);
        if (m?.Kind != RemoteLayoutProtocol.Snapshot) return;
        lock (gate)
        {
            ExpireOperation(now);
            var p = presence();
            if (!p.Connected || m.Run != p.RunId || source.ToString() != p.RemoteIp || p.RunId != run || address != p.RemoteIp) return;
            bool queryReply = queries.Remove(m.Request, out long queryAt);
            bool opReply = operation?.Request == m.Request;
            if (m.Request != 0 && !queryReply && !opReply) return;
            // Unsolicited snapshots may update an already observed epoch, never establish one.
            if (latest is null && !queryReply && !opReply) return;
            if (m.Request == 0 && m.State == 2 && latest?.State != 2) return; // Establish deadline from a timed request/reply.
            if (latest is not null && (m.Epoch != latest.Epoch ? !queryReply : m.Serial <= latest.Serial)) return;
            if (latest is not null && m.Epoch != latest.Epoch) queries.Clear();
            long transit = queryReply ? now - queryAt : opReply ? now - operationSentAt : 0;
            long proposedDeadline = now + (long)(m.Remaining / 1000.0 * Stopwatch.Frequency) - Math.Max(0, transit);
            if (latest?.Epoch == m.Epoch && latest.Transaction == m.Transaction && latest.State == 2 && m.State == 2)
                deadline = Math.Min(deadline, proposedDeadline);
            else deadline = proposedDeadline;
            latest = m; receivedAt = now;
            // On the same base a reconciliation Query must have observed/consumed the
            // original token. A Ready reply with a lower token is not a sealed transaction.
            bool reconciled = queryReply && (reconciliation is null || m.Epoch != reconciliation.Epoch ||
                m.Revision != reconciliation.Revision || m.Geometry != reconciliation.Geometry ||
                m.Transaction >= reconciliation.Transaction);
            if (opReply || reconciled) { operation = reconciliation = null; uncertain = false; }
        }
    }
}
