package com.rightpad.capture;

/** Logical LR defaults and feedback policy; transport mapping belongs to ScreenControlDefinition. */
record SlideControlLRDefinition(String id, String label, SlideControlLRGesture.Config config,
        ScreenControlFeedback.Style feedbackStyle) {
    SlideControlLRDefinition {
        java.util.Objects.requireNonNull(id);
        java.util.Objects.requireNonNull(label);
        java.util.Objects.requireNonNull(config);
        java.util.Objects.requireNonNull(feedbackStyle);
    }
    static SlideControlLRDefinition phaseSix() {
        return new SlideControlLRDefinition("xbox.x.slide_lr", "X", SlideControlLRGesture.Config.defaults(),
                ScreenControlFeedback.Style.SYSTEM_CLICK);
    }
}
