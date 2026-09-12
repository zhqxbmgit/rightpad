package com.rightpad.capture;

import java.net.Inet4Address;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.util.Arrays;

// Worker-owned deterministic selection; times are monotonic milliseconds.
final class DiscoverySelection {
    static final long STALE_MS = 2500;
    DiscoveryProtocol.Offer current;
    InetSocketAddress target;
    private long expiryDeadline;
    boolean accept(DiscoveryProtocol.Offer offer, InetAddress source, long now) {
        if (!(source instanceof Inet4Address) || source.isAnyLocalAddress()
                || source.isLoopbackAddress() || source.isMulticastAddress()) return false;
        if (current != null && !Arrays.equals(current.receiverId, offer.receiverId)) return false;
        current = offer;
        target = new InetSocketAddress(source, offer.touchPort);
        expiryDeadline = now + STALE_MS;
        return true;
    }
    boolean expire(long now) {
        if (current == null || now < expiryDeadline) return false;
        clear();
        return true;
    }
    void clear() { current = null; target = null; }
    // Pause suspends probing, not sender identity. Input remains disabled until a real OFFER.
    void awaitResumeConfirmation(long now) { if (current != null) expiryDeadline = now + STALE_MS; }
}
