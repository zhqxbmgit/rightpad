package com.rightpad.capture;

import android.annotation.SuppressLint;
import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RadialGradient;
import android.graphics.RectF;
import android.graphics.Shader;
import android.graphics.Typeface;
import android.graphics.Insets;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowInsets;

final class TouchCaptureView extends View {
    private static final String STATUS_RUNNING = "运行中";
    private static final String STATUS_PAUSED = "已暂停";

    private final TouchSampleLogger logger = new TouchSampleLogger();
    private final TouchRecordWriter recordWriter;
    private final UdpTouchSender udpSender;
    private final String receiverAddress;
    private final Runnable requestExit;
    private final float density;
    private final float scaledDensity;

    private final Paint backgroundPaint = fillPaint();
    private final Paint ambientPaint = fillPaint();
    private final Paint topArcPaint = fillPaint();
    private final Paint topArcStrokePaint = strokePaint();
    private final Paint bottomBackPaint = fillPaint();
    private final Paint bottomMiddlePaint = fillPaint();
    private final Paint bottomFrontPaint = fillPaint();
    private final Paint statusGlowPaint = fillPaint();
    private final Paint statusFillPaint = fillPaint();
    private final Paint statusStrokePaint = strokePaint();
    private final Paint wifiPaint = strokePaint();
    private final Paint statusTextPaint = fillPaint();
    private final Paint addressTextPaint = fillPaint();
    private final Paint settingsFillPaint = fillPaint();
    private final Paint settingsStrokePaint = strokePaint();
    private final Paint gearPaint = strokePaint();
    private final Paint batteryOutlinePaint = strokePaint();
    private final Paint batteryTerminalPaint = fillPaint();
    private final Paint batteryFillPaint = fillPaint();
    private final Paint batteryTextPaint = fillPaint();
    private final Paint powerFillPaint = fillPaint();
    private final Paint powerIconPaint = strokePaint();

    private final Path bottomBackPath = new Path();
    private final Path bottomMiddlePath = new Path();
    private final Path bottomFrontPath = new Path();
    private final Path gearPath = new Path();
    private final RectF wifiOuterArc = new RectF();
    private final RectF wifiInnerArc = new RectF();
    private final RectF batteryBody = new RectF();
    private final RectF batteryTerminal = new RectF();
    private final RectF batteryFill = new RectF();
    private final RectF powerIconArc = new RectF();
    private final PowerGestureTracker powerGesture = new PowerGestureTracker();

    private int activePointerId = MotionEvent.INVALID_POINTER_ID;
    private long sessionId;
    private int safeInsetTop;
    private int safeInsetLeft;
    private int safeInsetRight;
    private boolean senderRunning;
    private float statusCenterX;
    private float statusCenterY;
    private float statusRadius;
    private float statusTextX;
    private float statusTitleBaseline;
    private float addressBaseline;
    private float settingsCenterX;
    private float settingsCenterY;
    private float settingsRadius;
    private float wifiDotY;
    private float wifiDotRadius;
    private float statusGlowRadius;
    private float gearHoleRadius;
    private float batteryTextX;
    private float batteryTextBaseline;
    private float powerCenterX;
    private float powerCenterY;
    private float powerRadius;
    private float powerLineTop;
    private int batteryPercent = BatteryDisplay.UNKNOWN_PERCENT;
    private String batteryText = BatteryDisplay.text(BatteryDisplay.UNKNOWN_PERCENT);

    TouchCaptureView(Context context, TouchRecordWriter recordWriter, UdpTouchSender udpSender,
            String receiverAddress, int receiverPort, Runnable requestExit) {
        super(context);
        this.recordWriter = recordWriter;
        this.udpSender = udpSender;
        this.receiverAddress = receiverAddress;
        this.requestExit = requestExit;
        density = getResources().getDisplayMetrics().density;
        scaledDensity = getResources().getDisplayMetrics().scaledDensity;
        configurePaints();
        setContentDescription(receiverAddress + ":" + receiverPort);
        setOnApplyWindowInsetsListener((view, windowInsets) -> {
            Insets cutoutSafe = windowInsets.getInsets(WindowInsets.Type.displayCutout());
            // Both controls sit outside the centered camera cutout. Preserve side
            // cutout safety without shifting the whole row below its top inset;
            // transient system bars overlay the edge-to-edge surface without a jump.
            if (safeInsetTop != 0 || safeInsetLeft != cutoutSafe.left
                    || safeInsetRight != cutoutSafe.right) {
                safeInsetTop = 0;
                safeInsetLeft = cutoutSafe.left;
                safeInsetRight = cutoutSafe.right;
                prepareGeometry(getWidth(), getHeight());
                invalidate();
            }
            return windowInsets;
        });
    }

    private static Paint fillPaint() {
        Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG | Paint.DITHER_FLAG);
        paint.setStyle(Paint.Style.FILL);
        return paint;
    }

    private static Paint strokePaint() {
        Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG | Paint.DITHER_FLAG);
        paint.setStyle(Paint.Style.STROKE);
        paint.setStrokeCap(Paint.Cap.ROUND);
        paint.setStrokeJoin(Paint.Join.ROUND);
        return paint;
    }

    private void configurePaints() {
        statusFillPaint.setColor(Color.rgb(7, 48, 43));
        statusStrokePaint.setColor(Color.rgb(7, 151, 105));
        statusStrokePaint.setStrokeWidth(dp(0.8f));
        wifiPaint.setColor(Color.rgb(32, 249, 174));
        wifiPaint.setStrokeWidth(dp(3f));

        statusTextPaint.setColor(Color.rgb(241, 245, 250));
        statusTextPaint.setTextSize(sp(15.5f));
        statusTextPaint.setTypeface(Typeface.create("sans-serif-medium", Typeface.NORMAL));
        addressTextPaint.setColor(Color.rgb(139, 151, 177));
        addressTextPaint.setTextSize(sp(12f));
        addressTextPaint.setTypeface(Typeface.create("sans-serif", Typeface.NORMAL));

        settingsFillPaint.setColor(Color.argb(128, 31, 41, 58));
        settingsStrokePaint.setColor(Color.argb(112, 76, 89, 112));
        settingsStrokePaint.setStrokeWidth(dp(0.7f));
        gearPaint.setColor(Color.rgb(213, 220, 234));
        gearPaint.setStrokeWidth(dp(2f));
        batteryOutlinePaint.setColor(Color.rgb(170, 181, 201));
        batteryOutlinePaint.setStrokeWidth(dp(1f));
        batteryTerminalPaint.setColor(Color.rgb(170, 181, 201));
        batteryFillPaint.setColor(Color.rgb(0, 240, 181));
        batteryTextPaint.setColor(Color.rgb(164, 174, 194));
        batteryTextPaint.setTextSize(sp(13f));
        batteryTextPaint.setTypeface(Typeface.create("sans-serif-medium", Typeface.NORMAL));
        powerFillPaint.setColor(Color.rgb(252, 26, 23));
        powerIconPaint.setColor(Color.WHITE);
        powerIconPaint.setStrokeWidth(dp(2.1f));
        topArcStrokePaint.setColor(Color.argb(90, 66, 82, 111));
        topArcStrokePaint.setStrokeWidth(dp(0.45f));
    }

    @Override
    protected void onAttachedToWindow() {
        super.onAttachedToWindow();
        requestApplyInsets();
    }

    @Override
    protected void onSizeChanged(int width, int height, int oldWidth, int oldHeight) {
        super.onSizeChanged(width, height, oldWidth, oldHeight);
        prepareGeometry(width, height);
    }

    private void prepareGeometry(int width, int height) {
        if (width <= 0 || height <= 0) return;

        backgroundPaint.setShader(new LinearGradient(0f, 0f, width * 0.72f, height,
                new int[] {Color.rgb(25, 33, 48), Color.rgb(13, 19, 31), Color.rgb(6, 10, 17)},
                new float[] {0f, 0.48f, 1f}, Shader.TileMode.CLAMP));
        ambientPaint.setShader(new RadialGradient(width * 0.37f, height * 0.12f,
                width * 0.72f,
                new int[] {Color.argb(112, 42, 57, 83), Color.argb(30, 24, 34, 52), Color.TRANSPARENT},
                new float[] {0f, 0.52f, 1f}, Shader.TileMode.CLAMP));

        float topArcX = width * 1.14f;
        float topArcY = -width * 0.39f;
        float topArcRadius = width * 0.67f;
        topArcPaint.setShader(new RadialGradient(width * 0.72f, -width * 0.04f,
                width * 0.76f,
                new int[] {Color.rgb(43, 59, 86), Color.rgb(24, 35, 55), Color.rgb(11, 17, 28)},
                new float[] {0f, 0.58f, 1f}, Shader.TileMode.CLAMP));
        topArcPaint.setAlpha(205);
        topArcStrokePaint.setShader(null);
        topArcStrokePaint.setColor(Color.argb(86, 69, 87, 119));

        bottomBackPaint.setShader(new LinearGradient(0f, height * 0.72f, width * 0.62f, height,
                Color.rgb(18, 27, 42), Color.rgb(8, 13, 22), Shader.TileMode.CLAMP));
        bottomBackPaint.setAlpha(170);
        bottomMiddlePaint.setShader(new LinearGradient(0f, height * 0.83f, width * 0.73f, height,
                Color.rgb(18, 27, 43), Color.rgb(9, 15, 25), Shader.TileMode.CLAMP));
        bottomMiddlePaint.setAlpha(145);
        bottomFrontPaint.setShader(new LinearGradient(0f, height * 0.85f, width * 0.52f, height,
                Color.rgb(19, 28, 44), Color.rgb(11, 18, 29), Shader.TileMode.CLAMP));
        bottomFrontPaint.setAlpha(160);

        bottomBackPath.reset();
        bottomBackPath.moveTo(0f, height * 0.727f);
        bottomBackPath.cubicTo(width * 0.19f, height * 0.765f,
                width * 0.13f, height * 0.875f, width * 0.31f, height * 0.914f);
        bottomBackPath.cubicTo(width * 0.48f, height * 0.953f,
                width * 0.58f, height * 0.962f, width * 0.72f, height);
        bottomBackPath.lineTo(0f, height);
        bottomBackPath.close();

        bottomMiddlePath.reset();
        bottomMiddlePath.moveTo(0f, height * 0.856f);
        bottomMiddlePath.cubicTo(width * 0.22f, height * 0.850f,
                width * 0.36f, height * 0.902f, width * 0.51f, height * 0.953f);
        bottomMiddlePath.cubicTo(width * 0.57f, height * 0.973f,
                width * 0.62f, height * 0.988f, width * 0.66f, height);
        bottomMiddlePath.lineTo(0f, height);
        bottomMiddlePath.close();

        bottomFrontPath.reset();
        bottomFrontPath.moveTo(0f, height * 0.861f);
        bottomFrontPath.cubicTo(width * 0.18f, height * 0.858f,
                width * 0.35f, height * 0.903f, width * 0.50f, height);
        bottomFrontPath.lineTo(0f, height);
        bottomFrontPath.close();

        float top = Math.max(safeInsetTop, dp(5f)) + dp(14.5f);
        statusRadius = dp(19f);
        statusCenterX = safeInsetLeft + dp(24f) + statusRadius;
        statusCenterY = top + dp(25f);
        statusTextX = statusCenterX + statusRadius + dp(10.5f);
        statusTitleBaseline = top + dp(20f);
        addressBaseline = top + dp(40f);

        settingsRadius = dp(19f);
        powerRadius = settingsRadius;
        TopToolLayout toolLayout = TopToolLayout.create(width, safeInsetRight, density,
                settingsRadius, batteryTextPaint.measureText("100%"));
        settingsCenterX = toolLayout.settingsCenterX;
        settingsCenterY = statusCenterY;
        powerCenterX = toolLayout.powerCenterX;
        powerCenterY = statusCenterY;
        batteryTextX = toolLayout.percentTextX;
        batteryTextBaseline = statusCenterY
                - (batteryTextPaint.ascent() + batteryTextPaint.descent()) * 0.5f;
        float batteryHalfHeight = dp(6f);
        batteryBody.set(toolLayout.batteryBodyLeft, statusCenterY - batteryHalfHeight,
                toolLayout.batteryBodyRight, statusCenterY + batteryHalfHeight);
        batteryTerminal.set(batteryBody.right + dp(1.5f), statusCenterY - dp(3f),
                batteryBody.right + dp(3.5f), statusCenterY + dp(3f));
        updateBatteryFillGeometry();
        wifiOuterArc.set(statusCenterX - dp(13f), statusCenterY - dp(11.5f),
                statusCenterX + dp(13f), statusCenterY + dp(11.5f));
        wifiInnerArc.set(statusCenterX - dp(8f), statusCenterY - dp(5f),
                statusCenterX + dp(8f), statusCenterY + dp(8.5f));
        wifiDotY = statusCenterY + dp(9.5f);
        wifiDotRadius = dp(2.6f);
        statusGlowRadius = dp(32f);
        statusGlowPaint.setShader(new RadialGradient(statusCenterX, statusCenterY, statusGlowRadius,
                Color.argb(76, 20, 224, 154), Color.TRANSPARENT, Shader.TileMode.CLAMP));

        prepareGearPath(settingsCenterX, settingsCenterY, dp(9.6f), dp(7.3f));
        gearHoleRadius = dp(3.7f);
        float powerIconRadius = dp(8f);
        powerIconArc.set(powerCenterX - powerIconRadius, powerCenterY - powerIconRadius,
                powerCenterX + powerIconRadius, powerCenterY + powerIconRadius);
        powerLineTop = powerCenterY - dp(10f);

        topArcGeometryX = topArcX;
        topArcGeometryY = topArcY;
        topArcGeometryRadius = topArcRadius;
    }

    private float topArcGeometryX;
    private float topArcGeometryY;
    private float topArcGeometryRadius;

    private void prepareGearPath(float centerX, float centerY, float outerRadius, float innerRadius) {
        gearPath.reset();
        for (int i = 0; i < 48; i++) {
            double angle = -Math.PI / 2.0 + i * Math.PI * 2.0 / 48.0;
            int toothPhase = i % 6;
            float radius = toothPhase == 2 || toothPhase == 3 ? outerRadius : innerRadius;
            float x = centerX + (float) Math.cos(angle) * radius;
            float y = centerY + (float) Math.sin(angle) * radius;
            if (i == 0) gearPath.moveTo(x, y); else gearPath.lineTo(x, y);
        }
        gearPath.close();
    }

    void setSenderRunning(boolean running) {
        if (senderRunning == running) return;
        senderRunning = running;
        invalidate();
    }

    void setBatteryLevel(int level, int scale) {
        int nextPercent = BatteryDisplay.percent(level, scale);
        if (batteryPercent == nextPercent) return;
        batteryPercent = nextPercent;
        batteryText = BatteryDisplay.text(nextPercent);
        switch (BatteryDisplay.colorBand(nextPercent)) {
            case BatteryDisplay.COLOR_LOW:
                batteryFillPaint.setColor(Color.rgb(211, 106, 104));
                break;
            case BatteryDisplay.COLOR_MEDIUM:
                batteryFillPaint.setColor(Color.rgb(198, 168, 93));
                break;
            default:
                batteryFillPaint.setColor(Color.rgb(0, 240, 181));
                break;
        }
        updateBatteryFillGeometry();
        invalidate();
    }

    private void updateBatteryFillGeometry() {
        float inset = dp(2f);
        float availableWidth = Math.max(0f, batteryBody.width() - inset * 2f);
        float fraction = batteryPercent == BatteryDisplay.UNKNOWN_PERCENT
                ? 0f : batteryPercent / 100f;
        batteryFill.set(batteryBody.left + inset, batteryBody.top + inset,
                batteryBody.left + inset + availableWidth * fraction, batteryBody.bottom - inset);
    }

    @Override
    protected void onDraw(Canvas canvas) {
        super.onDraw(canvas);
        canvas.drawRect(0f, 0f, getWidth(), getHeight(), backgroundPaint);
        canvas.drawRect(0f, 0f, getWidth(), getHeight(), ambientPaint);
        canvas.drawCircle(topArcGeometryX, topArcGeometryY, topArcGeometryRadius, topArcPaint);
        canvas.drawCircle(topArcGeometryX, topArcGeometryY, topArcGeometryRadius, topArcStrokePaint);
        canvas.drawPath(bottomBackPath, bottomBackPaint);
        canvas.drawPath(bottomMiddlePath, bottomMiddlePaint);
        canvas.drawPath(bottomFrontPath, bottomFrontPaint);

        canvas.drawCircle(statusCenterX, statusCenterY, statusGlowRadius, statusGlowPaint);
        canvas.drawCircle(statusCenterX, statusCenterY, statusRadius, statusFillPaint);
        canvas.drawCircle(statusCenterX, statusCenterY, statusRadius, statusStrokePaint);
        canvas.drawArc(wifiOuterArc, 205f, 130f, false, wifiPaint);
        canvas.drawArc(wifiInnerArc, 210f, 120f, false, wifiPaint);
        canvas.drawCircle(statusCenterX, wifiDotY, wifiDotRadius, wifiPaint);

        canvas.drawText(senderRunning ? STATUS_RUNNING : STATUS_PAUSED,
                statusTextX, statusTitleBaseline, statusTextPaint);
        canvas.drawText(receiverAddress, statusTextX, addressBaseline, addressTextPaint);

        canvas.drawRoundRect(batteryBody, dp(2f), dp(2f), batteryOutlinePaint);
        canvas.drawRoundRect(batteryTerminal, dp(1f), dp(1f), batteryTerminalPaint);
        if (batteryFill.width() > 0f) {
            canvas.drawRoundRect(batteryFill, dp(1f), dp(1f), batteryFillPaint);
        }
        canvas.drawText(batteryText, batteryTextX, batteryTextBaseline, batteryTextPaint);

        canvas.drawCircle(settingsCenterX, settingsCenterY, settingsRadius, settingsFillPaint);
        canvas.drawCircle(settingsCenterX, settingsCenterY, settingsRadius, settingsStrokePaint);
        canvas.drawPath(gearPath, gearPaint);
        canvas.drawCircle(settingsCenterX, settingsCenterY, gearHoleRadius, gearPaint);
        canvas.drawCircle(powerCenterX, powerCenterY, powerRadius, powerFillPaint);
        canvas.drawArc(powerIconArc, -44f, 268f, false, powerIconPaint);
        canvas.drawLine(powerCenterX, powerLineTop, powerCenterX, powerCenterY - dp(1f),
                powerIconPaint);
        // TODO: A settings UI needs an explicit input-mode switch and touch arbitration;
        // this affordance intentionally remains part of the full-screen capture surface.
    }

    private float dp(float value) {
        return value * density;
    }

    private float sp(float value) {
        return value * scaledDensity;
    }

    // This surface records touch facts; it deliberately has no click/gesture action.
    @SuppressLint("ClickableViewAccessibility")
    @Override
    public boolean onTouchEvent(MotionEvent event) {
        int action = event.getActionMasked();

        if (action == MotionEvent.ACTION_DOWN
                && powerGesture.onDown(event.getX(), event.getY(), event.getEventTime(),
                        powerCenterX, powerCenterY, powerRadius)) {
            stopCapture("power_gesture");
            return true;
        }
        if (powerGesture.isClaimed()) {
            if (event.getPointerCount() != 1 || action == MotionEvent.ACTION_POINTER_DOWN) {
                powerGesture.onCancel();
            } else if (action == MotionEvent.ACTION_MOVE) {
                float maximumMovement = dp(8f);
                int historySize = event.getHistorySize();
                for (int i = 0; i < historySize; i++) {
                    powerGesture.onMove(event.getHistoricalX(i), event.getHistoricalY(i),
                            maximumMovement);
                }
                powerGesture.onMove(event.getX(), event.getY(), maximumMovement);
            } else if (action == MotionEvent.ACTION_UP) {
                powerGesture.onMove(event.getX(), event.getY(), dp(8f));
                if (powerGesture.onUp(event.getX(), event.getY(), event.getEventTime(),
                        powerCenterX, powerCenterY, powerRadius, 300L)) {
                    requestExit.run();
                }
            } else if (action == MotionEvent.ACTION_CANCEL) {
                powerGesture.onCancel();
            }
            return true;
        }

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
