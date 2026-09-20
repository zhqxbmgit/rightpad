package com.rightpad.capture;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.LinkedHashMap;
import java.util.Map;

final class ControlConfigProtocol {
    static final int HEADER = 36, RECORD = 12, LR_RECORD = 14, MAX_RECORDS = 16, MAX_SIZE = HEADER + 255 * MAX_RECORDS;
    static final int B_ID = 1, X_ID = 2;
    record Slide(int upTenthsDp, int downTenthsDp, int tapHoldMs, int longPressMs) {
        boolean valid() { return upTenthsDp >= 1 && upTenthsDp <= 500 && downTenthsDp >= 1 && downTenthsDp <= 500
                && tapHoldMs >= 1 && tapHoldMs <= 200 && longPressMs >= 50 && longPressMs <= 2000; }
        SlideControlGesture.Config gesture(float density) {
            return new SlideControlGesture.Config(upTenthsDp / 10f * density, downTenthsDp / 10f * density, tapHoldMs, longPressMs);
        }
    }
    record SlideLR(int leftTenthsDp, int rightTenthsDp, int upTenthsDp, int tapHoldMs, int longPressMs) {
        boolean valid() { return leftTenthsDp >= 1 && leftTenthsDp <= 500 && rightTenthsDp >= 1 && rightTenthsDp <= 500
                && upTenthsDp >= 1 && upTenthsDp <= 500 && tapHoldMs >= 1 && tapHoldMs <= 200
                && longPressMs >= 50 && longPressMs <= 2000; }
        SlideControlLRGesture.Config gesture() {
            return new SlideControlLRGesture.Config(leftTenthsDp / 10f, rightTenthsDp / 10f, upTenthsDp / 10f, tapHoldMs, longPressMs);
        }
    }
    record Snapshot(long runId, long epoch, long revision, Map<Integer, Slide> records,
            Map<Integer, SlideLR> lrRecords, int version) {
        Snapshot { records = Map.copyOf(records); lrRecords = Map.copyOf(lrRecords); }
    }
    static boolean isConfig(byte[] bytes, int length) {
        return length >= 4 && bytes[0] == 'R' && bytes[1] == 'P' && bytes[2] == 'C' && bytes[3] == 'T';
    }
    static Snapshot decode(byte[] bytes, int length) {
        if (length > bytes.length || length < HEADER || length > MAX_SIZE || !isConfig(bytes, length)) return null;
        ByteBuffer b = ByteBuffer.wrap(bytes, 0, length).order(ByteOrder.LITTLE_ENDIAN);
        int version = bytes[4];
        if ((version != 1 && version != 2) || bytes[5] != 1 || b.getShort(6) != 0 || b.getShort(34) != 0) return null;
        int count = Short.toUnsignedInt(b.getShort(32));
        if (count < 1 || count > MAX_RECORDS || b.getLong(16) == 0 || b.getLong(24) == 0) return null;
        if (version == 1 && length != HEADER + count * RECORD) return null;
        Map<Integer, Slide> records = new LinkedHashMap<>();
        Map<Integer, SlideLR> lrRecords = new LinkedHashMap<>();
        java.util.Set<Integer> seen = new java.util.HashSet<>();
        int at = HEADER;
        for (int i = 0; i < count; i++) {
            if (at + 4 > length) return null;
            int id = Short.toUnsignedInt(b.getShort(at));
            int kind = Byte.toUnsignedInt(bytes[at + 2]);
            int size = version == 1 ? RECORD : Byte.toUnsignedInt(bytes[at + 3]);
            if (size < 4 || at + size > length || id == 0) return null;
            if (version == 1 && (kind != 1 || bytes[at + 3] != 0 || !seen.add(id))) return null;
            if (version == 2 && (id == B_ID || id == X_ID) && !seen.add(id)) return null;
            if ((id == B_ID && kind != 1) || (id == X_ID && kind != 2)) return null;
            if (kind == 1) {
                if (size != RECORD) return null;
                Slide slide = new Slide(Short.toUnsignedInt(b.getShort(at + 4)), Short.toUnsignedInt(b.getShort(at + 6)),
                        Short.toUnsignedInt(b.getShort(at + 8)), Short.toUnsignedInt(b.getShort(at + 10)));
                if (!slide.valid()) return null;
                if (version == 1 || id == B_ID) records.put(id, slide);
            } else if (kind == 2) {
                if (size != LR_RECORD) return null;
                SlideLR lr = new SlideLR(Short.toUnsignedInt(b.getShort(at + 4)), Short.toUnsignedInt(b.getShort(at + 6)),
                        Short.toUnsignedInt(b.getShort(at + 8)), Short.toUnsignedInt(b.getShort(at + 10)), Short.toUnsignedInt(b.getShort(at + 12)));
                if (!lr.valid()) return null;
                if (id == X_ID) lrRecords.put(id, lr);
            }
            at += size;
        }
        if (at != length || !records.containsKey(B_ID) || (version == 2 && !lrRecords.containsKey(X_ID))) return null;
        return new Snapshot(b.getLong(8), b.getLong(16), b.getLong(24), records, lrRecords, version);
    }
    static byte[] request(long run, long epoch, long revision) {
        return ByteBuffer.allocate(26).order(ByteOrder.LITTLE_ENDIAN).put((byte)2).put((byte)6)
                .putLong(run).putLong(epoch).putLong(revision).array();
    }
}
