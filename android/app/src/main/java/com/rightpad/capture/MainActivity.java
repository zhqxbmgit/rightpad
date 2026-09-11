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
    private TouchRecordWriter recordWriter;
    private UdpTouchSender udpSender;
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
                udpSender.getReceiverAddress(), udpSender.getReceiverPort(),
                this::finishAndRemoveTask);
        setContentView(captureView);
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
        udpSender.setForeground(true);
        captureView.setSenderRunning(true);
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
        captureView.setSenderRunning(false);
        udpSender.setForeground(false);
        captureView.stopCapture("activity_paused");
        super.onPause();
    }

    @Override
    protected void onDestroy() {
        udpSender.close();
        if (recordWriter != null) {
            recordWriter.close();
        }
        super.onDestroy();
    }

    private void updateBattery(Intent intent) {
        int level = intent == null ? -1
                : intent.getIntExtra(BatteryManager.EXTRA_LEVEL, -1);
        int scale = intent == null ? 0
                : intent.getIntExtra(BatteryManager.EXTRA_SCALE, 0);
        captureView.setBatteryLevel(level, scale);
    }
}
