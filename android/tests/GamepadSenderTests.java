package com.rightpad.capture;
import java.net.*;
import java.nio.*;
import java.util.*;

public final class GamepadSenderTests {
    static int checks;
    static void check(boolean ok, String message) { checks++; if (!ok) throw new AssertionError(message); }
    static long seq(byte[] p) { return Integer.toUnsignedLong(ByteBuffer.wrap(p).order(ByteOrder.LITTLE_ENDIAN).getInt(10)); }
    static byte[] next(DatagramSocket socket, int type) throws Exception {
        long until = System.nanoTime() + 2_000_000_000L;
        while (System.nanoTime() < until) { byte[] p = UdpTouchSenderTest.receive(socket); if (p[1] == type) return p; }
        throw new AssertionError("packet type absent " + type);
    }
    public static void main(String[] args) throws Exception {
        GamepadSendState slot = new GamepadSendState();
        slot.change(new GamepadState(GamepadState.B), 0);
        GamepadSendState.Packet first = slot.claim(0, true);
        check(first.sequence() == 0 && first.copies() == 3, "transition sequence/copies");
        check(slot.claim(99_999_999, true) == null, "refresh not early");
        GamepadSendState.Packet refresh = slot.claim(100_000_000, true);
        check(refresh.sequence() == 1 && refresh.copies() == 1 && refresh.state().equals(first.state()), "refresh new sequence");
        slot.change(GamepadState.NEUTRAL, 110_000_000);
        check(slot.claim(110_000_000, false).state().neutral(), "pause may flush Neutral");
        check(slot.claim(9_000_000_000L, true) == null, "Neutral no refresh");
        slot.change(new GamepadState(GamepadState.B), 0); slot.change(GamepadState.NEUTRAL, 1);
        check(slot.claim(2, true).state().neutral(), "coalesced old B cannot override newer Neutral");
        check(slot.claim(900_000_000, true) == null, "no stale held refresh");
        java.lang.reflect.Field nextSequence = GamepadSendState.class.getDeclaredField("nextSequence");
        nextSequence.setAccessible(true); nextSequence.setLong(slot, 0xffffffffL);
        slot.change(new GamepadState(GamepadState.B), 0);
        check(slot.claim(0, true).sequence() == 0xffffffffL, "uint32 maximum");
        slot.change(new GamepadState(GamepadState.Y), 1);
        check(slot.claim(1, true).sequence() == 0, "uint32 wrap independent of Touch");
        try (DatagramSocket socket = new DatagramSocket(0, InetAddress.getLoopbackAddress());
             DatagramSocket target = new DatagramSocket(0, InetAddress.getLoopbackAddress());
             UdpTouchSender sender = new UdpTouchSender("127.0.0.1", socket.getLocalPort())) {
            socket.setSoTimeout(2000); target.setSoTimeout(2000);
            sender.setForeground(true); long run = UdpTouchSenderTest.run(next(socket, 4));
            sender.submitGamepad(new GamepadState(GamepadState.B));
            byte[] p = next(socket, 5);
            check(seq(p) == 0 && UdpTouchSenderTest.run(p) == run, "same route/run independent GP baseline");
            check(Arrays.equals(p, next(socket, 5)) && Arrays.equals(p, next(socket, 5)), "3 identical bytes");
            sender.submit(UdpTouchSenderTest.touch(TouchSample.Action.DOWN));
            check(UdpTouchSenderTest.sequence(next(socket, 1)) == 0, "GP leaves Touch baseline zero");
            byte[] refreshed = next(socket, 5);
            check(seq(refreshed) > 0 && refreshed[14] == GamepadState.B, "worker held refresh");
            sender.setForeground(false);
            do { p = next(socket, 5); } while (p[14] != 0);
            check(p[14] == 0 && p[28] == 1, "foreground false sends FORCE_NEUTRAL");
            sender.setForeground(true); next(socket, 4);
            sender.submit(UdpTouchSenderTest.touch(TouchSample.Action.DOWN));
            check(UdpTouchSenderTest.sequence(next(socket, 1)) == 1, "pause/GP preserves Touch sequence");
            sender.submitGamepad(new GamepadState(GamepadState.Y));
            do { p = next(socket, 5); } while (p[14] != GamepadState.Y);
            sender.setTarget(new InetSocketAddress("127.0.0.1", target.getLocalPort()));
            long newRun = UdpTouchSenderTest.run(next(target, 4));
            check(newRun != run, "route replaces run");
            do { p = next(socket, 5); } while (p[14] != 0);
            check(UdpTouchSenderTest.run(p) == run && p[28] == 1, "old route best effort FORCE_NEUTRAL");
            sender.submitGamepad(new GamepadState(GamepadState.A)); p = next(target, 5);
            check(seq(p) == 0 && p[14] == GamepadState.A, "new route fresh GP sequence");
            sender.submit(UdpTouchSenderTest.touch(TouchSample.Action.DOWN));
            check(UdpTouchSenderTest.sequence(next(target, 1)) == 0, "new route fresh Touch sequence");
        }
        java.util.concurrent.CountDownLatch dequeued = new java.util.concurrent.CountDownLatch(1);
        java.util.concurrent.CountDownLatch release = new java.util.concurrent.CountDownLatch(1);
        try (DatagramSocket socket = new DatagramSocket(0, InetAddress.getLoopbackAddress());
             UdpTouchSender sender = new UdpTouchSender(new InetSocketAddress("127.0.0.1", socket.getLocalPort()), () -> {
                 dequeued.countDown();
                 boolean done = false;
                 while (!done) { try { release.await(); done = true; } catch (InterruptedException ignored) { } }
             })) {
            socket.setSoTimeout(2000); sender.setForeground(true); next(socket, 4);
            sender.submit(UdpTouchSenderTest.touch(TouchSample.Action.DOWN));
            check(dequeued.await(2, java.util.concurrent.TimeUnit.SECONDS), "worker stalled before Touch send");
            sender.submitGamepad(new GamepadState(GamepadState.B)); sender.submitGamepad(GamepadState.NEUTRAL);
            release.countDown();
            byte[] neutral = next(socket, 5);
            check(neutral[14] == 0 && seq(neutral) == 1, "actual worker prioritizes latest Neutral over obsolete B");
            check(Arrays.equals(neutral, next(socket, 5)) && Arrays.equals(neutral, next(socket, 5)), "coalesced Neutral triplicate");
            sender.submitGamepad(new GamepadState(GamepadState.Y));
            int moves = 0, gamepads = 0;
            long until = System.nanoTime() + 450_000_000L;
            while (System.nanoTime() < until) {
                sender.submit(UdpTouchSenderTest.touch(TouchSample.Action.MOVE));
                byte[] p = UdpTouchSenderTest.receive(socket);
                if (p[1] == 2) moves++;
                if (p[1] == 5) gamepads++;
                Thread.sleep(2);
            }
            check(moves > 50 && gamepads >= 5, "Touch and held refresh both progress under continuous load");
            sender.close();
            byte[] finalState;
            do { finalState = next(socket, 5); } while (finalState[14] != 0);
            check(finalState[14] == 0 && finalState[28] == 1 && !sender.canCapture(), "close worker sends final FORCE_NEUTRAL and rejects capture");
        } finally { release.countDown(); }
        System.out.println("RESULT GamepadSenderTests checks=" + checks + " failed=0");
    }
}
