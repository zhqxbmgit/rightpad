package com.rightpad.capture;

import java.io.File;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

public final class ScreenControlsTest {
    private static int passed;
    private static final SlideControlGesture.Config CONFIG = SlideControlGesture.Config.phaseOne(1);
    private static final ScreenControlDefinition DEFINITION = new ScreenControlDefinition(
            "test.secondary.slide", "Q", CONFIG, 100, .2f, .3f);
    private static final List<ScreenControlDefinition> DEFINITIONS = List.of(DEFINITION);

    interface Test { void run() throws Exception; }
    private static void test(String name, Test test) throws Exception {
        test.run();
        passed++;
        System.out.println("PASS " + name);
    }
    private static void check(boolean condition) { if (!condition) throw new AssertionError(); }
    private static void rect(ControlRect r, int x, int y, int w, int h) {
        check(r.x == x && r.y == y && r.width == w && r.height == h);
    }
    private static SlideControlGesture gesture() {
        SlideControlGesture g = new SlideControlGesture(a -> { });
        g.down(100, 0, CONFIG);
        return g;
    }
    private static ScreenControlLayoutEditor editor() {
        ScreenControlLayoutEditor e = new ScreenControlLayoutEditor(DEFINITIONS,
                Map.of(DEFINITION.id, new ControlRect(200, 300, 100, 100)), 1000, 1000);
        e.begin();
        return e;
    }
    private static void drag(ScreenControlLayoutEditor e, float x, float y, float dx, float dy) {
        e.startDrag(x, y, 8);
        e.drag(x + dx, y + dy);
        e.endDrag();
    }
    public static void main(String[] args) throws Exception {
        test("DOWN pending and neutral", () -> {
            SlideControlGesture g = gesture();
            check(g.state() == SlideControlGesture.State.PENDING && g.active());
            check(g.action() == SlideControlGesture.Action.NEUTRAL);
        });
        test("Tap UP starts BASE pulse; idle visual", () -> {
            SlideControlGesture g = gesture(); g.up(100, 50);
            check(g.state() == SlideControlGesture.State.TAP_PULSE && !g.active());
            check(g.action() == SlideControlGesture.Action.BASE);
        });
        test("Pulse lasts exactly 25 ms", () -> {
            SlideControlGesture g = gesture(); g.up(100, 50); g.advance(74);
            check(g.action() == SlideControlGesture.Action.BASE); g.advance(75);
            check(g.action() == SlideControlGesture.Action.NEUTRAL);
        });
        test("Delayed UP still gets full pulse from output time", () -> {
            SlideControlGesture g = gesture(); g.up(100, 50, 80); g.advance(104);
            check(g.action() == SlideControlGesture.Action.BASE); g.advance(105);
            check(g.action() == SlideControlGesture.Action.NEUTRAL);
        });
        test("Long press 399/400 deadline", () -> {
            SlideControlGesture g = gesture(); g.advance(399);
            check(g.action() == SlideControlGesture.Action.NEUTRAL); g.advance(400);
            check(g.state() == SlideControlGesture.State.BASE_HELD);
            check(g.action() == SlideControlGesture.Action.BASE);
        });
        test("Long press UP releases without pulse", () -> {
            SlideControlGesture g = gesture(); g.advance(400); g.up(100, 450);
            check(g.action() == SlideControlGesture.Action.NEUTRAL && g.deadline() == Long.MAX_VALUE);
        });
        test("UP after long deadline even without timer callback is no tap", () -> {
            SlideControlGesture g = gesture(); g.up(100, 401);
            check(g.state() == SlideControlGesture.State.IDLE);
        });
        test("Up threshold", () -> {
            SlideControlGesture g = gesture(); g.move(99.4f, 20);
            check(g.state() == SlideControlGesture.State.PENDING); g.move(99.2f, 30);
            check(g.action() == SlideControlGesture.Action.SLIDE_UP);
        });
        test("Down threshold", () -> {
            SlideControlGesture g = gesture(); g.move(102.9f, 20);
            check(g.state() == SlideControlGesture.State.PENDING); g.move(103, 30);
            check(g.action() == SlideControlGesture.Action.SLIDE_DOWN);
        });
        test("Direction lock in both directions", () -> {
            SlideControlGesture g = gesture(); g.move(90, 20); g.move(120, 30); g.advance(400);
            check(g.action() == SlideControlGesture.Action.SLIDE_UP);
            g = gesture(); g.move(110, 20); g.move(90, 30);
            check(g.action() == SlideControlGesture.Action.SLIDE_DOWN);
        });
        for (int direction : new int[] {-1, 1}) {
            test("Design A release BASE then slide " + direction, () -> {
                List<SlideControlGesture.Action> actions = new ArrayList<>();
                SlideControlGesture g = new SlideControlGesture(actions::add);
                g.down(100, 0, CONFIG); g.advance(400); g.move(100 + direction * 10, 500);
                check(actions.equals(List.of(SlideControlGesture.Action.BASE, SlideControlGesture.Action.NEUTRAL,
                        direction < 0 ? SlideControlGesture.Action.SLIDE_UP : SlideControlGesture.Action.SLIDE_DOWN)));
            });
        }
        test("CANCEL pending no tap", () -> {
            List<SlideControlGesture.Action> actions = new ArrayList<>();
            SlideControlGesture g = new SlideControlGesture(actions::add);
            g.down(100, 0, CONFIG); g.cancel(); g.up(100, 20); g.advance(999);
            check(actions.isEmpty());
        });
        for (int state = 0; state < 4; state++) {
            final int mode = state;
            test("Cancel held/pulse " + state, () -> {
                SlideControlGesture g = gesture();
                if (mode == 0) g.advance(400);
                else if (mode == 1) g.move(90, 20);
                else if (mode == 2) g.move(110, 20);
                else g.up(100, 20);
                g.cancel(); g.advance(10000);
                check(g.action() == SlideControlGesture.Action.NEUTRAL && !g.active());
            });
        }
        test("Configuration snapshot and next DOWN", () -> {
            SlideControlGesture.Config config = new SlideControlGesture.Config(10, 20, 70, 800);
            SlideControlGesture g = new SlideControlGesture(a -> { });
            g.down(100, 0, config); config = CONFIG;
            g.move(95, 50); g.advance(400); check(g.state() == SlideControlGesture.State.PENDING);
            g.up(100, 500); g.advance(569); check(g.action() == SlideControlGesture.Action.BASE);
            g.advance(570); check(g.action() == SlideControlGesture.Action.NEUTRAL);
            g.down(100, 600, config); g.move(95, 650);
            check(g.action() == SlideControlGesture.Action.SLIDE_UP);
        });
        test("New DOWN replaces old pulse without stale timer release", () -> {
            SlideControlGesture g = gesture(); g.up(100, 10); g.down(100, 20, CONFIG);
            g.move(90, 25); g.advance(35);
            check(g.action() == SlideControlGesture.Action.SLIDE_UP);
        });
        test("Density converts dp once", () -> {
            SlideControlGesture.Config config = SlideControlGesture.Config.phaseOne(3);
            check(Math.abs(config.upThresholdPx - 2.1f) < .001 && config.downThresholdPx == 9);
        });
        Map<String, ControlRect> controls = Map.of(DEFINITION.id, new ControlRect(100, 100, 100, 100));
        test("Router inside = SCREEN_CONTROL", () -> {
            ScreenControlRouter r = new ScreenControlRouter();
            check(r.down(100, 100, 3, false, false, true, controls) == ScreenControlRouter.Owner.SCREEN_CONTROL);
            check(r.controlId().equals(DEFINITION.id));
        });
        test("Router outside/right/bottom edge = MOUSE, no hit slop", () -> {
            for (float[] p : new float[][] {{99.9f, 100}, {200, 150}, {150, 200}, {0, 0}}) {
                ScreenControlRouter r = new ScreenControlRouter();
                check(r.down(p[0], p[1], 0, false, false, true, controls) == ScreenControlRouter.Owner.MOUSE);
            }
        });
        test("Router mouse entering control retains owner", () -> {
            ScreenControlRouter r = new ScreenControlRouter(); r.down(0, 0, 0, false, false, true, controls);
            check(r.accepts(1, 0, false) && r.owner() == ScreenControlRouter.Owner.MOUSE);
        });
        test("Router control leaving rect retains owner", () -> {
            ScreenControlRouter r = new ScreenControlRouter(); r.down(150, 150, 0, false, false, true, controls);
            check(r.accepts(1, 0, false) && r.owner() == ScreenControlRouter.Owner.SCREEN_CONTROL);
        });
        test("Second pointer cancels every owner until new DOWN", () -> {
            for (int i = 0; i < 4; i++) {
                ScreenControlRouter r = new ScreenControlRouter();
                r.down(i == 3 ? 0 : 150, 150, 0, i == 0, i == 1, true, controls);
                check(!r.accepts(2, 0, true) && r.owner() == ScreenControlRouter.Owner.NONE);
                check(!r.accepts(1, 0, false));
            }
        });
        test("Missing pointer cancels", () -> {
            ScreenControlRouter r = new ScreenControlRouter(); r.down(150, 150, 3, false, false, true, controls);
            check(!r.accepts(1, 4, false));
        });
        test("Settings and Power take precedence", () -> {
            ScreenControlRouter r = new ScreenControlRouter();
            check(r.down(150, 150, 0, true, false, true, controls) == ScreenControlRouter.Owner.SETTINGS);
            check(r.down(150, 150, 0, false, true, true, controls) == ScreenControlRouter.Owner.POWER);
        });
        test("Offline mouse NONE; local controls remain available", () -> {
            ScreenControlRouter r = new ScreenControlRouter();
            check(r.down(0, 0, 0, false, false, false, controls) == ScreenControlRouter.Owner.NONE);
            check(r.down(150, 150, 0, false, false, false, controls) == ScreenControlRouter.Owner.SCREEN_CONTROL);
        });
        test("Definition defaults square and bounded", () -> {
            ControlRect r = DEFINITION.defaultRect(1000, 2000);
            check(r.width == r.height && r.valid(1000, 2000));
        });
        test("Body move", () -> { ScreenControlLayoutEditor e = editor(); drag(e, 250, 350, 50, -50); rect(e.selectedRect(), 250, 250, 100, 100); });
        test("All four edge resizes", () -> {
            ScreenControlLayoutEditor e = editor(); drag(e, 204, 350, -50, 0); rect(e.selectedRect(), 150, 300, 150, 100);
            e = editor(); drag(e, 296, 350, 50, 0); rect(e.selectedRect(), 200, 300, 150, 100);
            e = editor(); drag(e, 250, 304, 0, -50); rect(e.selectedRect(), 200, 250, 100, 150);
            e = editor(); drag(e, 250, 396, 0, 50); rect(e.selectedRect(), 200, 300, 100, 150);
        });
        test("All four corners freely change aspect ratio", () -> {
            for (int x : new int[] {204, 296}) for (int y : new int[] {304, 396}) {
                ScreenControlLayoutEditor e = editor(); drag(e, x, y, x < 250 ? -40 : 40, y < 350 ? -70 : 70);
                check(e.selectedRect().width == 140 && e.selectedRect().height == 170);
            }
        });
        test("Move clamps all boundaries", () -> {
            ScreenControlLayoutEditor e = editor(); drag(e, 250, 350, -2000, -2000); rect(e.selectedRect(), 0, 0, 100, 100);
            drag(e, 50, 50, 2000, 2000); rect(e.selectedRect(), 900, 900, 100, 100);
        });
        test("Resize clamps boundary", () -> {
            ScreenControlLayoutEditor e = editor(); drag(e, 296, 396, 2000, 2000); rect(e.selectedRect(), 200, 300, 800, 700);
        });
        test("Minimum resize 20 px", () -> {
            ScreenControlLayoutEditor e = editor(); drag(e, 296, 396, -2000, -2000); rect(e.selectedRect(), 200, 300, 20, 20);
        });
        test("Valid integer numeric update accepts rectangles", () -> {
            ScreenControlLayoutEditor e = editor(); check(e.numeric("100", "150", "200", "70"));
            rect(e.selectedRect(), 100, 150, 200, 70); check(e.canSave());
        });
        test("Invalid numeric preserves last valid preview and disables Save", () -> {
            ScreenControlLayoutEditor e = editor();
            for (String[] fields : new String[][] {{"950", "300", "200", "100"}, {"", "300", "100", "100"},
                    {"1.5", "300", "100", "100"}, {"-1", "300", "100", "100"}, {"200", "300", "19", "100"},
                    {"2147483647", "300", "100", "100"}}) {
                check(!e.numeric(fields[0], fields[1], fields[2], fields[3]));
                rect(e.selectedRect(), 200, 300, 100, 100); check(!e.canSave());
            }
        });
        test("Dragging resolves invalid text with one draft", () -> {
            ScreenControlLayoutEditor e = editor(); e.numeric("bad", "0", "100", "100");
            drag(e, 250, 350, 10, 20); check(e.canSave()); rect(e.selectedRect(), 210, 320, 100, 100);
        });
        test("Blank editor DOWN does not clear invalid numeric state", () -> {
            ScreenControlLayoutEditor e = editor(); e.numeric("bad", "0", "100", "100");
            check(!e.startDrag(600, 600, 8)); check(!e.canSave());
            rect(e.selectedRect(), 200, 300, 100, 100);
        });
        test("Cancel restores committed", () -> {
            ScreenControlLayoutEditor e = editor(); e.numeric("1", "2", "300", "200"); e.cancel();
            rect(e.layout().get(DEFINITION.id), 200, 300, 100, 100);
        });
        File directory = Files.createTempDirectory("rightpad-layout-test").toFile();
        File file = new File(directory, "layout.properties");
        ScreenControlLayoutStore store = new ScreenControlLayoutStore(file);
        test("Save commits only after persistence; generic ID", () -> {
            ScreenControlLayoutEditor e = editor(); e.numeric("11", "22", "150", "90"); e.save(store);
            check(!e.editing()); rect(e.layout().get(DEFINITION.id), 11, 22, 150, 90);
            String saved = Files.readString(file.toPath()); check(saved.contains("controls.test.secondary.slide.xRatio"));
            check(saved.contains("version=1") && !saved.contains("bWidth"));
        });
        test("Restart/reload normalized roundtrip", () -> {
            ScreenControlLayoutStore restarted = new ScreenControlLayoutStore(file);
            rect(restarted.load(DEFINITIONS, 1000, 1000).get(DEFINITION.id), 11, 22, 150, 90);
            rect(restarted.load(DEFINITIONS, 2000, 3000).get(DEFINITION.id), 22, 66, 300, 270);
        });
        test("Reset is draft only and Cancel keeps saved layout", () -> {
            ScreenControlLayoutEditor e = new ScreenControlLayoutEditor(DEFINITIONS, store.load(DEFINITIONS, 1000, 1000), 1000, 1000);
            e.begin(); e.reset(); rect(e.selectedRect(), 200, 300, 100, 100);
            rect(store.load(DEFINITIONS, 1000, 1000).get(DEFINITION.id), 11, 22, 150, 90);
            e.cancel(); rect(e.layout().get(DEFINITION.id), 11, 22, 150, 90);
        });
        test("Invalid draft cannot Save", () -> {
            ScreenControlLayoutEditor e = editor(); e.numeric("950", "0", "200", "100");
            boolean rejected = false;
            try { e.save(store); } catch (IllegalStateException expected) { rejected = true; }
            check(rejected); rect(store.load(DEFINITIONS, 1000, 1000).get(DEFINITION.id), 11, 22, 150, 90);
        });
        test("Store independently rejects out-of-bounds", () -> {
            boolean rejected = false;
            try { store.save(Map.of("other", new ControlRect(990, 0, 100, 100)), 1000, 1000); }
            catch (IllegalArgumentException expected) { rejected = true; }
            check(rejected);
        });
        test("Disk failure retains draft and committed", () -> {
            ScreenControlLayoutEditor e = editor(); e.numeric("11", "22", "150", "90");
            boolean failed = false;
            try { e.save(new ScreenControlLayoutStore(new File(directory, "missing/layout"))); }
            catch (java.io.IOException expected) { failed = true; }
            check(failed && e.editing()); e.cancel(); rect(e.layout().get(DEFINITION.id), 200, 300, 100, 100);
        });
        test("Load validates/clamps malicious geometry", () -> {
            Files.writeString(file.toPath(), "version=1\ncontrols.test.secondary.slide.xRatio=1\ncontrols.test.secondary.slide.yRatio=-1\ncontrols.test.secondary.slide.widthRatio=2\ncontrols.test.secondary.slide.heightRatio=0\n");
            rect(store.load(DEFINITIONS, 1000, 1000).get(DEFINITION.id), 0, 0, 1000, 20);
        });
        test("Malformed/nonfinite/unknown version uses defaults", () -> {
            for (String value : new String[] {"version=99", "version=1\ncontrols.test.secondary.slide.xRatio=NaN", "version=1\ncontrols.test.secondary.slide.xRatio=broken"}) {
                Files.writeString(file.toPath(), value); rect(store.load(DEFINITIONS, 1000, 1000).get(DEFINITION.id), 200, 300, 100, 100);
            }
        });
        test("Multiple IDs use same editor/store", () -> {
            ScreenControlDefinition other = new ScreenControlDefinition("other", "R", CONFIG, 60, .6f, .7f);
            List<ScreenControlDefinition> defs = List.of(DEFINITION, other);
            Map<String, ControlRect> initial = new LinkedHashMap<>();
            initial.put(DEFINITION.id, DEFINITION.defaultRect(1000, 1000));
            initial.put(other.id, other.defaultRect(1000, 1000));
            ScreenControlLayoutEditor e = new ScreenControlLayoutEditor(defs, initial, 1000, 1000);
            e.begin(); drag(e, 630, 730, 10, 20); check(e.selectedId().equals("other"));
            e.save(store); rect(store.load(defs, 1000, 1000).get("other"), 610, 720, 60, 60);
        });
        Files.deleteIfExists(file.toPath()); Files.deleteIfExists(new File(file.getPath() + ".tmp").toPath()); Files.delete(directory.toPath());
        System.out.println("ScreenControlsTest: " + passed + " tests passed");
    }
}
