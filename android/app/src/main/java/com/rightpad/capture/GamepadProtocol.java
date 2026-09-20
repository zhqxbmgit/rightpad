package com.rightpad.capture;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;

final class GamepadProtocol {
    static final int SIZE = 30;
    static byte[] encode(long senderRunId, long sequence, GamepadState state) {
        return encode(senderRunId, sequence, GamepadStateSubmission.ordinary(state));
    }
    static byte[] encode(long senderRunId, long sequence, GamepadStateSubmission submission) {
        GamepadState state = submission.state();
        if (sequence < 0 || sequence > 0xffffffffL) throw new IllegalArgumentException("Invalid gamepad sequence");
        return ByteBuffer.allocate(SIZE).order(ByteOrder.LITTLE_ENDIAN)
                .put((byte) 2).put((byte) 5).putLong(senderRunId).putInt((int) sequence)
                .putShort((short) state.buttons()).put((byte) state.leftTrigger()).put((byte) state.rightTrigger())
                .putShort(state.leftThumbX()).putShort(state.leftThumbY())
                .putShort(state.rightThumbX()).putShort(state.rightThumbY())
                .putShort((short) submission.minimumWireHoldMs())
                .put((byte) (submission.safetyNeutral() ? 1 : 0)).put((byte) 0).array();
    }
}
