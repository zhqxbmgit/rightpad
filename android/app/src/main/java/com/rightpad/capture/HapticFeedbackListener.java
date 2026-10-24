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
    java.util.function.Consumer<byte[]> layouts = ignored -> { };
    java.util.function.Consumer<ModeContactStream.Reply> modes = ignored -> { };
    java.util.function.Consumer<TouchCancellation.Ack> cancellations = ignored -> { };
    interface SocketFactory { DatagramSocket open() throws IOException; }
    private final SocketFactory socketFactory;
    private final int port;
    private volatile Object reception;
    private final java.util.concurrent.atomic.AtomicReference<Runnable> pendingLayout = new java.util.concurrent.atomic.AtomicReference<>();
    private final java.util.concurrent.atomic.AtomicBoolean layoutPosted = new java.util.concurrent.atomic.AtomicBoolean();
    private final java.util.function.Consumer<ControlConfigProtocol.Snapshot> configs;
    private final java.util.function.BiConsumer<StatusProtocol.Snapshot, Long> statuses;
    private final Object lifecycle = new Object();
    private final Thread worker;
    private volatile Identity identity;
    private volatile boolean closed;
    private DatagramSocket socket; // lifecycle lock; only worker opens it.
    private long accepted, rejected;

    HapticFeedbackListener(Runnable feedback) {
        this(feedback, ignored -> { });
    }
    HapticFeedbackListener(Runnable feedback, java.util.function.Consumer<ControlConfigProtocol.Snapshot> configs) {
        this(feedback, configs, (ignored, at) -> { });
    }
    HapticFeedbackListener(Runnable feedback, java.util.function.Consumer<ControlConfigProtocol.Snapshot> configs,
            java.util.function.BiConsumer<StatusProtocol.Snapshot, Long> statuses) {
        this(feedback,configs,statuses,HapticFeedbackProtocol.PORT,()->new DatagramSocket(null));
    }
    HapticFeedbackListener(Runnable feedback, java.util.function.Consumer<ControlConfigProtocol.Snapshot> configs,
            java.util.function.BiConsumer<StatusProtocol.Snapshot, Long> statuses,int port,SocketFactory socketFactory) {
        this.port=port; this.socketFactory=socketFactory;
        this.feedback = feedback;
        this.configs = configs;
        this.statuses = statuses;
        worker = new Thread(this::run, "RightpadHaptic");
        worker.start();
    }
    // UI thread only. Repeated discovery confirmations preserve pending identities.
    void setActive(InetAddress address, long runId) {
        Identity previous = identity;
        if (closed || (previous != null && Objects.equals(previous.address, address) && previous.runId == runId)) return;
        synchronized (lifecycle) {
            reception=null;
            identity = address == null ? null : new Identity(address, runId);
            if (socket != null) socket.close();
            lifecycle.notifyAll();
        }
        validation.configure(address, runId);
        ui.removeCallbacksAndMessages(null);
        pendingLayout.set(null);
        layoutPosted.set(false);
    }
    void expectUp(long runId, long sessionId, long sequence) {
        validation.expectUp(runId, sessionId, sequence, System.nanoTime());
    }
    private void run() {
        byte[] bytes = new byte[ControlConfigProtocol.MAX_SIZE + 1];
        int failures=0;
        try {
            while (!closed) {
                Identity current;
                synchronized (lifecycle) {
                    while (!closed && identity == null) lifecycle.wait();
                    if (closed) break;
                    current = identity;
                }
                Object generation=new Object();
                try (DatagramSocket opened = socketFactory.open()) {
                    synchronized (lifecycle) {
                        if (closed || current != identity) continue;
                        opened.bind(new InetSocketAddress(port));
                        socket = opened;
                        reception=generation;
                    }
                    Log.i(TAG, "listener_active port=50002 receiver=" + current.address.getHostAddress()
                            + " senderRunId=" + Long.toUnsignedString(current.runId, 16));
                    while (!closed && current == identity) {
                        DatagramPacket packet = new DatagramPacket(bytes, bytes.length);
                        opened.receive(packet);
                        if(closed || current!=identity || generation!=reception) break;
                        failures=0;
                        if(TouchCancellation.matches(bytes,packet.getLength())) {
                            var ack=TouchCancellation.decode(bytes,packet.getLength());
                            InetAddress source=packet.getAddress();
                            ui.post(()-> {
                                if(!closed && current==identity && generation==reception && current.address.equals(source)
                                        && ack!=null && ack.run()==current.runId) cancellations.accept(ack);
                            });
                            continue;
                        }
                        if(ModeContactStream.matches(bytes,packet.getLength())) {
                            var reply=ModeContactStream.decode(bytes,packet.getLength());
                            InetAddress source=packet.getAddress();
                            ui.post(()-> {
                                if(!closed && current==identity && generation==reception && current.address.equals(source) && reply!=null && reply.run()==current.runId) modes.accept(reply);
                            });
                            continue;
                        }
                        if (RemoteLayoutProtocol.matches(bytes, packet.getLength())) {
                            byte[] layout = java.util.Arrays.copyOf(bytes, packet.getLength());
                            InetAddress source = packet.getAddress();
                            pendingLayout.set(() -> {
                                if (!closed && current == identity && generation==reception && current.address.equals(source)) layouts.accept(layout);
                            });
                            if (layoutPosted.compareAndSet(false, true)) ui.post(() -> {
                                Runnable action = pendingLayout.getAndSet(null);
                                layoutPosted.set(false);
                                if (action != null) action.run();
                            });
                            continue;
                        }
                        if (StatusProtocol.isStatus(bytes, packet.getLength())) {
                            var snapshot = StatusProtocol.decode(bytes, packet.getLength());
                            InetAddress source = packet.getAddress();
                            long receivedAt = System.nanoTime();
                            ui.post(() -> {
                                if (!closed && current == identity && generation==reception && current.address.equals(source)
                                        && snapshot != null && snapshot.runId() == current.runId)
                                    statuses.accept(snapshot, receivedAt);
                            });
                            continue;
                        }
                        if (ControlConfigProtocol.isConfig(bytes, packet.getLength())) {
                            var snapshot = ControlConfigProtocol.decode(bytes, packet.getLength());
                            InetAddress source = packet.getAddress();
                            ui.post(() -> {
                                if (!closed && current == identity && generation==reception && current.address.equals(source)
                                        && snapshot != null && snapshot.runId() == current.runId) configs.accept(snapshot);
                            });
                            continue;
                        }
                        HapticFeedbackProtocol.Click click = HapticFeedbackProtocol.decode(bytes, packet.getLength());
                        InetAddress source = packet.getAddress();
                        ui.post(() -> {
                            // Recheck lifecycle at execution, including receive/post versus pause races.
                            if (closed || current != identity || generation!=reception) return;
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
                        if(++failures==1 || failures%32==0) Log.e(TAG, "listener_error retry="+failures, exception);
                        synchronized (lifecycle) {
                            reception=null;
                            long deadline=System.nanoTime()+Math.min(2000L,100L << Math.min(5,failures-1))*1_000_000L;
                            while (!closed && current == identity) {
                                long remaining=deadline-System.nanoTime();
                                if(remaining<=0) break;
                                java.util.concurrent.TimeUnit.NANOSECONDS.timedWait(lifecycle,remaining);
                            }
                        }
                    }
                } finally {
                    synchronized (lifecycle) { socket = null; if(reception==generation) reception=null; }
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
