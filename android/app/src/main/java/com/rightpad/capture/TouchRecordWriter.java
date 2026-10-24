package com.rightpad.capture;

import java.io.BufferedWriter;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.OutputStreamWriter;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;

final class TouchRecordWriter implements TouchDiagnostics.Sink {
    private static final String HEADER =
            "sessionId,sampleIndex,action,source,x,y,eventTimeNs";

    private final File file;
    private BufferedWriter writer;

    TouchRecordWriter(File filesDirectory) throws IOException {
        file = createRecordingFile(filesDirectory);
        writer = new BufferedWriter(new OutputStreamWriter(
                new FileOutputStream(file), StandardCharsets.UTF_8));
        try {
            writer.write(HEADER);
            writer.newLine();
            writer.flush();
        } catch (IOException exception) {
            try { writer.close(); } catch (IOException ignored) { }
            throw exception;
        }
    }

    @Override public void write(TouchSample sample, long sampleIndex) throws IOException {
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
    }

    @Override
    public void close() throws IOException { writer.close(); }

    @Override public String description() { return file.getAbsolutePath(); }

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
