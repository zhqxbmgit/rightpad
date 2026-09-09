package com.rightpad.capture;

import java.net.*;
import java.nio.*;
import java.util.*;

public final class UdpTouchSenderTest {
    static void check(boolean value, String message) { if (!value) throw new AssertionError(message); }
    static byte[] receive(DatagramSocket socket) throws Exception {
        DatagramPacket packet = new DatagramPacket(new byte[65507], 65507);
        socket.receive(packet); return Arrays.copyOf(packet.getData(), packet.getLength());
    }
    static long run(byte[] bytes) { return ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN).getLong(2); }
    static long sequence(byte[] bytes) { return Integer.toUnsignedLong(ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN).getInt(16)); }
    static TouchSample[] touch(TouchSample.Action action) {
        return new TouchSample[] {new TouchSample(100, 100, System.nanoTime(), action, 0, 1, false)};
    }
    public static void main(String[] args) throws Exception {
        HeartbeatSchedule schedule = new HeartbeatSchedule();
        check(!schedule.claimDue(0), "disabled initially");
        schedule.setEnabled(true, 0);
        check(schedule.claimDue(0), "immediate");
        check(!schedule.claimDue(499_999_999), "not early");
        check(schedule.claimDue(5_000_000_000L), "overdue once");
        check(!schedule.claimDue(5_000_000_000L), "no catch-up burst");
        schedule.setEnabled(false, 0); check(!schedule.claimDue(Long.MAX_VALUE), "paused");
        System.out.println("PASS heartbeat schedule / overdue no burst");
        try (DatagramSocket socket = new DatagramSocket(0, InetAddress.getLoopbackAddress())) {
            socket.setSoTimeout(2000);
            long firstRun;
            try (UdpTouchSender sender = new UdpTouchSender("127.0.0.1", socket.getLocalPort())) {
                sender.setForeground(true);
                byte[] heartbeat = receive(socket); check(heartbeat.length == 10 && heartbeat[1] == 4, "immediate heartbeat");
                firstRun = run(heartbeat);
                sender.submit(touch(TouchSample.Action.DOWN));
                byte[] down = receive(socket); check(sequence(down) == 0 && run(down) == firstRun, "first seq zero / same run");
                check(Arrays.equals(down, receive(socket)) && Arrays.equals(down, receive(socket)), "DOWN exact triplicate");
                sender.setForeground(false);
                Thread.sleep(100); socket.setSoTimeout(50);
                try { while (true) receive(socket); } catch (SocketTimeoutException expected) { }
                socket.setSoTimeout(650);
                try { receive(socket); throw new AssertionError("heartbeat while paused"); } catch (SocketTimeoutException expected) { }
                sender.submit(touch(TouchSample.Action.MOVE)); // Must not consume sequence while paused.
                long resumed = System.nanoTime(); sender.setForeground(true);
                socket.setSoTimeout(2000); heartbeat = receive(socket);
                check(heartbeat[1] == 4 && run(heartbeat) == firstRun, "resume same run");
                check(System.nanoTime() - resumed < 400_000_000L, "resume immediate heartbeat");
                sender.submit(touch(TouchSample.Action.UP));
                byte[] up = receive(socket); check(sequence(up) == 1, "heartbeat / pause do not consume touch sequence");
                check(Arrays.equals(up, receive(socket)) && Arrays.equals(up, receive(socket)), "UP exact triplicate");
                System.out.println("PASS sender pause / resume / identity / sequence / copies");
                long until = System.nanoTime() + 1_600_000_000L;
                int heartbeats = 0, moves = 0;
                socket.setSoTimeout(15);
                while (System.nanoTime() < until) {
                    sender.submit(touch(TouchSample.Action.MOVE));
                    byte[] packet = receive(socket);
                    if (packet[1] == 4) heartbeats++; else { check(packet[1] == 2, "MOVE single copy"); moves++; }
                    Thread.sleep(1);
                }
                check(heartbeats >= 3 && heartbeats <= 4 && moves > 300, "high frequency does not starve heartbeat");
                System.out.println("PASS high frequency heartbeat independence moves=" + moves + " heartbeats=" + heartbeats);
            }
            socket.setSoTimeout(50);
            try { while (true) receive(socket); } catch (SocketTimeoutException expected) { }
            socket.setSoTimeout(2000);
            try (UdpTouchSender sender = new UdpTouchSender("127.0.0.1", socket.getLocalPort())) {
                sender.setForeground(true); byte[] heartbeat = receive(socket);
                check(run(heartbeat) != firstRun, "new sender new run");
                sender.submit(touch(TouchSample.Action.DOWN)); check(sequence(receive(socket)) == 0, "new sender sequence zero");
            }
            System.out.println("PASS recreated sender identity / baseline");
        }
        System.out.println("RESULT sender tests=4 passed=4 failed=0");
    }
}
