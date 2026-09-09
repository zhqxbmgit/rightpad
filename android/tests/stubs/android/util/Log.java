package android.util;

// JVM socket tests only; never packaged into the APK.
public final class Log {
    public static int i(String tag, String message) { return 0; }
    public static int w(String tag, String message) { return 0; }
    public static int e(String tag, String message) { throw new AssertionError(message); }
    public static int e(String tag, String message, Throwable cause) { throw new AssertionError(message, cause); }
}
