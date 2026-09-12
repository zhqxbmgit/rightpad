package com.rightpad.capture;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.Arrays;

final class DiscoveryProtocol {
    static final int PORT = 50001, DISCOVER_SIZE = 16, OFFER_SIZE = 40;
    static byte[] discover(long nonce) {
        return ByteBuffer.allocate(DISCOVER_SIZE).order(ByteOrder.LITTLE_ENDIAN)
                .put(new byte[] {'R', 'P', 'A', 'D', 1, 1, 0, 0}).putLong(nonce).array();
    }
    static final class Offer {
        final byte[] receiverId;
        final int touchPort;
        Offer(byte[] id, int port) { receiverId = id; touchPort = port; }
        String identity() {
            StringBuilder value = new StringBuilder(32);
            for (byte b : receiverId) value.append(String.format(java.util.Locale.ROOT, "%02x", b & 255));
            return value.toString();
        }
    }
    static Offer offer(byte[] data, int length, long nonce) {
        if (length != OFFER_SIZE || data.length < length || data[0] != 'R' || data[1] != 'P'
                || data[2] != 'A' || data[3] != 'D' || data[4] != 1 || data[5] != 2
                || data[6] != 0 || data[7] != 0 || data[35] != 0 || data[34] != 2) return null;
        ByteBuffer bytes = ByteBuffer.wrap(data).order(ByteOrder.LITTLE_ENDIAN);
        if (bytes.getLong(8) != nonce || Short.toUnsignedInt(bytes.getShort(32)) != 50000) return null;
        return new Offer(Arrays.copyOfRange(data, 16, 32), 50000);
    }
}
