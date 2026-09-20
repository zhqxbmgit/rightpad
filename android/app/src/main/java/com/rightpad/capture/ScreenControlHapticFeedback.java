package com.rightpad.capture;

import android.content.Context;
import android.os.Build;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.util.Log;

/** Local Screen Control feedback only; mouse CONFIRM and RPHF are independent. */
final class ScreenControlHapticFeedback implements ScreenControlFeedback.Backend {
    private final Context context;
    private Vibrator vibrator;
    ScreenControlHapticFeedback(Context context) { this.context = context; }
    @Override public boolean available() {
        vibrator = (Vibrator) context.getSystemService(Context.VIBRATOR_SERVICE);
        return vibrator != null && vibrator.hasVibrator();
    }
    @Override public int apiLevel() { return Build.VERSION.SDK_INT; }
    @Override public void predefinedClick(ScreenControlFeedback.Event event) {
        vibrator.vibrate(VibrationEffect.createPredefined(VibrationEffect.EFFECT_CLICK));
        log(event, "EFFECT_CLICK");
    }
    @Override public void oneShot(ScreenControlFeedback.Event event, long durationMs, int amplitude) {
        vibrator.vibrate(VibrationEffect.createOneShot(durationMs, amplitude));
        log(event, "ONE_SHOT_" + durationMs + "_" + amplitude);
    }
    @SuppressWarnings("deprecation")
    @Override public void legacy(ScreenControlFeedback.Event event, long durationMs) {
        vibrator.vibrate(durationMs);
        log(event, "LEGACY_" + durationMs);
    }
    private static void log(ScreenControlFeedback.Event event, String path) {
        Log.i("RightpadControlHaptic", "requested event=" + event + " path=" + path);
    }
}
