package com.rightpad.capture;

import android.util.Log;

import java.io.BufferedWriter;
import java.io.Closeable;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.OutputStreamWriter;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;

final class TouchRecordWriter implements Closeable {
    private static final String TAG = "RightpadRecord";
    private static final String HEADER =
            "sessionId,sampleIndex,action,source,x,y,eventTimeNs";

    private final File file;
    private BufferedWriter writer;
    private long sampleIndex;

    TouchRecordWriter(File filesDirectory) throws IOException {
        file = createRecordingFile(filesDirectory);
        writer = new BufferedWriter(new OutputStreamWriter(
                new FileOutputStream(file), StandardCharsets.UTF_8));
        writer.write(HEADER);
        writer.newLine();
        writer.flush();
        Log.i(TAG, "recording_started path=" + file.getAbsolutePath());
    }

    void write(TouchSample sample) {
        if (writer == null) {
            return;
        }

        try {
            writer.write(Long.toString(sample.sessionId));
            writer.write(',');
            writer.write(Long.toString(sampleIndex));
            writer.write(',');
            writer.write(sample.action.name());
            writer.write(',');
            writer.write(sample.historical ? "historical" : "current");
            writer.write(',');
            writer.write(Float.toString(sample.x));
            writer.write(',');
            writer.write(Float.toString(sample.y));
            writer.write(',');
            writer.write(Long.toString(sample.eventTimeNs));
            writer.newLine();
            sampleIndex++;
        } catch (IOException exception) {
            Log.e(TAG, "recording_failed path=" + file.getAbsolutePath(), exception);
            closeAfterFailure();
        }
    }

    void flush() {
        if (writer == null) {
            return;
        }

        try {
            writer.flush();
        } catch (IOException exception) {
            Log.e(TAG, "flush_failed path=" + file.getAbsolutePath(), exception);
            closeAfterFailure();
        }
    }

    @Override
    public void close() {
        if (writer == null) {
            return;
        }

        try {
            writer.close();
            Log.i(TAG, "recording_closed path=" + file.getAbsolutePath()
                    + " samples=" + sampleIndex);
        } catch (IOException exception) {
            Log.e(TAG, "close_failed path=" + file.getAbsolutePath(), exception);
        } finally {
            writer = null;
        }
    }

    private void closeAfterFailure() {
        try {
            writer.close();
        } catch (IOException ignored) {
            // The original write or flush error is already logged.
        } finally {
            writer = null;
        }
    }

    private static File createRecordingFile(File filesDirectory) throws IOException {
        String timestamp = new SimpleDateFormat("yyyyMMdd-HHmmss", Locale.US)
                .format(new Date());
        String baseName = "touch-recording-" + timestamp;

        for (int suffix = 0; ; suffix++) {
            String fileName = suffix == 0
                    ? baseName + ".csv"
                    : baseName + "-" + suffix + ".csv";
            File candidate = new File(filesDirectory, fileName);
            if (candidate.createNewFile()) {
                return candidate;
            }
        }
    }
}
