package com.rightpad.capture;

import android.view.HapticFeedbackConstants;
import android.view.View;

/** Android execution for accepted Touchpad clicks; no local touch recognition. */
final class TouchpadClickHapticFeedback implements TouchpadClickFeedback.Backend {
    private final View view;
    TouchpadClickHapticFeedback(View view) { this.view = view; }
    @Override public boolean confirm() {
        return view.performHapticFeedback(HapticFeedbackConstants.CONFIRM);
    }
}
