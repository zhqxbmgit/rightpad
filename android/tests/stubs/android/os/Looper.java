package android.os;

// Deterministic main-loop stand-in for JVM listener lifecycle tests; never in the APK.
public final class Looper {
    private static final Looper MAIN = new Looper();
    public static Looper getMainLooper() { return MAIN; }
}
