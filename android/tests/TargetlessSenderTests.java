package com.rightpad.capture;

import java.lang.reflect.Field;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.SocketTimeoutException;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.Arrays;

/** Phase 10A: gate-held state/deadline assertions plus real loopback safety output. */
public final class TargetlessSenderTests {
    private static int checks;
    private static void check(boolean value, String message) {
        checks++;
        if (!value) throw new AssertionError(message);
    }
    private static Object field(Object owner, String name) throws Exception {
        Field f = owner.getClass().getDeclaredField(name);
        f.setAccessible(true);
        return f.get(owner);
    }
    private static Object gate(UdpTouchSender sender) throws Exception { return field(sender, "sendGate"); }
    private static GamepadSendState gamepad(UdpTouchSender sender) throws Exception {
        return (GamepadSendState) field(sender, "gamepad");
    }
    private static Thread worker(UdpTouchSender sender) throws Exception { return (Thread) field(sender, "thread"); }
    private static void join(UdpTouchSender sender) throws Exception {
        worker(sender).join(3000);
        check(!worker(sender).isAlive() && (boolean) field(sender, "closed"), "close terminates existing worker");
    }
    private static GamepadState axes() {
        return new GamepadState(GamepadState.B, 21, 34, (short) -1200, (short) 2300, (short) -3400, (short) 4500);
    }
    private static DatagramSocket socket() throws Exception {
        DatagramSocket s = new DatagramSocket(0, InetAddress.getLoopbackAddress());
        s.setSoTimeout(2000);
        return s;
    }
    private static InetSocketAddress address(DatagramSocket s) {
        return new InetSocketAddress(s.getLocalAddress(), s.getLocalPort());
    }
    private static byte[] receive(DatagramSocket s) throws Exception {
        DatagramPacket p = new DatagramPacket(new byte[4096], 4096);
        s.receive(p);
        return Arrays.copyOf(p.getData(), p.getLength());
    }
    private static long sequence(byte[] p) {
        return Integer.toUnsignedLong(ByteBuffer.wrap(p).order(ByteOrder.LITTLE_ENDIAN).getInt(10));
    }
    private static void triple(DatagramSocket s, byte[] expected) throws Exception {
        byte[] first;
        do {
            first = receive(s);
            check(first[1] == 4 || first[1] == 7 || first[1] == 5, "only heartbeat/profile/gamepad traffic");
        } while (first[1] != 5 || sequence(first) != sequence(expected));
        check(Arrays.equals(expected, first), "exact current run/sequence/state/flags bytes");
        check(Arrays.equals(first, receive(s)), "second identical safety/state copy");
        check(Arrays.equals(first, receive(s)), "third identical safety/state copy");
    }
    private static void quiet(DatagramSocket s) throws Exception {
        s.setSoTimeout(80);
        try { receive(s); throw new AssertionError("unexpected datagram after lifecycle completion"); }
        catch (SocketTimeoutException expected) { checks++; }
        finally { s.setSoTimeout(2000); }
    }
    // Caller owns sendGate, so the worker cannot consume and hide a bad pending state.
    private static void pristine(UdpTouchSender sender) throws Exception {
        check(!sender.hasTarget() && !sender.canCapture(), "no route/capture");
        check(!(boolean) field(sender, "foreground"), "foreground off");
        GamepadSendState g = gamepad(sender);
        check(!g.used() && g.latest().neutral(), "targetless route has pristine Neutral state");
        check(field(g, "pending") == null && field(g, "pulse") == null, "no unsendable pending Neutral or hold");
        check((long) field(g, "nextSequence") == 0, "no targetless gamepad sequence consumed");
        check(sender.queuedForTest() == 0 && (long) field(sender, "activeSession") == -1, "Touch queue and session cleared");
        HeartbeatSchedule h = (HeartbeatSchedule) field(sender, "heartbeat");
        for (long now : new long[] { 0, 1, 100_000_000L, 9_000_000_000L, Long.MAX_VALUE / 2 }) {
            check(g.waitNanos(now, false) == Long.MAX_VALUE, "no gamepad deadline at arbitrary clock value");
            check(h.waitNanos(now) == Long.MAX_VALUE, "heartbeat disabled");
            check(Math.min(h.waitNanos(now), g.waitNanos(now, false)) == Long.MAX_VALUE,
                    "worker wait cannot become a zero-deadline loop");
        }
    }
    private static void parked(UdpTouchSender sender) throws Exception {
        // Secondary integration witness. The state/deadline proof above does not rely on CPU or sleep.
        long until = System.nanoTime() + 3_000_000_000L;
        Thread t = worker(sender);
        while (t.getState() != Thread.State.TIMED_WAITING && t.getState() != Thread.State.WAITING
                && System.nanoTime() < until) Thread.yield();
        check(t.isAlive() && (t.getState() == Thread.State.TIMED_WAITING || t.getState() == Thread.State.WAITING),
                "targetless worker parks in its queue wait");
    }
    private static void noTarget() throws Exception {
        try (DatagramSocket s = socket(); UdpTouchSender sender = new UdpTouchSender()) {
            synchronized (gate(sender)) {
                sender.enableControlRequests();
                sender.publishProfile(1); // Independent unsent profile/config state must not wake an off route.
                for (int i = 0; i < 8; i++) {
                    sender.setForeground(true);
                    sender.submitGamepad(axes());
                    sender.submit(UdpTouchSenderTest.touch(TouchSample.Action.DOWN));
                    sender.setForeground(false);
                    pristine(sender);
                    sender.setForeground(false);
                    pristine(sender);
                }
                check((long) field(sender, "nextSequence") == 0, "no targetless Touch sequence consumed");
            }
            parked(sender);
            quiet(s);
            sender.close(); join(sender);
            synchronized (gate(sender)) { pristine(sender); check(field(sender, "retiredGamepadNeutral") == null, "no targetless close release"); }
            quiet(s);
        }
        System.out.println("PASS targetless foreground on/off/repeat: pristine state, infinite wait, parked worker, no datagrams");
    }
    private static void targetPause(boolean held) throws Exception {
        try (DatagramSocket s = socket(); UdpTouchSender sender = new UdpTouchSender()) {
            byte[] expected;
            synchronized (gate(sender)) {
                sender.setTarget(address(s)); sender.setForeground(true);
                if (held) sender.submitGamepad(new GamepadStateSubmission(axes(), 25, false));
                sender.submit(UdpTouchSenderTest.touch(TouchSample.Action.DOWN));
                sender.setForeground(false);
                GamepadSendState g = gamepad(sender);
                GamepadSendState.Packet p = (GamepadSendState.Packet) field(g, "pending");
                check(p != null && p.state().neutral() && p.submission().safetyNeutral(), "target pause creates safety Neutral");
                check(p.copies() == 3 && p.sequence() == (held ? 1 : 0), "one version, existing three-copy policy");
                check(field(g, "pulse") == null && g.waitNanos(0, false) == 0, "real target owns immediately sendable release");
                check(sender.queuedForTest() == 0 && (long) field(sender, "activeSession") == -1, "pause clears Touch state");
                check(((HeartbeatSchedule) field(sender, "heartbeat")).waitNanos(0) == Long.MAX_VALUE, "pause disables heartbeat");
                expected = GamepadProtocol.encode(sender.getSenderRunId(), p.sequence(), p.submission());
            }
            triple(s, expected);
            synchronized (gate(sender)) { check(field(gamepad(sender), "pending") == null, "worker sends and clears target Neutral"); }
            quiet(s);
            sender.close(); join(sender);
            triple(s, GamepadProtocol.encode(sender.getSenderRunId(), held ? 2 : 1, GamepadStateSubmission.safety()));
            quiet(s);
        }
        System.out.println("PASS target-present foreground off: safety Neutral retained, held=" + held);
    }
    private static void removedBeforePause() throws Exception {
        try (DatagramSocket s = socket(); UdpTouchSender sender = new UdpTouchSender()) {
            byte[] retired;
            synchronized (gate(sender)) {
                sender.setTarget(address(s)); sender.setForeground(true);
                long oldRun = sender.getSenderRunId();
                sender.submitGamepad(axes());
                sender.setTarget(null);
                sender.setForeground(false);
                pristine(sender);
                check(sender.getSenderRunId() != oldRun, "removal rotates route run");
                Object old = field(sender, "retiredGamepadNeutral");
                check(old != null, "old route release retained separately");
                retired = GamepadProtocol.encode(oldRun, 1, GamepadStateSubmission.safety());
                check(Arrays.equals(retired, (byte[]) field(old, "bytes")), "retired release belongs to old route");
            }
            triple(s, retired); parked(sender);
            synchronized (gate(sender)) { pristine(sender); check(field(sender, "retiredGamepadNeutral") == null, "retired release drained once"); }
            sender.close(); join(sender); quiet(s);
        }
        System.out.println("PASS target removed before foreground off: old-route release only, new route pristine");
    }
    private static void closeRules() throws Exception {
        try (DatagramSocket s = socket(); UdpTouchSender unused = new UdpTouchSender()) {
            synchronized (gate(unused)) {
                unused.setTarget(address(s)); unused.close();
                check(field(unused, "retiredGamepadNeutral") == null, "unused target close creates no final Neutral");
            }
            join(unused); quiet(s);
        }
        try (DatagramSocket s = socket(); UdpTouchSender used = new UdpTouchSender()) {
            long run;
            synchronized (gate(used)) {
                used.setTarget(address(s)); used.setForeground(true); run = used.getSenderRunId();
                used.submitGamepad(axes()); used.close();
                check(field(used, "retiredGamepadNeutral") != null, "used target close retains final Neutral");
                check(!used.canCapture(), "closing rejects capture");
            }
            join(used);
            triple(s, GamepadProtocol.encode(run, 1, GamepadStateSubmission.safety()));
            used.close(); quiet(s);
        }
        System.out.println("PASS close: unused target silent, used target final safety triplicate, idempotent shutdown");
    }
    private static void replacement() throws Exception {
        try (DatagramSocket a = socket(); DatagramSocket b = socket(); UdpTouchSender sender = new UdpTouchSender()) {
            long oldRun, newRun;
            synchronized (gate(sender)) {
                sender.setTarget(address(a)); sender.setForeground(true); oldRun = sender.getSenderRunId();
                sender.submitGamepad(axes());
                sender.setTarget(address(b)); newRun = sender.getSenderRunId();
                check(newRun != oldRun, "replacement rotates run");
                check(!gamepad(sender).used(), "replacement has fresh gamepad baseline");
                check((long) field(sender, "nextSequence") == 0 && (long) field(sender, "profileSequence") == 0,
                        "existing Touch/profile route baselines unchanged");
                sender.submitGamepad(axes());
            }
            triple(a, GamepadProtocol.encode(oldRun, 1, GamepadStateSubmission.safety()));
            triple(b, GamepadProtocol.encode(newRun, 0, axes()));
            quiet(a);
            byte[] release;
            synchronized (gate(sender)) {
                sender.setForeground(false);
                GamepadSendState.Packet p = (GamepadSendState.Packet) field(gamepad(sender), "pending");
                release = GamepadProtocol.encode(newRun, p.sequence(), p.submission());
            }
            triple(b, release);
            sender.close(); join(sender);
            triple(b, GamepadProtocol.encode(newRun, sequence(release) + 1, GamepadStateSubmission.safety()));
            quiet(a); quiet(b);
        }
        System.out.println("PASS replacement: retired route safety, new route sequence zero, ordinary axes and final safety");
    }
    public static void main(String[] args) throws Exception {
        noTarget();
        targetPause(false); targetPause(true);
        removedBeforePause(); closeRules(); replacement();
        System.out.println("RESULT TargetlessSenderTests checks=" + checks + " failed=0");
    }
}
