package com.rightpad.capture;

import android.annotation.SuppressLint;
import android.content.Context;
import android.view.MotionEvent;
import android.view.View;

final class TouchCaptureView extends View {
    private final TouchSampleLogger logger = new TouchSampleLogger();
    private final TouchRecordWriter recordWriter;
    private final UdpTouchSender udpSender;
    private int activePointerId = MotionEvent.INVALID_POINTER_ID;
    private long sessionId;

    TouchCaptureView(Context context, TouchRecordWriter recordWriter, UdpTouchSender udpSender) {
        super(context);
        this.recordWriter = recordWriter;
        this.udpSender = udpSender;
    }

    // This surface records touch facts; it deliberately has no click/gesture action.
    @SuppressLint("ClickableViewAccessibility")
    @Override
    public boolean onTouchEvent(MotionEvent event) {
        int action = event.getActionMasked();

        if (action == MotionEvent.ACTION_DOWN) {
            stopCapture("new_down");
            if (event.getPointerCount() == 1
                    && event.getToolType(0) == MotionEvent.TOOL_TYPE_FINGER) {
                sessionId++;
                activePointerId = event.getPointerId(0);
                captureEvent(event, 0, TouchSample.Action.DOWN);
            }
            return true;
        }

        // After rejection, ignore the rest of the stream until a fresh ACTION_DOWN.
        if (activePointerId == MotionEvent.INVALID_POINTER_ID) {
            return true;
        }
        if (action == MotionEvent.ACTION_POINTER_DOWN || event.getPointerCount() != 1) {
            stopCapture("multiple_pointers");
            return true;
        }

        int pointerIndex = event.findPointerIndex(activePointerId);
        if (pointerIndex < 0) {
            stopCapture("missing_pointer");
            return true;
        }

        switch (action) {
            case MotionEvent.ACTION_MOVE:
                captureEvent(event, pointerIndex, TouchSample.Action.MOVE);
                break;
            case MotionEvent.ACTION_UP:
                captureEvent(event, pointerIndex, TouchSample.Action.UP);
                activePointerId = MotionEvent.INVALID_POINTER_ID;
                flushRecording();
                break;
            case MotionEvent.ACTION_CANCEL:
                captureEvent(event, pointerIndex, TouchSample.Action.CANCEL);
                activePointerId = MotionEvent.INVALID_POINTER_ID;
                flushRecording();
                break;
            default:
                break;
        }
        return true;
    }

    void stopCapture(String reason) {
        if (activePointerId != MotionEvent.INVALID_POINTER_ID) {
            logger.logInterruption(sessionId, activePointerId, reason);
            activePointerId = MotionEvent.INVALID_POINTER_ID;
        }
        flushRecording();
    }

    private void captureEvent(MotionEvent event, int pointerIndex, TouchSample.Action action) {
        int historySize = action == TouchSample.Action.MOVE ? event.getHistorySize() : 0;
        TouchSample[] samples = new TouchSample[historySize + 1];
        for (int i = 0; i < historySize; i++) {
            samples[i] = new TouchSample(event.getHistoricalX(pointerIndex, i),
                    event.getHistoricalY(pointerIndex, i), event.getHistoricalEventTimeNanos(i),
                    action, activePointerId, sessionId, true);
        }
        samples[historySize] = new TouchSample(event.getX(pointerIndex), event.getY(pointerIndex),
                event.getEventTimeNanos(), action, activePointerId, sessionId, false);
        for (TouchSample sample : samples) {
            recordSample(sample, event.getHistorySize());
        }
        // CANCEL remains a local fact; Protocol v2 has no CANCEL or synthetic UP.
        if (action != TouchSample.Action.CANCEL) udpSender.submit(samples);
    }

    private void recordSample(TouchSample sample, int historySize) {
        if (recordWriter != null) {
            recordWriter.write(sample);
        }
        logger.log(sample, historySize);
    }

    private void flushRecording() {
        if (recordWriter != null) {
            recordWriter.flush();
        }
    }
}
