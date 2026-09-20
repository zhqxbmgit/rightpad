package com.rightpad.capture;

import java.util.ArrayList;
import java.util.List;
import static com.rightpad.capture.SlideControlLRGesture.Action.*;

public final class SlideControlLRTests {
    private static int checks;
    private static void check(boolean ok, String message) {
        if (!ok) throw new AssertionError(message);
        checks++;
    }
    private static void invalid(Runnable action) {
        try { action.run(); throw new AssertionError("invalid config accepted"); }
        catch (IllegalArgumentException expected) { checks++; }
    }
    private static final class Clock implements SlideControlLRInstance.Scheduler {
        long time, due = Long.MAX_VALUE;
        Runnable callback;
        public long now() { return time; }
        public void postDelayed(Runnable next, long delay) {
            check(delay >= 0 && callback == null, "one nonnegative scheduled callback");
            callback = next; due = time + delay;
        }
        public void removeCallbacks(Runnable old) { if (callback == old) { callback = null; due = Long.MAX_VALUE; } }
        void at(long now) {
            time = now;
            if (callback != null && due <= time) {
                Runnable next = callback; callback = null; due = Long.MAX_VALUE; next.run();
            }
        }
    }
    private static final class Backend implements ScreenControlFeedback.Backend {
        int api = 34, attempts;
        boolean available = true, fail, failAvailable;
        final List<String> calls = new ArrayList<>();
        public boolean available() { if (failAvailable) throw new SecurityException(); return available; }
        public int apiLevel() { return api; }
        private void record(String call) { attempts++; calls.add(call); if (fail) throw new SecurityException("haptic failed"); }
        public void predefinedClick(ScreenControlFeedback.Event event) { record(event + ":CLICK"); }
        public void oneShot(ScreenControlFeedback.Event event, long ms, int amplitude) { record(event + ":" + ms + "/" + amplitude); }
        public void legacy(ScreenControlFeedback.Event event, long ms) { record(event + ":" + ms); }
    }
    private static final class Fixture {
        final Clock clock = new Clock();
        final Backend backend = new Backend();
        final List<SlideControlLRGesture.Action> actions = new ArrayList<>();
        final SlideControlLRInstance control;
        Fixture() { this(SlideControlLRDefinition.phaseSix(), 1); }
        Fixture(SlideControlLRDefinition definition, float density) {
            control = new SlideControlLRInstance(definition, density, actions::add,
                    new ScreenControlFeedback(backend), clock);
        }
        void down() { control.down(100, 100, clock.time); }
        void move(float dx, float dy, long at) { clock.at(at); control.move(100 + dx, 100 + dy, at); }
        void up(long eventTime, long outputTime) { clock.time = outputTime; control.up(eventTime); }
        void neutral() {
            check(control.gesture.action() == NEUTRAL && !control.gesture.active()
                    && control.gesture.deadline() == Long.MAX_VALUE && clock.callback == null, "released without stuck state");
        }
    }
    private static void tapAndSnapshot() {
        Fixture f = new Fixture(); f.down();
        check(f.actions.isEmpty() && f.control.gesture.active(), "DOWN pending visual without BASE");
        check(f.clock.due == 400 && f.backend.calls.equals(List.of("PRESS:CLICK")), "DOWN timer and one click");
        f.control.updateConfig(new SlideControlLRGesture.Config(20, 20, 20, 100, 800));
        f.up(100, 150);
        check(f.actions.equals(List.of(BASE)) && !f.control.gesture.active(), "UP BASE pulse with idle visual");
        check(f.control.gesture.minimumHoldMs() == 25 && f.clock.due == 175, "DOWN TapHold snapshot from actual output time");
        f.clock.at(174); check(f.control.gesture.action() == BASE, "minimum pulse before deadline");
        f.clock.at(175); f.neutral();
        check(f.actions.equals(List.of(BASE, NEUTRAL)) && f.backend.attempts == 1, "pulse has no extra feedback");
        f.down(); check(f.clock.due == 975, "next DOWN receives new long threshold");
        f.up(200, 200); check(f.control.gesture.minimumHoldMs() == 100 && f.clock.due == 300, "next Tap uses new hold");
        f.clock.at(300); f.neutral();

        f = new Fixture(); f.down(); f.control.cancel(); f.clock.at(2000); f.up(2100, 2100); f.neutral();
        check(f.actions.isEmpty() && f.backend.attempts == 1, "pending CANCEL never Tap or feedback");
        f = new Fixture(); f.down(); f.up(10, 10); f.control.cancel(); f.clock.at(1000); f.neutral();
        check(f.actions.equals(List.of(BASE, NEUTRAL)), "cancel pending pulse clears delayed release");
        f = new Fixture(); f.down(); f.up(10, 10); f.clock.at(11); f.down(); f.clock.at(35);
        check(f.control.gesture.state() == SlideControlLRGesture.State.PENDING && f.clock.due == 411,
                "new contact cancels old pulse deadline");
        f.control.cancel(); f.neutral();
    }
    private static void longPress() {
        for (boolean cancel : new boolean[] {false, true}) {
            Fixture f = new Fixture(); f.down();
            f.control.updateConfig(new SlideControlLRGesture.Config(20, 20, 20, 100, 800));
            f.clock.at(399); check(f.actions.isEmpty(), "before long threshold no BASE");
            f.clock.at(400); check(f.actions.equals(List.of(BASE)) && f.control.gesture.active(), "snapshot LongPress exact threshold");
            check(f.backend.attempts == 1 && f.clock.callback == null, "LongPress no haptic/no repeating timer");
            if (cancel) f.control.cancel(); else f.up(500, 500);
            f.neutral(); check(f.actions.equals(List.of(BASE, NEUTRAL)) && f.backend.attempts == 1, "LongPress release once, silent");
        }
        Fixture f = new Fixture(); f.down(); f.up(400, 600); f.neutral();
        check(f.actions.equals(List.of(BASE, NEUTRAL)), "delayed timer UP at LongPress threshold is not a tap pulse");
    }
    private static void directions() {
        float[][] deltas = {{-12, 0}, {3, 0}, {0, -2}};
        SlideControlLRGesture.Action[] actions = {SLIDE_LEFT, SLIDE_RIGHT, SLIDE_UP};
        for (int i = 0; i < deltas.length; i++) {
            for (boolean cancel : new boolean[] {false, true}) {
                Fixture f = new Fixture(); f.down();
                float dx = deltas[i][0], dy = deltas[i][1];
                f.control.updateConfig(new SlideControlLRGesture.Config(40, 40, 40, 100, 800));
                f.move(dx * .99f, dy * .99f, 10);
                check(f.actions.isEmpty() && f.backend.attempts == 1, "below threshold no commit");
                f.move(dx, dy, 20);
                check(f.actions.equals(List.of(actions[i])) && f.control.gesture.active(), "exact DOWN-snapshot threshold commits");
                check(f.backend.calls.equals(List.of("PRESS:CLICK", "DIRECTION_COMMIT:CLICK")), "one additional direction click");
                f.move(-30, -40, 30); f.move(40, 400, 40); f.clock.at(900);
                check(f.actions.equals(List.of(actions[i])) && f.backend.attempts == 2, "first commit wins, repeated MOVE silent");
                check(f.clock.callback == null && f.control.gesture.minimumHoldMs() == 0, "direction cancels long timer, no tap dwell");
                if (cancel) f.control.cancel(); else f.up(1000, 1000);
                f.neutral(); check(f.actions.equals(List.of(actions[i], NEUTRAL)) && f.backend.attempts == 2, "direction release correct and silent");
            }
            Fixture f = new Fixture(); f.down(); f.clock.at(400); f.move(deltas[i][0], deltas[i][1], 410);
            check(f.actions.equals(List.of(BASE, NEUTRAL, actions[i])), "Design A BASE UP before direction DOWN");
            check(f.backend.calls.equals(List.of("PRESS:CLICK", "DIRECTION_COMMIT:CLICK")), "Design A only direction adds feedback");
            f.up(420, 420); f.neutral();
        }
        Fixture f = new Fixture(); f.down(); f.move(4, -10, 20);
        check(f.control.gesture.action() == SLIDE_RIGHT, "horizontal priority +4/-10 => RIGHT");
        f = new Fixture(); f.down(); f.move(-12, -100, 20);
        check(f.control.gesture.action() == SLIDE_LEFT, "left wins over larger upward displacement");
        f = new Fixture(); f.down(); f.move(-11, -2, 20);
        check(f.control.gesture.action() == SLIDE_UP, "up wins only below selected horizontal threshold");
        f = new Fixture(); f.down(); f.move(-3, 0, 20);
        check(f.control.gesture.action() == NEUTRAL, "left uses 12 not right 3");
        f = new Fixture(SlideControlLRDefinition.phaseSix(), 2); f.down(); f.move(5.99f, 0, 20);
        check(f.control.gesture.action() == NEUTRAL, "density scales threshold once");
        f.move(6, 0, 30); check(f.control.gesture.action() == SLIDE_RIGHT, "scaled exact threshold");
    }
    private static void downward() {
        Fixture f = new Fixture(); f.down(); f.move(0, 10000, 200);
        check(f.actions.isEmpty() && f.control.gesture.active(), "down has no direction or slop cancellation");
        f.up(300, 300); check(f.actions.equals(List.of(BASE)), "large downward short release still Tap");
        f.clock.at(325); f.neutral(); check(f.backend.attempts == 1, "downward Tap no extra feedback");
        f = new Fixture(); f.down(); f.move(0, 10000, 300); f.clock.at(400);
        check(f.actions.equals(List.of(BASE)), "large downward hold still LongPress");
        f.up(500, 500); f.neutral(); check(f.backend.attempts == 1, "downward LongPress silent");
    }
    private static void policies() {
        for (int api : new int[] {23, 25, 26, 28, 29, 34, 36}) {
            String effect = api >= 29 ? "CLICK" : api >= 26 ? "20/120" : "20";
            for (int failure : new int[] {0, 1, 2, 3}) {
                Fixture f = new Fixture(); f.backend.api = api;
                f.backend.fail = failure == 1; f.backend.available = failure != 2; f.backend.failAvailable = failure == 3;
                f.down(); f.clock.at(400); f.move(3, 0, 450); f.up(500, 500); f.neutral();
                check(f.actions.equals(List.of(BASE, NEUTRAL, SLIDE_RIGHT, NEUTRAL)), "haptic failure cannot change gesture state");
                check(f.backend.calls.equals(failure < 2 ? List.of("PRESS:" + effect, "DIRECTION_COMMIT:" + effect) : List.of()),
                        "exact API choice, no failure fallback/retry");
                f.backend.fail = false; f.backend.available = true; f.backend.failAvailable = false;
                int previous = f.backend.attempts; f.clock.at(600); f.down(); f.up(610, 610); f.clock.at(635); f.neutral();
                check(f.backend.attempts == previous + 1, "later input/feedback survives failure");
            }
        }
        var b = new ScreenControlDefinition("arbitrary", "Q", SlideControlGesture.Config.phaseOne(1), 64, .5f, .5f);
        check(b.feedbackStyle == ScreenControlFeedback.Style.STRONG_ONE_SHOT, "existing definitions keep strong style");
        var strong = new SlideControlLRDefinition("unrelated", "Z", SlideControlLRGesture.Config.defaults(), ScreenControlFeedback.Style.STRONG_ONE_SHOT);
        Fixture f = new Fixture(strong, 1); f.down(); f.move(3, 0, 20);
        check(f.backend.calls.equals(List.of("PRESS:10/255", "DIRECTION_COMMIT:10/255")), "definition policy not label/id/type selects haptic");
    }
    private static void config() {
        var c = SlideControlLRGesture.Config.defaults();
        check(c.leftThresholdDp() == 12 && c.rightThresholdDp() == 3 && c.upThresholdDp() == 2
                && c.tapHoldMs() == 25 && c.longPressMs() == 400, "Moonlight defaults");
        for (float bad : new float[] {0, -.1f, .099f, 50.01f, Float.NaN, Float.POSITIVE_INFINITY}) {
            invalid(() -> new SlideControlLRGesture.Config(bad, 3, 2, 25, 400));
            invalid(() -> new SlideControlLRGesture.Config(12, bad, 2, 25, 400));
            invalid(() -> new SlideControlLRGesture.Config(12, 3, bad, 25, 400));
        }
        for (long bad : new long[] {0, 201}) invalid(() -> new SlideControlLRGesture.Config(12, 3, 2, bad, 400));
        for (long bad : new long[] {49, 2001}) invalid(() -> new SlideControlLRGesture.Config(12, 3, 2, 25, bad));
        check(new SlideControlLRGesture.Config(.1f, 50, .1f, 1, 50).tapHoldMs() == 1, "inclusive lower limits");
        check(new SlideControlLRGesture.Config(50, .1f, 50, 200, 2000).longPressMs() == 2000, "inclusive upper limits");
    }
    public static void main(String[] args) {
        config(); tapAndSnapshot(); longPress(); directions(); downward(); policies();
        System.out.println("RESULT SlideControlLRTests checks=" + checks + " failed=0");
    }
}
