package com.rightpad.capture;

import android.app.Activity;
import android.app.Instrumentation;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.os.Bundle;
import android.os.SystemClock;
import android.view.InputDevice;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.accessibility.AccessibilityNodeInfo;
import android.widget.Button;
import android.widget.EditText;
import java.io.File;
import java.io.FileOutputStream;
import java.lang.reflect.Field;
import java.util.List;

/** Dependency-free device instrumentation, excluded from the production APK. */
public final class ScreenControlsSmoke extends Instrumentation {
    private MainActivity activity;
    private TouchCaptureView surface;
    private int checks;
    private long downTime;
    private Throwable mainFailure;
    private byte[] savedLayout;
    private File savedLayoutFile;

    @Override public void onCreate(Bundle arguments) { super.onCreate(arguments); start(); }
    @Override public void onStart() {
        Bundle result = new Bundle();
        try {
            launch();
            savedLayoutFile = new File(getTargetContext().getFilesDir(), "screen-controls.properties");
            savedLayout = savedLayoutFile.exists() ? java.nio.file.Files.readAllBytes(savedLayoutFile.toPath()) : null;
            // Editor assertions need room to move/resize. Restore the user's exact file in finally.
            main(() -> {
                var rectangles = new java.util.LinkedHashMap<>(surface.controls.rects());
                rectangles.put("xbox.b.slide", new ControlRect(100, 600, 240, 160));
                surface.controls.layout = new ScreenControlLayoutEditor(surface.controls.definitions,
                        rectangles, surface.getWidth(), surface.getHeight());
            });
            long baseline = sequence();
            main(() -> {
                ControlRect r = rect();
                ControlRect defaultRect = surface.controls.definitions.get(0).defaultRect(surface.getWidth(), surface.getHeight());
                check(defaultRect.width == defaultRect.height, "default square");
                check(r.valid(surface.getWidth(), surface.getHeight()), "default visible/bounded");
                check(pixel(r) == 0xFF00C853, "idle solid green");
            });
            screenshot("idle");
            ControlRect initial = currentRect();
            event(MotionEvent.ACTION_DOWN, initial.x + 30, initial.y + 30, false);
            main(() -> check(pixel(rect()) == 0xFFFF3B30, "DOWN solid red"));
            screenshot("active");
            event(MotionEvent.ACTION_UP, initial.x + 30, initial.y + 30, false);
            main(() -> check(pixel(rect()) == 0xFF00C853, "UP green"));
            SystemClock.sleep(50);
            main(() -> check(surface.controls.instances.values().iterator().next().gesture.action()
                    == SlideControlGesture.Action.NEUTRAL, "tap timer neutral"));
            check(sequence() == baseline, "control has no Mouse Touch output");
            event(MotionEvent.ACTION_DOWN, initial.x + 30, initial.y + 30, false);
            waitForBase();
            main(() -> check(surface.controls.instances.values().iterator().next().gesture.action()
                    == SlideControlGesture.Action.BASE, "held before ACTION_CANCEL"));
            event(MotionEvent.ACTION_CANCEL, initial.x + 30, initial.y + 30, false);
            SystemClock.sleep(60);
            main(() -> check(surface.controls.instances.values().iterator().next().gesture.action()
                    == SlideControlGesture.Action.NEUTRAL, "ACTION_CANCEL safety Neutral"));
            openEditor();
            main(() -> check(surface.controls.editing(), "Settings to generic editor"));
            screenshot("editor");
            int canvasHeight = surface.getHeight();
            main(() -> {
                field("X").requestFocus();
                ((android.view.inputmethod.InputMethodManager) activity.getSystemService(Activity.INPUT_METHOD_SERVICE))
                        .showSoftInput(field("X"), android.view.inputmethod.InputMethodManager.SHOW_IMPLICIT);
            });
            SystemClock.sleep(800);
            main(() -> {
                android.view.WindowInsets insets = field("X").getRootWindowInsets();
                check(insets.isVisible(android.view.WindowInsets.Type.ime()), "real soft keyboard opened");
                int[] position = new int[2]; button("Save").getLocationOnScreen(position);
                check(position[1] + button("Save").getHeight() <= canvasHeight
                        - insets.getInsets(android.view.WindowInsets.Type.ime()).bottom, "Save panel above keyboard");
                check(surface.getHeight() == canvasHeight && surface.controls.layout.height == canvasHeight,
                        "keyboard preserves canvas coordinate system");
            });
            screenshot("keyboard");
            main(() -> surface.controls.draftChanged.run());
            SystemClock.sleep(400);
            drag(initial.x + initial.width / 2f, initial.y + initial.height / 2f, 50, -60);
            main(() -> { check(rect().x == initial.x + 50 && rect().y == initial.y - 60, "body move"); fieldsMatch(); });
            ControlRect r = currentRect();
            drag(r.right() - 3, r.y + r.height / 2f, 80, 0);
            main(() -> { check(rect().width == initial.width + 80, "width resize"); fieldsMatch(); });
            r = currentRect();
            drag(r.x + r.width / 2f, r.bottom() - 3, 0, 50);
            main(() -> { check(rect().height == initial.height + 50, "height resize"); fieldsMatch(); });
            r = currentRect();
            int oldW = r.width, oldH = r.height;
            drag(r.right() - 3, r.bottom() - 3, -30, 60);
            main(() -> { check(rect().width == oldW - 30 && rect().height == oldH + 60, "corner free resize"); fieldsMatch(); });
            main(() -> {
                field("X").setText("100"); check(rect().x == 100, "X live input");
                field("Y").setText("600"); check(rect().y == 600, "Y live input");
                field("Width").setText("240"); check(rect().width == 240, "Width live input");
                field("Height").setText("160"); check(rect().height == 160, "Height live input");
                field("X").setText(Integer.toString(surface.getWidth() - 10));
                check(!button("Save").isEnabled(), "invalid input disables Save");
                check(field("X").getError() != null && rect().x == 100, "invalid field retains legal preview");
            });
            screenshot("invalid");
            event(MotionEvent.ACTION_DOWN, 600, 1200, true);
            event(MotionEvent.ACTION_UP, 600, 1200, true);
            main(() -> check(!button("Save").isEnabled() && field("X").getError() != null,
                    "blank editor click preserves invalid field"));
            drag(220, 680, -1000, -1000);
            main(() -> { check(rect().x == 0 && rect().y == 0, "drag clamps boundary"); fieldsMatch(); });
            main(() -> {
                field("X").setText("100"); field("Y").setText("600");
                button("Save").performClick();
                check(!surface.controls.editing(), "Save exits");
            });
            check(sequence() == baseline, "settings/editor never submit Mouse Touch");
            ActivityMonitor monitor = addMonitor(MainActivity.class.getName(), null, false);
            main(() -> activity.recreate());
            activity = (MainActivity) waitForMonitorWithTimeout(monitor, 5000);
            removeMonitor(monitor);
            check(activity != null, "Activity recreated");
            acquireSurface();
            main(() -> check(rect().x == 100 && rect().y == 600 && rect().width == 240 && rect().height == 160,
                    "recreated Activity reloads persisted rectangle"));
            openEditor();
            main(() -> { field("X").setText("300"); button("Cancel").performClick();
                check(rect().x == 100, "Cancel restores committed"); });
            openEditor();
            main(() -> {
                button("Reset").performClick(); check(rect().width == rect().height, "Reset default square in draft");
                try { check(surface.controls.store.load(surface.controls.definitions, surface.getWidth(), surface.getHeight())
                        .get("xbox.b.slide").width == 240, "Reset does not persist"); }
                catch (Exception error) { throw new AssertionError(error); }
                button("Cancel").performClick(); check(rect().width == 240 && rect().height == 160, "Reset Cancel restores rectangle");
            });
            // Lifecycle and mode changes cancel delayed output, not just active contacts.
            event(MotionEvent.ACTION_DOWN, 120, 620, false);
            event(MotionEvent.ACTION_UP, 120, 620, false);
            main(() -> { surface.stopCapture("smoke_pause"); check(neutral(), "stop cancels pulse"); });
            event(MotionEvent.ACTION_DOWN, 120, 620, false);
            waitForBase();
            main(() -> check(surface.controls.instances.get("xbox.b.slide").gesture.action() == SlideControlGesture.Action.BASE,
                    "real timer long press"));
            event(MotionEvent.ACTION_MOVE, 120, 590, false);
            main(() -> check(surface.controls.instances.get("xbox.b.slide").gesture.action() == SlideControlGesture.Action.SLIDE_UP,
                    "real View Design A"));
            event(MotionEvent.ACTION_CANCEL, 120, 590, false);
            main(() -> check(neutral(), "View CANCEL neutral"));
            screenshot("saved");
            feedbackRules();
            touchpadConfirmPolicy();
            lrPolicyAndScheduling();
            lrProduction();
            lrRuntimeConfig();
            result.putString("stream", "PASS ScreenControlsSmoke checks=" + checks + "\n");
        } catch (Throwable error) {
            result.putString("stream", "FAIL checks=" + checks + " " + android.util.Log.getStackTraceString(error));
        } finally {
            try {
                if (savedLayoutFile != null) {
                    if (savedLayout == null) java.nio.file.Files.deleteIfExists(savedLayoutFile.toPath());
                    else java.nio.file.Files.write(savedLayoutFile.toPath(), savedLayout);
                    main(() -> { surface.stopCapture("smoke_restore_layout"); surface.controls.layout = null;
                        surface.controls.resize(surface.getWidth(), surface.getHeight()); });
                }
            } catch (Throwable error) { result.putString("stream", "FAIL restoring user layout: " + error); }
        }
        finish(result.getString("stream", "").startsWith("PASS") ? Activity.RESULT_OK : Activity.RESULT_CANCELED, result);
    }
    private void lrRuntimeConfig() {
        main(() -> {
            var output = new java.util.ArrayList<GamepadStateSubmission>();
            var controls = new ScreenControls(surface, output::add);
            controls.feedback = new ScreenControlFeedback(new ScreenControlFeedback.Backend() {
                public int apiLevel() { return 29; }
                public boolean available() { return false; }
                public void predefinedClick(ScreenControlFeedback.Event event) { }
                public void oneShot(ScreenControlFeedback.Event event, long ms, int amplitude) { }
                public void legacy(ScreenControlFeedback.Event event, long ms) { }
            });
            byte[] bytes = java.util.HexFormat.of().parseHex("5250435402010000070000000000000001000000000000000200000000000000020000000100010c07001e00190090010200020e78001e00140019009001");
            check(controls.config.accept(ControlConfigProtocol.decode(bytes, bytes.length)), "production adapter v2 defaults");
            String id = "xbox.x.slide_lr";
            float density = surface.getResources().getDisplayMetrics().density;
            long now = SystemClock.uptimeMillis();
            MotionEvent down = MotionEvent.obtain(now, now, MotionEvent.ACTION_DOWN, 100, 100, 0);
            controls.down(id, down); down.recycle();
            var p = java.nio.ByteBuffer.wrap(bytes).order(java.nio.ByteOrder.LITTLE_ENDIAN);
            p.putLong(24, 3).putShort(54, (short)100).putShort(40, (short)15);
            check(controls.config.accept(ControlConfigProtocol.decode(bytes, bytes.length)), "production adapter atomic B+X update during contact");
            MotionEvent move = MotionEvent.obtain(now, now+1, MotionEvent.ACTION_MOVE, 100+4*density, 100, 0);
            controls.touch(id, move); move.recycle();
            check(controls.instances.get(id).lrGesture.action()==SlideControlLRGesture.Action.SLIDE_RIGHT, "production active gesture keeps right3");
            controls.cancel();
            down = MotionEvent.obtain(now, now+2, MotionEvent.ACTION_DOWN, 100, 100, 0); controls.down(id,down); down.recycle();
            move = MotionEvent.obtain(now, now+3, MotionEvent.ACTION_MOVE, 100+4*density, 100, 0); controls.touch(id,move); move.recycle();
            check(controls.instances.get(id).lrGesture.action()==SlideControlLRGesture.Action.NEUTRAL, "production next DOWN uses right10");
            move = MotionEvent.obtain(now, now+4, MotionEvent.ACTION_MOVE, 100+10*density, 100, 0); controls.touch(id,move); move.recycle();
            check(controls.instances.get(id).lrGesture.action()==SlideControlLRGesture.Action.SLIDE_RIGHT, "production new right10 commits");
            check(controls.config.gesture(ControlConfigProtocol.B_ID,1,null).upThresholdPx==1.5f, "production B in same snapshot");
            controls.cancel();
        });
    }
    private void lrPolicyAndScheduling() throws Exception {
        var calls = new java.util.ArrayList<String>();
        int[] api = {34};
        boolean[] fail = {false};
        ScreenControlFeedback policy = new ScreenControlFeedback(new ScreenControlFeedback.Backend() {
            public boolean available() { return true; }
            public int apiLevel() { return api[0]; }
            private void record(String value) {
                calls.add(value);
                if (fail[0]) throw new IllegalStateException("test LR feedback failure");
            }
            public void predefinedClick(ScreenControlFeedback.Event event) { record(event + ":CLICK"); }
            public void oneShot(ScreenControlFeedback.Event event, long ms, int amplitude) { record(event + ":" + ms + "/" + amplitude); }
            public void legacy(ScreenControlFeedback.Event event, long ms) { record(event + ":" + ms); }
        });
        main(() -> {
            check(surface.controls.definitions.size() == 2 && surface.controls.instances.containsKey("xbox.x.slide_lr"),
                    "B and LR production registry");
            check(surface.controls.definitions.get(0).feedbackStyle == ScreenControlFeedback.Style.STRONG_ONE_SHOT,
                    "B definition retains strong feedback");
            for (int level : new int[] {25, 26, 28, 29, 36}) {
                api[0] = level; calls.clear();
                policy.emit(ScreenControlFeedback.Event.PRESS, ScreenControlFeedback.Style.SYSTEM_CLICK);
                policy.emit(ScreenControlFeedback.Event.DIRECTION_COMMIT, ScreenControlFeedback.Style.SYSTEM_CLICK);
                String effect = level >= 29 ? "CLICK" : level >= 26 ? "20/120" : "20";
                check(calls.equals(List.of("PRESS:" + effect, "DIRECTION_COMMIT:" + effect)), "LR API policy " + level);
            }
            api[0] = 34; calls.clear();
        });
        long touchBaseline = sequence();
        var actions = new java.util.ArrayList<SlideControlLRGesture.Action>();
        var longPress = new java.util.concurrent.CountDownLatch(1);
        var tapRelease = new java.util.concurrent.CountDownLatch(1);
        SlideControlLRInstance[] lr = new SlideControlLRInstance[1];
        boolean[] awaitingTap = {false};
        main(() -> {
            android.os.Handler handler = new android.os.Handler(android.os.Looper.getMainLooper());
            lr[0] = new SlideControlLRInstance(SlideControlLRDefinition.phaseSix(), 1, action -> {
                actions.add(action);
                if (action == SlideControlLRGesture.Action.BASE) longPress.countDown();
                if (awaitingTap[0] && action == SlideControlLRGesture.Action.NEUTRAL) tapRelease.countDown();
            }, policy, new SlideControlLRInstance.Scheduler() {
                public long now() { return SystemClock.uptimeMillis(); }
                public void postDelayed(Runnable callback, long delay) { handler.postDelayed(callback, delay); }
                public void removeCallbacks(Runnable callback) { handler.removeCallbacks(callback); }
            });
            lr[0].updateConfig(new SlideControlLRGesture.Config(12, 3, 2, 30, 70));
            lr[0].down(100, 100, SystemClock.uptimeMillis());
            check(actions.isEmpty() && lr[0].gesture.active(), "LR DOWN pending visual no logical BASE");
            lr[0].updateConfig(new SlideControlLRGesture.Config(20, 20, 20, 100, 2000));
        });
        try {
            check(longPress.await(1500, java.util.concurrent.TimeUnit.MILLISECONDS), "real Handler LongPress uses DOWN snapshot");
            main(() -> {
                check(calls.equals(List.of("PRESS:CLICK")), "LongPress no extra feedback");
                fail[0] = true;
                lr[0].move(104, 90, SystemClock.uptimeMillis());
                check(actions.equals(List.of(SlideControlLRGesture.Action.BASE, SlideControlLRGesture.Action.NEUTRAL,
                        SlideControlLRGesture.Action.SLIDE_RIGHT)), "LR Design A release order/horizontal priority despite failure");
                lr[0].move(50, 50, SystemClock.uptimeMillis());
                lr[0].up(SystemClock.uptimeMillis());
                check(calls.equals(List.of("PRESS:CLICK", "DIRECTION_COMMIT:CLICK")), "repeated MOVE and UP silent");
                check(lr[0].gesture.action() == SlideControlLRGesture.Action.NEUTRAL, "LR UP neutral");
                fail[0] = false; calls.clear(); actions.clear();
                lr[0].updateConfig(new SlideControlLRGesture.Config(12, 3, 2, 30, 400));
                lr[0].down(0, 0, SystemClock.uptimeMillis());
                lr[0].move(0, 10000, SystemClock.uptimeMillis());
                awaitingTap[0] = true;
                lr[0].up(SystemClock.uptimeMillis());
                check(lr[0].gesture.action() == SlideControlLRGesture.Action.BASE, "downward movement permits Tap");
            });
            check(tapRelease.await(1500, java.util.concurrent.TimeUnit.MILLISECONDS), "real Handler releases Tap pulse");
            main(() -> {
                check(actions.equals(List.of(SlideControlLRGesture.Action.BASE, SlideControlLRGesture.Action.NEUTRAL)),
                        "Tap timer emits one release");
                check(calls.equals(List.of("PRESS:CLICK")), "Tap pulse and timer no extra feedback");
                lr[0].down(0, 0, SystemClock.uptimeMillis()); lr[0].cancel();
                check(lr[0].gesture.deadline() == Long.MAX_VALUE && lr[0].gesture.action() == SlideControlLRGesture.Action.NEUTRAL,
                        "cancel removes logical deadline");
            });
            check(sequence() == touchBaseline, "isolated LR creates no Touch transport");
            main(() -> {
                // Exercise the real API 29+ backend independently of any production registration.
                var real = new ScreenControlFeedback(new ScreenControlHapticFeedback(activity));
                real.emit(ScreenControlFeedback.Event.PRESS, ScreenControlFeedback.Style.SYSTEM_CLICK);
                real.emit(ScreenControlFeedback.Event.DIRECTION_COMMIT, ScreenControlFeedback.Style.SYSTEM_CLICK);
            });
        } finally { main(() -> lr[0].cancel()); }
    }
    private void lrProduction() throws Exception {
        String id = "xbox.x.slide_lr";
        var feedbackEvents = new java.util.ArrayList<ScreenControlFeedback.Event>();
        ScreenControlFeedback original = surface.controls.feedback;
        boolean[] fail = {false};
        main(() -> surface.controls.feedback = new ScreenControlFeedback(new ScreenControlFeedback.Backend() {
            public boolean available() { return true; }
            public int apiLevel() { return 36; }
            public void predefinedClick(ScreenControlFeedback.Event e) {
                feedbackEvents.add(e); if (fail[0]) throw new IllegalStateException("LR test failure");
            }
            public void oneShot(ScreenControlFeedback.Event e, long ms, int amplitude) { throw new AssertionError("LR must use SYSTEM_CLICK"); }
            public void legacy(ScreenControlFeedback.Event e, long ms) { throw new AssertionError("LR unexpected legacy"); }
        }));
        try {
            ControlRect[] bounds = new ControlRect[1];
            main(() -> {
                bounds[0] = surface.controls.rects().get(id);
                check(bounds[0].valid(surface.getWidth(), surface.getHeight()), "production X saved rectangle bounded");
                ControlRect defaultRect = surface.controls.instances.get(id).definition.defaultRect(surface.getWidth(), surface.getHeight());
                check(defaultRect.valid(surface.getWidth(), surface.getHeight()) && defaultRect.width == defaultRect.height,
                        "production X bounded default square");
                check(pixel(bounds[0]) == 0xFF00C853, "production X idle solid green");
                android.util.Log.i("RightpadSmoke", "X_LAYOUT view=" + surface.getWidth() + "x" + surface.getHeight()
                        + " rect=" + bounds[0].x + "," + bounds[0].y + "," + bounds[0].width + "," + bounds[0].height);
            });
            float x = bounds[0].x + bounds[0].width / 2f, y = bounds[0].y + bounds[0].height / 2f;
            float density = activity.getResources().getDisplayMetrics().density;
            ScreenControlInstance c = surface.controls.instances.get(id);
            long baseline = sequence();
            event(MotionEvent.ACTION_DOWN, x, y, false);
            main(() -> check(c.active() && c.neutral() && pixel(bounds[0]) == 0xFFFF3B30, "X pending red no BASE"));
            event(MotionEvent.ACTION_UP, x, y, false);
            SystemClock.sleep(80);
            main(() -> check(c.neutral() && pixel(bounds[0]) == 0xFF00C853 && feedbackEvents.size() == 1, "X Tap release green and one feedback"));
            event(MotionEvent.ACTION_DOWN, x, y, false);
            main(() -> {
                long now = SystemClock.uptimeMillis();
                MotionEvent release = MotionEvent.obtain(downTime, now, MotionEvent.ACTION_MOVE, x - 60, y, 0);
                release.setSource(InputDevice.SOURCE_TOUCHSCREEN);
                release.addBatch(now + 1, x, y, 1, 1, 0);
                release.setAction(MotionEvent.ACTION_UP);
                check(release.getHistorySize() == 1, "LR release-history fixture");
                surface.dispatchTouchEvent(release); release.recycle();
                check(c.lrGesture.state() == SlideControlLRGesture.State.TAP_PULSE && feedbackEvents.size() == 2,
                        "LR UP history cannot commit a new direction or feedback");
            });
            SystemClock.sleep(80);
            float[][] moves = {{-12 * density, 0}, {3 * density, 0}, {0, -2 * density}};
            SlideControlLRGesture.Action[] directions = {SlideControlLRGesture.Action.SLIDE_LEFT,
                    SlideControlLRGesture.Action.SLIDE_RIGHT, SlideControlLRGesture.Action.SLIDE_UP};
            for (int i = 0; i < moves.length; i++) for (boolean design : new boolean[] {false, true}) {
                final int index = i;
                main(feedbackEvents::clear);
                event(MotionEvent.ACTION_DOWN, x, y, false);
                if (design) {
                    long until = SystemClock.uptimeMillis() + 2000;
                    boolean[] held = {false};
                    do {
                        main(() -> held[0] = c.lrGesture.action() == SlideControlLRGesture.Action.BASE);
                        if (held[0]) break;
                        SystemClock.sleep(20);
                    } while (SystemClock.uptimeMillis() < until);
                    check(held[0], "X production LongPress");
                }
                fail[0] = design;
                event(MotionEvent.ACTION_MOVE, x + moves[i][0], y + moves[i][1], false);
                main(() -> check(c.lrGesture.action() == directions[index] && pixel(bounds[0]) == 0xFFFF3B30,
                        "production LR direction/Design A despite feedback failure"));
                event(MotionEvent.ACTION_MOVE, 600, 1200, false);
                main(() -> check(c.lrGesture.action() == directions[index], "X owner retained outside rect"));
                event(design ? MotionEvent.ACTION_CANCEL : MotionEvent.ACTION_UP, 600, 1200, false);
                main(() -> check(c.neutral() && feedbackEvents.equals(List.of(ScreenControlFeedback.Event.PRESS,
                        ScreenControlFeedback.Event.DIRECTION_COMMIT)), "LR UP/CANCEL neutral, exactly two feedback attempts"));
                fail[0] = false;
            }
            event(MotionEvent.ACTION_DOWN, x, y, false);
            event(MotionEvent.ACTION_MOVE, x + 3 * density, y, false);
            main(() -> {
                long now = SystemClock.uptimeMillis();
                MotionEvent.PointerProperties[] properties = {new MotionEvent.PointerProperties(), new MotionEvent.PointerProperties()};
                MotionEvent.PointerCoords[] coordinates = {new MotionEvent.PointerCoords(), new MotionEvent.PointerCoords()};
                for (int i = 0; i < 2; i++) { properties[i].id = i; properties[i].toolType = MotionEvent.TOOL_TYPE_FINGER;
                    coordinates[i].x = x + i; coordinates[i].y = y; coordinates[i].pressure = 1; }
                MotionEvent second = MotionEvent.obtain(downTime, now, MotionEvent.ACTION_POINTER_DOWN | (1 << MotionEvent.ACTION_POINTER_INDEX_SHIFT),
                        2, properties, coordinates, 0, 0, 1, 1, 0, 0, InputDevice.SOURCE_TOUCHSCREEN, 0);
                surface.dispatchTouchEvent(second); second.recycle();
                check(neutral(), "second pointer safety clears X and B");
            });
            check(sequence() == baseline, "X gestures never create Mouse Touch packets");
            openEditor();
            ControlRect beforeB = currentRect();
            drag(x, y, 20, -20);
            main(() -> {
                check(surface.controls.layout.selectedId().equals(id), "generic editor selects X");
                button("Save").performClick();
                ControlRect bx = rect(), xx = surface.controls.rects().get(id);
                check(bx.x == beforeB.x && bx.y == beforeB.y && bx.width == beforeB.width && bx.height == beforeB.height,
                        "X Save preserves B rect");
                check(xx.x == bounds[0].x + 20 && xx.y == bounds[0].y - 20, "X editor move persisted");
            });
        } finally { main(() -> { surface.controls.feedback = original; surface.stopCapture("lr_smoke_cleanup"); }); }
    }
    private void touchpadConfirmPolicy() {
        main(() -> {
            int[] calls = {0};
            boolean[] performed = {true}, fail = {false};
            View target = new View(activity) {
                @Override public boolean performHapticFeedback(int effect) {
                    check(effect == android.view.HapticFeedbackConstants.CONFIRM, "production Touchpad CONFIRM");
                    calls[0]++;
                    if (fail[0]) throw new IllegalStateException("test CONFIRM failure");
                    return performed[0];
                }
            };
            TouchpadClickFeedback feedback = new TouchpadClickFeedback(new TouchpadClickHapticFeedback(target));
            check(feedback.acceptedClick() && calls[0] == 1, "production adapter calls View exactly once");
            performed[0] = false;
            check(!feedback.acceptedClick() && calls[0] == 2, "system false result no fallback");
            fail[0] = true;
            check(!feedback.acceptedClick() && calls[0] == 3, "production failure contained");
            fail[0] = false; performed[0] = true;
            check(feedback.acceptedClick() && calls[0] == 4, "production feedback recovers");
        });
    }
    private void waitForBase() {
        long until = SystemClock.uptimeMillis() + 2500;
        boolean[] ready = {false};
        do {
            main(() -> ready[0] = surface.controls.instances.get("xbox.b.slide").gesture.action()
                    == SlideControlGesture.Action.BASE);
            if (ready[0]) return;
            SystemClock.sleep(20);
        } while (SystemClock.uptimeMillis() < until);
        throw new AssertionError("LongPress did not reach BASE before timeout");
    }
    private void feedbackRules() throws Exception {
        var events = new java.util.ArrayList<ScreenControlFeedback.Event>();
        boolean[] fail = {false};
        ScreenControlFeedback original = surface.controls.feedback;
        main(() -> surface.controls.feedback = new ScreenControlFeedback(new ScreenControlFeedback.Backend() {
            public boolean available() { return true; }
            public int apiLevel() { return 34; }
            public void predefinedClick(ScreenControlFeedback.Event event) { throw new AssertionError("B must not use EFFECT_CLICK"); }
            public void oneShot(ScreenControlFeedback.Event event, long ms, int amplitude) {
                if (ms != 10 || amplitude != 255) throw new AssertionError("expected fixed 10ms/255");
                events.add(event); if (fail[0]) throw new IllegalStateException("test haptic failure");
            }
            public void legacy(ScreenControlFeedback.Event event, long ms) { throw new AssertionError("unexpected legacy"); }
        }));
        try {
            event(MotionEvent.ACTION_DOWN, 220, 680, false);
            main(() -> check(events.equals(List.of(ScreenControlFeedback.Event.PRESS)), "control DOWN feedback once"));
            event(MotionEvent.ACTION_MOVE, 220, 679.5f, false);
            main(() -> check(events.size() == 1, "below threshold no feedback"));
            event(MotionEvent.ACTION_MOVE, 220, 650, false);
            main(() -> check(events.size() == 2 && events.get(1) == ScreenControlFeedback.Event.DIRECTION_COMMIT,
                    "first Up direction feedback"));
            event(MotionEvent.ACTION_MOVE, 220, 720, false);
            main(() -> check(events.size() == 2, "locked direction no repeated feedback"));
            event(MotionEvent.ACTION_UP, 220, 720, false);
            main(() -> check(events.size() == 2, "UP no feedback"));
            event(MotionEvent.ACTION_DOWN, 220, 680, false);
            event(MotionEvent.ACTION_MOVE, 220, 710, false);
            event(MotionEvent.ACTION_MOVE, 220, 740, false);
            main(() -> check(events.size() == 4 && events.get(3) == ScreenControlFeedback.Event.DIRECTION_COMMIT,
                    "Down direction feedback once"));
            event(MotionEvent.ACTION_CANCEL, 220, 740, false);
            main(() -> check(events.size() == 4, "CANCEL no feedback"));
            for (int y : new int[] {650, 710}) {
                event(MotionEvent.ACTION_DOWN, 220, 680, false);
                int before = events.size();
                waitForBase();
                main(() -> check(events.size() == before && surface.controls.instances.get("xbox.b.slide")
                        .gesture.action() == SlideControlGesture.Action.BASE, "LongPress no feedback"));
                event(MotionEvent.ACTION_MOVE, 220, y, false);
                main(() -> check(events.size() == before + 1 && events.get(before) == ScreenControlFeedback.Event.DIRECTION_COMMIT,
                        "Design A direction feedback"));
                event(MotionEvent.ACTION_UP, 220, y, false);
            }
            event(MotionEvent.ACTION_DOWN, 220, 680, false);
            int tapCount = events.size();
            event(MotionEvent.ACTION_UP, 220, 680, false);
            SystemClock.sleep(60);
            main(() -> check(events.size() == tapCount && neutral(), "tap pulse and expiry no feedback"));
            event(MotionEvent.ACTION_DOWN, 220, 680, false);
            int releaseCount = events.size();
            event(MotionEvent.ACTION_UP, 220, 640, false);
            main(() -> check(events.size() == releaseCount, "direction resolved on UP has no feedback"));
            int outsideCount = events.size();
            event(MotionEvent.ACTION_DOWN, 600, 1200, false);
            event(MotionEvent.ACTION_MOVE, 610, 1200, false);
            event(MotionEvent.ACTION_UP, 610, 1200, false);
            main(() -> check(events.size() == outsideCount, "Mouse owner no Screen Control feedback"));
            float[] power = new float[2];
            main(() -> { try {
                Field x = TouchCaptureView.class.getDeclaredField("powerCenterX"); x.setAccessible(true);
                Field y = TouchCaptureView.class.getDeclaredField("powerCenterY"); y.setAccessible(true);
                power[0] = x.getFloat(surface); power[1] = y.getFloat(surface);
            } catch (Exception e) { throw new AssertionError(e); } });
            event(MotionEvent.ACTION_DOWN, power[0], power[1], false);
            event(MotionEvent.ACTION_CANCEL, power[0], power[1], false);
            main(() -> check(events.size() == outsideCount, "Power owner no Screen Control feedback"));
            openEditor();
            drag(220, 680, 10, 10);
            drag(337, 690, 10, 0);
            main(() -> { field("X").setText("100"); button("Reset").performClick(); button("Cancel").performClick(); });
            openEditor();
            main(() -> button("Save").performClick());
            main(() -> check(events.size() == outsideCount, "Settings/editor move resize numeric Save Cancel Reset no feedback"));
            main(() -> fail[0] = true);
            event(MotionEvent.ACTION_DOWN, 220, 680, false);
            event(MotionEvent.ACTION_MOVE, 220, 650, false);
            main(() -> {
                check(surface.controls.instances.get("xbox.b.slide").gesture.action() == SlideControlGesture.Action.SLIDE_UP,
                        "failed feedback preserves logical Y");
                try {
                    Field field = ScreenControls.class.getDeclaredField("gamepad"); field.setAccessible(true);
                    Field published = GamepadAggregator.class.getDeclaredField("published"); published.setAccessible(true);
                    check(((GamepadState) published.get(field.get(surface.controls))).buttons() == GamepadState.Y,
                            "failed feedback preserves full gamepad Y");
                } catch (Exception e) { throw new AssertionError(e); }
            });
            event(MotionEvent.ACTION_CANCEL, 220, 650, false);
            main(() -> check(neutral(), "failed feedback preserves safety Neutral"));
        } finally { main(() -> { surface.stopCapture("feedback_smoke_complete"); surface.controls.feedback = original; }); }
    }
    private void launch() throws Exception {
        ActivityMonitor monitor = addMonitor(MainActivity.class.getName(), null, false);
        // HyperOS blocks an instrumentation process starting an Activity from the background.
        // The shell launch follows the same authorized ADB path as deployment.
        try (android.os.ParcelFileDescriptor pipe = getUiAutomation().executeShellCommand(
                "am start -n com.rightpad.capture/.MainActivity")) {
            try (java.io.FileInputStream input = new java.io.FileInputStream(pipe.getFileDescriptor())) {
                while (input.read() != -1) { }
            }
        }
        activity = (MainActivity) waitForMonitorWithTimeout(monitor, 10000);
        removeMonitor(monitor);
        check(activity != null, "Activity launched");
        acquireSurface();
    }
    private void acquireSurface() {
        waitForIdleSync();
        main(() -> surface = (TouchCaptureView) ((ViewGroup) ((ViewGroup) activity.findViewById(android.R.id.content))
                .getChildAt(0)).getChildAt(0));
        SystemClock.sleep(500);
    }
    private boolean neutral() { return surface.controls.instances.values().stream().allMatch(ScreenControlInstance::neutral); }
    private ControlRect rect() { return surface.controls.rects().get("xbox.b.slide"); }
    private ControlRect currentRect() { final ControlRect[] r = new ControlRect[1]; main(() -> r[0] = rect()); return r[0]; }
    private void check(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
        checks++;
        android.util.Log.i("RightpadSmoke", "PASS " + message);
    }
    private int pixel(ControlRect r) {
        Bitmap bitmap = Bitmap.createBitmap(surface.getWidth(), surface.getHeight(), Bitmap.Config.ARGB_8888);
        surface.draw(new Canvas(bitmap));
        int value = bitmap.getPixel(r.x + 8, r.y + 8);
        bitmap.recycle();
        return value;
    }
    private void main(Runnable action) {
        mainFailure = null;
        runOnMainSync(() -> { try { action.run(); } catch (Throwable error) { mainFailure = error; } });
        if (mainFailure != null) throw new AssertionError(mainFailure);
    }
    private long sequence() {
        long[] value = new long[1];
        main(() -> { try {
            Field sender = TouchCaptureView.class.getDeclaredField("udpSender"); sender.setAccessible(true);
            Field sequence = UdpTouchSender.class.getDeclaredField("nextSequence"); sequence.setAccessible(true);
            value[0] = sequence.getLong(sender.get(surface));
        } catch (Exception error) { throw new AssertionError(error); } });
        return value[0];
    }
    private void event(int action, float x, float y, boolean mouse) {
        main(() -> {
            long now = SystemClock.uptimeMillis();
            if (action == MotionEvent.ACTION_DOWN) downTime = now;
            MotionEvent.PointerProperties p = new MotionEvent.PointerProperties();
            p.id = 0; p.toolType = mouse ? MotionEvent.TOOL_TYPE_MOUSE : MotionEvent.TOOL_TYPE_FINGER;
            MotionEvent.PointerCoords c = new MotionEvent.PointerCoords(); c.x = x; c.y = y; c.pressure = 1; c.size = 1;
            MotionEvent event = MotionEvent.obtain(downTime, now, action, 1, new MotionEvent.PointerProperties[] {p},
                    new MotionEvent.PointerCoords[] {c}, 0, 0, 1, 1, 0, 0,
                    mouse ? InputDevice.SOURCE_MOUSE : InputDevice.SOURCE_TOUCHSCREEN, 0);
            surface.dispatchTouchEvent(event); event.recycle();
        });
        waitForIdleSync();
    }
    private void drag(float x, float y, float dx, float dy) {
        event(MotionEvent.ACTION_DOWN, x, y, true); event(MotionEvent.ACTION_MOVE, x + dx, y + dy, true);
        event(MotionEvent.ACTION_UP, x + dx, y + dy, true);
    }
    private void openEditor() throws Exception {
        float[] center = new float[2];
        main(() -> { try {
            Field x = TouchCaptureView.class.getDeclaredField("settingsCenterX"); x.setAccessible(true);
            Field y = TouchCaptureView.class.getDeclaredField("settingsCenterY"); y.setAccessible(true);
            center[0] = x.getFloat(surface); center[1] = y.getFloat(surface);
        } catch (Exception error) { throw new AssertionError(error); } });
        event(MotionEvent.ACTION_DOWN, center[0], center[1], false);
        event(MotionEvent.ACTION_UP, center[0], center[1], false);
        SystemClock.sleep(300);
        AccessibilityNodeInfo root = getUiAutomation().getRootInActiveWindow();
        List<AccessibilityNodeInfo> nodes = root.findAccessibilityNodeInfosByText("Edit Controls Layout");
        check(!nodes.isEmpty(), "Settings gear opens menu");
        check(nodes.get(0).performAction(AccessibilityNodeInfo.ACTION_CLICK), "menu entry clickable");
        waitForIdleSync(); SystemClock.sleep(200);
    }
    private View find(View view, String value, boolean description) {
        if (description && value.contentEquals(view.getContentDescription() == null ? "" : view.getContentDescription())) return view;
        if (!description && view instanceof Button && value.contentEquals(((Button) view).getText())) return view;
        if (view instanceof ViewGroup) for (int i = 0; i < ((ViewGroup) view).getChildCount(); i++) {
            View found = find(((ViewGroup) view).getChildAt(i), value, description); if (found != null) return found;
        }
        return null;
    }
    private EditText field(String name) { return (EditText) find(activity.getWindow().getDecorView(), name + " (px)", true); }
    private Button button(String name) { return (Button) find(activity.getWindow().getDecorView(), name, false); }
    private void fieldsMatch() {
        ControlRect r = rect();
        check(field("X").getText().toString().equals("" + r.x) && field("Y").getText().toString().equals("" + r.y)
                && field("Width").getText().toString().equals("" + r.width) && field("Height").getText().toString().equals("" + r.height),
                "drag synchronizes X/Y/Width/Height");
    }
    private void screenshot(String name) throws Exception {
        waitForIdleSync(); SystemClock.sleep(100);
        Bitmap bitmap = getUiAutomation().takeScreenshot();
        try (FileOutputStream output = new FileOutputStream(new File(getTargetContext().getExternalFilesDir(null), "controls-" + name + ".png"))) {
            bitmap.compress(Bitmap.CompressFormat.PNG, 100, output);
        }
        bitmap.recycle();
    }
}
