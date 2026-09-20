package com.rightpad.capture;

import java.util.HashSet;
import java.util.Set;

/** UI-owned, immutable snapshots. Receiver behavior configuration is never persisted on Android. */
final class ControlConfigCache {
    private ControlConfigProtocol.Snapshot current;
    private final Set<Long> retiredEpochs = new HashSet<>();
    void reset() { current = null; retiredEpochs.clear(); }
    long epoch() { return current == null ? 0 : current.epoch(); }
    long revision() { return current == null ? 0 : current.revision(); }
    boolean accept(ControlConfigProtocol.Snapshot next) {
        if (next == null || retiredEpochs.contains(next.epoch())) return false;
        if (current != null) {
            if (next.version() < current.version()) return false; // Do not downgrade a complete v2 snapshot.
            if (next.epoch() == current.epoch()) {
                int order = Long.compareUnsigned(next.revision(), current.revision());
                if (order < 0 || (order == 0 && next.version() <= current.version())) return false;
            } else retiredEpochs.add(current.epoch());
        }
        current = next;
        return true;
    }
    SlideControlLRGesture.Config lrGesture(int protocolId, SlideControlLRGesture.Config defaults) {
        ControlConfigProtocol.SlideLR value = current == null ? null : current.lrRecords().get(protocolId);
        return value == null ? defaults : value.gesture();
    }
    SlideControlGesture.Config gesture(int protocolId, float density, SlideControlGesture.Config defaults) {
        ControlConfigProtocol.Slide value = current == null ? null : current.records().get(protocolId);
        return value == null ? defaults : value.gesture(density);
    }
}
