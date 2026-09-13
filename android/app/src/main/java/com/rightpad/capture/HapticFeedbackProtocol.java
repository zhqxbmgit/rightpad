package com.rightpad.capture;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;

final class HapticFeedbackProtocol {
    static final int PORT = 50002;
    static final int SIZE = 24;
    static final class Click {
        final long runId, sessionId, upSequence;
        Click(long runId, long sessionId, long upSequence) {
            this.runId = runId;
            this.sessionId = sessionId;
            this.upSequence = upSequence;
        }
    }
    static Click decode(byte[] bytes, int length) {
        if (length != SIZE || bytes.length < length || bytes[0] != 'R' || bytes[1] != 'P'
                || bytes[2] != 'H' || bytes[3] != 'F' || bytes[4] != 1 || bytes[5] != 1
                || bytes[6] != 0 || bytes[7] != 0) return null;
        ByteBuffer b = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN);
        return new Click(b.getLong(8), Integer.toUnsignedLong(b.getInt(16)),
                Integer.toUnsignedLong(b.getInt(20)));
    }
}
