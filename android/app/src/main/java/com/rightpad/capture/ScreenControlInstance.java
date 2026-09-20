package com.rightpad.capture;

final class ScreenControlInstance {
    interface Listener { void changed(int buttons, long minimumHoldMs); }
    final ScreenControlDefinition definition;
    final SlideControlGesture gesture;
    final SlideControlLRGesture lrGesture;

    ScreenControlInstance(ScreenControlDefinition definition, Listener listener) {
        this.definition = definition;
        if (definition.lr == null) {
            lrGesture = null;
            gesture = new SlideControlGesture(action -> listener.changed(definition.buttonsFor(action), minimumHoldMs()));
        } else {
            gesture = null;
            lrGesture = new SlideControlLRGesture(action -> listener.changed(definition.buttonsFor(action), minimumHoldMs()));
        }
    }
    boolean active() { return lrGesture == null ? gesture.active() : lrGesture.active(); }
    boolean neutral() { return lrGesture == null ? gesture.action() == SlideControlGesture.Action.NEUTRAL
            : lrGesture.action() == SlideControlLRGesture.Action.NEUTRAL; }
    boolean directionActive() { return lrGesture == null
            ? gesture.action() == SlideControlGesture.Action.SLIDE_UP || gesture.action() == SlideControlGesture.Action.SLIDE_DOWN
            : lrGesture.action() == SlideControlLRGesture.Action.SLIDE_LEFT || lrGesture.action() == SlideControlLRGesture.Action.SLIDE_RIGHT
                || lrGesture.action() == SlideControlLRGesture.Action.SLIDE_UP; }
    String actionName() { return lrGesture == null ? gesture.action().name() : lrGesture.action().name(); }
    long minimumHoldMs() { return lrGesture == null ? gesture.minimumWireHoldMs() : lrGesture.minimumHoldMs(); }
    long deadline() { return lrGesture == null ? gesture.deadline() : lrGesture.deadline(); }
    void down(float x, float y, long now, SlideControlGesture.Config config, float density) {
        down(x, y, now, config, definition.lr == null ? null : definition.lr.config(), density);
    }
    void down(float x, float y, long now, SlideControlGesture.Config config, SlideControlLRGesture.Config lrConfig, float density) {
        if (lrGesture == null) gesture.down(y, now, config);
        else lrGesture.down(x, y, now, lrConfig, density);
    }
    void move(float x, float y, long now) {
        if (lrGesture == null) gesture.move(y, now); else lrGesture.move(x, y, now);
    }
    void up(float x, float y, long now, long outputTime) {
        if (lrGesture == null) gesture.up(y, now, outputTime); else lrGesture.up(now, outputTime);
    }
    void advance(long now) { if (lrGesture == null) gesture.advance(now); else lrGesture.advance(now); }
    void cancel() { if (lrGesture == null) gesture.cancel(); else lrGesture.cancel(); }
}
