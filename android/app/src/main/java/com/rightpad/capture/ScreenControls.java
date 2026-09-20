package com.rightpad.capture;

import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.os.SystemClock;
import android.util.Log;
import android.view.MotionEvent;
import android.view.View;
import java.io.File;
import java.io.IOException;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/** Canvas adapter and UI-thread deadline scheduling for registered controls. */
final class ScreenControls {
    final List<ScreenControlDefinition> definitions = new ArrayList<>();
    final Map<String, ScreenControlInstance> instances = new LinkedHashMap<>();
    final ScreenControlLayoutStore store;
    private final View host;
    private final Paint fill = new Paint();
    private final Paint text = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint selection = new Paint();
    private final float density;
    ScreenControlLayoutEditor layout;
    Runnable draftChanged = () -> { };
    Runnable boundsChanged = () -> { };
    private boolean editorContact;
    private final GamepadAggregator gamepad;
    ScreenControlFeedback feedback;
    final ControlConfigCache config = new ControlConfigCache();

    ScreenControls(View host, GamepadAggregator.Sink sink) {
        this.host = host;
        feedback = new ScreenControlFeedback(new ScreenControlHapticFeedback(host.getContext()));
        gamepad = new GamepadAggregator(sink);
        density = host.getResources().getDisplayMetrics().density;
        // Phase 1 registration. All routing, drawing, state, editing and storage below use IDs.
        register(new ScreenControlDefinition(ControlConfigProtocol.B_ID, "xbox.b.slide", "B",
                SlideControlGesture.Config.phaseOne(density), Math.round(64 * density), .65f, .55f,
                GamepadState.B, GamepadState.Y, GamepadState.A));
        register(ScreenControlDefinition.lr(SlideControlLRDefinition.phaseSix(), Math.round(64 * density), .15f, .55f,
                GamepadState.X, GamepadState.DPAD_LEFT, GamepadState.DPAD_RIGHT, GamepadState.DPAD_UP));
        store = new ScreenControlLayoutStore(new File(host.getContext().getFilesDir(), "screen-controls.properties"));
        text.setColor(Color.WHITE);
        text.setTextAlign(Paint.Align.CENTER);
        selection.setColor(Color.WHITE);
    }
    private void register(ScreenControlDefinition definition) {
        definitions.add(definition);
        instances.put(definition.id, new ScreenControlInstance(definition,
                (buttons, hold) -> {
                    Log.i("RightpadControl", "id=" + definition.id + " action=" + instances.get(definition.id).actionName()
                            + " logicalNs=" + System.nanoTime());
                    gamepad.contribute(definition.id, buttons, hold);
                }));
    }
    void resize(int width, int height) {
        if (width < ControlRect.MIN_SIZE || height < ControlRect.MIN_SIZE) return;
        if (layout != null && layout.width == width && layout.height == height) return;
        cancel();
        boundsChanged.run(); // A real View resize discards draft rather than saving stale coordinates.
        Map<String, ControlRect> loaded;
        try { loaded = store.load(definitions, width, height); }
        catch (IOException error) {
            Log.e("RightpadControl", "layout_load_failed", error);
            loaded = new LinkedHashMap<>();
            for (ScreenControlDefinition d : definitions) loaded.put(d.id, d.defaultRect(width, height));
        }
        layout = new ScreenControlLayoutEditor(definitions, loaded, width, height);
    }
    boolean editing() { return layout != null && layout.editing(); }
    Map<String, ControlRect> rects() {
        return layout == null ? java.util.Collections.emptyMap() : layout.layout();
    }
    void draw(Canvas canvas) {
        if (layout == null) return;
        for (ScreenControlInstance instance : instances.values()) {
            ControlRect r = rects().get(instance.definition.id);
            fill.setColor(instance.active() ? 0xFFFF3B30 : 0xFF00C853);
            canvas.drawRect(r.x, r.y, r.right(), r.bottom(), fill);
            text.setTextSize(Math.min(28 * density, Math.min(r.width, r.height) * .55f));
            canvas.drawText(instance.definition.label, r.x + r.width / 2f,
                    r.y + r.height / 2f - (text.ascent() + text.descent()) / 2, text);
        }
        if (!editing()) return;
        ControlRect r = layout.selectedRect();
        selection.setStyle(Paint.Style.STROKE);
        selection.setStrokeWidth(2);
        canvas.drawRect(r.x + 1, r.y + 1, r.right() - 1, r.bottom() - 1, selection);
        selection.setStyle(Paint.Style.FILL);
        float size = handleSize(r);
        for (ScreenControlLayoutEditor.Handle h : ScreenControlLayoutEditor.Handle.values()) {
            if (h == ScreenControlLayoutEditor.Handle.NONE || h == ScreenControlLayoutEditor.Handle.MOVE) continue;
            float[] center = ScreenControlLayoutEditor.center(r, h);
            float x = Math.max(r.x + size / 2, Math.min(center[0], r.right() - size / 2));
            float y = Math.max(r.y + size / 2, Math.min(center[1], r.bottom() - size / 2));
            canvas.drawRect(x - size / 2, y - size / 2, x + size / 2, y + size / 2, selection);
        }
    }
    private float handleSize(ControlRect r) { return Math.min(7 * density, Math.min(r.width, r.height) / 3f); }
    void down(String id, MotionEvent event) {
        ScreenControlInstance c = instances.get(id);
        var snapshot = c.definition.lr == null ? config.gesture(c.definition.protocolId, density, c.definition.slide) : null;
        if (snapshot != null) Log.i("RightpadControl", "gesture_config id=" + id + " epoch=" + Long.toUnsignedString(config.epoch(), 16)
                + " revision=" + Long.toUnsignedString(config.revision()) + " upPx=" + snapshot.upThresholdPx
                + " downPx=" + snapshot.downThresholdPx + " tapMs=" + snapshot.tapHoldMs + " longMs=" + snapshot.longPressMs);
        var lrSnapshot = c.definition.lr == null ? null : config.lrGesture(c.definition.protocolId, c.definition.lr.config());
        if (lrSnapshot != null) Log.i("RightpadControl", "gesture_config id=" + id + " epoch=" + Long.toUnsignedString(config.epoch(), 16)
                + " revision=" + Long.toUnsignedString(config.revision()) + " lr=" + lrSnapshot);
        gamepad.batch(() -> c.down(event.getX(), event.getY(), event.getEventTime(), snapshot, lrSnapshot, density));
        schedule();
        feedback.emit(ScreenControlFeedback.Event.PRESS, c.definition.feedbackStyle);
    }
    void touch(String id, MotionEvent event) {
        ScreenControlInstance control = instances.get(id);
        boolean previousDirection = control.directionActive();
        gamepad.batch(() -> {
        int action = event.getActionMasked();
        if (action == MotionEvent.ACTION_MOVE || action == MotionEvent.ACTION_UP) {
            // B keeps its existing UP-history behavior. LR commits only on MOVE,
            // including MOVE history; releasing a pending LR must not invent a direction.
            if (control.lrGesture == null || action == MotionEvent.ACTION_MOVE) {
                for (int i = 0; i < event.getHistorySize(); i++)
                    control.move(event.getHistoricalX(i), event.getHistoricalY(i), event.getHistoricalEventTime(i));
            }
            if (action == MotionEvent.ACTION_UP) control.up(event.getX(), event.getY(), event.getEventTime(), SystemClock.uptimeMillis());
            else control.move(event.getX(), event.getY(), event.getEventTime());
        } else if (action == MotionEvent.ACTION_CANCEL) {
            for (ScreenControlInstance c : instances.values()) c.cancel();
            gamepad.safetyClear();
        }
        });
        schedule();
        // Observe the committed action after publishing gamepad state. UP/history may also
        // resolve a direction logically, but release events must never produce feedback.
        if (event.getActionMasked() == MotionEvent.ACTION_MOVE && !previousDirection && control.directionActive())
            feedback.emit(ScreenControlFeedback.Event.DIRECTION_COMMIT, instances.get(id).definition.feedbackStyle);
    }
    private final Runnable deadlineTick = this::tick;
    private void tick() {
        long now = SystemClock.uptimeMillis();
        gamepad.batch(() -> { for (ScreenControlInstance c : instances.values()) c.advance(now); });
        schedule();
    }
    private void schedule() {
        host.removeCallbacks(deadlineTick);
        long deadline = Long.MAX_VALUE;
        for (ScreenControlInstance c : instances.values()) deadline = Math.min(deadline, c.deadline());
        if (deadline != Long.MAX_VALUE) host.postDelayed(deadlineTick, Math.max(0, deadline - SystemClock.uptimeMillis()));
        host.invalidate();
    }
    void cancel() {
        host.removeCallbacks(deadlineTick);
        gamepad.batch(() -> {
            for (ScreenControlInstance c : instances.values()) c.cancel();
            gamepad.safetyClear();
        });
        editorContact = false;
        if (editing()) layout.endDrag();
        host.invalidate();
    }
    void editTouch(MotionEvent event) {
        int action = event.getActionMasked();
        if (event.getPointerCount() != 1 || action == MotionEvent.ACTION_POINTER_DOWN
                || action == MotionEvent.ACTION_CANCEL) {
            editorContact = false;
            layout.endDrag();
            return;
        }
        if (action == MotionEvent.ACTION_DOWN) {
            editorContact = layout.startDrag(event.getX(), event.getY(), 7 * density);
            if (editorContact) draftChanged.run();
        } else if (editorContact && (action == MotionEvent.ACTION_MOVE || action == MotionEvent.ACTION_UP)) {
            layout.drag(event.getX(), event.getY());
            draftChanged.run();
            if (action == MotionEvent.ACTION_UP) {
                editorContact = false;
                layout.endDrag();
            }
        }
        host.invalidate();
    }
}
