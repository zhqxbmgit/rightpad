package com.rightpad.capture;

/** Project logical bits, matching the ABI-2 XboxGamepadButtons (not XInput wire masks). */
record GamepadState(int buttons, int leftTrigger, int rightTrigger,
        short leftThumbX, short leftThumbY, short rightThumbX, short rightThumbY) {
    static final int A = 1, B = 2, X = 4, Y = 8, ALLOWED_BUTTONS = 0x7fff;
    static final int DPAD_UP = 1 << 11, DPAD_DOWN = 1 << 12, DPAD_LEFT = 1 << 13, DPAD_RIGHT = 1 << 14;
    static final GamepadState NEUTRAL = new GamepadState(0);
    GamepadState(int buttons) { this(buttons, 0, 0, (short) 0, (short) 0, (short) 0, (short) 0); }
    GamepadState {
        if ((buttons & ~ALLOWED_BUTTONS) != 0 || leftTrigger < 0 || leftTrigger > 255
                || rightTrigger < 0 || rightTrigger > 255) throw new IllegalArgumentException("Invalid gamepad state");
    }
    boolean neutral() { return equals(NEUTRAL); }
}
