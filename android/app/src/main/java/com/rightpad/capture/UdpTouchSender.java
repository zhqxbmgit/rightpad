package com.rightpad.capture;

import android.util.Log;
import java.io.Closeable;
import java.io.IOException;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetSocketAddress;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.ThreadLocalRandom;
import java.util.concurrent.TimeUnit;

final class UdpTouchSender implements Closeable {
    private static final String TAG = "RightpadUdp";
    private static final int QUEUE_CAPACITY = 8;
    private static final class Route {
        final InetSocketAddress endpoint;
        final long runId = ThreadLocalRandom.current().nextLong();
        final byte[] heartbeatBytes = ProtocolV2Encoder.heartbeat(runId);
        boolean firstHeartbeat = true; // Accessed only under sendGate.
        Route(InetSocketAddress endpoint) { this.endpoint = endpoint; }
    }
    private static final class Pending {
        final Route route;
        final byte[] bytes;
        Pending(Route route, byte[] bytes) { this.route = route; this.bytes = bytes; }
    }
    private final ArrayBlockingQueue<Pending> pending = new ArrayBlockingQueue<>(QUEUE_CAPACITY);
    // Only lifecycle transitions take this gate on UI. ACTION_MOVE never acquires it.
    // Completion of a transition guarantees no subsequent send can use the previous route.
    private final Object sendGate = new Object();
    private final Thread thread;
    private final Runnable beforeSendForTest;
    private volatile DatagramSocket socket;
    private volatile boolean closed, foreground;
    private volatile Route route = new Route(null);
    private final HeartbeatSchedule heartbeat = new HeartbeatSchedule();
    private long nextSequence;
    private long activeSession = -1;

    UdpTouchSender() { this(null, null); }
    // Test-only endpoint / dequeue barrier injection, never used by the Activity.
    UdpTouchSender(String address, int port) { this(new InetSocketAddress(address, port), null); }
    UdpTouchSender(InetSocketAddress endpoint, Runnable beforeSendForTest) {
        this.beforeSendForTest = beforeSendForTest;
        if (endpoint != null) route = new Route(endpoint);
        thread = new Thread(this::sendLoop, "RightpadUdpSender");
        thread.start();
    }
    String getReceiverAddress() { return route.endpoint == null ? null : route.endpoint.getAddress().getHostAddress(); }
    int getReceiverPort() { return route.endpoint == null ? 0 : route.endpoint.getPort(); }
    boolean hasTarget() { return route.endpoint != null; }
    boolean canCapture() { return !closed && foreground && route.endpoint != null; }
    long runIdForTest() { return route.runId; }
    int queuedForTest() { return pending.size(); }

    // UI thread only, after TouchCaptureView.stopCapture. Identity changes also force a new run.
    void setTarget(InetSocketAddress target) {
        synchronized (sendGate) {
            if (closed) return;
            pending.clear();
            activeSession = -1;
            nextSequence = 0;
            route = new Route(target);
            heartbeat.setEnabled(foreground && target != null, System.nanoTime());
        }
        thread.interrupt();
        Log.i(TAG, "target_changed destination=" + target + " senderRunId="
                + Long.toUnsignedString(route.runId, 16) + " sequence=0");
    }

    // Called only on UI. Only encoded bytes and their immutable route cross the boundary.
    void submit(TouchSample[] samples) {
        Route current = route;
        if (closed || !foreground || current.endpoint == null || samples.length == 0) return;
        TouchSample first = samples[0];
        if (first.action != TouchSample.Action.DOWN && first.sessionId != activeSession) return;
        long sequence = nextSequence;
        byte[] bytes;
        try { bytes = ProtocolV2Encoder.encode(samples, sequence, current.runId); }
        catch (IllegalArgumentException exception) {
            Log.e(TAG, "packet_rejected sequence=" + sequence + " reason=" + exception.getMessage());
            return;
        }
        nextSequence++;
        if (first.action == TouchSample.Action.DOWN) activeSession = first.sessionId;
        if (first.action == TouchSample.Action.UP) activeSession = -1;
        Pending packet = new Pending(current, bytes);
        if (!pending.offer(packet)) {
            Pending dropped = pending.poll();
            if (dropped != null) Log.w(TAG, "queue_overflow droppedSequence=" + sequenceOf(dropped.bytes));
            pending.offer(packet);
        }
    }
    void setForeground(boolean enabled) {
        synchronized (sendGate) {
            if (closed) return;
            foreground = enabled;
            heartbeat.setEnabled(enabled && route.endpoint != null, System.nanoTime());
            if (!enabled) { pending.clear(); activeSession = -1; }
        }
        thread.interrupt();
        Log.i(TAG, "foreground=" + enabled + " senderRunId=" + Long.toUnsignedString(route.runId, 16));
    }
    private void sendLoop() {
        try (DatagramSocket senderSocket = new DatagramSocket()) {
            socket = senderSocket;
            Log.i(TAG, "sender_started queueCapacity=" + QUEUE_CAPACITY);
            while (!closed) {
                synchronized (sendGate) { sendHeartbeat(senderSocket, route); }
                Pending packet;
                try { packet = pending.poll(heartbeat.waitNanos(System.nanoTime()), TimeUnit.NANOSECONDS); }
                catch (InterruptedException exception) { continue; }
                if (packet == null) continue;
                if (beforeSendForTest != null) beforeSendForTest.run();
                synchronized (sendGate) {
                    if (closed || !foreground || packet.route != route || route.endpoint == null) continue;
                    if (!sendHeartbeat(senderSocket, route)) continue;
                    byte[] bytes = packet.bytes;
                    int copies = bytes[1] == 2 ? 1 : 3;
                    try {
                        DatagramPacket datagram = new DatagramPacket(bytes, bytes.length, route.endpoint);
                        for (int copy = 0; copy < copies; copy++) senderSocket.send(datagram);
                        Log.i(TAG, "packet_sent sequence=" + sequenceOf(bytes)
                                + " bytes=" + bytes.length + " copies=" + copies);
                    } catch (IOException exception) { if (!closed) Log.e(TAG, "send_failed", exception); }
                }
            }
        } catch (IOException exception) { if (!closed) Log.e(TAG, "sender_open_failed", exception); }
        finally { closed = true; pending.clear(); Log.i(TAG, "sender_stopped"); }
    }
    private boolean sendHeartbeat(DatagramSocket senderSocket, Route current) {
        if (closed || !foreground || current.endpoint == null) return false;
        boolean due = heartbeat.claimDue(System.nanoTime());
        if (!current.firstHeartbeat && !due) return true;
        try {
            senderSocket.send(new DatagramPacket(current.heartbeatBytes, current.heartbeatBytes.length, current.endpoint));
            current.firstHeartbeat = false;
            return true;
        } catch (IOException exception) {
            if (!closed) Log.e(TAG, "heartbeat_send_failed (check local-network permission on EPERM/EACCES)", exception);
            return false;
        }
    }
    private static long sequenceOf(byte[] bytes) {
        return Integer.toUnsignedLong(ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN).getInt(16));
    }
    @Override public void close() {
        synchronized (sendGate) { closed = true; pending.clear(); }
        DatagramSocket currentSocket = socket;
        if (currentSocket != null) currentSocket.close();
        thread.interrupt();
    }
}
