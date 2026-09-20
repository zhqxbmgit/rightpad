package com.rightpad.capture;

public final class ScreenControlFeedbackTests {
    private static int checks;
    private static final class Fake implements ScreenControlFeedback.Backend {
        int api = 34, oneShot, legacy;
        boolean available = true, failVibrate, failAvailable;
        ScreenControlFeedback.Event lastEvent;
        public boolean available() { if (failAvailable) throw new SecurityException(); return available; }
        public int apiLevel() { return api; }
        public void predefinedClick(ScreenControlFeedback.Event event) { throw new AssertionError("B must not use EFFECT_CLICK"); }
        public void oneShot(ScreenControlFeedback.Event event, long ms, int amplitude) {
            check(ms == 10 && amplitude == 255, "fixed 10ms/255 one-shot"); oneShot++; lastEvent = event;
            if (failVibrate) throw new SecurityException();
        }
        public void legacy(ScreenControlFeedback.Event event, long ms) {
            check(ms == 10, "fixed legacy 10ms"); legacy++; lastEvent = event;
            if (failVibrate) throw new IllegalStateException();
        }
    }
    private static void check(boolean value, String message) { if (!value) throw new AssertionError(message); checks++; }
    private static void emit(Fake fake) { new ScreenControlFeedback(fake).emit(ScreenControlFeedback.Event.PRESS); }
    public static void main(String[] args) {
        Fake f;
        for (int api : new int[] {26, 28, 29, 34, 36}) {
            f = new Fake(); f.api = api; emit(f);
            check(f.oneShot == 1 && f.legacy == 0 && f.lastEvent == ScreenControlFeedback.Event.PRESS,
                    "PRESS uses exactly one fixed one-shot");
            new ScreenControlFeedback(f).emit(ScreenControlFeedback.Event.DIRECTION_COMMIT);
            check(f.oneShot == 2 && f.lastEvent == ScreenControlFeedback.Event.DIRECTION_COMMIT,
                    "direction uses same fixed one-shot");
        }
        f = new Fake(); f.api = 25; emit(f); check(f.legacy == 1 && f.oneShot == 0, "old API");
        new ScreenControlFeedback(f).emit(ScreenControlFeedback.Event.DIRECTION_COMMIT);
        check(f.legacy == 2 && f.lastEvent == ScreenControlFeedback.Event.DIRECTION_COMMIT, "old API direction");
        f = new Fake(); f.available = false; emit(f); check(f.oneShot + f.legacy == 0, "no vibrator");
        f = new Fake(); f.failAvailable = true; emit(f); check(f.oneShot + f.legacy == 0, "service failure contained");
        for (int api : new int[] {25, 36}) {
            f = new Fake(); f.api = api; f.failVibrate = true; emit(f);
            check(f.oneShot + f.legacy == 1, "vibration failure contained without retries or stronger effect");
        }
        System.out.println("RESULT ScreenControlFeedbackTests checks=" + checks + " failed=0");
    }
}
