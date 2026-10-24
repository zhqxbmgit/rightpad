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
    private volatile TouchDiagnostics diagnostics;
    void setDiagnostics(TouchDiagnostics value) { diagnostics = value; }
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
    interface SendOperation { void send(DatagramSocket socket, DatagramPacket packet) throws IOException; }
    private final SendOperation sendOperation;
    private long heartbeatFailures;
    private long admissionAborts;
    private volatile TouchCancellation cancellation;
    private volatile boolean resyncRequired;
    private Pending retiredTouchCancel;
    private long activeDown = -1;
    private volatile long cancelledThrough = -1;
    private final android.os.Handler resyncUi = new android.os.Handler(android.os.Looper.getMainLooper());
    private Runnable resyncObserver = () -> { };
    void setResyncObserver(Runnable observer) { resyncObserver=observer; }
    void cancelReply(TouchCancellation.Ack ack) {
        synchronized(sendGate) {
            if (!closed && !closing && !resyncRequired && cancellation!=null && cancellation.matches(ack)) cancellation=null;
        }
        thread.interrupt();
    }
    void cancelTouch() {
        synchronized(sendGate) { cancelTouchLocked(); }
        thread.interrupt();
    }
    private void cancelTouchLocked() {
        if(closed || closing || activeSession<0 || route.endpoint==null) return;
        cancellation=new TouchCancellation(route.runId,activeSession,activeDown,nextSequence++,System.nanoTime());
        cancelledThrough=cancellation.sequence;
        activeSession=-1; pending.clear();
    }
    private void retireTouchLocked() {
        cancelTouchLocked();
        if(cancellation!=null) {
            // Only one final old-route cancellation; absence of all old-run presence
            // traffic guarantees Receiver expiry even if this datagram is lost.
            TouchCancellation c=cancellation;
            retiredTouchCancel=new Pending(route,new TouchCancellation(c.run,c.session,c.down,c.sequence,0).claim(0));
        }
        cancellation=null; resyncRequired=false;
    }
    private volatile boolean closed, closing, foreground;
    private volatile Route route = new Route(null);
    private final HeartbeatSchedule heartbeat = new HeartbeatSchedule();
    private GamepadSendState gamepad = new GamepadSendState();
    private ModeContactStream mode = new ModeContactStream();
    private Pending retiredModeCancel;
    void modeReply(ModeContactStream.Reply reply) {
        synchronized(sendGate) { if(!closed && !closing && foreground && reply!=null && reply.run()==route.runId) mode.reply(reply,System.nanoTime()); }
        thread.interrupt();
    }
    boolean modeDown() {
        boolean accepted;
        synchronized(sendGate) { accepted=canCapture() && mode.down(System.nanoTime()); }
        thread.interrupt();return accepted;
    }
    void modeUp() { synchronized(sendGate) {mode.up();} thread.interrupt(); }
    void modeCancel() { synchronized(sendGate) {mode.cancel();} thread.interrupt(); }
    // Transport mirror of ScreenControlRouter's authoritative UI state, guarded by sendGate.
    private int latestProfile;
    private long profileSequence;
    private boolean profilePending = true;
    private Pending retiredGamepadNeutral; // The only old-route traffic permitted: a best-effort final Neutral.
    private long nextSequence;
    private long activeSession = -1;
    private boolean controlRequests;
    private long configEpoch, configRevision, requestAt;
    private Pending layoutResponse;
    void submitLayout(byte[] bytes) {
        synchronized (sendGate) {
            if (!closed && !closing && foreground && route.endpoint != null && bytes.length >= 16 &&
                    ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN).getLong(8) == route.runId)
                layoutResponse = new Pending(route, bytes);
        }
        thread.interrupt();
    }
    void enableControlRequests() { synchronized (sendGate) { controlRequests = true; requestAt = 0; } thread.interrupt(); }
    void knownControlConfig(long epoch, long revision) {
        synchronized (sendGate) { configEpoch = epoch; configRevision = revision; }
    }

    UdpTouchSender() { this(null, null); }
    // Test-only endpoint / dequeue barrier injection, never used by the Activity.
    UdpTouchSender(String address, int port) { this(new InetSocketAddress(address, port), null); }
    UdpTouchSender(InetSocketAddress endpoint, Runnable beforeSendForTest) {
        this(endpoint,beforeSendForTest,DatagramSocket::send);
    }
    UdpTouchSender(InetSocketAddress endpoint, Runnable beforeSendForTest, SendOperation operation) {
        this.beforeSendForTest = beforeSendForTest;
        sendOperation=operation;
        if (endpoint != null) route = new Route(endpoint);
        thread = new Thread(this::sendLoop, "RightpadUdpSender");
        thread.start();
    }
    String getReceiverAddress() { return route.endpoint == null ? null : route.endpoint.getAddress().getHostAddress(); }
    int getReceiverPort() { return route.endpoint == null ? 0 : route.endpoint.getPort(); }
    boolean hasTarget() { return route.endpoint != null; }
    boolean canCapture() { return !closed && !closing && foreground && route.endpoint != null && cancellation==null && !resyncRequired; }
    long runIdForTest() { return route.runId; }
    long getSenderRunId() { return route.runId; }
    void setUpObserver(UpObserver observer) { upObserver = observer; }
    int queuedForTest() { return pending.size(); }

    // UI thread only, after TouchCaptureView.stopCapture. Identity changes also force a new run.
    void setTarget(InetSocketAddress target) {
        if (route.endpoint != null && diagnostics != null) diagnostics.stop("receiver_changed");
        synchronized (sendGate) {
            if (closed || closing) return;
            retireTouchLocked();
            if (route.endpoint != null && gamepad.used()) {
                GamepadSendState.Packet neutral = gamepad.retire();
                retiredGamepadNeutral = new Pending(route, GamepadProtocol.encode(route.runId, neutral.sequence(), neutral.submission()));
            }
            gamepad = new GamepadSendState();
            mode.cancel();
            byte[] cancel=mode.claim(route.runId,System.nanoTime());
            if(route.endpoint!=null && cancel!=null) retiredModeCancel=new Pending(route,cancel);
            mode=new ModeContactStream();
            pending.clear();
            activeSession = -1;
            layoutResponse = null;
            nextSequence = 0;
            cancelledThrough=-1;
            route = new Route(target);
            profileSequence = 0;
            profilePending = true;
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
        if (!canCapture() || samples.length == 0) return;
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
        if (first.action == TouchSample.Action.DOWN) { activeSession = first.sessionId; activeDown=sequence; }
        if (first.action == TouchSample.Action.UP) activeSession = -1;
        Pending packet = new Pending(current, bytes);
        if (!pending.offer(packet)) {
            Pending dropped = pending.poll();
            if (dropped != null) Log.w(TAG, "queue_overflow droppedSequence=" + sequenceOf(dropped.bytes));
            pending.offer(packet);
        }
    }
    void setForeground(boolean enabled) {
        if (!enabled && diagnostics != null) diagnostics.stop("sender_paused");
        synchronized (sendGate) {
            if (closed || closing) return;
            if(!enabled) cancelTouchLocked();
            // A targetless route cannot drain gamepad work; keep its wait deadline idle.
            if (!enabled && route.endpoint != null) gamepad.change(GamepadStateSubmission.safety(), System.nanoTime());
            if(!enabled) mode.cancel();
            foreground = enabled;
            if (enabled) { requestAt = 0; profilePending = true; }
            heartbeat.setEnabled(enabled && route.endpoint != null, System.nanoTime());
            if (!enabled) { pending.clear(); activeSession = -1; layoutResponse = null; }
        }
        thread.interrupt();
        Log.i(TAG, "foreground=" + enabled + " senderRunId=" + Long.toUnsignedString(route.runId, 16));
    }
    void submitGamepad(GamepadState state) {
        submitGamepad(GamepadStateSubmission.ordinary(state));
    }
    void publishProfile(int profile) {
        if (profile < 0 || profile > 1) throw new IllegalArgumentException("profile");
        synchronized (sendGate) {
            if (closed || closing || profile == latestProfile) return;
            latestProfile = profile;
            profileSequence = (profileSequence + 1) & 0xffffffffL;
            profilePending = true;
        }
        thread.interrupt();
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
                    if(retiredTouchCancel!=null) {
                        try {sendOperation.send(senderSocket,new DatagramPacket(retiredTouchCancel.bytes,retiredTouchCancel.bytes.length,retiredTouchCancel.route.endpoint));}
                        catch(IOException exception) {Log.w(TAG,"retired_touch_cancel_failed");}
                        retiredTouchCancel=null;
                    }
                    if (retiredGamepadNeutral != null) {
                        sendGamepad(senderSocket, retiredGamepadNeutral, 3);
                        retiredGamepadNeutral = null;
                    }
                    if(retiredModeCancel!=null) {
                        try {senderSocket.send(new DatagramPacket(retiredModeCancel.bytes,retiredModeCancel.bytes.length,retiredModeCancel.route.endpoint));}
                        catch(IOException exception) {Log.e(TAG,"mode_cancel_failed",exception);}
                        retiredModeCancel=null;
                    }
                    if (closing) { closed = true; continue; }
                    if(cancellation!=null) {
                        long cancelNow=System.nanoTime();
                        if(cancellation.expired(cancelNow)) {
                            // No old-route heartbeat/gamepad/Mode/config work may keep a
                            // failed cancellation alive. UI rotates the run and fences all
                            // caches before resuming; if UI cannot run, presence expires.
                            resyncRequired=true;
                            Route failedRoute=route;
                            resyncUi.post(()-> {
                                if(!closed && !closing && route==failedRoute && resyncRequired) {
                                    setTarget(failedRoute.endpoint);
                                    resyncObserver.run();
                                }
                            });
                            cancellation=null;
                            Log.w(TAG,"touch_cancel_unconfirmed resync_required");
                        } else {
                            byte[] cancel=cancellation.claim(cancelNow);
                            if(cancel!=null) {
                                try {sendOperation.send(senderSocket,new DatagramPacket(cancel,cancel.length,route.endpoint));}
                                catch(IOException exception) { /* Bounded cancellation attempts; report once at exhaustion. */ }
                            }
                        }
                    }
                    if(resyncRequired) {
                        try {TimeUnit.MILLISECONDS.timedWait(sendGate,100);}
                        catch(InterruptedException ignored) { }
                        continue;
                    }
                    boolean admitted = sendHeartbeat(senderSocket, route);
                    if (admitted && profilePending) {
                        // Latest-state coalescing: one immutable version, exactly three attempts,
                        // on the existing socket after heartbeat, without blocking Touch on an ACK.
                        byte[] bytes = MotionProfileProtocol.encode(route.runId, profileSequence, latestProfile);
                        profilePending = false;
                        for (int copy = 0; copy < 3; copy++) {
                            try { senderSocket.send(new DatagramPacket(bytes, bytes.length, route.endpoint)); }
                            catch (IOException exception) { Log.e(TAG, "profile_send_failed", exception); }
                        }
                        Log.i(TAG, "profile_sent senderRunId=" + Long.toUnsignedString(route.runId, 16)
                                + " sequence=" + profileSequence + " profile=" + latestProfile + " copies=3");
                    }
                    if (route.endpoint != null && (admitted || !foreground)) {
                        GamepadSendState.Packet state = gamepad.claim(System.nanoTime(), foreground);
                        if (state != null) sendGamepad(senderSocket, new Pending(route,
                                GamepadProtocol.encode(route.runId, state.sequence(), state.submission())), state.copies(), state);
                    }
                    long now = System.nanoTime();
                    if(route.endpoint!=null && (admitted || !foreground)) {
                        byte[] modePacket=mode.claim(route.runId,now);
                        if(modePacket!=null) {
                            try {senderSocket.send(new DatagramPacket(modePacket,modePacket.length,route.endpoint));}
                            catch(IOException exception) {Log.e(TAG,"mode_send_failed",exception);}
                        }
                    }
                    if (admitted && layoutResponse != null) {
                        Pending response = layoutResponse; layoutResponse = null;
                        if (response.route == route) {
                            try { senderSocket.send(new DatagramPacket(response.bytes, response.bytes.length, route.endpoint)); }
                            catch (IOException exception) { Log.e(TAG, "layout_response_failed", exception); }
                        }
                    }
                    if (admitted && controlRequests && now >= requestAt) {
                        requestAt = now + (configEpoch == 0 ? 1_000_000_000L : 5_000_000_000L);
                        byte[] request = ControlConfigProtocol.request(route.runId, configEpoch, configRevision);
                        try { senderSocket.send(new DatagramPacket(request, request.length, route.endpoint)); }
                        catch (IOException exception) { Log.e(TAG, "control_request_failed", exception); }
                    }
                    wait = Math.min(heartbeat.waitNanos(now), gamepad.waitNanos(now, foreground));
                    if(route.endpoint!=null) wait=Math.min(wait,mode.waitNanos(now));
                    if (controlRequests && foreground && route.endpoint != null) wait = Math.min(wait, Math.max(0, requestAt - now));
                    if (!admitted && foreground) wait = Math.max(heartbeat.waitNanos(now), TimeUnit.MILLISECONDS.toNanos(10));
                    if(cancellation!=null) wait=Math.min(wait,cancellation.waitNanos(now));
                }
                Pending packet;
                try { packet = pending.poll(wait, TimeUnit.NANOSECONDS); }
                catch (InterruptedException exception) { continue; }
                if (packet == null) continue;
                if (beforeSendForTest != null) beforeSendForTest.run();
                synchronized (sendGate) {
                    if (closed || closing || !foreground || packet.route != route || route.endpoint == null || sequenceOf(packet.bytes)<=cancelledThrough) continue;
                    if (!sendHeartbeat(senderSocket, route)) {
                        // Before the first successful heartbeat, reject this bounded
                        // local queue as a safety cancellation rather than replaying
                        // stale DOWN/UP later. A periodic failure still attempts Touch.
                        cancelTouchLocked();
                        pending.clear();
                        if(++admissionAborts==1 || admissionAborts%64==0) Log.w(TAG,"touch_admission_aborted first_heartbeat_not_sent count="+admissionAborts);
                        continue;
                    }
                    if(cancellation!=null || resyncRequired) continue;
                    byte[] bytes = packet.bytes;
                    int copies = bytes[1] == 2 ? 1 : 3;
                    try {
                        DatagramPacket datagram = new DatagramPacket(bytes, bytes.length, route.endpoint);
                        for (int copy = 0; copy < copies; copy++) sendOperation.send(senderSocket,datagram);
                        TouchDiagnostics diagnostic = diagnostics;
                        if (diagnostic != null && diagnostic.active())
                            diagnostic.packet(sequenceOf(bytes), bytes.length, copies, false);
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
            TouchDiagnostics diagnostic = diagnostics;
            if (diagnostic != null && diagnostic.active())
                diagnostic.packet(Integer.toUnsignedLong(ByteBuffer.wrap(packet.bytes)
                        .order(ByteOrder.LITTLE_ENDIAN).getInt(10)), packet.bytes.length, copies, true);
        } catch (IOException exception) {
            if (state != null && firstSendNs == 0) gamepad.failed(state, System.nanoTime());
            if (!closed) Log.e(TAG, "gamepad_send_failed", exception);
        }
    }
    private boolean sendHeartbeat(DatagramSocket senderSocket, Route current) {
        if (closed || !foreground || current.endpoint == null) return false;
        boolean due = heartbeat.claimDue(System.nanoTime());
        if (!due) return !current.firstHeartbeat;
        try {
            sendOperation.send(senderSocket,new DatagramPacket(current.heartbeatBytes, current.heartbeatBytes.length, current.endpoint));
            current.firstHeartbeat = false;
            return true;
        } catch (IOException exception) {
            if (!closed && (++heartbeatFailures==1 || heartbeatFailures%64==0)) Log.e(TAG, "heartbeat_send_failed count="+heartbeatFailures, exception);
            return !current.firstHeartbeat;
        }
    }
    private static long sequenceOf(byte[] bytes) {
        return Integer.toUnsignedLong(ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN).getInt(16));
    }
    @Override public void close() {
        if (diagnostics != null) diagnostics.stop("sender_closed");
        synchronized (sendGate) {
            if (closed || closing) return;
            retireTouchLocked();
            // UI only requests shutdown. All sends (including the final Neutral) stay on the existing worker.
            if (route.endpoint != null && gamepad.used()) {
                GamepadSendState.Packet neutral = gamepad.retire();
                retiredGamepadNeutral = new Pending(route, GamepadProtocol.encode(route.runId, neutral.sequence(), neutral.submission()));
            }
            closing = true; foreground = false; pending.clear(); heartbeat.setEnabled(false, System.nanoTime());
            mode.cancel();byte[] cancel=mode.claim(route.runId,System.nanoTime());
            if(route.endpoint!=null && cancel!=null) retiredModeCancel=new Pending(route,cancel);
        }
        thread.interrupt();
    }
}
