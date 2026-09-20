package com.rightpad.capture;

/** Invoked only after Receiver CLICK validation. Independent of Screen Control policy. */
final class TouchpadClickFeedback {
    interface Backend {
        boolean confirm();
    }
    private final Backend backend;
    TouchpadClickFeedback(Backend backend) { this.backend = backend; }

    boolean acceptedClick() {
        try {
            return backend.confirm();
        } catch (RuntimeException ignored) {
            // Vibration is a side effect, never a prerequisite for input or future feedback.
            return false;
        }
    }
}
