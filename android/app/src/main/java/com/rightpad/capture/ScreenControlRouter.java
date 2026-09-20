package com.rightpad.capture;

import java.util.Map;

/** Owner is selected exactly once per DOWN and never transferred mid-contact. */
final class ScreenControlRouter {
    enum Owner { SETTINGS, POWER, SCREEN_CONTROL, MOUSE, NONE }
    private Owner owner = Owner.NONE;
    private String controlId;
    private int pointerId = -1;

    Owner owner() { return owner; }
    String controlId() { return controlId; }
    Owner down(float x, float y, int pointer, boolean settings, boolean power,
            boolean mouseAllowed, Map<String, ControlRect> controls) {
        cancel();
        pointerId = pointer;
        if (settings) owner = Owner.SETTINGS;
        else if (power) owner = Owner.POWER;
        else {
            for (Map.Entry<String, ControlRect> entry : controls.entrySet()) {
                if (entry.getValue().contains(x, y)) {
                    owner = Owner.SCREEN_CONTROL;
                    controlId = entry.getKey();
                    return owner;
                }
            }
            owner = mouseAllowed ? Owner.MOUSE : Owner.NONE;
        }
        return owner;
    }
    boolean accepts(int count, int pointer, boolean secondPointerDown) {
        if (count != 1 || pointer != pointerId || secondPointerDown) {
            cancel();
            return false;
        }
        return owner != Owner.NONE;
    }
    void cancel() { owner = Owner.NONE; controlId = null; pointerId = -1; }
}
