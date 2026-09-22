package com.rightpad.capture;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;

/** RPST v1, independent of RPHF and RPCT on the existing feedback socket. */
final class StatusProtocol {
    static final int SIZE = 36;
    static final int STOPPED = 0, STARTING = 1, RUNNING = 2, STOPPING = 3, ERROR = 4;
    static final int MOUSE_HEALTHY = 1, GAMEPAD_AVAILABLE = 2, RUNTIME_ERROR = 4,
            MOUSE_FAILURE = 8, GAMEPAD_FAILURE = 16;
    record Snapshot(long runId, long epoch, long revision, int state, int flags) { }

    static boolean isStatus(byte[] bytes, int length) {
        return bytes != null && length >= 4 && length <= bytes.length
                && bytes[0] == 'R' && bytes[1] == 'P' && bytes[2] == 'S' && bytes[3] == 'T';
    }
    static Snapshot decode(byte[] bytes, int length) {
        if (!isStatus(bytes, length) || length != SIZE || bytes[4] != 1 || bytes[5] != 1
                || bytes[6] != 0 || bytes[7] != 0 || bytes[34] != 0 || bytes[35] != 0
                || (bytes[32] & 255) > ERROR || (bytes[33] & 0xE0) != 0) return null;
        ByteBuffer buffer = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN);
        long epoch = buffer.getLong(16), revision = buffer.getLong(24);
        if (epoch == 0 || revision == 0) return null;
        return new Snapshot(buffer.getLong(8), epoch, revision, bytes[32], bytes[33]);
    }
}
