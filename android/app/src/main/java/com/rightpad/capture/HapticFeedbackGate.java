package com.rightpad.capture;

import java.net.InetAddress;
import java.util.ArrayDeque;
import java.util.Iterator;

// UI-thread owned. Correlates raw UP identities only; contains no gesture recognition.
final class HapticFeedbackGate {
    static final long MAX_AGE_NS = 1_000_000_000L;
    static final int CAPACITY = 32;
    private static final class Pending {
        final long sessionId, sequence, at;
        Pending(long sessionId, long sequence, long at) {
            this.sessionId = sessionId; this.sequence = sequence; this.at = at;
        }
    }
    private final ArrayDeque<Pending> pending = new ArrayDeque<>();
    private InetAddress receiver;
    private long runId;
    private long lastUpSequence = -1;

    void configure(InetAddress receiver, long runId) {
        this.receiver = receiver;
        this.runId = runId;
        pending.clear();
        lastUpSequence = -1;
    }
    void expectUp(long runId, long sessionId, long sequence, long now) {
        if (receiver == null || this.runId != runId || sequence <= lastUpSequence) return;
        lastUpSequence = sequence;
        expire(now);
        if (pending.size() == CAPACITY) pending.removeFirst();
        pending.addLast(new Pending(sessionId, sequence, now));
    }
    boolean accept(InetAddress source, HapticFeedbackProtocol.Click click, long now) {
        if (receiver == null || !receiver.equals(source) || click == null || click.runId != runId) return false;
        expire(now);
        Iterator<Pending> it = pending.iterator();
        while (it.hasNext()) {
            Pending up = it.next();
            if (up.sessionId == click.sessionId && up.sequence == click.upSequence) {
                it.remove(); // One callback, including duplicate datagrams and reordered feedback.
                return true;
            }
        }
        return false;
    }
    private void expire(long now) {
        while (!pending.isEmpty() && now - pending.peekFirst().at >= MAX_AGE_NS) pending.removeFirst();
    }
}
