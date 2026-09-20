package com.rightpad.capture;

/** UI-thread adapter: immutable definition policy, replaceable future-contact config, one deadline callback. */
final class SlideControlLRInstance {
    interface Scheduler {
        long now();
        void postDelayed(Runnable callback, long delayMs);
        void removeCallbacks(Runnable callback);
    }
    final SlideControlLRDefinition definition;
    final SlideControlLRGesture gesture;
    private final ScreenControlFeedback feedback;
    private final Scheduler scheduler;
    private final float density;
    private SlideControlLRGesture.Config config;
    private final Runnable deadlineTick = this::tick;

    SlideControlLRInstance(SlideControlLRDefinition definition, float density,
            SlideControlLRGesture.Listener listener, ScreenControlFeedback feedback, Scheduler scheduler) {
        this.definition = definition;
        this.density = density;
        this.feedback = feedback;
        this.scheduler = scheduler;
        config = definition.config();
        gesture = new SlideControlLRGesture(listener);
    }
    void updateConfig(SlideControlLRGesture.Config next) { config = java.util.Objects.requireNonNull(next); }
    void down(float x, float y, long eventTime) {
        gesture.down(x, y, eventTime, config, density);
        schedule();
        feedback.emit(ScreenControlFeedback.Event.PRESS, definition.feedbackStyle());
    }
    void move(float x, float y, long eventTime) {
        boolean committed = gesture.move(x, y, eventTime);
        schedule();
        if (committed) feedback.emit(ScreenControlFeedback.Event.DIRECTION_COMMIT, definition.feedbackStyle());
    }
    void up(long eventTime) {
        gesture.up(eventTime, scheduler.now());
        schedule();
    }
    void cancel() {
        scheduler.removeCallbacks(deadlineTick);
        gesture.cancel();
    }
    private void tick() {
        gesture.advance(scheduler.now());
        schedule();
    }
    private void schedule() {
        scheduler.removeCallbacks(deadlineTick);
        if (gesture.deadline() != Long.MAX_VALUE)
            scheduler.postDelayed(deadlineTick, Math.max(0, gesture.deadline() - scheduler.now()));
    }
}
