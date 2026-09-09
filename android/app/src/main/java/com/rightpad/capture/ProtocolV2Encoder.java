package com.rightpad.capture;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;

final class ProtocolV2Encoder {
    private static final int HEADER_SIZE = 20;
    private static final int SAMPLE_SIZE = 16;
    // IPv4 UDP payload limit; an oversized event is rejected whole, never split.
    private static final int MAX_SAMPLES = (65507 - HEADER_SIZE) / SAMPLE_SIZE;

    static byte[] heartbeat(long senderRunId) {
        return ByteBuffer.allocate(10).order(ByteOrder.LITTLE_ENDIAN)
                .put((byte) 2).put((byte) 4).putLong(senderRunId).array();
    }

    static byte[] encode(TouchSample[] samples, long sequence, long senderRunId) {
        if (samples.length == 0 || samples.length > MAX_SAMPLES) {
            throw new IllegalArgumentException("invalid_sample_count");
        }
        TouchSample first = samples[0];
        int eventType;
        switch (first.action) {
            case DOWN: eventType = 1; break;
            case MOVE: eventType = 2; break;
            case UP: eventType = 3; break;
            default: throw new IllegalArgumentException("unsupported_event");
        }
        if (eventType != 2 && samples.length != 1) {
            throw new IllegalArgumentException("invalid_sample_count");
        }
        if (sequence < 0 || sequence > 0xffffffffL
                || first.sessionId < 0 || first.sessionId > 0xffffffffL) {
            throw new IllegalArgumentException("uint32_out_of_range");
        }

        ByteBuffer buffer = ByteBuffer.allocate(HEADER_SIZE + samples.length * SAMPLE_SIZE)
                .order(ByteOrder.LITTLE_ENDIAN);
        buffer.put((byte) 2).put((byte) eventType).putLong(senderRunId).putShort((short) samples.length)
                .putInt((int) first.sessionId).putInt((int) sequence);
        for (TouchSample sample : samples) {
            if (sample.action != first.action || sample.sessionId != first.sessionId
                    || !Float.isFinite(sample.x) || !Float.isFinite(sample.y)) {
                throw new IllegalArgumentException("invalid_sample");
            }
            buffer.putLong(sample.eventTimeNs).putFloat(sample.x).putFloat(sample.y);
        }
        return buffer.array();
    }
}
