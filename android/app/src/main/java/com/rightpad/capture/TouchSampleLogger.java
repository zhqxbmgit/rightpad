package com.rightpad.capture;

import android.util.Log;

final class TouchSampleLogger {
    private static final String TAG = "RightpadTouch";
    private long previousSessionId = -1;
    private long previousEventTimeNs;

    void log(TouchSample sample, int historySize) {
        String interval = sample.sessionId == previousSessionId
                ? Long.toString(sample.eventTimeNs - previousEventTimeNs) : "NA";
        Log.i(TAG, "session=" + sample.sessionId
                + " pointer=" + sample.pointerId
                + " action=" + sample.action
                + " source=" + (sample.historical ? "historical" : "current")
                + " x=" + sample.x + " y=" + sample.y
                + " eventTimeNs=" + sample.eventTimeNs
                + " dtNs=" + interval
                + " historySize=" + historySize);
        previousSessionId = sample.sessionId;
        previousEventTimeNs = sample.eventTimeNs;
    }

    void logInterruption(long sessionId, int pointerId, String reason) {
        // Lifecycle and rejected input are diagnostics, not fabricated MotionEvents.
        Log.i(TAG, "session=" + sessionId + " pointer=" + pointerId
                + " status=INTERRUPTED reason=" + reason);
    }
}
