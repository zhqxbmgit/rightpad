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
    // UI-thread, Activity-lifetime state only. C1 does not shape or route Mouse differently.
    private Profile profile = Profile.NORMAL;
    private Owner owner = Owner.NONE;
    private String controlId;
    private int pointerId = -1;

    Owner owner() { return owner; }
    String controlId() { return controlId; }
    Profile profile() { return profile; }
    boolean modeUp(int count, int pointer) {
        if (!accepts(count, pointer, false) || owner != Owner.MODE) return false;
        profile = profile == Profile.NORMAL ? Profile.CINEMATIC : Profile.NORMAL;
        cancel();
        return true;
    }
    Owner down(float x, float y, int pointer, boolean settings, boolean power,
            boolean mouseAllowed, Map<String, ControlRect> controls) {
        cancel();
        pointerId = pointer;
        if (settings) owner = Owner.SETTINGS;
        else if (power) owner = Owner.POWER;
        else {
            for (Map.Entry<String, ControlRect> entry : controls.entrySet()) {
                if (entry.getValue().contains(x, y)) {
                    owner = MODE_ID.equals(entry.getKey()) ? Owner.MODE : Owner.SCREEN_CONTROL;
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
