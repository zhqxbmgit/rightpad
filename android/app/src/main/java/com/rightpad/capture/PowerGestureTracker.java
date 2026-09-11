package com.rightpad.capture;

final class PowerGestureTracker {
    private boolean claimed;
    private boolean cancelled;
    private boolean delivered;
    private float downX;
    private float downY;
    private long downTimeMs;

    boolean onDown(float x, float y, long eventTimeMs, float centerX, float centerY,
            float radius) {
        claimed = inside(x, y, centerX, centerY, radius);
        cancelled = false;
        delivered = false;
        downX = x;
        downY = y;
        downTimeMs = eventTimeMs;
        return claimed;
    }

    void onMove(float x, float y, float maximumMovement) {
        if (!claimed || cancelled) return;
        float dx = x - downX;
        float dy = y - downY;
        if (dx * dx + dy * dy > maximumMovement * maximumMovement) cancelled = true;
    }

    boolean onUp(float x, float y, long eventTimeMs, float centerX, float centerY,
            float radius, long maximumDurationMs) {
        if (!claimed || delivered) return false;
        boolean activate = !cancelled
                && eventTimeMs - downTimeMs >= 0L
                && eventTimeMs - downTimeMs <= maximumDurationMs
                && inside(x, y, centerX, centerY, radius);
        delivered = activate;
        claimed = false;
        return activate;
    }

    void onCancel() {
        cancelled = true;
        claimed = false;
    }

    boolean isClaimed() {
        return claimed;
    }

    private static boolean inside(float x, float y, float centerX, float centerY, float radius) {
        float dx = x - centerX;
        float dy = y - centerY;
        return dx * dx + dy * dy <= radius * radius;
    }
}
