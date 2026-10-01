package com.rightpad.capture;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.NoSuchFileException;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.util.Arrays;
import java.util.List;

public final class LayoutTestFileGuardTests {
    private static int checks;
    private static final byte[] ORIGINAL = { '#', 0, (byte) 255, '\r', '\n', 'X', '=', '1', '0' };
    private static final byte[] FIXTURE = { 1, 2, 3 };
    private static void check(boolean ok, String message) {
        checks++;
        if (!ok) throw new AssertionError(message);
    }
    private static final class Operations extends LayoutTestFileGuard.FileOperations {
        int writes, deletes, evidenceCalls;
        IOException inspectFailure, readFailure;
        boolean failWrite, failDelete, failEvidence, corruptWrite;
        @Override boolean exists(Path path) throws IOException {
            if (inspectFailure != null) throw inspectFailure;
            return super.exists(path);
        }
        @Override byte[] read(Path path) throws IOException {
            if (readFailure != null) throw readFailure;
            return super.read(path);
        }
        @Override void write(Path path, byte[] bytes) throws IOException {
            writes++;
            if (failWrite) throw new IOException("injected restore write failure");
            super.write(path, corruptWrite ? FIXTURE : bytes);
        }
        @Override void delete(Path path) throws IOException {
            deletes++;
            if (failDelete) throw new IOException("injected restore delete failure");
            super.delete(path);
        }
        @Override Path evidence(Path directory, byte[] bytes) throws IOException {
            evidenceCalls++;
            if (failEvidence) throw new IOException("injected evidence failure");
            return super.evidence(directory, bytes);
        }
    }
    interface Body { void run() throws Exception; }
    private static void temporary(BodyWithDirectory body) throws Exception {
        Path directory = Files.createTempDirectory("rightpad-layout-guard-");
        try { body.run(directory); }
        finally {
            // Only this test's flat, freshly created directory is removed.
            for (Path entry : entries(directory)) Files.delete(entry);
            Files.delete(directory);
        }
    }
    interface BodyWithDirectory { void run(Path directory) throws Exception; }
    private static List<Path> entries(Path directory) throws IOException {
        try (var files = Files.list(directory)) { return files.sorted().toList(); }
    }
    private static IOException ioFailure(Body body, String message) throws Exception {
        try { body.run(); throw new AssertionError("Expected failure: " + message); }
        catch (IOException error) { check(error.getMessage().contains(message), "clear error: " + message); return error; }
    }
    private static void exact(Path path, byte[] bytes) throws Exception {
        byte[] actual = Files.readAllBytes(path);
        check(Arrays.equals(bytes, actual), "exact original bytes restored");
        MessageDigest digest = MessageDigest.getInstance("SHA-256");
        check(Arrays.equals(digest.digest(bytes), digest.digest(actual)), "SHA-256 restored");
    }
    private static void absent() throws Exception {
        temporary(directory -> {
            Path path = directory.resolve("layout"); Operations ops = new Operations();
            LayoutTestFileGuard guard = new LayoutTestFileGuard(path, directory, ops);
            check(guard.state() == LayoutTestFileGuard.State.UNKNOWN && !guard.canMutate(), "unknown forbids mutation");
            guard.close(); check(ops.writes == 0 && ops.deletes == 0, "unknown cleanup is a no-op");
            guard.capture();
            check(guard.state() == LayoutTestFileGuard.State.ORIGINAL_ABSENT && guard.canMutate(), "confirmed absent permits fixture");
            try (guard) { Files.write(path, FIXTURE); }
            check(!Files.exists(path), "original absence restored, no default file left");
            check(!guard.canMutate(), "finished guard forbids further fixture mutation");
            guard.close(); check(ops.deletes == 1 && ops.writes == 0, "double cleanup removes fixture only once");
            Files.write(path, ORIGINAL); guard.close(); exact(path, ORIGINAL);
            check(ops.deletes == 1, "repeated close never deletes a later file");
        });
    }
    private static void existing(byte[] bytes, int failureKind) throws Exception {
        temporary(directory -> {
            Path path = directory.resolve("layout"); Files.write(path, bytes);
            Operations ops = new Operations(); LayoutTestFileGuard guard = new LayoutTestFileGuard(path, directory, ops);
            guard.capture();
            check(guard.state() == LayoutTestFileGuard.State.BACKUP_COMPLETE && guard.canMutate(), "complete backup permits mutation");
            check(ops.writes == 0 && ops.deletes == 0, "capture itself does not mutate layout");
            try (guard) {
                Files.write(path, FIXTURE);
                if (failureKind == 1) throw new IOException("test body failed");
                if (failureKind == 2) throw new AssertionError("instrumentation assertion failed");
            } catch (IOException | AssertionError error) {
                check(failureKind != 0 && error.getMessage().equals(failureKind == 1
                        ? "test body failed" : "instrumentation assertion failed"), "body failure stays visible");
            }
            exact(path, bytes); guard.close(); exact(path, bytes);
            check(ops.writes == 1 && ops.deletes == 0, "restore once, never deletes existing file");
        });
    }
    private static void failedBackup(int failureKind) throws Exception {
        temporary(directory -> {
            Path path = directory.resolve("layout"); Files.write(path, ORIGINAL);
            Path sibling = directory.resolve("unrelated"); Files.write(sibling, FIXTURE);
            List<Path> before = entries(directory);
            Operations ops = new Operations();
            if (failureKind == 0) ops.readFailure = new IOException("injected backup read failure");
            if (failureKind == 1) ops.readFailure = new NoSuchFileException("read failure is not confirmed absence");
            if (failureKind == 2) ops.inspectFailure = new IOException("cannot determine existence");
            LayoutTestFileGuard guard = new LayoutTestFileGuard(path, directory, ops);
            boolean[] bodyRan = { false };
            ioFailure(() -> {
                try (guard) {
                    guard.capture();
                    if (guard.canMutate()) { bodyRan[0] = true; Files.write(path, FIXTURE); }
                }
            }, "Layout backup failed; fixture mutation forbidden");
            guard.close();
            check(!bodyRan[0] && !guard.canMutate(), "backup failure stops dependent body");
            check(guard.state() == LayoutTestFileGuard.State.BACKUP_FAILED, "failure never means absent");
            check(ops.writes == 0 && ops.deletes == 0 && ops.evidenceCalls == 0, "backup failure: zero write/delete/replacement evidence calls");
            check(entries(directory).equals(before), "no temp/replacement/rename, directory unchanged");
            exact(path, ORIGINAL); exact(sibling, FIXTURE);
        });
    }
    private static void failedRestore(boolean evidenceFailure, boolean corruptWrite) throws Exception {
        temporary(directory -> {
            Path path = directory.resolve("layout"); Files.write(path, ORIGINAL);
            Operations ops = new Operations(); LayoutTestFileGuard guard = new LayoutTestFileGuard(path, directory, ops);
            guard.capture(); Files.write(path, FIXTURE);
            ops.failWrite = !corruptWrite; ops.corruptWrite = corruptWrite; ops.failEvidence = evidenceFailure;
            IOException error = ioFailure(guard::close, "Layout restore failed");
            check(!guard.canMutate(), "failed restore cannot enable more mutation");
            check(ops.evidenceCalls == 1, "restore failure attempts to preserve backup evidence");
            if (evidenceFailure) {
                check(error.getCause().getSuppressed().length == 1, "evidence failure also remains visible");
                check(error.getMessage().contains("unavailable"), "no false claim of durable backup");
            } else {
                Path evidence = entries(directory).stream().filter(p -> !p.equals(path)).findFirst().orElseThrow();
                exact(evidence, ORIGINAL);
                check(error.getMessage().contains(evidence.toString()), "error identifies backup evidence path");
            }
            ops.failWrite = false; ops.corruptWrite = false;
            guard.restore(); exact(path, ORIGINAL); guard.close();
            check(ops.deletes == 0, "restore failure/retry never deletes original");
        });
    }
    private static void absentRestoreFailure() throws Exception {
        temporary(directory -> {
            Path path = directory.resolve("layout"); Operations ops = new Operations();
            LayoutTestFileGuard guard = new LayoutTestFileGuard(path, directory, ops); guard.capture();
            Files.write(path, FIXTURE); ops.failDelete = true;
            ioFailure(guard::close, "Layout restore failed");
            check(ops.evidenceCalls == 0, "absent original has no invented backup bytes");
            ops.failDelete = false; guard.close(); check(!Files.exists(path), "absent restore can retry");
        });
    }
    public static void main(String[] args) throws Exception {
        absent();
        existing(ORIGINAL, 0); existing(new byte[0], 0); existing(ORIGINAL, 1); existing(ORIGINAL, 2);
        for (int i = 0; i < 3; i++) failedBackup(i);
        failedRestore(false, false); failedRestore(true, false); failedRestore(false, true);
        absentRestoreFailure();
        System.out.println("RESULT LayoutTestFileGuardTests checks=" + checks + " failed=0");
    }
}
