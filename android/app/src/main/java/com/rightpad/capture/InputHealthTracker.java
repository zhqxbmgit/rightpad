package com.rightpad.capture;

import static com.rightpad.capture.InputHealthStatus.Health.*;

/** UI-thread owned; monotonic receive age and sender identity are independent of Touch activity. */
final class InputHealthTracker {
    static final long STALE_NS = 1_500_000_000L;
    private boolean connected;
    private long runId, receivedAt;
    private StatusProtocol.Snapshot snapshot;

    void configure(boolean active, long run) {
        if (active != connected || run != runId) snapshot = null;
        connected = active;
        runId = run;
    }
    boolean accept(StatusProtocol.Snapshot value, long now) {
        if (!connected || value == null || value.runId() != runId) return false;
        snapshot = value;
        receivedAt = now;
        return true;
    }
    InputHealthStatus display(ControlConfigCache cache, long now) {
        if (!connected) return InputHealthStatus.OFFLINE;
        if (snapshot == null) return new InputHealthStatus(true, PENDING, OFFLINE, PENDING);
        if (now - receivedAt > STALE_NS)
            return new InputHealthStatus(true, PENDING, PENDING, PENDING);
        int state = snapshot.state(), flags = snapshot.flags();
        boolean fatal = state == StatusProtocol.ERROR || (flags & StatusProtocol.RUNTIME_ERROR) != 0;
        var input = fatal || (flags & StatusProtocol.MOUSE_FAILURE) != 0 ? ERROR
                : state == StatusProtocol.STOPPED ? OFFLINE
                : state == StatusProtocol.RUNNING && (flags & StatusProtocol.MOUSE_HEALTHY) != 0 ? GOOD : PENDING;
        var xbox = (flags & StatusProtocol.GAMEPAD_FAILURE) != 0 ? ERROR
                : state == StatusProtocol.STARTING || state == StatusProtocol.STOPPING ? PENDING
                : fatal || state == StatusProtocol.STOPPED ? OFFLINE
                : (flags & StatusProtocol.GAMEPAD_AVAILABLE) != 0 ? GOOD : ERROR;
        var config = cache.complete() && cache.epoch() == snapshot.epoch()
                && cache.revision() == snapshot.revision() ? GOOD : PENDING;
        return new InputHealthStatus(true, input, xbox, config);
    }
}
