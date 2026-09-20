package com.rightpad.capture;

import android.os.Handler;
import java.net.*;
import java.nio.*;
import java.util.concurrent.atomic.AtomicReference;
import java.util.function.BooleanSupplier;

public final class HapticListenerTest {
    private static void check(boolean value, String message) { if (!value) throw new AssertionError(message); }
    private static void until(BooleanSupplier condition) throws Exception {
        long deadline = System.nanoTime() + 2_000_000_000L;
        while (!condition.getAsBoolean()) {
            if (System.nanoTime() >= deadline) throw new AssertionError("condition timed out");
            Thread.sleep(5);
        }
    }
    private static byte[] packet(long run, long session, long sequence) {
        return ByteBuffer.allocate(24).order(ByteOrder.LITTLE_ENDIAN)
                .put(new byte[]{'R','P','H','F',1,1,0,0}).putLong(run).putInt((int)session).putInt((int)sequence).array();
    }
    public static void main(String[] args) throws Exception {
        AtomicReference<Throwable> error = new AtomicReference<>();
        Thread.setDefaultUncaughtExceptionHandler((thread, failure) -> error.set(failure));
        Thread uiThread = Thread.currentThread();
        int[] count = {0};
        boolean[] fail = {false};
        TouchpadClickFeedback clickFeedback = new TouchpadClickFeedback(new TouchpadClickFeedback.Backend() {
            public boolean confirm() {
                count[0]++;
                if (fail[0]) throw new IllegalStateException("test CONFIRM failure");
                return true;
            }
        });
        InetAddress address = InetAddress.getLoopbackAddress();
        try (DatagramSocket sender = new DatagramSocket(); HapticFeedbackListener listener = new HapticFeedbackListener(() -> {
            check(Thread.currentThread() == uiThread, "callback must run on UI thread"); clickFeedback.acceptedClick();
        })) {
            listener.setActive(address, 7);
            listener.expectUp(7, 1, 1);
            // Allow initial worker bind, then send repeatedly until receive posts; dedupe remains required.
            long deadline = System.nanoTime() + 2_000_000_000L;
            while (!Handler.hasPending()) {
                sender.send(new DatagramPacket(packet(7,1,1),24,address,50002));
                Thread.sleep(10);
                check(System.nanoTime() < deadline, "listener bind and receive");
            }
            check(count[0] == 0, "network worker cannot vibrate");
            Handler.drain();
            check(count[0] == 1, "valid real UDP callback once");
            // All rejected identities and duplicate packets pass through the actual listener/gate.
            for (byte[] rejected : new byte[][] {packet(7,1,1), packet(6,1,1), packet(7,9,1), packet(7,1,9), new byte[24]}) {
                sender.send(new DatagramPacket(rejected,24,address,50002));
                until(Handler::hasPending); Handler.drain();
                check(count[0] == 1, "duplicate/stale/session/unissued/invalid feedback cannot vibrate");
            }
            fail[0] = true;
            listener.expectUp(7, 2, 2);
            sender.send(new DatagramPacket(packet(7,2,2),24,address,50002));
            until(Handler::hasPending); Handler.drain();
            check(count[0] == 2, "backend failure attempted once and did not escape UI callback");
            sender.send(new DatagramPacket(packet(7,2,2),24,address,50002));
            until(Handler::hasPending); Handler.drain();
            check(count[0] == 2, "failed vibration does not rearm dedupe");
            fail[0] = false;
            listener.expectUp(7, 2, 4);
            sender.send(new DatagramPacket(packet(7,2,4),24,address,50002));
            until(Handler::hasPending); Handler.drain();
            check(count[0] == 3, "next valid feedback still works after backend failure");
            listener.expectUp(7, 2, 3);
            sender.send(new DatagramPacket(packet(7,2,3),24,address,50002));
            until(Handler::hasPending);
            listener.setActive(null, 0); // pending callback is deliberately held behind pause.
            Handler.drain();
            check(count[0] == 3, "pause clears pending UI feedback");
            try (DatagramSocket rebound = new DatagramSocket(50002)) { check(rebound.isBound(), "pause releases port synchronously"); }
            listener.setActive(address, 7);
            listener.expectUp(7, 3, 5);
            deadline = System.nanoTime() + 2_000_000_000L;
            while (!Handler.hasPending()) {
                sender.send(new DatagramPacket(packet(7,3,5),24,address,50002));
                Thread.sleep(10);
                check(System.nanoTime() < deadline, "resume bind");
            }
            listener.setActive(address, 8);
            Handler.drain();
            check(count[0] == 3, "run/target transition invalidates old posted event");
        }
        until(() -> Thread.getAllStackTraces().keySet().stream().noneMatch(t -> t.getName().equals("RightpadHaptic") && t.isAlive()));
        Handler.drain();
        try (DatagramSocket rebound = new DatagramSocket(50002)) { check(rebound.isBound(), "destroy releases port"); }
        check(error.get() == null, "worker exception: " + error.get());
        check(count[0] == 3, "destroy has no late callback");
        System.out.println("PASS haptic listener real UDP / UI dispatch / pause / resume / transition / close / thread exit");
    }
}
