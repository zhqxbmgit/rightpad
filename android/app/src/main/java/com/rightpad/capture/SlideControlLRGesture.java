package com.rightpad.capture;

/** Logical LR/Up state machine. No Android, Xbox mapping, transport or haptic dependency. */
final class SlideControlLRGesture {
    enum Action { NEUTRAL, BASE, SLIDE_LEFT, SLIDE_RIGHT, SLIDE_UP }
    enum State { IDLE, PENDING, BASE_HELD, LEFT_HELD, RIGHT_HELD, UP_HELD, TAP_PULSE }

    record Config(float leftThresholdDp, float rightThresholdDp, float upThresholdDp,
            long tapHoldMs, long longPressMs) {
        Config {
            threshold(leftThresholdDp); threshold(rightThresholdDp); threshold(upThresholdDp);
            if (tapHoldMs < 1 || tapHoldMs > 200 || longPressMs < 50 || longPressMs > 2000)
                throw new IllegalArgumentException("Invalid LR timing");
        }
        private static void threshold(float value) {
            if (!Float.isFinite(value) || value < .1f || value > 50f)
                throw new IllegalArgumentException("Invalid LR threshold");
        }
        static Config defaults() { return new Config(12f, 3f, 2f, 25, 400); }
    }
    interface Listener { void changed(Action action); }
    private final Listener listener;
    private Config snapshot;
    private State state = State.IDLE;
    private Action action = Action.NEUTRAL;
    private float downX, downY, leftPx, rightPx, upPx;
    private long deadline = Long.MAX_VALUE;

    SlideControlLRGesture(Listener listener) { this.listener = listener; }
    State state() { return state; }
    Action action() { return action; }
    boolean active() { return state != State.IDLE && state != State.TAP_PULSE; }
    long deadline() { return deadline; }
    long minimumHoldMs() { return state == State.TAP_PULSE ? snapshot.tapHoldMs() : 0; }

    void down(float x, float y, long now, Config config, float density) {
        if (!Float.isFinite(density) || density <= 0 || !Float.isFinite(50f * density))
            throw new IllegalArgumentException("Invalid density");
        java.util.Objects.requireNonNull(config);
        cancel();
        snapshot = config; // Immutable per-contact snapshot, including Tap Hold.
        leftPx = snapshot.leftThresholdDp() * density;
        rightPx = snapshot.rightThresholdDp() * density;
        upPx = snapshot.upThresholdDp() * density;
        downX = x; downY = y;
        state = State.PENDING;
        deadline = now + snapshot.longPressMs();
    }
    void advance(long now) {
        if (now < deadline) return;
        deadline = Long.MAX_VALUE;
        if (state == State.PENDING) {
            state = State.BASE_HELD;
            emit(Action.BASE);
        } else if (state == State.TAP_PULSE) {
            state = State.IDLE;
            emit(Action.NEUTRAL);
        }
    }
    boolean move(float x, float y, long now) {
        advance(now);
        if (state != State.PENDING && state != State.BASE_HELD) return false;
        float dx = x - downX, dy = y - downY;
        Action direction;
        // Horizontal priority is intentional, even when vertical displacement is larger.
        if (Math.abs(dx) >= (dx < 0 ? leftPx : rightPx))
            direction = dx < 0 ? Action.SLIDE_LEFT : Action.SLIDE_RIGHT;
        else if (dy < 0 && -dy >= upPx) direction = Action.SLIDE_UP;
        else return false; // Down movement neither commits nor cancels pending Tap/LongPress.
        emit(Action.NEUTRAL); // Design A: BASE release precedes direction press.
        state = switch (direction) {
            case SLIDE_LEFT -> State.LEFT_HELD;
            case SLIDE_RIGHT -> State.RIGHT_HELD;
            case SLIDE_UP -> State.UP_HELD;
            default -> throw new AssertionError(direction);
        };
        deadline = Long.MAX_VALUE;
        emit(direction);
        return true;
    }
    void up(long now, long outputTime) {
        if (!active()) return;
        advance(now);
        // Only MOVE detects direction, matching SlideButtonLR. UP has no new commit.
        if (state == State.PENDING) {
            state = State.TAP_PULSE;
            deadline = outputTime + snapshot.tapHoldMs();
            emit(Action.BASE);
        } else cancel();
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
