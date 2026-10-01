package com.rightpad.capture;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;

/** Independent v2 state message; never changes Touch or gamepad bytes. */
final class MotionProfileProtocol {
    static byte[] encode(long runId, long sequence, int profile) {
        if (profile < 0 || profile > 1) throw new IllegalArgumentException("profile");
        if (sequence < 0 || sequence > 0xffffffffL) throw new IllegalArgumentException("profileSequence");
        return ByteBuffer.allocate(16).order(ByteOrder.LITTLE_ENDIAN)
                .put((byte) 2).put((byte) 7).putLong(runId).putInt((int) sequence)
                .put((byte) profile).put((byte) 0).array();
    }
}
