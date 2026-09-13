package com.rightpad.capture;

import android.os.Handler;
import android.os.Looper;
import android.util.Log;
import java.io.Closeable;
import java.io.IOException;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.util.Objects;

final class HapticFeedbackListener implements Closeable {
    private static final String TAG = "RightpadHaptic";
    private static final class Identity {
        final InetAddress address;
        final long runId;
        Identity(InetAddress address, long runId) { this.address = address; this.runId = runId; }
    }
    private final Handler ui = new Handler(Looper.getMainLooper());
    private final HapticFeedbackGate validation = new HapticFeedbackGate();
    private final Runnable feedback;
    private final Object lifecycle = new Object();
    private final Thread worker;
    private volatile Identity identity;
    private volatile boolean closed;
    private DatagramSocket socket; // lifecycle lock; only worker opens it.
    private long accepted, rejected;

    HapticFeedbackListener(Runnable feedback) {
        this.feedback = feedback;
        worker = new Thread(this::run, "RightpadHaptic");
        worker.start();
    }
    // UI thread only. Repeated discovery confirmations preserve pending identities.
    void setActive(InetAddress address, long runId) {
        Identity previous = identity;
        if (closed || (previous != null && Objects.equals(previous.address, address) && previous.runId == runId)) return;
        synchronized (lifecycle) {
            identity = address == null ? null : new Identity(address, runId);
            if (socket != null) socket.close();
            lifecycle.notifyAll();
        }
        validation.configure(address, runId);
        ui.removeCallbacksAndMessages(null);
    }
    void expectUp(long runId, long sessionId, long sequence) {
        validation.expectUp(runId, sessionId, sequence, System.nanoTime());
    }
    private void run() {
        byte[] bytes = new byte[HapticFeedbackProtocol.SIZE + 1];
        try {
            while (!closed) {
                Identity current;
                synchronized (lifecycle) {
                    while (!closed && identity == null) lifecycle.wait();
                    if (closed) break;
                    current = identity;
                }
                try (DatagramSocket opened = new DatagramSocket(null)) {
                    synchronized (lifecycle) {
                        if (closed || current != identity) continue;
                        opened.bind(new InetSocketAddress(HapticFeedbackProtocol.PORT));
                        socket = opened;
                    }
                    Log.i(TAG, "listener_active port=50002 receiver=" + current.address.getHostAddress()
                            + " senderRunId=" + Long.toUnsignedString(current.runId, 16));
                    while (!closed && current == identity) {
                        DatagramPacket packet = new DatagramPacket(bytes, bytes.length);
                        opened.receive(packet);
                        HapticFeedbackProtocol.Click click = HapticFeedbackProtocol.decode(bytes, packet.getLength());
                        InetAddress source = packet.getAddress();
                        ui.post(() -> {
                            // Recheck lifecycle at execution, including receive/post versus pause races.
                            if (closed || current != identity) return;
                            if (!validation.accept(source, click, System.nanoTime())) {
                                if (++rejected == 1 || rejected % 1024 == 0)
                                    Log.w(TAG, "feedback_rejected count=" + rejected);
                                return;
                            }
                            feedback.run();
                            Log.i(TAG, "click_accepted senderRunId=" + Long.toUnsignedString(click.runId, 16)
                                    + " sessionId=" + click.sessionId + " upSequence=" + click.upSequence
                                    + " count=" + (++accepted));
                        });
                    }
                } catch (IOException | SecurityException exception) {
                    if (!closed && current == identity) {
                        Log.e(TAG, "listener_error (check local-network permission on EPERM/EACCES)", exception);
                        // No retry scheduler: a fresh lifecycle/target activation can reopen feedback.
                        synchronized (lifecycle) {
                            while (!closed && current == identity) lifecycle.wait();
                        }
                    }
                } finally {
                    synchronized (lifecycle) { socket = null; }
                }
            }
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
        } finally { Log.i(TAG, "listener_stopped"); }
    }
    @Override public void close() {
        setActive(null, 0);
        synchronized (lifecycle) {
            closed = true;
            lifecycle.notifyAll();
        }
    }
}
