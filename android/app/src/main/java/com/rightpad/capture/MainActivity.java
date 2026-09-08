package com.rightpad.capture;

import android.app.Activity;
import android.os.Bundle;
import android.util.Log;

import java.io.IOException;

public final class MainActivity extends Activity {
    private static final String RECORD_TAG = "RightpadRecord";

    private TouchCaptureView captureView;
    private TouchRecordWriter recordWriter;
    private UdpTouchSender udpSender;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        try {
            recordWriter = new TouchRecordWriter(getFilesDir());
        } catch (IOException exception) {
            Log.e(RECORD_TAG, "recording_open_failed", exception);
        }
        udpSender = new UdpTouchSender();
        captureView = new TouchCaptureView(this, recordWriter, udpSender);
        setContentView(captureView);
    }

    @Override
    protected void onPause() {
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
}
