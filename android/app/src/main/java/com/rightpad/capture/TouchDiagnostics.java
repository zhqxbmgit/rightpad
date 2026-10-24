package com.rightpad.capture;

import android.util.Log;
import java.io.Closeable;
import java.io.File;
import java.io.IOException;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicLong;

/** One explicitly requested, bounded diagnostic session. Never owns real input. */
final class TouchDiagnostics {
    static final String EXTRA = "rightpad.diagnostic.touch";
    static final int CAPACITY = 256;
    static final int MAX_RECORDS = 10_000;
    static final long MAX_NANOS = TimeUnit.SECONDS.toNanos(60);
    interface Sink extends Closeable {
        void write(TouchSample sample, long index) throws IOException;
        String description();
    }
    interface Factory { Sink open() throws IOException; }
    private record Entry(TouchSample sample, int historySize, long index,
                         long sequence, int bytes, int copies, boolean gamepad) { }
    private final ArrayBlockingQueue<Entry> queue;
    private final AtomicInteger attempted = new AtomicInteger();
    private final AtomicInteger producing = new AtomicInteger();
    private final AtomicLong sampleIndex = new AtomicLong();
    private final AtomicInteger dropped = new AtomicInteger();
    private final long deadline;
    private final int limit;
    private final Thread worker;
    private volatile String stopReason;
    private volatile boolean finished;
    private int written;

    // Disabled means no directory access, writer, queue, logger or worker creation.
    static TouchDiagnostics start(boolean enabled, File directory) {
        if (!enabled) return null;
        try {
            return new TouchDiagnostics(() -> new TouchRecordWriter(directory), CAPACITY, MAX_RECORDS, MAX_NANOS);
        } catch (RuntimeException exception) {
            Log.w("RightpadRecord", "recording_start_failed kind=" + exception.getClass().getSimpleName());
            return null;
        }
    }

    TouchDiagnostics(Factory factory, int capacity, int limit, long durationNanos) {
        queue = new ArrayBlockingQueue<>(capacity);
        this.limit = limit;
        deadline = System.nanoTime() + durationNanos;
        worker = new Thread(() -> run(factory), "RightpadTouchDiagnostic");
        worker.start();
    }

    boolean active() { return stopReason == null && !finished; }
    private boolean accepting() { return active() && attempted.get() < limit; }

    void samples(TouchSample[] samples, int historySize) {
        for (TouchSample sample : samples) {
            if (!accepting()) return;
            offer(new Entry(sample, historySize, sampleIndex.getAndIncrement(), 0, 0, 0, false));
        }
    }

    void packet(long sequence, int bytes, int copies, boolean gamepad) {
        if (accepting()) offer(new Entry(null, 0, 0, sequence, bytes, copies, gamepad));
    }

    private void offer(Entry entry) {
        if (!active()) return;
        if (System.nanoTime() >= deadline) { stop("time_limit"); return; }
        // Count attempts, including full-queue losses, to bound all detailed work.
        producing.incrementAndGet();
        try {
            int ordinal = attempted.getAndUpdate(value -> Math.min(value + 1, limit));
            if (ordinal >= limit) return;
            if (!queue.offer(entry)) dropped.incrementAndGet();
            if (!active()) queue.remove(entry);
        } finally { producing.decrementAndGet(); }
        // The worker drains admitted records at the count limit; lifecycle stop discards them.
    }

    void stop(String reason) {
        if (stopReason != null || finished) return;
        stopReason = reason;
        queue.clear();
        worker.interrupt(); // No join, flush or file operation on the caller.
    }

    private void run(Factory factory) {
        String result = "record_limit";
        Sink sink = null;
        try {
            if (!active()) return;
            sink = factory.open();
            Log.i("RightpadRecord", "recording_started path=" + sink.description()
                    + " maxRecords=" + limit + " durationNs=" + (deadline - System.nanoTime()));
            TouchSampleLogger logger = new TouchSampleLogger();
            while (active()) {
                long remaining = deadline - System.nanoTime();
                if (remaining <= 0) { result = "time_limit"; break; }
                Entry entry;
                try { entry = queue.poll(Math.min(remaining, TimeUnit.MILLISECONDS.toNanos(100)), TimeUnit.NANOSECONDS); }
                catch (InterruptedException exception) { continue; }
                if (!active()) break;
                if (entry != null) {
                    if (entry.sample != null) {
                        sink.write(entry.sample, entry.index);
                        logger.log(entry.sample, entry.historySize);
                        written++;
                    } else {
                        Log.i("RightpadUdp", (entry.gamepad ? "gamepad_sent" : "packet_sent")
                                + " sequence=" + entry.sequence + " bytes=" + entry.bytes + " copies=" + entry.copies);
                    }
                }
                if (attempted.get() >= limit && producing.get() == 0 && queue.isEmpty()) break;
            }
        } catch (IOException | RuntimeException exception) {
            result = "io_failed";
            Log.w("RightpadRecord", "recording_failed kind=" + exception.getClass().getSimpleName());
        } finally {
            // Fence producers before closing. Only this worker ever touches the sink.
            if (stopReason == null) stopReason = result;
            queue.clear();
            if (sink != null) {
                try { sink.close(); }
                catch (IOException | RuntimeException exception) {
                    Log.w("RightpadRecord", "recording_close_failed kind=" + exception.getClass().getSimpleName());
                }
            }
            Log.i("RightpadRecord", "recording_stopped reason=" + stopReason
                    + " samples=" + written + " attempted=" + Math.min(attempted.get(), limit)
                    + " dropped=" + dropped.get());
            finished = true;
        }
    }

    boolean awaitForTest(long millis) throws InterruptedException { worker.join(millis); return !worker.isAlive(); }
    int droppedForTest() { return dropped.get(); }
    String reasonForTest() { return stopReason; }
}
