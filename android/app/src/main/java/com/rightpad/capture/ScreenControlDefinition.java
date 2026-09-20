package com.rightpad.capture;

final class ScreenControlDefinition {
    final String id, label;
    final int protocolId;
    final SlideControlGesture.Config slide;
    final SlideControlLRDefinition lr;
    final ScreenControlFeedback.Style feedbackStyle;
    private final int defaultSize;
    private final float defaultXRatio, defaultYRatio;
    private final int baseButtons, upButtons, downButtons, leftButtons, rightButtons;

    ScreenControlDefinition(String id, String label, SlideControlGesture.Config slide,
            int defaultSize, float defaultXRatio, float defaultYRatio) {
        this(id, label, slide, defaultSize, defaultXRatio, defaultYRatio, 0, 0, 0);
    }
    ScreenControlDefinition(String id, String label, SlideControlGesture.Config slide,
            int defaultSize, float defaultXRatio, float defaultYRatio, int baseButtons, int upButtons, int downButtons) {
        this(0, id, label, slide, defaultSize, defaultXRatio, defaultYRatio, baseButtons, upButtons, downButtons);
    }
    ScreenControlDefinition(int protocolId, String id, String label, SlideControlGesture.Config slide,
            int defaultSize, float defaultXRatio, float defaultYRatio, int baseButtons, int upButtons, int downButtons) {
        this(protocolId, id, label, slide, defaultSize, defaultXRatio, defaultYRatio,
                baseButtons, upButtons, downButtons, ScreenControlFeedback.Style.STRONG_ONE_SHOT);
    }
    ScreenControlDefinition(int protocolId, String id, String label, SlideControlGesture.Config slide,
            int defaultSize, float defaultXRatio, float defaultYRatio, int baseButtons, int upButtons, int downButtons,
            ScreenControlFeedback.Style feedbackStyle) {
        this(protocolId, id, label, slide, null, defaultSize, defaultXRatio, defaultYRatio,
                baseButtons, upButtons, downButtons, 0, 0, feedbackStyle);
    }
    static ScreenControlDefinition lr(SlideControlLRDefinition lr, int size, float xRatio, float yRatio,
            int base, int left, int right, int up) {
        return new ScreenControlDefinition(ControlConfigProtocol.X_ID, lr.id(), lr.label(), null, lr, size, xRatio, yRatio,
                base, up, 0, left, right, lr.feedbackStyle());
    }
    private ScreenControlDefinition(int protocolId, String id, String label, SlideControlGesture.Config slide,
            SlideControlLRDefinition lr, int defaultSize, float defaultXRatio, float defaultYRatio,
            int baseButtons, int upButtons, int downButtons, int leftButtons, int rightButtons,
            ScreenControlFeedback.Style feedbackStyle) {
        this.protocolId = protocolId;
        this.id = id;
        this.label = label;
        this.slide = slide;
        this.lr = lr;
        this.feedbackStyle = java.util.Objects.requireNonNull(feedbackStyle);
        this.defaultSize = defaultSize;
        this.defaultXRatio = defaultXRatio;
        this.defaultYRatio = defaultYRatio;
        this.baseButtons = new GamepadState(baseButtons).buttons();
        this.upButtons = new GamepadState(upButtons).buttons();
        this.downButtons = new GamepadState(downButtons).buttons();
        this.leftButtons = new GamepadState(leftButtons).buttons();
        this.rightButtons = new GamepadState(rightButtons).buttons();
    }
    int buttonsFor(SlideControlLRGesture.Action action) {
        return switch (action) {
            case BASE -> baseButtons;
            case SLIDE_LEFT -> leftButtons;
            case SLIDE_RIGHT -> rightButtons;
            case SLIDE_UP -> upButtons;
            case NEUTRAL -> 0;
        };
    }
    int buttonsFor(SlideControlGesture.Action action) {
        return switch (action) {
            case BASE -> baseButtons;
            case SLIDE_UP -> upButtons;
            case SLIDE_DOWN -> downButtons;
            case NEUTRAL -> 0;
        };
    }
    ControlRect defaultRect(int width, int height) {
        int size = Math.max(ControlRect.MIN_SIZE, Math.min(defaultSize, Math.min(width, height)));
        return new ControlRect(Math.round(width * defaultXRatio), Math.round(height * defaultYRatio),
                size, size).clamp(width, height);
    }
}
