package com.rightpad.capture;

import java.util.Map;

/** Owner is selected exactly once per DOWN and never transferred mid-contact. */
final class ScreenControlRouter {
    static final String MODE_ID = "rightpad.input.mode";
    enum Owner { SETTINGS, POWER, MODE, SCREEN_CONTROL, MOUSE, NONE }
    enum Profile {
        NORMAL("M"), CINEMATIC("C");
        final String label;
        Profile(String label) { this.label = label; }
    }
    private Owner owner = Owner.NONE;
    private String controlId;
    private int pointerId = -1;

    Owner owner() { return owner; }
    String controlId() { return controlId; }
    // Static retained label/legacy wire identity, not a local or Receiver mode selection.
    Profile profile() { return Profile.NORMAL; }
    boolean modeUp(int count, int pointer) {
        if (!accepts(count, pointer, false) || owner != Owner.MODE) return false;
        // Reserved control consumes the matching release without invoking a business action.
        cancel();
        return true;
    }
    static String controlAt(float x, float y, Map<String, ControlRect> controls) {
        for (Map.Entry<String, ControlRect> entry : controls.entrySet())
            if (entry.getValue().contains(x, y)) return entry.getKey();
        return null;
    }
    Owner down(float x, float y, int pointer, boolean settings, boolean power,
            boolean mouseAllowed, Map<String, ControlRect> controls) {
        cancel();
        pointerId = pointer;
        if (settings) owner = Owner.SETTINGS;
        else if (power) owner = Owner.POWER;
        else {
            controlId = controlAt(x, y, controls);
            if (controlId != null) {
                owner = MODE_ID.equals(controlId) ? Owner.MODE : Owner.SCREEN_CONTROL;
                return owner;
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
