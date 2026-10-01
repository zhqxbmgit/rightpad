package com.rightpad.capture;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.NoSuchFileException;
import java.nio.file.Path;
import java.nio.file.attribute.BasicFileAttributes;
import java.util.Arrays;

/** Test-only protection of the user's exact layout bytes, including failed backup/cleanup. */
final class LayoutTestFileGuard implements AutoCloseable {
    enum State { UNKNOWN, ORIGINAL_ABSENT, BACKUP_COMPLETE, BACKUP_FAILED }

    static class FileOperations {
        boolean exists(Path path) throws IOException {
            // Files.exists also returns false when existence cannot be determined.
            try { Files.readAttributes(path, BasicFileAttributes.class); return true; }
            catch (NoSuchFileException absent) { return false; }
        }
        byte[] read(Path path) throws IOException { return Files.readAllBytes(path); }
        void write(Path path, byte[] bytes) throws IOException { Files.write(path, bytes); }
        void delete(Path path) throws IOException { Files.deleteIfExists(path); }
        Path evidence(Path directory, byte[] bytes) throws IOException {
            Path copy = Files.createTempFile(directory, "layout-restore-failed-", ".backup");
            Files.write(copy, bytes);
            return copy;
        }
    }

    private final Path path, evidenceDirectory;
    private final FileOperations files;
    private State state = State.UNKNOWN;
    private byte[] original;
    private boolean restoreAttempted, restored;
    private Path evidence;

    LayoutTestFileGuard(Path path, Path evidenceDirectory) {
        this(path, evidenceDirectory, new FileOperations());
    }
    LayoutTestFileGuard(Path path, Path evidenceDirectory, FileOperations files) {
        this.path = path; this.evidenceDirectory = evidenceDirectory; this.files = files;
    }
    State state() { return state; }
    boolean canMutate() {
        return !restoreAttempted && (state == State.ORIGINAL_ABSENT || state == State.BACKUP_COMPLETE);
    }
    void capture() throws IOException {
        if (state != State.UNKNOWN) throw new IllegalStateException("Layout backup already attempted");
        try {
            if (!files.exists(path)) { state = State.ORIGINAL_ABSENT; return; }
            original = files.read(path);
            state = State.BACKUP_COMPLETE;
        } catch (IOException | RuntimeException error) {
            state = State.BACKUP_FAILED;
            throw new IOException("Layout backup failed; fixture mutation forbidden: " + path, error);
        }
    }
    void restore() throws IOException {
        if (restored || state == State.UNKNOWN || state == State.BACKUP_FAILED) return;
        restoreAttempted = true;
        try {
            if (state == State.ORIGINAL_ABSENT) {
                files.delete(path);
                if (files.exists(path)) throw new IOException("Layout still exists after removal");
            } else {
                files.write(path, original);
                if (!Arrays.equals(original, files.read(path))) throw new IOException("Restored layout bytes differ");
            }
            restored = true;
        } catch (IOException | RuntimeException error) {
            // Retain the in-memory bytes for a retry and a separate disk copy when possible.
            if (state == State.BACKUP_COMPLETE && evidence == null) {
                try { evidence = files.evidence(evidenceDirectory, original); }
                catch (IOException | RuntimeException backupError) { error.addSuppressed(backupError); }
            }
            throw new IOException("Layout restore failed: " + path + "; backup evidence="
                    + (evidence == null ? "unavailable (original state retained in guard)" : evidence), error);
        }
    }
    @Override public void close() throws IOException { restore(); }
}
