package android.util;

// JVM socket tests only; never packaged into the APK.
public final class Log {
    public static volatile boolean expectedNetworkFailures;
    public static int i(String tag, String message) { return 0; }
    public static int w(String tag, String message) { return 0; }
    public static int e(String tag, String message) { throw new AssertionError(message); }
    public static int e(String tag, String message, Throwable cause) {
        if(expectedNetworkFailures && (cause instanceof java.io.IOException || cause instanceof SecurityException)) return 0;
        throw new AssertionError(message, cause);
    }
}
