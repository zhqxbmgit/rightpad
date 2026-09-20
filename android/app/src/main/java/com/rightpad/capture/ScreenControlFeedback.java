package com.rightpad.capture;

/** Best-effort feedback policy, independent of gesture meaning and gamepad output. */
final class ScreenControlFeedback {
    enum Event { PRESS, DIRECTION_COMMIT }
    enum Style { STRONG_ONE_SHOT, SYSTEM_CLICK }
    interface Backend {
        boolean available();
        int apiLevel();
        void predefinedClick(Event event);
        void oneShot(Event event, long durationMs, int amplitude);
        void legacy(Event event, long durationMs);
    }
    private final Backend backend;
    ScreenControlFeedback(Backend backend) { this.backend = backend; }

    void emit(Event event) {
        emit(event, Style.STRONG_ONE_SHOT);
    }
    void emit(Event event, Style style) {
        try {
            if (!backend.available()) return;
            int api = backend.apiLevel();
            if (style == Style.SYSTEM_CLICK) {
                if (api >= 29) backend.predefinedClick(event);
                else if (api >= 26) backend.oneShot(event, 20, 120);
                else backend.legacy(event, 20);
            } else {
                if (api >= 26) backend.oneShot(event, 10, 255);
                else backend.legacy(event, 10);
            }
        } catch (RuntimeException ignored) {
            // Missing hardware, permission/service errors or failed vibration never affect input.
        }
    }
}
