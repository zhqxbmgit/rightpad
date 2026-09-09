package com.rightpad.capture;

// Only deadline/state arithmetic; UDP and lifecycle remain in UdpTouchSender/MainActivity.
final class HeartbeatSchedule {
    static final long INTERVAL_NS = 500_000_000L;
    private boolean enabled;
    private long nextDeadline;

    synchronized void setEnabled(boolean value, long now) {
        enabled = value;
        if (value) nextDeadline = now;
    }

    synchronized boolean claimDue(long now) {
        if (!enabled || now - nextDeadline < 0) return false;
        nextDeadline = now + INTERVAL_NS; // Late wakes send once, never replay missed periods.
        return true;
    }

    synchronized long waitNanos(long now) {
        return enabled ? Math.max(0, nextDeadline - now) : Long.MAX_VALUE;
    }
}
