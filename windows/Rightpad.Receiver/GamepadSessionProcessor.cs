using System.Diagnostics;
using System.Net;

namespace Rightpad.Receiver;

internal readonly record struct GamepadSessionStats(long Accepted, long Duplicates, long Stale, long Rejected, long LeaseExpirations);

/// <summary>Independent full-state ordering, minimum dwell and lease. No gesture or Motion ownership.</summary>
internal sealed class GamepadSessionProcessor(Func<XboxGamepadState, bool> submit, Action<string>? log = null,
    Func<long>? timestamp = null)
{
    public static readonly TimeSpan Lease = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan MaintenancePeriod = TimeSpan.FromMilliseconds(40);
    private readonly object gate = new();
    private SenderPresence authority = new();
    private uint? sequence;
    private XboxGamepadState state;
    private long refreshedAt;
    private long minimumDwellUntil;
    private XboxGamepadState? pendingState;
    private readonly SemaphoreSlim deadlineChanged = new(0, 1);
    private readonly Func<long> clock = timestamp ?? Stopwatch.GetTimestamp;
    private bool failed, stopped;
    private GamepadSessionStats stats;
    public GamepadSessionStats Stats { get { lock (gate) return stats; } }
    internal XboxGamepadState State { get { lock (gate) return state; } }

    public void UpdatePresence(SenderPresence presence)
    {
        lock (gate)
        {
            bool newRun = authority.RunId != presence.RunId;
            if (newRun || !presence.Connected || authority.RemoteIp != presence.RemoteIp) Neutral("presence_change");
            if (newRun) sequence = null;
            // Disconnect/reconnect of the same run retains its watermark: late duplicates cannot re-press.
            authority = presence;
        }
    }
    public bool Process(GamepadStatePacket packet, IPAddress source, long now)
    {
        lock (gate)
        {
            CheckLeaseLocked(now);
            if (stopped || failed || !authority.Connected || packet.SenderRunId != authority.RunId
                || source.ToString() != authority.RemoteIp)
            { stats = stats with { Rejected = stats.Rejected + 1 }; return false; }
            if (sequence is uint previous)
            {
                uint delta = unchecked(packet.Sequence - previous);
                if (delta == 0) { stats = stats with { Duplicates = stats.Duplicates + 1 }; return false; }
                // Serial-number arithmetic: exactly half the range is ambiguous and rejected too.
                if (delta >= 0x80000000U) { stats = stats with { Stale = stats.Stale + 1 }; return false; }
            }
            sequence = packet.Sequence;
            refreshedAt = now;
            stats = stats with { Accepted = stats.Accepted + 1 };
            log?.Invoke($"gamepad_received: senderRunId={packet.SenderRunId:X16} sequence={packet.Sequence} buttons={(ushort)packet.State.Buttons} minimumDwellMs={packet.MinimumDwellMs} forceNeutral={packet.ForceNeutral} atTicks={now}");
            if (packet.ForceNeutral) { Neutral("force_neutral"); return !failed; }
            if (packet.State == XboxGamepadState.Neutral && now < minimumDwellUntil)
            {
                pendingState = packet.State;
                WakeDeadline();
                return true;
            }
            pendingState = null;
            // Zero-dwell same-state refresh renews the lease, not the original dwell deadline.
            // A changed state or a new explicit dwell replaces the previous constraint.
            if (packet.State != state || packet.MinimumDwellMs != 0)
                minimumDwellUntil = now + (long)Math.Ceiling(packet.MinimumDwellMs * (double)Stopwatch.Frequency / 1000);
            if (packet.State == XboxGamepadState.Neutral) minimumDwellUntil = 0;
            WakeDeadline();
            if (packet.State != state && !Output(packet.State)) return false;
            if (packet.MinimumDwellMs != 0)
            {
                // Acceptance includes successful backend submission. Backend call time must not
                // consume the requested visible dwell; this also enforces the receive-time bound.
                minimumDwellUntil = Math.Max(minimumDwellUntil, clock() +
                    (long)Math.Ceiling(packet.MinimumDwellMs * (double)Stopwatch.Frequency / 1000));
                WakeDeadline();
            }
            return true;
        }
    }
    private bool Output(XboxGamepadState next)
    {
        long submittedAt = clock();
        try {
            if (submit(next)) {
                state = next;
                log?.Invoke($"gamepad_output: senderRunId={authority.RunId:X16} sequence={sequence} buttons={(ushort)next.Buttons} submittedAtTicks={submittedAt} completedAtTicks={clock()}");
                return true;
            }
        }
        catch (Exception e) { log?.Invoke($"gamepad_processor_error: {e.Message}"); }
        failed = true;
        state = XboxGamepadState.Neutral;
        ClearDwell();
        // Phase-2 backend handles ambiguous report failure by neutralizing and destroying its device.
        log?.Invoke("gamepad_processor_unavailable");
        return false;
    }
    public void CheckLease(long now) { lock (gate) CheckLeaseLocked(now); }
    internal void CheckDeadlines(long now)
    {
        lock (gate)
        {
            CheckLeaseLocked(now); // Safety always wins over an ordinary deferred release.
            if (!stopped && !failed && pendingState.HasValue && now >= minimumDwellUntil)
                Neutral("minimum_dwell_complete");
        }
    }
    private void WakeDeadline() { if (deadlineChanged.CurrentCount == 0) deadlineChanged.Release(); }
    private void ClearDwell() { pendingState = null; minimumDwellUntil = 0; WakeDeadline(); }
    private void CheckLeaseLocked(long now)
    {
        if (state == XboxGamepadState.Neutral || Stopwatch.GetElapsedTime(refreshedAt, now) < Lease) return;
        stats = stats with { LeaseExpirations = stats.LeaseExpirations + 1 };
        log?.Invoke($"gamepad_lease_expired: sequence={sequence} elapsedMs={Stopwatch.GetElapsedTime(refreshedAt, now).TotalMilliseconds:F3}");
        Neutral("lease_expired"); // Keep sequence: an expired duplicate cannot revive a held state.
    }
    private void Neutral(string reason)
    {
        ClearDwell();
        if (state == XboxGamepadState.Neutral) return;
        Output(XboxGamepadState.Neutral);
        state = XboxGamepadState.Neutral;
        log?.Invoke($"gamepad_neutral: reason={reason}");
    }
    public void Stop()
    {
        lock (gate) { stopped = true; Neutral("stop"); }
    }
    public async Task MaintainAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int waitMs;
                lock (gate)
                {
                    if (stopped) return;
                    long now = clock();
                    CheckDeadlines(now);
                    // Ceiling avoids truncating a fractional millisecond into an early/busy wake.
                    // Every wake rechecks monotonic time; timer imprecision never permits early output.
                    waitMs = pendingState.HasValue
                        ? (int)Math.Clamp(Math.Ceiling((minimumDwellUntil - now) * 1000.0 / Stopwatch.Frequency), 1, MaintenancePeriod.TotalMilliseconds)
                        : (int)MaintenancePeriod.TotalMilliseconds;
                }
                await deadlineChanged.WaitAsync(waitMs, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}
