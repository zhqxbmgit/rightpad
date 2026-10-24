package com.rightpad.capture;

import java.io.*;
import java.net.*;
import java.nio.file.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;

/** Host JVM tests: real diagnostic CSV and loopback UDP, no Android/desktop input. */
public final class TouchDiagnosticsTests {
    private static int groups;
    private static void check(boolean value, String message) { if (!value) throw new AssertionError(message); }
    private static void pass(String message) { groups++; System.out.println("PASS " + message); }
    private static TouchSample sample(TouchSample.Action action, int n, boolean historical) {
        return new TouchSample(120.25f + n, 360.5f - n, 1_000_000_000L + n * 4_000_000L,
                action, 0, 37, historical);
    }
    private static final TouchSample[][] INPUT = {
            {sample(TouchSample.Action.DOWN, 0, false)},
            {sample(TouchSample.Action.MOVE, 1, true), sample(TouchSample.Action.MOVE, 2, true),
                    sample(TouchSample.Action.MOVE, 3, false)},
            {sample(TouchSample.Action.UP, 4, false)}
    };
    private static class MemorySink implements TouchDiagnostics.Sink {
        final List<TouchSample> samples = new ArrayList<>();
        final AtomicInteger closes = new AtomicInteger();
        @Override public void write(TouchSample sample, long index) throws IOException {
            check(Thread.currentThread().getName().equals("RightpadTouchDiagnostic"), "writer thread ownership");
            samples.add(sample);
        }
        @Override public String description() { return "test"; }
        @Override public void close() throws IOException { closes.incrementAndGet(); }
    }
    private static void await(CountDownLatch latch) throws IOException {
        // Deliberately stalled sink that ignores interruption, like an in-flight disk call.
        while (latch.getCount() != 0) {
            try { if (!latch.await(2, TimeUnit.SECONDS)) throw new IOException("test barrier timeout"); }
            catch (InterruptedException ignored) { }
        }
    }
    private static void done(TouchDiagnostics diagnostics) throws Exception {
        check(diagnostics.awaitForTest(2500), "diagnostic worker terminates");
    }
    private static byte[] receiveTouch(DatagramSocket socket) throws IOException {
        while (true) {
            DatagramPacket packet = new DatagramPacket(new byte[65507], 65507);
            socket.receive(packet);
            if (packet.getData()[1] >= 1 && packet.getData()[1] <= 3)
                return Arrays.copyOf(packet.getData(), packet.getLength());
        }
    }
    private static void sendFixture(UdpTouchSender sender, DatagramSocket socket, long firstSequence,
                                    TouchDiagnostics diagnostic) throws Exception {
        for (int i = 0; i < INPUT.length; i++) {
            byte[] expected = ProtocolV2Encoder.encode(INPUT[i], firstSequence + i, sender.getSenderRunId());
            sender.submit(INPUT[i]);
            if (diagnostic != null) diagnostic.samples(INPUT[i], i == 1 ? 2 : 0);
            int copies = i == 1 ? 1 : 3;
            for (int copy = 0; copy < copies; copy++)
                check(Arrays.equals(expected, receiveTouch(socket)), "exact bytes/run/session/sequence/history/copies");
        }
        socket.setSoTimeout(60);
        try { receiveTouch(socket); throw new AssertionError("extra Touch datagram"); }
        catch (SocketTimeoutException expected) { }
        socket.setSoTimeout(2000);
    }
    public static void main(String[] args) throws Exception {
        Path evidence = Path.of(args[1]); Files.createDirectories(evidence);
        Path disabled = evidence.resolve("disabled-must-not-exist");
        check(!Files.exists(disabled), "fresh disabled fixture");
        check(TouchDiagnostics.start(false, disabled.toFile()) == null, "disabled has no runtime");
        check(!Files.exists(disabled), "disabled never creates recording directory/file");
        pass("default disabled creates no file, queue or worker");

        Path csv = Files.createTempDirectory(evidence, "csv-");
        Path old = csv.resolve("old-evidence.csv"); Files.writeString(old, "KEEP EXACTLY\n");
        TouchDiagnostics d = new TouchDiagnostics(() -> new TouchRecordWriter(csv.toFile()), 16, 5, TimeUnit.SECONDS.toNanos(2));
        for (int i = 0; i < INPUT.length; i++) d.samples(INPUT[i], i == 1 ? 2 : 0);
        done(d); check(d.reasonForTest().equals("record_limit"), "fixed record limit reason");
        Path recording;
        try (var paths = Files.list(csv)) { recording = paths.filter(p -> !p.equals(old)).findFirst().orElseThrow(); }
        List<String> lines = Files.readAllLines(recording);
        check(lines.size() == 6 && lines.get(0).equals("sessionId,sampleIndex,action,source,x,y,eventTimeNs"), "CSV header/limit");
        int index = 0;
        for (TouchSample[] batch : INPUT) for (TouchSample s : batch) {
            String row = "37," + index++ + "," + s.action + "," + (s.historical ? "historical" : "current")
                    + "," + s.x + "," + s.y + "," + s.eventTimeNs;
            check(lines.get(index).equals(row), "exact CSV fields/order/repeated historical samples");
        }
        check(Files.readString(old).equals("KEEP EXACTLY\n"), "old evidence preserved");
        long csvBytes = Files.size(recording);
        d.samples(INPUT[0], 0); Thread.sleep(30);
        check(Files.size(recording) == csvBytes, "no writes past record limit");
        Set<String> names = new HashSet<>();
        try (var files = Files.list(csv)) { files.forEach(p -> names.add(p.getFileName().toString())); }
        for (int i = 0; i < 2; i++) { try (TouchRecordWriter writer = new TouchRecordWriter(csv.toFile())) {
            check(names.add(Path.of(writer.description()).getFileName().toString()), "unique recording filename");
        } }
        pass("explicit CSV exact fields/history/order, record cap, unique names and old-file preservation bytes=" + csvBytes);

        MemorySink timed = new MemorySink();
        d = new TouchDiagnostics(() -> timed, 4, 100, TimeUnit.MILLISECONDS.toNanos(50));
        done(d); check(d.reasonForTest().equals("time_limit") && timed.closes.get() == 1, "idle deadline/close");
        pass("idle time limit terminates and closes worker");

        CountDownLatch entered = new CountDownLatch(1), release = new CountDownLatch(1);
        MemorySink stalled = new MemorySink() {
            @Override public void write(TouchSample sample, long index) throws IOException {
                entered.countDown(); await(release); super.write(sample, index);
            }
        };
        d = new TouchDiagnostics(() -> stalled, 2, 10, TimeUnit.SECONDS.toNanos(3));
        d.samples(INPUT[0], 0); check(entered.await(1, TimeUnit.SECONDS), "sink blocked");
        try (DatagramSocket socket = new DatagramSocket(0, InetAddress.getLoopbackAddress());
             UdpTouchSender sender = new UdpTouchSender("127.0.0.1", socket.getLocalPort())) {
            socket.setSoTimeout(2000); sender.setForeground(true);
            sender.setDiagnostics(d);
            sendFixture(sender, socket, 0, d);
            check(d.droppedForTest() > 0, "full diagnostic queue drops only diagnostics");
            check(stalled.closes.get() == 0, "disk still blocked while all UDP copies delivered");
            d.stop("activity_paused");
            check(stalled.closes.get() == 0, "stop returns without waiting on sink");
            release.countDown(); done(d);
            check(stalled.samples.size() == 1 && stalled.closes.get() == 1, "discard queued old work, close in-flight once");
            sendFixture(sender, socket, 3, d);
            check(stalled.samples.size() == 1, "resume does not restart diagnostic");
        } finally { release.countDown(); d.stop("test_finally"); done(d); }
        pass("blocked I/O/full queue preserves real UDP, nonwaiting stop discards old work; post-stop input exact");

        for (String phase : List.of("open", "write", "close")) {
            MemorySink failing = new MemorySink() {
                @Override public void write(TouchSample sample, long index) throws IOException {
                    if (phase.equals("write")) throw new IOException("expected write failure");
                    super.write(sample, index);
                }
                @Override public void close() throws IOException {
                    super.close(); if (phase.equals("close")) throw new IOException("expected close/flush failure");
                }
            };
            TouchDiagnostics failure = new TouchDiagnostics(() -> {
                if (phase.equals("open")) throw new IOException("expected open failure");
                return failing;
            }, 16, 100, TimeUnit.SECONDS.toNanos(2));
            try (DatagramSocket socket = new DatagramSocket(0, InetAddress.getLoopbackAddress());
                 UdpTouchSender sender = new UdpTouchSender("127.0.0.1", socket.getLocalPort())) {
                socket.setSoTimeout(2000); sender.setForeground(true); sender.setDiagnostics(failure);
                sendFixture(sender, socket, 0, failure);
                failure.stop("test_close"); done(failure);
                sendFixture(sender, socket, 3, failure);
            }
            check(failing.closes.get() == (phase.equals("open") ? 0 : 1), "failed resources close once");
        }
        pass("open/write/close-flush failures isolated, exact UDP continues before and after failure");

        try (DatagramSocket socket = new DatagramSocket(0, InetAddress.getLoopbackAddress());
             UdpTouchSender sender = new UdpTouchSender("127.0.0.1", socket.getLocalPort())) {
            socket.setSoTimeout(2000); sender.setForeground(true);
            sendFixture(sender, socket, 0, null);
            MemorySink enabled = new MemorySink();
            d = new TouchDiagnostics(() -> enabled, 32, 100, TimeUnit.SECONDS.toNanos(3));
            sender.setDiagnostics(d); sendFixture(sender, socket, 3, d);
            d.stop("diagnostic_stopped"); done(d);
            sendFixture(sender, socket, 6, d);
            sender.setDiagnostics(null); sendFixture(sender, socket, 9, null);
            check(enabled.samples.size() == 5, "enabled observes same five immutable samples");
        }
        pass("disabled/enabled/stopped/disabled actual sender has exact byte, identity, sequence and 3/1/3 copy semantics");

        for (String transition : List.of("pause", "target", "close")) {
            MemorySink sink = new MemorySink();
            d = new TouchDiagnostics(() -> sink, 16, 100, TimeUnit.SECONDS.toNanos(2));
            try (UdpTouchSender sender = new UdpTouchSender("127.0.0.1", 9)) {
                sender.setDiagnostics(d);
                if (transition.equals("pause")) sender.setForeground(false);
                if (transition.equals("target")) sender.setTarget(new InetSocketAddress("127.0.0.1", 10));
            }
            done(d); check(!d.active() && sink.closes.get() <= 1, "lifecycle worker ends");
        }
        pass("sender pause/target/close end diagnostic worker without restart");

        Path source = Path.of(args[0], "app/src/main/java/com/rightpad/capture");
        String activity = Files.readString(source.resolve("MainActivity.java"));
        String view = Files.readString(source.resolve("TouchCaptureView.java"));
        String sender = Files.readString(source.resolve("UdpTouchSender.java"));
        check(activity.contains("getBooleanExtra(TouchDiagnostics.EXTRA, false)")
                && activity.contains("diagnosticRequested = savedInstanceState == null")
                && activity.contains("getIntent().removeExtra(TouchDiagnostics.EXTRA)")
                && activity.contains("diagnosticRequested ? TouchDiagnostics.start(true, getFilesDir()) : null"), "production default and one-launch entry");
        check(activity.contains("udpSender.setDiagnostics(diagnostics)") && activity.contains("new TouchCaptureView(this, diagnostics"), "both production paths wired");
        check(view.indexOf("udpSender.submit(samples)") < view.indexOf("diagnostics.samples(samples"), "real input submitted before diagnostics");
        check(!view.contains("recordWriter") && !view.contains("logger.log(sample") && !view.contains("flushRecording"), "no UI recording/log string/flush");
        check(!sender.contains("new TouchRecordWriter") && !sender.contains("Log.i(TAG, \"packet_sent")
                && !sender.contains("Log.i(TAG, \"gamepad_sent"), "no sender file or eager success logging");
        check(activity.contains("diagnostics.stop(\"activity_paused\")") && activity.contains("diagnostics.stop(\"activity_destroyed\")")
                && activity.contains("diagnostics.stop(\"power_exit\")"), "activity lifetime wired");
        pass("actual production default/call order/log gating/lifecycle wiring");
        System.out.println("RESULT TouchDiagnostics groups=" + groups + " passed=" + groups + " failed=0 hostJVM=true defaultCsvBytes=0 fixtureCsvBytes=" + csvBytes);
    }
}
