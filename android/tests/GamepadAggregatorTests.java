package com.rightpad.capture;
import java.util.*;

public final class GamepadAggregatorTests {
    static int checks;
    static void check(boolean ok, String message) { checks++; if (!ok) throw new AssertionError(message); }
    public static void main(String[] args) {
        List<GamepadState> states = new ArrayList<>();
        GamepadAggregator agg = new GamepadAggregator(submission -> states.add(submission.state()));
        ScreenControlDefinition d = new ScreenControlDefinition("xbox.b.slide", "B",
                SlideControlGesture.Config.phaseOne(1), 64, .65f, .55f, GamepadState.B, GamepadState.Y, GamepadState.A);
        SlideControlGesture gesture = new SlideControlGesture(action -> agg.action(d, action));
        agg.batch(() -> { gesture.down(600, 0, d.slide); gesture.up(600, 10); });
        check(states.equals(List.of(new GamepadState(GamepadState.B))), "tap at UP");
        agg.batch(() -> gesture.advance(34)); check(states.size() == 1, "25ms not early");
        agg.batch(() -> gesture.advance(35)); check(states.get(1).neutral(), "25ms existing scheduler neutral");
        for (boolean up : new boolean[] {true, false}) {
            states.clear();
            agg.batch(() -> { gesture.down(600, 1000, d.slide); gesture.advance(1400); });
            check(states.equals(List.of(new GamepadState(GamepadState.B))), "long press B");
            agg.batch(() -> gesture.move(up ? 590 : 610, 1500));
            check(states.size() == 2 && states.get(1).buttons() == (up ? GamepadState.Y : GamepadState.A), "one full replacement, no intermediate Neutral");
            agg.batch(gesture::cancel); check(states.get(2).neutral(), "CANCEL neutral");
        }
        ScreenControlDefinition other = new ScreenControlDefinition("other", "X", d.slide, 64, 0, 0, GamepadState.X, 0, 0);
        agg.batch(() -> { agg.action(d, SlideControlGesture.Action.BASE); agg.action(other, SlideControlGesture.Action.BASE); });
        check(states.get(states.size()-1).buttons() == (GamepadState.B | GamepadState.X), "generic control union");
        agg.action(d, SlideControlGesture.Action.NEUTRAL);
        check(states.get(states.size()-1).buttons() == GamepadState.X, "release preserves other contribution");
        agg.batch(() -> { gesture.cancel(); agg.clear(); });
        check(states.get(states.size()-1).neutral(), "stop/editor cancel clears aggregator");
        int count = states.size(); agg.clear(); check(states.size() == count, "idempotent Neutral");
        System.out.println("RESULT GamepadAggregatorTests checks=" + checks + " failed=0");
    }
}
