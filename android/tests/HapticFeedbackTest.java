package com.rightpad.capture;

import java.net.*;
import java.nio.*;
import java.util.*;

public final class HapticFeedbackTest {
    private static int checks;
    private static void check(boolean value, String message) {
        checks++;
        if (!value) throw new AssertionError(message);
    }
    private static byte[] packet(long run, long session, long sequence) {
        return ByteBuffer.allocate(24).order(ByteOrder.LITTLE_ENDIAN)
                .put(new byte[]{'R', 'P', 'H', 'F', 1, 1, 0, 0})
                .putLong(run).putInt((int)session).putInt((int)sequence).array();
    }
    private static HapticFeedbackProtocol.Click click(long run, long session, long sequence) {
        return HapticFeedbackProtocol.decode(packet(run, session, sequence), 24);
    }
    public static void main(String[] args) throws Exception {
        byte[] golden = HexFormat.of().parseHex("52504846010100001032547698BADCFEEFCDAB8998BADCFE");
        HapticFeedbackProtocol.Click c = HapticFeedbackProtocol.decode(golden, 24);
        check(c != null && c.runId == 0xFEDCBA9876543210L && c.sessionId == 0x89ABCDEFL
                && c.upSequence == 0xFEDCBA98L, "Windows golden uint64/uint32 parsing");
        check(Arrays.equals(golden, packet(c.runId, c.sessionId, c.upSequence)), "golden encoder agreement");
        for (int length = 0; length < 24; length++) {
            check(HapticFeedbackProtocol.decode(golden, length) == null, "short datagram " + length);
            check(HapticFeedbackProtocol.decode(Arrays.copyOf(golden, length), 24) == null, "short storage " + length);
        }
        check(HapticFeedbackProtocol.decode(Arrays.copyOf(golden, 25), 25) == null, "oversized");
        for (int i = 0; i < 8; i++) {
            byte[] bad = golden.clone(); bad[i] ^= 0x80;
            check(HapticFeedbackProtocol.decode(bad, 24) == null, "strict header " + i);
        }
        check(click(0, 0, 0) != null, "zero identities legal");
        InetAddress a = InetAddress.getByName("192.0.2.1"), b = InetAddress.getByName("192.0.2.2");
        HapticFeedbackGate gate = new HapticFeedbackGate();
        gate.expectUp(7, 1, 1, 0);
        check(!gate.accept(a, click(7, 1, 1), 1), "foreground false");
        gate.configure(a, 7);
        gate.expectUp(7, 1, 1, 0);
        check(!gate.accept(b, click(7, 1, 1), 1), "other receiver");
        check(!gate.accept(a, click(6, 1, 1), 1), "old sender run");
        check(!gate.accept(a, click(7, 2, 1), 1), "wrong session");
        check(!gate.accept(a, click(7, 1, 2), 1), "unissued UP");
        check(!gate.accept(a, null, 1), "invalid codec");
        check(gate.accept(a, click(7, 1, 1), 1), "current receiver/run/session/UP");
        check(!gate.accept(a, click(7, 1, 1), 2), "duplicate");
        gate.expectUp(7, 1, 1, 3);
        check(!gate.accept(a, click(7, 1, 1), 4), "duplicate registration cannot rearm");
        gate.expectUp(7, 2, 3, 5); gate.expectUp(7, 3, 5, 5);
        check(gate.accept(a, click(7, 3, 5), 6), "later event first");
        check(gate.accept(a, click(7, 2, 3), 7), "reordered distinct event once");
        check(!gate.accept(a, click(7, 3, 5), 8), "older duplicate after reorder");
        gate.expectUp(7, 4, 7, 10);
        gate.configure(b, 8);
        check(!gate.accept(a, click(7, 4, 7), 11), "transition invalidates pending UI event");
        gate.expectUp(8, 1, 1, 11);
        check(gate.accept(b, click(8, 1, 1), 12), "new target can reuse sequence/session with new run");
        gate.configure(null, 0);
        check(!gate.accept(b, click(8, 1, 1), 13), "paused UI callback rejected");
        gate.configure(b, 8);
        check(!gate.accept(b, click(8, 1, 1), 14), "same-run resume never revives pre-pause event");
        gate.expectUp(8, 2, 3, 20);
        check(gate.accept(b, click(8, 2, 3), 20 + HapticFeedbackGate.MAX_AGE_NS - 1), "fresh until deadline");
        gate.expectUp(8, 3, 5, 30);
        check(!gate.accept(b, click(8, 3, 5), 30 + HapticFeedbackGate.MAX_AGE_NS), "stale at deadline");
        gate.configure(a, Long.MIN_VALUE);
        gate.expectUp(Long.MIN_VALUE, 0xffffffffL, 0xffffffffL, 0);
        check(gate.accept(a, click(Long.MIN_VALUE, 0xffffffffL, 0xffffffffL), 1), "unsigned identity preserved");
        gate.configure(a, 9);
        for (int i = 0; i <= HapticFeedbackGate.CAPACITY; i++) gate.expectUp(9, i, i, i);
        check(!gate.accept(a, click(9, 0, 0), 100), "bounded pending storage drops oldest");
        check(gate.accept(a, click(9, 32, 32), 100), "newest retained");

        try (DatagramSocket socket = new DatagramSocket(0, InetAddress.getLoopbackAddress());
             UdpTouchSender sender = new UdpTouchSender("127.0.0.1", socket.getLocalPort())) {
            socket.setSoTimeout(2000);
            List<long[]> observed = new ArrayList<>();
            sender.setUpObserver((run, session, seq) -> observed.add(new long[]{run, session, seq}));
            sender.setForeground(true);
            TouchSample[] down = UdpTouchSenderTest.touch(TouchSample.Action.DOWN);
            TouchSample[] up = UdpTouchSenderTest.touch(TouchSample.Action.UP);
            sender.submit(down); sender.submit(up);
            check(observed.size() == 1, "only raw UP notification, not DOWN/heartbeat/copies");
            long[] identity = observed.get(0);
            check(identity[0] == sender.getSenderRunId() && identity[1] == 1 && identity[2] == 1,
                    "single Sender owner supplies actual encoded identity");
            byte[] wire;
            do { wire = UdpTouchSenderTest.receive(socket); } while (wire[1] != 3);
            check(UdpTouchSenderTest.run(wire) == identity[0] && UdpTouchSenderTest.sequence(wire) == identity[2],
                    "observer matches real UDP UP");
            sender.setForeground(false); sender.submit(up);
            check(observed.size() == 1, "paused submit never registers feedback");
        }
        System.out.println("PASS haptic codec / identity / dedupe / lifecycle / expiry / raw Sender: " + checks + " checks");
    }
}
