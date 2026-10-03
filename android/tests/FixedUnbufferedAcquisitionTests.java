package com.rightpad.capture;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Map;

/** JVM admission/variant contract; ScreenControlsSmoke verifies the actual Android API. */
public final class FixedUnbufferedAcquisitionTests {
    private static int checks;
    private static void check(boolean value, String message) {
        checks++;
        if (!value) throw new AssertionError(message);
    }
    public static void main(String[] args) throws Exception {
        var router = new ScreenControlRouter();
        var controls = Map.of("xbox.b.slide", new ControlRect(0, 0, 20, 20),
                "xbox.x.slide_lr", new ControlRect(30, 0, 20, 20),
                ScreenControlRouter.MODE_ID, new ControlRect(60, 0, 20, 20));
        check(router.profile() == ScreenControlRouter.Profile.NORMAL, "fresh profile M");
        for (int pass = 0; pass < 2; pass++) {
            check(router.down(100, 100, 0, false, false, true, controls) == ScreenControlRouter.Owner.MOUSE,
                    "Touchpad admitted in both profiles");
            check(router.accepts(1, 0, false), "matching single pointer owns contact");
            check(!router.accepts(2, 0, true), "second pointer abort");
            check(router.owner() == ScreenControlRouter.Owner.NONE, "abort clears owner");
            check(router.down(100, 100, 0, false, false, false, controls) == ScreenControlRouter.Owner.NONE,
                    "unready or non-finger denied Mouse admission");
            check(router.down(5, 5, 0, false, false, true, controls) == ScreenControlRouter.Owner.SCREEN_CONTROL,
                    "B is not Mouse");
            check(router.down(35, 5, 0, false, false, true, controls) == ScreenControlRouter.Owner.SCREEN_CONTROL,
                    "X is not Mouse");
            check(router.down(65, 5, 0, false, false, true, controls) == ScreenControlRouter.Owner.MODE,
                    "Mode is not Mouse");
            var before = router.profile();
            check(!router.modeUp(1, 9) && router.profile() == before, "wrong pointer cannot toggle");
            router.down(65, 5, 0, false, false, true, controls);
            check(router.modeUp(1, 0) && router.profile() != before, "matching UP changes actual profile");
            check(router.down(100, 100, 0, true, false, true, controls) == ScreenControlRouter.Owner.SETTINGS,
                    "Settings is not Mouse");
            check(router.down(100, 100, 0, false, true, true, controls) == ScreenControlRouter.Owner.POWER,
                    "Power is not Mouse");
        }
        Path root = Path.of(args[0]);
        String view = Files.readString(root.resolve("app/src/main/java/com/rightpad/capture/TouchCaptureView.java"));
        String down = view.substring(view.indexOf("if (action == MotionEvent.ACTION_DOWN)"), view.indexOf("if (action == MotionEvent.ACTION_CANCEL)"));
        String request = "requestUnbufferedDispatch(event);";
        check(view.indexOf(request) == view.lastIndexOf(request) && view.indexOf(request) >= 0,
                "one actual View request call site");
        int mouse = down.indexOf("owner == ScreenControlRouter.Owner.MOUSE");
        int call = down.indexOf(request);
        check(mouse >= 0 && call > mouse && call < down.indexOf("captureEvent(event, 0, TouchSample.Action.DOWN)"),
                "request uses original DOWN on admitted Mouse before extraction");
        check(down.contains("event.getPointerCount() != 1") && down.contains("MotionEvent.TOOL_TYPE_FINGER"),
                "single finger guards precede admission");
        check(!view.contains("BuildConfig") && !view.contains("acquisition()"), "no variant or acquisition mode state");
        String build = Files.readString(root.resolve("app/build.gradle"));
        check(!build.contains("MR8H_ACQUISITION_AB"), "debug and release share fixed policy");
        check(build.contains("main.java.exclude '**/TouchAcquisitionState.java'")
                && build.contains("androidTest.java.exclude '**/HumanAcquisitionSmoke.java'"), "historical sources excluded");
        System.out.println("RESULT FixedUnbufferedAcquisitionTests checks=" + checks + " failed=0");
    }
}
