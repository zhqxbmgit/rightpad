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
    interface UpObserver { void submitted(long runId, long sessionId, long sequence); }
    private UpObserver upObserver; // UI-thread raw packet identity notification; never a gesture decision.
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
    // Lifecycle/gamepad transitions take this gate on UI. Touch ACTION_MOVE never acquires it.
    // Completion of a transition guarantees no subsequent send can use the previous route.
    private final Object sendGate = new Object();
    private final Thread thread;
    private final Runnable beforeSendForTest;
    private volatile boolean closed, closing, foreground;
    private volatile Route route = new Route(null);
    private final HeartbeatSchedule heartbeat = new HeartbeatSchedule();
    private GamepadSendState gamepad = new GamepadSendState();
    private Pending retiredGamepadNeutral; // The only old-route traffic permitted: a best-effort final Neutral.
    private long nextSequence;
    private long activeSession = -1;
    private boolean controlRequests;
    private long configEpoch, configRevision, requestAt;
    void enableControlRequests() { synchronized (sendGate) { controlRequests = true; requestAt = 0; } thread.interrupt(); }
    void knownControlConfig(long epoch, long revision) {
        synchronized (sendGate) { configEpoch = epoch; configRevision = revision; }
    }

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
    boolean canCapture() { return !closed && !closing && foreground && route.endpoint != null; }
    long runIdForTest() { return route.runId; }
    long getSenderRunId() { return route.runId; }
    void setUpObserver(UpObserver observer) { upObserver = observer; }
    int queuedForTest() { return pending.size(); }

    // UI thread only, after TouchCaptureView.stopCapture. Identity changes also force a new run.
    void setTarget(InetSocketAddress target) {
        synchronized (sendGate) {
            if (closed || closing) return;
            if (route.endpoint != null && gamepad.used()) {
                GamepadSendState.Packet neutral = gamepad.retire();
                retiredGamepadNeutral = new Pending(route, GamepadProtocol.encode(route.runId, neutral.sequence(), neutral.submission()));
            }
            gamepad = new GamepadSendState();
            pending.clear();
            activeSession = -1;
            nextSequence = 0;
            route = new Route(target);
            configEpoch = configRevision = requestAt = 0;
            heartbeat.setEnabled(foreground && target != null, System.nanoTime());
        }
        thread.interrupt();
        Log.i(TAG, "target_changed destination=" + target + " senderRunId="
                + Long.toUnsignedString(route.runId, 16) + " sequence=0");
    }

    // Called only on UI. Only encoded bytes and their immutable route cross the boundary.
    void submit(TouchSample[] samples) {
        Route current = route;
        if (closed || closing || !foreground || current.endpoint == null || samples.length == 0) return;
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
        if (first.action == TouchSample.Action.UP && upObserver != null)
            upObserver.submitted(current.runId, first.sessionId, sequence);
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
            if (closed || closing) return;
            if (!enabled) gamepad.change(GamepadStateSubmission.safety(), System.nanoTime());
            foreground = enabled;
            if (enabled) requestAt = 0;
            heartbeat.setEnabled(enabled && route.endpoint != null, System.nanoTime());
            if (!enabled) { pending.clear(); activeSession = -1; }
        }
        thread.interrupt();
        Log.i(TAG, "foreground=" + enabled + " senderRunId=" + Long.toUnsignedString(route.runId, 16));
    }
    void submitGamepad(GamepadState state) {
        submitGamepad(GamepadStateSubmission.ordinary(state));
    }
    void submitGamepad(GamepadStateSubmission submission) {
        synchronized (sendGate) {
            if (closed || closing || route.endpoint == null || (!foreground && !submission.state().neutral())) return;
            gamepad.change(submission, System.nanoTime());
        }
        thread.interrupt();
    }
    private void sendLoop() {
        try (DatagramSocket senderSocket = new DatagramSocket()) {
            Log.i(TAG, "sender_started queueCapacity=" + QUEUE_CAPACITY);
            while (!closed) {
                long wait;
                synchronized (sendGate) {
                    if (retiredGamepadNeutral != null) {
                        sendGamepad(senderSocket, retiredGamepadNeutral, 3);
                        retiredGamepadNeutral = null;
                    }
                    if (closing) { closed = true; continue; }
                    boolean admitted = sendHeartbeat(senderSocket, route);
                    if (route.endpoint != null && (admitted || !foreground)) {
                        GamepadSendState.Packet state = gamepad.claim(System.nanoTime(), foreground);
                        if (state != null) sendGamepad(senderSocket, new Pending(route,
                                GamepadProtocol.encode(route.runId, state.sequence(), state.submission())), state.copies(), state);
                    }
                    long now = System.nanoTime();
                    if (admitted && controlRequests && now >= requestAt) {
                        requestAt = now + (configEpoch == 0 ? 1_000_000_000L : 5_000_000_000L);
                        byte[] request = ControlConfigProtocol.request(route.runId, configEpoch, configRevision);
                        try { senderSocket.send(new DatagramPacket(request, request.length, route.endpoint)); }
                        catch (IOException exception) { Log.e(TAG, "control_request_failed", exception); }
                    }
                    wait = Math.min(heartbeat.waitNanos(now), gamepad.waitNanos(now, foreground));
                    if (controlRequests && foreground && route.endpoint != null) wait = Math.min(wait, Math.max(0, requestAt - now));
                    if (!admitted && foreground) wait = Math.max(wait, TimeUnit.MILLISECONDS.toNanos(10));
                }
                Pending packet;
                try { packet = pending.poll(wait, TimeUnit.NANOSECONDS); }
                catch (InterruptedException exception) { continue; }
                if (packet == null) continue;
                if (beforeSendForTest != null) beforeSendForTest.run();
                synchronized (sendGate) {
                    if (closed || closing || !foreground || packet.route != route || route.endpoint == null) continue;
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
    private void sendGamepad(DatagramSocket senderSocket, Pending packet, int copies) {
        sendGamepad(senderSocket, packet, copies, null);
    }
    private void sendGamepad(DatagramSocket senderSocket, Pending packet, int copies, GamepadSendState.Packet state) {
        long firstSendNs = 0, lastSendNs = 0;
        try {
            DatagramPacket datagram = new DatagramPacket(packet.bytes, packet.bytes.length, packet.route.endpoint);
            for (int copy = 0; copy < copies; copy++) {
                senderSocket.send(datagram);
                lastSendNs = System.nanoTime();
                if (copy == 0) {
                    firstSendNs = lastSendNs;
                    if (state != null) gamepad.sent(state, firstSendNs);
                }
            }
            Log.i(TAG, "gamepad_sent senderRunId=" + Long.toUnsignedString(packet.route.runId, 16)
                    + " sequence=" + Integer.toUnsignedLong(ByteBuffer.wrap(packet.bytes)
                    .order(ByteOrder.LITTLE_ENDIAN).getInt(10)) + " buttons="
                    + Short.toUnsignedInt(ByteBuffer.wrap(packet.bytes).order(ByteOrder.LITTLE_ENDIAN).getShort(14))
                    + " copies=" + copies + " firstSendNs=" + firstSendNs + " lastSendNs=" + lastSendNs);
        } catch (IOException exception) {
            if (state != null && firstSendNs == 0) gamepad.failed(state, System.nanoTime());
            if (!closed) Log.e(TAG, "gamepad_send_failed", exception);
        }
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
        synchronized (sendGate) {
            if (closed || closing) return;
            // UI only requests shutdown. All sends (including the final Neutral) stay on the existing worker.
            if (route.endpoint != null && gamepad.used()) {
                GamepadSendState.Packet neutral = gamepad.retire();
                retiredGamepadNeutral = new Pending(route, GamepadProtocol.encode(route.runId, neutral.sequence(), neutral.submission()));
            }
            closing = true; foreground = false; pending.clear(); heartbeat.setEnabled(false, System.nanoTime());
        }
        thread.interrupt();
    }
}
