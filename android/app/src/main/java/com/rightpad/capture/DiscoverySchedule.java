package com.rightpad.capture;

final class DiscoverySchedule {
    private int probe;
    private long deadline;
    void reset(long now) { probe = 0; deadline = now; }
    boolean due(long now, boolean connected) {
        if (now < deadline) return false;
        long interval = connected || probe >= 3 ? 1000 : probe < 2 ? 250 : 500;
        probe++;
        deadline = now + interval;
        return true;
    }
}
