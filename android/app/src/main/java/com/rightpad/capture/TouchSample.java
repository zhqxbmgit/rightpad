package com.rightpad.capture;

final class TouchSample {
    enum Action { DOWN, MOVE, UP, CANCEL }

    final float x;
    final float y;
    // MotionEvent nanoseconds in the uptime time base, not callback arrival time.
    final long eventTimeNs;
    final Action action;
    final int pointerId;
    final long sessionId;
    final boolean historical;

    TouchSample(float x, float y, long eventTimeNs, Action action,
            int pointerId, long sessionId, boolean historical) {
        this.x = x;
        this.y = y;
        this.eventTimeNs = eventTimeNs;
        this.action = action;
        this.pointerId = pointerId;
        this.sessionId = sessionId;
        this.historical = historical;
    }
}
