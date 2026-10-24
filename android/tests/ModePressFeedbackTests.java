package com.rightpad.capture;

import java.nio.file.*;
import javax.tools.ToolProvider;

/** Executes the production View dispatch/cleanup methods in a headless shell, no device input. */
public final class ModePressFeedbackTests {
    private static String method(String source, String signature) {
        int start = source.indexOf(signature), open = source.indexOf('{', start), depth = 1, end = open + 1;
        if (start < 0 || open < 0) throw new AssertionError("missing production method " + signature);
        while (depth != 0) { char c = source.charAt(end++); if (c == '{') depth++; if (c == '}') depth--; }
        return source.substring(start, end);
    }
    public static void main(String[] args) throws Exception {
        Path root = Path.of(args[0]), output = Path.of(args[1]);
        Files.createDirectories(output);
        String source = Files.readString(root.resolve("app/src/main/java/com/rightpad/capture/TouchCaptureView.java"));
        String fixture = Files.readString(root.resolve("tests/fixtures/ModePressViewFixture.java.txt"))
                .replace("// PRODUCTION_DISPATCH", method(source, "public boolean onTouchEvent(MotionEvent event)"))
                .replace("// PRODUCTION_CLEANUP", method(source, "void stopCapture(String reason)"));
        Path file = output.resolve("ModePressViewFixture.java");
        Files.writeString(file, fixture);
        String cp = System.getProperty("java.class.path");
        int result = ToolProvider.getSystemJavaCompiler().run(null, System.out, System.err,
                "--release", "17", "-encoding", "UTF-8", "-cp", cp, "-d", output.toString(), file.toString());
        if (result != 0) throw new AssertionError("headless View fixture compilation failed");
        String javaExe = Path.of(System.getProperty("java.home"), "bin", "java.exe").toString();
        int exit = new ProcessBuilder(javaExe, "-cp", output + java.io.File.pathSeparator + cp,
                "com.rightpad.capture.ModePressViewFixture").inheritIO().start().waitFor();
        if (exit != 0) throw new AssertionError("headless View tests failed");
    }
}
