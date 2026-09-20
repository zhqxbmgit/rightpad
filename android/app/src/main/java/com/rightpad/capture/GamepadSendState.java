package com.rightpad.capture;

/** Single coalesced slot, owned under UdpTouchSender.sendGate. No queue, thread or socket. */
final class GamepadSendState {
    static final long REFRESH_NS = 100_000_000L;
    record Packet(long sequence, GamepadStateSubmission submission, int copies) {
        GamepadState state() { return submission.state(); }
    }
    private GamepadState latest = GamepadState.NEUTRAL;
    private Packet pending;
    private long nextSequence, refreshAt;
    private boolean used;
    private Packet pulse;
    private long holdNs, firstSentAt, retryAt;
    private boolean pulseSent;
    GamepadState latest() { return latest; }
    boolean used() { return used; }
    void change(GamepadState state, long now) {
        change(GamepadStateSubmission.ordinary(state), now);
    }
    void change(GamepadStateSubmission submission, long now) {
        GamepadState state = submission.state();
        if (submission.safetyNeutral()) clearHold();
        // Safety must also upgrade an already queued ordinary Neutral on the wire.
        if (latest.equals(state) && !submission.safetyNeutral()) return;
        latest = state;
        if (!state.neutral()) clearHold(); // A new full state replaces the old pulse, without an edge queue.
        pending = version(submission, 3);
        if (submission.minimumWireHoldMs() != 0) {
            pulse = pending;
            holdNs = submission.minimumWireHoldMs() * 1_000_000L;
            retryAt = now;
        }
        if (!state.neutral() || pulse == null) refreshAt = now + REFRESH_NS;
    }
    private Packet version(GamepadState state, int copies) {
        return version(GamepadStateSubmission.ordinary(state), copies);
    }
    private Packet version(GamepadStateSubmission submission, int copies) {
        Packet packet = new Packet(nextSequence, submission, copies);
        nextSequence = (nextSequence + 1) & 0xffffffffL;
        used = true;
        return packet;
    }
    private void clearHold() { pulse = null; pulseSent = false; holdNs = 0; }
    Packet retire() { clearHold(); latest = GamepadState.NEUTRAL; pending = null; return version(GamepadStateSubmission.safety(), 3); }
    // Called immediately after the FIRST successful DatagramSocket.send, under the same send gate.
    void sent(Packet packet, long actualSendTime) {
        if (packet == pulse && !pulseSent) { pulseSent = true; firstSentAt = actualSendTime; }
    }
    void failed(Packet packet, long now) {
        if (packet == pulse && !pulseSent) retryAt = now + REFRESH_NS;
    }
    private long remainingHold(long now) { return Math.max(0, holdNs - (now - firstSentAt)); }
    Packet claim(long now, boolean foreground) {
        if (pulse != null && !pulseSent && foreground) {
            if (now < retryAt) return null;
            if (pending == pulse) pending = null;
            refreshAt = now + REFRESH_NS;
            return pulse; // Preserve one unsent pulse even when logical Neutral is already pending.
        }
        if (pending != null && (foreground || pending.state().neutral())) {
            if (pending.state().neutral() && pulse != null && remainingHold(now) != 0) {
                if (!foreground || now < refreshAt) return null;
                // A longer generic dwell still renews the Receiver lease. The eventual Neutral must
                // follow this refresh in serial order, rather than reuse its earlier queued sequence.
                Packet refresh = version(pulse.state(), 1);
                pending = version(GamepadState.NEUTRAL, 3);
                refreshAt = now + REFRESH_NS;
                return refresh;
            }
            Packet result = pending; pending = null; refreshAt = now + REFRESH_NS;
            if (result.state().neutral()) clearHold();
            return result;
        }
        if (foreground && !latest.neutral() && now >= refreshAt) {
            refreshAt = now + REFRESH_NS;
            return version(latest, 1);
        }
        return null;
    }
    long waitNanos(long now, boolean foreground) {
        if (pulse != null && !pulseSent && foreground) return Math.max(0, retryAt - now);
        if (pending != null && pending.state().neutral() && pulse != null)
            return Math.min(remainingHold(now), foreground ? Math.max(0, refreshAt - now) : Long.MAX_VALUE);
        if (pending != null) return 0;
        return foreground && !latest.neutral() ? Math.max(0, refreshAt - now) : Long.MAX_VALUE;
    }
}
