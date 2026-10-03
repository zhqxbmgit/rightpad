package com.rightpad.capture;

import android.app.Activity;
import android.app.Instrumentation;
import android.os.Bundle;
import android.os.SystemClock;
import android.view.MotionEvent;
import android.view.View;
import android.view.Window;
import java.lang.reflect.Field;
import java.lang.reflect.InvocationTargetException;
import java.lang.reflect.Proxy;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import org.json.JSONArray;
import org.json.JSONObject;

/** Passive real Window observer. The host drives only the minimum M/C device smoke. */
public final class FixedAcquisitionDeviceSmoke extends Instrumentation {
    private MainActivity activity;
    private TouchCaptureView surface;
    private ScreenControlRouter router;
    private UdpTouchSender sender;
    private Window window;
    private Window.Callback original;
    private Field attachField, requestField, rootField, activeField, profileField, sequenceField;
    private final MotionEvent.PointerCoords coords = new MotionEvent.PointerCoords();
    private final long[] contacts = new long[3], samples = new long[3], resampled = new long[3], rootMoves = new long[3];
    private volatile boolean stop;
    private volatile String failure;
    private int phase, currentPhase = -1, moves, toggles;
    private long currentSamples, currentResampled;
    private static Field field(Class<?> type, String name) throws Exception {
        Field value = type.getDeclaredField(name); value.setAccessible(true); return value;
    }
    private void require(boolean value, String message) {
        if (!value) { failure = message; stop = true; }
    }
    @Override public void onCreate(Bundle arguments) { super.onCreate(arguments); start(); }
    @Override public void callActivityOnCreate(Activity target, Bundle state) {
        super.callActivityOnCreate(target, state);
        if (!(target instanceof MainActivity)) return;
        try {
            activity = (MainActivity) target;
            surface = (TouchCaptureView) field(MainActivity.class, "captureView").get(activity);
            router = (ScreenControlRouter) field(TouchCaptureView.class, "router").get(surface);
            sender = (UdpTouchSender) field(TouchCaptureView.class, "udpSender").get(surface);
            attachField = field(View.class, "mAttachInfo");
            Class<?> info = Class.forName("android.view.View$AttachInfo");
            requestField = field(info, "mUnbufferedDispatchRequested");
            rootField = field(info, "mViewRootImpl");
            activeField = field(Class.forName("android.view.ViewRootImpl"), "mUnbufferedInputDispatch");
            profileField = field(UdpTouchSender.class, "latestProfile");
            sequenceField = field(UdpTouchSender.class, "profileSequence");
            window = activity.getWindow(); original = window.getCallback();
            window.setCallback((Window.Callback) Proxy.newProxyInstance(Window.Callback.class.getClassLoader(),
                    new Class<?>[] {Window.Callback.class}, (proxy, method, args) -> {
                if (!method.getName().equals("dispatchTouchEvent")) {
                    try { return method.invoke(original, args); }
                    catch (InvocationTargetException error) { throw error.getCause(); }
                }
                MotionEvent event = (MotionEvent) args[0];
                int action = event.getActionMasked();
                var beforeOwner = router.owner();
                var beforeProfile = router.profile();
                long requests = surface.unbufferedDispatchRequestsForDiagnostics();
                long sequence = sequenceField.getLong(sender);
                Object attach = attachField.get(surface);
                Object root = attach == null ? null : rootField.get(attach);
                boolean unbuffered = root != null && activeField.getBoolean(root);
                Object result;
                try { result = method.invoke(original, args); }
                catch (InvocationTargetException error) { throw error.getCause(); }
                long delta = surface.unbufferedDispatchRequestsForDiagnostics() - requests;
                if (action == MotionEvent.ACTION_DOWN && router.owner() == ScreenControlRouter.Owner.MOUSE) {
                    require(delta == 1, "admitted DOWN request not exactly once");
                    require(attach != null && requestField.getBoolean(attach), "framework request flag absent");
                    require(surface.lastUnbufferedEventIdentityForDiagnostics() == System.identityHashCode(event),
                            "request did not use original event");
                    currentPhase = phase; moves = 0; currentSamples = currentResampled = 0;
                } else require(delta == 0, "request outside admitted Touchpad DOWN");
                boolean selected = currentPhase >= 0 && event.getPointerCount() == 1
                        && event.getToolType(0) == MotionEvent.TOOL_TYPE_FINGER
                        && (action == MotionEvent.ACTION_DOWN && router.owner() == ScreenControlRouter.Owner.MOUSE
                        || beforeOwner == ScreenControlRouter.Owner.MOUSE
                        && (action == MotionEvent.ACTION_MOVE || action == MotionEvent.ACTION_UP));
                if (selected) {
                    int history = action == MotionEvent.ACTION_MOVE ? event.getHistorySize() : 0;
                    for (int i = 0; i <= history; i++) {
                        if (i < history) event.getHistoricalPointerCoords(0, i, coords);
                        else event.getPointerCoords(0, coords);
                        currentSamples++; if (coords.isResampled()) currentResampled++;
                    }
                    if (action == MotionEvent.ACTION_MOVE) {
                        moves++; if (unbuffered) rootMoves[currentPhase]++;
                        require(unbuffered, "MOVE dispatch did not stay unbuffered");
                    }
                    if (action == MotionEvent.ACTION_UP) {
                        if (moves > 0) {
                            contacts[currentPhase]++; samples[currentPhase] += currentSamples;
                            resampled[currentPhase] += currentResampled;
                        }
                        currentPhase = -1;
                    }
                }
                if (action == MotionEvent.ACTION_CANCEL) currentPhase = -1;
                boolean committed = action == MotionEvent.ACTION_UP && beforeOwner == ScreenControlRouter.Owner.MODE
                        && beforeProfile != router.profile();
                require(sequenceField.getLong(sender) - sequence == (committed ? 1 : 0), "type7 sequence isolation failed");
                if (committed) {
                    require(phase < 2 && contacts[phase] >= (phase == 0 ? 2 : 1), "Mode changed before required contacts");
                    if (!stop) { phase++; toggles++; }
                }
                require(router.profile() == (phase == 1 ? ScreenControlRouter.Profile.CINEMATIC : ScreenControlRouter.Profile.NORMAL),
                        "M/C/M profile mismatch");
                require(profileField.getInt(sender) == (phase == 1 ? 1 : 0), "Sender profile mirror mismatch");
                if (phase == 2 && contacts[2] >= 1) stop = true;
                return result;
            }));
        } catch (Exception error) { failure = android.util.Log.getStackTraceString(error); stop = true; }
    }
    @Override public void onStart() {
        Bundle result = new Bundle();
        try {
            ActivityMonitor monitor = addMonitor(MainActivity.class.getName(), null, false);
            try (var pipe = getUiAutomation().executeShellCommand("am start -n com.rightpad.capture/.MainActivity");
                    var input = new java.io.FileInputStream(pipe.getFileDescriptor())) { while (input.read() != -1) { } }
            activity = (MainActivity) waitForMonitorWithTimeout(monitor, 10000); removeMonitor(monitor);
            require(activity != null && router.profile() == ScreenControlRouter.Profile.NORMAL, "fresh runtime not M");
            JSONObject readyData = new JSONObject();
            runOnMainSync(() -> { try {
                ControlRect rect = surface.controls.rects().get(ScreenControls.MODE_ID);
                readyData.put("modeX", rect.x + rect.width / 2); readyData.put("modeY", rect.y + rect.height / 2);
            } catch (Exception error) { throw new AssertionError(error); } });
            Bundle ready = new Bundle(); ready.putString("stream", "READY FixedAcquisitionDeviceSmoke " + readyData + "\n"); sendStatus(0, ready);
            long deadline = SystemClock.uptimeMillis() + 120000;
            while (!stop && SystemClock.uptimeMillis() < deadline) SystemClock.sleep(50);
            require(stop && toggles == 2 && contacts[0] >= 2 && contacts[1] >= 1 && contacts[2] >= 1, "incomplete device smoke");
            JSONObject data = new JSONObject();
            data.put("failure", failure == null ? JSONObject.NULL : failure); data.put("toggles", toggles);
            data.put("acquisition", "Fixed Unbuffered"); data.put("finalUi", router.profile().label);
            data.put("profileSequence", sequenceField.getLong(sender));
            data.put("actualRequestCalls", surface.unbufferedDispatchRequestsForDiagnostics());
            JSONArray phases = new JSONArray();
            for (int i = 0; i < 3; i++) {
                require(samples[i] > 0 && resampled[i] == 0, "resampled samples in fixed U phase=" + i);
                JSONObject row = new JSONObject(); row.put("profile", i == 1 ? "C" : "M"); row.put("contacts", contacts[i]);
                row.put("samples", samples[i]); row.put("resampled", resampled[i]); row.put("unbufferedRootMoves", rootMoves[i]); phases.put(row);
            }
            data.put("phases", phases); data.put("failure", failure == null ? JSONObject.NULL : failure);
            Files.write(new java.io.File(getTargetContext().getCacheDir(), "fixed-acquisition-smoke.json").toPath(), data.toString(2).getBytes(StandardCharsets.UTF_8));
            result.putString("stream", (failure == null ? "PASS" : "FAIL") + " FixedAcquisitionDeviceSmoke " + data + "\n");
        } catch (Throwable error) { failure = android.util.Log.getStackTraceString(error); result.putString("stream", "FAIL " + failure); }
        finally { if (window != null && original != null) runOnMainSync(() -> window.setCallback(original)); }
        finish(failure == null ? Activity.RESULT_OK : Activity.RESULT_CANCELED, result);
    }
}
