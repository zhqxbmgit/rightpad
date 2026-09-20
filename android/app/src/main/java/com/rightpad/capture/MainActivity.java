package com.rightpad.capture;

import android.app.Activity;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.os.BatteryManager;
import android.os.Bundle;
import android.util.Log;
import android.view.Window;
import android.view.WindowInsets;
import android.view.WindowInsetsController;
import android.view.WindowManager;

import java.io.IOException;

public final class MainActivity extends Activity {
    private static final String RECORD_TAG = "RightpadRecord";

    private TouchCaptureView captureView;
    private android.widget.FrameLayout root;
    private ScreenControlEditorPanel editorPanel;
    private final android.window.OnBackInvokedCallback editorBack = this::closeEditor;
    private TouchRecordWriter recordWriter;
    private UdpTouchSender udpSender;
    private ReceiverDiscoveryClient discovery;
    private HapticFeedbackListener haptics;
    private java.net.InetSocketAddress receiverTarget;
    private String receiverId;
    private boolean foreground;
    private boolean senderEnabled;
    private boolean batteryReceiverRegistered;
    private final IntentFilter batteryFilter = new IntentFilter(Intent.ACTION_BATTERY_CHANGED);
    private final BroadcastReceiver batteryReceiver = new BroadcastReceiver() {
        @Override
        public void onReceive(Context context, Intent intent) {
            updateBattery(intent);
        }
    };

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        configureEdgeToEdge();
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        try {
            recordWriter = new TouchRecordWriter(getFilesDir());
        } catch (IOException exception) {
            Log.e(RECORD_TAG, "recording_open_failed", exception);
        }
        udpSender = new UdpTouchSender();
        captureView = new TouchCaptureView(this, recordWriter, udpSender,
                this::exit, this::showSettings);
        root = new android.widget.FrameLayout(this);
        root.addView(captureView);
        setContentView(root);
        captureView.controls.boundsChanged = this::closeEditor;
        getWindow().setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_ADJUST_NOTHING);
        udpSender.enableControlRequests();
        haptics = new HapticFeedbackListener(captureView::performClickHaptic, snapshot -> {
            if (captureView.controls.config.accept(snapshot)) {
                // v1 can update B during deployment, but does not acknowledge a complete B+X config.
                udpSender.knownControlConfig(snapshot.version() == 2 ? snapshot.epoch() : 0,
                        snapshot.version() == 2 ? snapshot.revision() : 0);
                android.util.Log.i("RightpadControl", "config_accepted epoch=" + Long.toUnsignedString(snapshot.epoch(), 16)
                        + " revision=" + Long.toUnsignedString(snapshot.revision()) + " version=" + snapshot.version()
                        + " records=" + snapshot.records() + " lrRecords=" + snapshot.lrRecords());
            }
        });
        udpSender.setUpObserver(haptics::expectUp);
        discovery = new ReceiverDiscoveryClient(this, this::receiverChanged);
        updateBattery(registerReceiver(null, batteryFilter));
        applyImmersiveMode();
    }

    private void configureEdgeToEdge() {
        Window window = getWindow();
        window.setDecorFitsSystemWindows(false);
        window.setStatusBarContrastEnforced(false);
        window.setNavigationBarContrastEnforced(false);
        WindowManager.LayoutParams attributes = window.getAttributes();
        attributes.layoutInDisplayCutoutMode =
                WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_ALWAYS;
        window.setAttributes(attributes);
    }

    private void applyImmersiveMode() {
        WindowInsetsController controller = getWindow().getDecorView().getWindowInsetsController();
        if (controller != null) {
            controller.setSystemBarsAppearance(0,
                    WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS
                            | WindowInsetsController.APPEARANCE_LIGHT_NAVIGATION_BARS);
            controller.setSystemBarsBehavior(
                    WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
            controller.hide(WindowInsets.Type.statusBars()
                    | WindowInsets.Type.navigationBars());
        }
    }

    @Override
    protected void onResume() {
        super.onResume();
        applyImmersiveMode();
        if (!batteryReceiverRegistered) {
            Intent stickyBattery = registerReceiver(batteryReceiver, batteryFilter,
                    Context.RECEIVER_NOT_EXPORTED);
            batteryReceiverRegistered = true;
            updateBattery(stickyBattery);
        }
        foreground = true;
        discovery.setForeground(true); // Resume input only after a fresh Wi-Fi OFFER confirms readiness.
    }

    @Override
    public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        if (hasFocus) applyImmersiveMode();
    }

    @Override
    protected void onPause() {
        if (batteryReceiverRegistered) {
            unregisterReceiver(batteryReceiver);
            batteryReceiverRegistered = false;
        }
        foreground = false;
        haptics.setActive(null, 0);
        senderEnabled = false;
        captureView.setConnection(null);
        discovery.setForeground(false);
        udpSender.setForeground(false);
        captureView.stopCapture("activity_paused");
        super.onPause();
    }

    @Override
    protected void onDestroy() {
        haptics.close();
        discovery.close();
        udpSender.close();
        if (recordWriter != null) {
            recordWriter.close();
        }
        super.onDestroy();
    }

    private void receiverChanged(java.net.InetSocketAddress target, String id) {
        if (!foreground) return;
        if (!java.util.Objects.equals(receiverTarget, target) || !java.util.Objects.equals(receiverId, id)) {
            haptics.setActive(null, 0);
            captureView.stopCapture("receiver_changed");
            udpSender.setTarget(target);
            captureView.controls.config.reset();
            receiverTarget = target;
            receiverId = id;
        }
        boolean enable = target != null;
        if (senderEnabled != enable) {
            udpSender.setForeground(enable);
            senderEnabled = enable;
        }
        captureView.setConnection(target == null ? null : target.getAddress().getHostAddress());
        haptics.setActive(target == null ? null : target.getAddress(), udpSender.getSenderRunId());
    }

    private void exit() {
        haptics.close();
        captureView.stopCapture("power_exit");
        discovery.close();
        udpSender.close();
        finishAndRemoveTask();
    }

    private void showSettings() {
        captureView.setInputModal(true);
        android.app.AlertDialog dialog = new android.app.AlertDialog.Builder(this)
                .setTitle("Settings")
                .setItems(new String[] {"Edit Controls Layout"}, (d, which) -> openEditor())
                .create();
        dialog.setOnDismissListener(d -> {
            if (editorPanel == null) captureView.setInputModal(false);
        });
        dialog.show();
    }

    private void openEditor() {
        captureView.setInputModal(true);
        captureView.controls.layout.begin();
        editorPanel = new ScreenControlEditorPanel(this, captureView.controls, captureView, this::closeEditor);
        android.widget.FrameLayout.LayoutParams p = new android.widget.FrameLayout.LayoutParams(
                android.view.ViewGroup.LayoutParams.MATCH_PARENT, android.view.ViewGroup.LayoutParams.WRAP_CONTENT,
                android.view.Gravity.BOTTOM);
        root.addView(editorPanel, p);
        getOnBackInvokedDispatcher().registerOnBackInvokedCallback(
                android.window.OnBackInvokedDispatcher.PRIORITY_DEFAULT, editorBack);
        captureView.invalidate();
    }

    private void closeEditor() {
        if (editorPanel == null) return;
        if (captureView.controls.editing()) captureView.controls.layout.cancel();
        ((android.view.inputmethod.InputMethodManager) getSystemService(INPUT_METHOD_SERVICE))
                .hideSoftInputFromWindow(editorPanel.getWindowToken(), 0);
        root.removeView(editorPanel);
        getOnBackInvokedDispatcher().unregisterOnBackInvokedCallback(editorBack);
        editorPanel = null;
        captureView.controls.draftChanged = () -> { };
        captureView.setInputModal(false);
    }

    private void updateBattery(Intent intent) {
        int level = intent == null ? -1
                : intent.getIntExtra(BatteryManager.EXTRA_LEVEL, -1);
        int scale = intent == null ? 0
                : intent.getIntExtra(BatteryManager.EXTRA_SCALE, 0);
        captureView.setBatteryLevel(level, scale);
    }
}
