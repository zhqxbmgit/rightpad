package com.rightpad.capture;

import java.net.*;
import java.util.*;
import java.util.concurrent.*;

public final class GamepadWireHoldTests {
    static int checks;
    static final GamepadState B = new GamepadState(GamepadState.B), Y = new GamepadState(GamepadState.Y);
    static void check(boolean ok, String message) { checks++; if (!ok) throw new AssertionError(message); }
    static long ms(long value) { return value * 1_000_000L; }
    static GamepadStateSubmission pulse(long hold) { return new GamepadStateSubmission(B, hold, false); }
    public static void main(String[] args) throws Exception {
        for (int delay : new int[] {0, 12, 40}) {
            GamepadSendState slot = new GamepadSendState(); slot.change(pulse(25), 0);
            // A delayed worker must preserve even a pulse whose logical lifetime has ended.
            if (delay >= 25) slot.change(GamepadState.NEUTRAL, ms(25));
            GamepadSendState.Packet press = slot.claim(ms(delay), true);
            check(press.state().equals(B) && press.copies() == 3, "unsent pulse retained, triple copies");
            slot.sent(press, ms(delay));
            if (delay == 0) {
                check(slot.claim(ms(24), true) == null, "no early release at t=24");
                slot.change(GamepadState.NEUTRAL, ms(25));
                check(slot.waitNanos(ms(25), true) == 0, "logical expiry wakes immediately at completed hold");
            } else {
                if (delay < 25) slot.change(GamepadState.NEUTRAL, ms(25));
                check(slot.claim(ms(delay + 24), true) == null, "no early release, delay=" + delay);
                check(slot.waitNanos(ms(delay + 24), true) == ms(1), "poll wakes at actual-send + hold");
            }
            GamepadSendState.Packet neutral = slot.claim(ms(delay + 25), true);
            check(neutral.state().neutral() && neutral.copies() == 3 && neutral.sequence() > press.sequence(), "release at send +25, delay=" + delay);
            check(slot.claim(ms(500), true) == null, "completed pulse never replays");
        }
        for (boolean sent : new boolean[] {false, true}) {
            GamepadSendState slot = new GamepadSendState(); slot.change(pulse(25), 0);
            GamepadSendState.Packet p = slot.claim(0, true); if (sent) slot.sent(p, 0);
            slot.change(GamepadState.NEUTRAL, ms(1)); slot.change(GamepadStateSubmission.safety(), ms(2));
            check(slot.claim(ms(2), false).state().neutral(), "CANCEL/pause bypass sent/unsent hold");
            slot.sent(p, ms(3)); check(slot.claim(ms(200), true) == null, "stale send completion cannot revive canceled pulse");
        }
        GamepadSendState slot = new GamepadSendState(); slot.change(pulse(25), 0);
        GamepadSendState.Packet old = slot.claim(0, true); slot.sent(old, 0);
        slot.change(Y, ms(1));
        check(slot.claim(ms(1), true).state().equals(Y), "Design A replacement immediate");
        slot.sent(old, ms(2)); slot.change(GamepadState.NEUTRAL, ms(3));
        check(slot.claim(ms(3), true).state().neutral(), "old pulse cannot constrain replacement Neutral");
        slot.change(pulse(25), 0); old = slot.claim(0, true); slot.sent(old, 0);
        check(slot.retire().state().neutral(), "target/run/close retirement bypasses hold");
        check(slot.claim(ms(500), true) == null, "retired pressed state cannot replay");
        slot = new GamepadSendState(); slot.change(pulse(25), 0); old = slot.claim(0, true);
        slot.failed(old, ms(2)); slot.change(GamepadState.NEUTRAL, ms(25));
        check(slot.claim(ms(50), true) == null, "known failed send retries without busy spin");
        check(slot.claim(ms(102), true) == old, "same unsent pulse retried after known local failure");
        slot.sent(old, ms(103)); slot.sent(old, ms(110));
        check(slot.claim(ms(127), true) == null && slot.claim(ms(128), true).state().neutral(), "first success starts hold; extra copies never restart it");
        slot = new GamepadSendState(); slot.change(pulse(250), 0); old = slot.claim(0, true); slot.sent(old, 0);
        slot.change(GamepadState.NEUTRAL, ms(25));
        var r1 = slot.claim(ms(100), true); var r2 = slot.claim(ms(200), true); var end = slot.claim(ms(250), true);
        check(r1.state().equals(B) && r1.copies() == 1 && r2.sequence() > r1.sequence(), "longer generic dwell refreshes without resetting hold");
        check(end.state().neutral() && end.sequence() > r2.sequence(), "deferred Neutral newer than all refreshes");
        metadata();
        worker();
        System.out.println("RESULT GamepadWireHoldTests checks=" + checks + " failed=0");
    }
    static void metadata() {
        List<GamepadStateSubmission> output = new ArrayList<>();
        GamepadAggregator agg = new GamepadAggregator(output::add);
        var d = new ScreenControlDefinition("other-control", "X", new SlideControlGesture.Config(1, 3, 37, 400), 64, 0, 0, 4, 8, 1);
        SlideControlGesture[] gesture = new SlideControlGesture[1];
        gesture[0] = new SlideControlGesture(action -> agg.action(d, action, gesture[0].minimumWireHoldMs()));
        agg.batch(() -> { gesture[0].down(10, 0, d.slide); gesture[0].up(10, 1); });
        check(output.get(0).minimumWireHoldMs() == 37 && output.get(0).state().buttons() == 4, "generic control uses gesture snapshot TapHold, not B or fixed 25");
        agg.batch(() -> gesture[0].advance(38));
        check(output.get(1).state().neutral() && !output.get(1).safetyNeutral(), "tap expiry is ordinary Neutral");
        agg.batch(() -> { gesture[0].cancel(); agg.safetyClear(); });
        check(output.get(2).safetyNeutral(), "editor/stop safety reaches sender even after logical Neutral");
        agg.batch(() -> { gesture[0].down(10, 100, d.slide); gesture[0].advance(500); });
        check(output.get(3).minimumWireHoldMs() == 0, "LongPress no minimum hold");
        agg.batch(() -> gesture[0].move(8, 501));
        check(output.get(4).minimumWireHoldMs() == 0 && output.get(4).state().buttons() == 8, "Slide/Design A no minimum hold");
    }
    static void worker() throws Exception {
        CountDownLatch dequeued = new CountDownLatch(1), release = new CountDownLatch(1);
        try (DatagramSocket socket = new DatagramSocket(0, InetAddress.getLoopbackAddress());
             UdpTouchSender sender = new UdpTouchSender(new InetSocketAddress("127.0.0.1", socket.getLocalPort()), () -> {
                 dequeued.countDown(); boolean done=false;
                 while (!done) try { release.await(); done=true; } catch (InterruptedException ignored) { }
             })) {
            socket.setSoTimeout(2000); sender.setForeground(true); GamepadSenderTests.next(socket, 4);
            sender.submit(UdpTouchSenderTest.touch(TouchSample.Action.DOWN));
            check(dequeued.await(2, TimeUnit.SECONDS), "worker stalled before send");
            sender.submitGamepad(pulse(80)); sender.submitGamepad(GamepadState.NEUTRAL);
            release.countDown();
            byte[] press = GamepadSenderTests.next(socket, 5);
            long receivedAt = System.nanoTime();
            check(press[14] == GamepadState.B, "worker retains pressed despite logical Neutral");
            check(press.length == 30 && press[26] == 80 && press[27] == 0 && press[28] == 0, "pulse transport carries generic minimum dwell");
            check(Arrays.equals(press, GamepadSenderTests.next(socket, 5)) && Arrays.equals(press, GamepadSenderTests.next(socket, 5)), "pulse triplicate same bytes/sequence");
            sender.submit(UdpTouchSenderTest.touch(TouchSample.Action.MOVE));
            boolean moved = false;
            while (true) {
                byte[] p = UdpTouchSenderTest.receive(socket);
                if (p[1] == 2) moved = true;
                if (p[1] == 5 && p[14] == 0) {
                    check(p[26] == 0 && p[28] == 0, "ordinary pulse expiry is not FORCE_NEUTRAL");
                    check(moved, "Touch MOVE sent while Neutral waited for dwell");
                    check(System.nanoTime() - receivedAt >= ms(65), "local socket dwell not compressed"); break;
                }
            }
            sender.submitGamepad(pulse(1000));
            do { press = GamepadSenderTests.next(socket, 5); } while (press[14] != GamepadState.B);
            sender.setForeground(false);
            long pausedAt = System.nanoTime();
            do { press = GamepadSenderTests.next(socket, 5); } while (press[14] != 0);
            check(press[28] == 1, "pause carries FORCE_NEUTRAL");
            check(System.nanoTime()-pausedAt < ms(500), "actual pause bypasses remaining hold");
        } finally { release.countDown(); }
    }
}
