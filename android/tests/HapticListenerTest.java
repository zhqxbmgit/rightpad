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
        InetAddress address = InetAddress.getLoopbackAddress();
        try (DatagramSocket sender = new DatagramSocket(); HapticFeedbackListener listener = new HapticFeedbackListener(() -> {
            check(Thread.currentThread() == uiThread, "callback must run on UI thread"); count[0]++;
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
            listener.expectUp(7, 2, 3);
            sender.send(new DatagramPacket(packet(7,2,3),24,address,50002));
            until(Handler::hasPending);
            listener.setActive(null, 0); // pending callback is deliberately held behind pause.
            Handler.drain();
            check(count[0] == 1, "pause clears pending UI feedback");
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
            check(count[0] == 1, "run/target transition invalidates old posted event");
        }
        until(() -> Thread.getAllStackTraces().keySet().stream().noneMatch(t -> t.getName().equals("RightpadHaptic") && t.isAlive()));
        Handler.drain();
        try (DatagramSocket rebound = new DatagramSocket(50002)) { check(rebound.isBound(), "destroy releases port"); }
        check(error.get() == null, "worker exception: " + error.get());
        check(count[0] == 1, "destroy has no late callback");
        System.out.println("PASS haptic listener real UDP / UI dispatch / pause / resume / transition / close / thread exit");
    }
}
