package com.rightpad.capture;

import java.util.LinkedHashMap;
import java.util.Map;

/** UI-thread control contributions. One event transaction publishes one final full state. */
final class GamepadAggregator {
    interface Sink { void submit(GamepadStateSubmission submission); }
    private final Sink sink;
    private final Map<String, Integer> contributions = new LinkedHashMap<>();
    private final Map<String, Long> minimumHolds = new LinkedHashMap<>();
    private GamepadState published = GamepadState.NEUTRAL;
    private int batching;
    private boolean safety;
    GamepadAggregator(Sink sink) { this.sink = sink; }
    void action(ScreenControlDefinition definition, SlideControlGesture.Action action) {
        action(definition, action, 0);
    }
    void action(ScreenControlDefinition definition, SlideControlGesture.Action action, long minimumWireHoldMs) {
        contribute(definition.id, definition.buttonsFor(action), minimumWireHoldMs);
    }
    void contribute(String id, int buttons, long minimumWireHoldMs) {
        new GamepadStateSubmission(new GamepadState(buttons), minimumWireHoldMs, false);
        if (buttons == 0) {
            contributions.remove(id);
            minimumHolds.remove(id);
        } else {
            contributions.put(id, buttons);
            minimumHolds.put(id, minimumWireHoldMs);
        }
        publish();
    }
    void batch(Runnable operation) {
        batching++;
        try { operation.run(); }
        finally { batching--; publish(); }
    }
    void clear() { contributions.clear(); minimumHolds.clear(); publish(); }
    void safetyClear() { safety = true; clear(); }
    private void publish() {
        if (batching != 0) return;
        int buttons = 0;
        for (int contribution : contributions.values()) buttons |= contribution;
        // Phase 3 controls contribute buttons only; the reusable wire still carries every analog field.
        GamepadState next = new GamepadState(buttons);
        if (safety) {
            safety = false;
            published = GamepadState.NEUTRAL;
            // Even if logical Neutral was already published, an unsent/dwelling wire pulse may remain.
            sink.submit(GamepadStateSubmission.safety());
        } else if (!next.equals(published)) {
            long minimum = 0;
            if (!next.neutral()) for (long hold : minimumHolds.values()) minimum = Math.max(minimum, hold);
            published = next; sink.submit(new GamepadStateSubmission(next, minimum, false));
        }
    }
}
