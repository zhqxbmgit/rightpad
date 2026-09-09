package com.rightpad.capture;

import android.util.Log;

import java.io.Closeable;
import java.io.IOException;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.ThreadLocalRandom;
import java.util.concurrent.TimeUnit;

final class UdpTouchSender implements Closeable {
    private static final String TAG = "RightpadUdp";
    // Prototype destination, verified against the PC's route to Xiaomi 14.
    private static final String RECEIVER_IPV4 = "192.168.110.248";
    private static final int RECEIVER_PORT = 50000;
    private static final int QUEUE_CAPACITY = 8;

    private final ArrayBlockingQueue<byte[]> pending = new ArrayBlockingQueue<>(QUEUE_CAPACITY);
    private final Thread thread;
    private volatile DatagramSocket socket;
    private volatile boolean closed;
    private volatile boolean foreground;
    private final long senderRunId = ThreadLocalRandom.current().nextLong();
    private final HeartbeatSchedule heartbeat = new HeartbeatSchedule();
    private long nextSequence;
    private final String receiverAddress;
    private final int receiverPort;

    UdpTouchSender() {
        this(RECEIVER_IPV4, RECEIVER_PORT);
    }

    // Endpoint injection permits real socket tests without the development Receiver.
    UdpTouchSender(String receiverAddress, int receiverPort) {
        this.receiverAddress = receiverAddress;
        this.receiverPort = receiverPort;
        thread = new Thread(this::sendLoop, "RightpadUdpSender");
        thread.start();
    }

    // Called only on the UI thread. Only encoded bytes cross the thread boundary.
    void submit(TouchSample[] samples) {
        if (closed || !foreground) return;
        long sequence = nextSequence++;
        byte[] bytes;
        try {
            bytes = ProtocolV2Encoder.encode(samples, sequence, senderRunId);
        } catch (IllegalArgumentException exception) {
            Log.e(TAG, "packet_rejected sequence=" + sequence + " reason=" + exception.getMessage());
            return;
        }
        if (!pending.offer(bytes)) {
            // Bounded FIFO: never wait for the network or accumulate stale input forever.
            byte[] dropped = pending.poll();
            if (dropped != null) {
                Log.w(TAG, "queue_overflow droppedSequence=" + sequenceOf(dropped));
            }
            pending.offer(bytes); // One producer; removing one item guarantees space.
        }
    }

    void setForeground(boolean enabled) {
        if (closed) return;
        foreground = enabled;
        heartbeat.setEnabled(enabled, System.nanoTime());
        if (!enabled) pending.clear();
        thread.interrupt(); // Wake a timed poll; no heartbeat or marker enters the touch queue.
        Log.i(TAG, "foreground=" + enabled + " senderRunId=" + Long.toUnsignedString(senderRunId, 16));
    }

    private void sendLoop() {
        try (DatagramSocket senderSocket = new DatagramSocket()) {
            socket = senderSocket;
            InetAddress destination = InetAddress.getByName(receiverAddress);
            Log.i(TAG, "sender_started destination=" + receiverAddress + ":" + receiverPort
                    + " queueCapacity=" + QUEUE_CAPACITY + " senderRunId=" + Long.toUnsignedString(senderRunId, 16));
            byte[] heartbeatBytes = ProtocolV2Encoder.heartbeat(senderRunId);
            while (!closed) {
                if (heartbeat.claimDue(System.nanoTime()) && foreground) {
                    try { senderSocket.send(new DatagramPacket(heartbeatBytes, heartbeatBytes.length, destination, receiverPort)); }
                    catch (IOException exception) { if (!closed) Log.e(TAG, "heartbeat_send_failed", exception); }
                }
                byte[] bytes;
                try { bytes = pending.poll(heartbeat.waitNanos(System.nanoTime()), TimeUnit.NANOSECONDS); }
                catch (InterruptedException exception) { if (closed) break; else continue; }
                if (bytes == null || !foreground) continue;
                DatagramPacket packet = new DatagramPacket(bytes, bytes.length,
                        destination, receiverPort);
                int copies = bytes[1] == 2 ? 1 : 3;
                try {
                    for (int copy = 0; copy < copies && !closed && foreground; copy++) {
                        senderSocket.send(packet);
                    }
                    if (!closed) {
                        Log.i(TAG, "packet_sent sequence=" + sequenceOf(bytes)
                                + " bytes=" + bytes.length + " copies=" + copies);
                    }
                } catch (IOException exception) {
                    if (!closed) Log.e(TAG, "send_failed sequence=" + sequenceOf(bytes), exception);
                }
            }
        } catch (IOException exception) {
            if (!closed) Log.e(TAG, "sender_open_failed", exception);
        } finally {
            closed = true;
            pending.clear();
            Log.i(TAG, "sender_stopped");
        }
    }

    private static long sequenceOf(byte[] bytes) {
        return Integer.toUnsignedLong(ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN).getInt(16));
    }

    @Override
    public void close() {
        closed = true;
        pending.clear();
        DatagramSocket currentSocket = socket;
        if (currentSocket != null) currentSocket.close();
        thread.interrupt();
    }
}
