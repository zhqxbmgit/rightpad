package com.rightpad.capture;

import java.net.*;
import java.nio.*;
import java.util.*;

public final class MotionProfileTests {
    static int checks;
    static void check(boolean value, String message) { checks++; if (!value) throw new AssertionError(message); }
    static long sequence(byte[] p) { return Integer.toUnsignedLong(ByteBuffer.wrap(p).order(ByteOrder.LITTLE_ENDIAN).getInt(10)); }
    static byte[] triple(DatagramSocket socket, long run, long seq, int profile) throws Exception {
        byte[] first;
        do { first = UdpTouchSenderTest.receiveRaw(socket); } while (first[1] == 4);
        check(Arrays.equals(first, MotionProfileProtocol.encode(run, seq, profile)), "exact profile version/run/sequence/state");
        check(Arrays.equals(first, UdpTouchSenderTest.receiveRaw(socket)), "copy two identical");
        check(Arrays.equals(first, UdpTouchSenderTest.receiveRaw(socket)), "copy three identical");
        return first;
    }
    static void quiet(DatagramSocket socket) throws Exception {
        quiet(socket, false);
    }
    static void quiet(DatagramSocket socket, boolean lifecycle) throws Exception {
        socket.setSoTimeout(80);
        try {
            while (true) {
                byte[] p = UdpTouchSenderTest.receiveRaw(socket);
                check(p[1] == 4 || lifecycle && p[1] == 5 && p[14] == 0 && p[15] == 0,
                        "only existing lifecycle safety Neutral is permitted");
            }
        } catch (SocketTimeoutException expected) { checks++; }
        finally { socket.setSoTimeout(2000); }
    }
    public static void main(String[] args) throws Exception {
        byte[] golden = HexFormat.of().parseHex("0207080706050403028198badcfe0100");
        check(Arrays.equals(golden, MotionProfileProtocol.encode(0x8102030405060708L, 0xfedcba98L, 1)), "16-byte LE high-bit golden C");
        golden[14] = 0;
        check(Arrays.equals(golden, MotionProfileProtocol.encode(0x8102030405060708L, 0xfedcba98L, 0)), "golden M/reserved zero/exact length");
        for (int p : new int[]{-1, 2, 255}) {
            try { MotionProfileProtocol.encode(1, 0, p); throw new AssertionError("invalid profile"); }
            catch (IllegalArgumentException expected) { checks++; }
        }
        for (long seq : new long[]{-1, 0x100000000L}) {
            try { MotionProfileProtocol.encode(1, seq, 0); throw new AssertionError("invalid sequence"); }
            catch (IllegalArgumentException expected) { checks++; }
        }
        try (DatagramSocket a = new DatagramSocket(0, InetAddress.getLoopbackAddress());
             DatagramSocket b = new DatagramSocket(0, InetAddress.getLoopbackAddress());
             UdpTouchSender sender = new UdpTouchSender()) {
            a.setSoTimeout(2000); b.setSoTimeout(2000);
            sender.setForeground(true);
            check(!sender.hasTarget() && sender.queuedForTest() == 0, "fresh no target");
            quiet(a); quiet(b);
            sender.setTarget(new InetSocketAddress(a.getLocalAddress(), a.getLocalPort()));
            long run = sender.getSenderRunId();
            check(UdpTouchSenderTest.receiveRaw(a)[1] == 4, "heartbeat precedes profile");
            triple(a, run, 0, 0); quiet(a);
            // Exercise the real C1 owner and its matching-UP commit before publishing its value.
            ScreenControlRouter router = new ScreenControlRouter();
            Map<String, ControlRect> rects = Map.of(ScreenControlRouter.MODE_ID, new ControlRect(0, 0, 20, 20));
            check(router.profile() == ScreenControlRouter.Profile.NORMAL, "fresh UI M");
            router.down(5, 5, 1, false, false, true, rects);
            check(router.modeUp(1, 1), "matching UP commits");
            sender.publishProfile(router.profile() == ScreenControlRouter.Profile.NORMAL ? 0 : 1);
            triple(a, run, 1, 1); quiet(a);
            sender.publishProfile(1); quiet(a);
            sender.setForeground(false); quiet(a, true);
            sender.setForeground(true);
            triple(a, run, 1, 1);
            check(sender.getSenderRunId() == run && router.profile() == ScreenControlRouter.Profile.CINEMATIC, "same-run pause/resume preserves C");
            sender.setTarget(new InetSocketAddress(b.getLocalAddress(), b.getLocalPort()));
            long nextRun = sender.getSenderRunId();
            check(nextRun != run, "new route rotates run");
            check(UdpTouchSenderTest.receiveRaw(b)[1] == 4, "new route heartbeat first");
            triple(b, nextRun, 0, 1); quiet(a, true);
            router.down(5, 5, 1, false, false, true, rects); check(router.modeUp(1, 1), "C to M UP");
            sender.publishProfile(router.profile() == ScreenControlRouter.Profile.NORMAL ? 0 : 1);
            triple(b, nextRun, 1, 0); quiet(b);
            // A no-target change survives until the next ready route; Touch sequence stays zero.
            sender.setTarget(null); sender.publishProfile(1); quiet(a); quiet(b);
            sender.setTarget(new InetSocketAddress(a.getLocalAddress(), a.getLocalPort()));
            triple(a, sender.getSenderRunId(), 0, 1);
            sender.submit(UdpTouchSenderTest.touch(TouchSample.Action.DOWN));
            byte[] touch = UdpTouchSenderTest.receive(a);
            check(touch[1] == 1 && UdpTouchSenderTest.sequence(touch) == 0, "profile never consumes Touch sequence");
        }
        System.out.println("RESULT MotionProfileTests checks=" + checks + " failed=0");
    }
}
