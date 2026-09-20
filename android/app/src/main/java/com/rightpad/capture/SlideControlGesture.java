package com.rightpad.capture;

/** Logical states only: no Android, transport, Xbox or mouse dependency. */
final class SlideControlGesture {
    enum Action { NEUTRAL, BASE, SLIDE_UP, SLIDE_DOWN }
    enum State { IDLE, PENDING, BASE_HELD, UP_HELD, DOWN_HELD, TAP_PULSE }

    static final class Config {
        final float upThresholdPx, downThresholdPx;
        final long tapHoldMs, longPressMs;
        Config(float upThresholdPx, float downThresholdPx, long tapHoldMs, long longPressMs) {
            if (!Float.isFinite(upThresholdPx) || !Float.isFinite(downThresholdPx)
                    || upThresholdPx <= 0 || downThresholdPx <= 0
                    || tapHoldMs <= 0 || longPressMs <= 0) {
                throw new IllegalArgumentException("Invalid slide configuration");
            }
            this.upThresholdPx = upThresholdPx;
            this.downThresholdPx = downThresholdPx;
            this.tapHoldMs = tapHoldMs;
            this.longPressMs = longPressMs;
        }
        static Config phaseOne(float density) { return new Config(.7f * density, 3f * density, 25, 400); }
    }

    interface Listener { void changed(Action action); }
    private final Listener listener;
    private Config snapshot;
    private State state = State.IDLE;
    private Action action = Action.NEUTRAL;
    private float downY;
    private long deadline = Long.MAX_VALUE;

    SlideControlGesture(Listener listener) { this.listener = listener; }
    State state() { return state; }
    Action action() { return action; }
    boolean active() { return state != State.IDLE && state != State.TAP_PULSE; }
    long deadline() { return deadline; }
    long minimumWireHoldMs() { return state == State.TAP_PULSE ? snapshot.tapHoldMs : 0; }

    void down(float y, long now, Config config) {
        cancel(); // A new contact replaces a still-running tap pulse; no pulse queue.
        snapshot = new Config(config.upThresholdPx, config.downThresholdPx,
                config.tapHoldMs, config.longPressMs);
        downY = y;
        state = State.PENDING;
        deadline = now + snapshot.longPressMs;
    }
    void advance(long now) {
        if (now < deadline) return;
        if (state == State.PENDING) {
            state = State.BASE_HELD;
            emit(Action.BASE);
        } else if (state == State.TAP_PULSE) {
            state = State.IDLE;
            emit(Action.NEUTRAL);
        }
        deadline = Long.MAX_VALUE;
    }
    void move(float y, long now) {
        advance(now);
        if (state != State.PENDING && state != State.BASE_HELD) return;
        float dy = y - downY;
        if (dy <= -snapshot.upThresholdPx || dy >= snapshot.downThresholdPx) {
            // Design A: release BASE before the new direction; direction then locks.
            emit(Action.NEUTRAL);
            state = dy < 0 ? State.UP_HELD : State.DOWN_HELD;
            emit(dy < 0 ? Action.SLIDE_UP : Action.SLIDE_DOWN);
            deadline = Long.MAX_VALUE;
        }
    }
    void up(float y, long now) {
        up(y, now, now);
    }
    void up(float y, long now, long outputTime) {
        if (!active()) return;
        move(y, now);
        if (state == State.PENDING) {
            state = State.TAP_PULSE;
            emit(Action.BASE);
            deadline = outputTime + snapshot.tapHoldMs;
        } else {
            cancel();
        }
    }
    void cancel() {
        state = State.IDLE;
        deadline = Long.MAX_VALUE;
        emit(Action.NEUTRAL);
    }
    private void emit(Action next) {
        if (action == next) return;
        action = next;
        listener.changed(next);
    }
}
