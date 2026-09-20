package com.rightpad.capture;

import java.io.File;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;

public final class SlideControlLRTransportTests {
    private static int checks;
    private static void check(boolean ok, String message) { if (!ok) throw new AssertionError(message); checks++; }
    private static boolean same(ControlRect a, ControlRect b) {
        return a.x == b.x && a.y == b.y && a.width == b.width && a.height == b.height;
    }
    private static final ScreenControlDefinition X = ScreenControlDefinition.lr(SlideControlLRDefinition.phaseSix(),
            192, .15f, .55f, GamepadState.X, GamepadState.DPAD_LEFT, GamepadState.DPAD_RIGHT, GamepadState.DPAD_UP);
    private static final ScreenControlDefinition B = new ScreenControlDefinition(1, "xbox.b.slide", "B",
            SlideControlGesture.Config.phaseOne(1), 192, .65f, .55f, GamepadState.B, GamepadState.Y, GamepadState.A);
    private static final class Fixture {
        final List<GamepadStateSubmission> sent = new ArrayList<>();
        final GamepadAggregator aggregator = new GamepadAggregator(sent::add);
        final ScreenControlInstance x = new ScreenControlInstance(X, (buttons, hold) -> aggregator.contribute(X.id, buttons, hold));
        final ScreenControlInstance b = new ScreenControlInstance(B, (buttons, hold) -> aggregator.contribute(B.id, buttons, hold));
        void down() { aggregator.batch(() -> x.down(100, 100, 0, null, 1)); }
        void move(int dx, int dy, long now) { aggregator.batch(() -> x.move(100 + dx, 100 + dy, now)); }
        GamepadStateSubmission last() { return sent.get(sent.size() - 1); }
    }
    public static void main(String[] args) throws Exception {
        check(GamepadState.X == 0x0004 && GamepadState.DPAD_UP == 0x0800 && GamepadState.DPAD_LEFT == 0x2000
                && GamepadState.DPAD_RIGHT == 0x4000, "existing ABI logical bits");
        for (int button : new int[] {GamepadState.X, GamepadState.DPAD_LEFT, GamepadState.DPAD_RIGHT, GamepadState.DPAD_UP}) {
            var bytes = GamepadProtocol.encode(7, 8, new GamepadState(button));
            var wire = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN);
            check(bytes.length == 30 && bytes[0] == 2 && bytes[1] == 5, "unchanged 30-byte v2 type5");
            check(Short.toUnsignedInt(wire.getShort(14)) == button, "logical button at offset 14");
            for (int n = 16; n < 30; n++) check(bytes[n] == 0, "button-only analog/dwell/flags zero");
        }
        Fixture f = new Fixture(); f.down(); check(f.sent.isEmpty() && f.x.active(), "DOWN no Xbox X yet");
        f.aggregator.batch(() -> f.x.up(100, 100, 50, 50));
        check(f.last().state().buttons() == GamepadState.X && f.last().minimumWireHoldMs() == 25, "X tap requests existing minimum dwell");
        var pulse = GamepadProtocol.encode(7, 0, f.last());
        check(pulse[26] == 25 && pulse[27] == 0 && pulse[28] == 0, "X dwell encoded at unchanged offset");
        f.aggregator.batch(() -> f.x.advance(74)); check(f.sent.size() == 1, "X logical hold before deadline");
        f.aggregator.batch(() -> f.x.advance(75)); check(f.last().state().neutral(), "X logical deadline Neutral");
        int[][] moves = {{-12, 0, GamepadState.DPAD_LEFT}, {3, 0, GamepadState.DPAD_RIGHT}, {0, -2, GamepadState.DPAD_UP}};
        for (int[] move : moves) for (boolean design : new boolean[] {false, true}) for (boolean cancel : new boolean[] {false, true}) {
            Fixture g = new Fixture(); g.down();
            if (design) {
                g.aggregator.batch(() -> g.x.advance(400));
                check(g.last().state().buttons() == GamepadState.X && g.last().minimumWireHoldMs() == 0, "X LongPress held no dwell");
            }
            int before = g.sent.size(); g.move(move[0], move[1], design ? 450 : 20);
            check(g.sent.size() == before + 1 && g.last().state().buttons() == move[2], "one full X-to-Dpad replacement, no intermediate Neutral/combined");
            check(g.last().minimumWireHoldMs() == 0 && g.x.active(), "direction held with no artificial dwell");
            g.move(500, 500, 500); check(g.sent.size() == before + 1, "direction lock no new report");
            if (cancel) g.aggregator.batch(g.x::cancel); else g.aggregator.batch(() -> g.x.up(600, 600, 600, 600));
            check(g.last().state().neutral() && g.x.neutral() && !g.x.active(), "direction release or CANCEL");
        }
        Fixture pending = new Fixture(); pending.down(); pending.aggregator.batch(pending.x::cancel);
        check(pending.sent.isEmpty(), "pending CANCEL no tap");
        Fixture combined = new Fixture();
        combined.aggregator.batch(() -> { combined.b.down(0, 0, 0, B.slide, 1); combined.b.advance(400); });
        combined.down(); combined.aggregator.batch(() -> combined.x.advance(400));
        check(combined.last().state().buttons() == (GamepadState.B | GamepadState.X), "stable-ID contributors merge B + X");
        combined.move(3, 0, 450);
        check(combined.last().state().buttons() == (GamepadState.B | GamepadState.DPAD_RIGHT), "LR replacement preserves B contributor");
        combined.aggregator.batch(combined.x::cancel);
        check(combined.last().state().buttons() == GamepadState.B, "cancel X only withdraws X contributor");
        combined.aggregator.batch(combined.b::cancel); check(combined.last().state().neutral(), "B cleanup clears final contributor");
        for (int button : new int[] {GamepadState.X, GamepadState.DPAD_LEFT, GamepadState.DPAD_RIGHT, GamepadState.DPAD_UP}) {
            combined.aggregator.contribute(X.id, button, button == GamepadState.X ? 25 : 0);
            combined.aggregator.safetyClear();
            check(combined.last().safetyNeutral() && combined.last().state().neutral() && combined.last().minimumWireHoldMs() == 0,
                    "safety bypass for each LR state");
        }
        layoutAndRouting();
        System.out.println("RESULT SlideControlLRTransportTests checks=" + checks + " failed=0");
    }
    private static void layoutAndRouting() throws Exception {
        var directory = Files.createTempDirectory("rightpad-lr-layout");
        File file = directory.resolve("controls.properties").toFile();
        try {
            var store = new ScreenControlLayoutStore(file);
            var savedB = new ControlRect(1059, 600, 141, 2070);
            store.save(Map.of(B.id, savedB), 1200, 2670);
            byte[] before = Files.readAllBytes(file.toPath());
            var both = store.load(List.of(B, X), 1200, 2670);
            check(same(both.get(B.id), savedB), "adding X preserves saved B rect");
            check(java.util.Arrays.equals(before, Files.readAllBytes(file.toPath())), "load never overwrites user's file");
            ControlRect xr = both.get(X.id);
            check(same(xr, new ControlRect(180, 1469, 192, 192)) && xr.valid(1200, 2670), "deterministic square X default bounded");
            var editor = new ScreenControlLayoutEditor(List.of(B, X), both, 1200, 2670);
            editor.begin(); editor.startDrag(xr.x + 40, xr.y + 40, 7); editor.drag(xr.x + 60, xr.y + 80); editor.endDrag();
            check(editor.selectedId().equals(X.id), "generic editor selects X");
            editor.save(store); var loaded = store.load(List.of(B, X), 1200, 2670);
            check(same(loaded.get(B.id), savedB), "X editor Save does not alter B geometry");
            check(same(loaded.get(X.id), new ControlRect(200, 1509, 192, 192)), "X independent normalized persistence");
            var router = new ScreenControlRouter();
            for (String id : List.of(B.id, X.id)) {
                ControlRect r = loaded.get(id);
                check(router.down(r.x + 10, r.y + 10, 0, false, false, true, loaded) == ScreenControlRouter.Owner.SCREEN_CONTROL
                        && id.equals(router.controlId()), "B/X owner by DOWN rect");
                check(router.accepts(1, 0, false) && id.equals(router.controlId()), "owner retained without position re-routing");
                check(!router.accepts(2, 1, true) && router.owner() == ScreenControlRouter.Owner.NONE, "second pointer safe cancel");
            }
            check(router.down(600, 1000, 0, false, false, true, loaded) == ScreenControlRouter.Owner.MOUSE, "outside B/X owns Mouse");
            check(router.down(xr.x - 1, xr.y, 0, false, false, true, both) == ScreenControlRouter.Owner.MOUSE, "no invisible hit slop");
        } finally {
            Files.deleteIfExists(file.toPath()); Files.deleteIfExists(directory.resolve("controls.properties.tmp")); Files.delete(directory);
        }
    }
}
