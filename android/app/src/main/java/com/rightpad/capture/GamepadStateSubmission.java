package com.rightpad.capture;

/** Generic minimum hold used locally and encoded as Receiver minimum dwell. */
record GamepadStateSubmission(GamepadState state, long minimumWireHoldMs, boolean safetyNeutral) {
    GamepadStateSubmission {
        if (state == null || minimumWireHoldMs < 0 || minimumWireHoldMs > 65535
                || (minimumWireHoldMs != 0 && state.neutral()) || (safetyNeutral && !state.neutral())) {
            throw new IllegalArgumentException("Invalid gamepad submission");
        }
    }
    static GamepadStateSubmission ordinary(GamepadState state) { return new GamepadStateSubmission(state, 0, false); }
    static GamepadStateSubmission safety() { return new GamepadStateSubmission(GamepadState.NEUTRAL, 0, true); }
}
