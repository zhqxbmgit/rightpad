package com.rightpad.capture;

import java.nio.file.*;
import java.util.*;

/** Offline regression of the accepted saved geometry and reserved single-pointer owner. */
public final class ReservedControlTests {
    static int checks;
    static void check(boolean value, String message) { checks++; if (!value) throw new AssertionError(message); }
    static void rect(ControlRect r, int x, int y, int w, int h) {
        check(r.x == x && r.y == y && r.width == w && r.height == h, "accepted saved rectangle");
    }
    public static void main(String[] args) throws Exception {
        Path temp = Files.createTempDirectory("rightpad-reserved-control-");
        Path file = temp.resolve("screen-controls.properties");
        String fixture = """
                #Accepted user geometry, including left-side X
                version=1
                controls.rightpad.input.mode.xRatio=0.4275
                controls.rightpad.input.mode.yRatio=0.2891385767790262
                controls.rightpad.input.mode.widthRatio=0.22
                controls.rightpad.input.mode.heightRatio=0.7044943820224719
                controls.xbox.x.slide_lr.xRatio=0.0
                controls.xbox.x.slide_lr.yRatio=0.23820224719101124
                controls.xbox.x.slide_lr.widthRatio=0.056666666666666664
                controls.xbox.x.slide_lr.heightRatio=0.7617977528089888
                controls.xbox.b.slide.xRatio=0.8675
                controls.xbox.b.slide.yRatio=0.1895131086142322
                controls.xbox.b.slide.widthRatio=0.1325
                controls.xbox.b.slide.heightRatio=0.8104868913857678
                controls.future.reserved.data=preserve
                """;
        try {
            Files.writeString(file, fixture);
            byte[] original = Files.readAllBytes(file);
            var config = SlideControlGesture.Config.phaseOne(1);
            var mode = new ScreenControlDefinition(ScreenControlRouter.MODE_ID, "Mode", config, 64, .42f, .55f);
            var b = new ScreenControlDefinition("xbox.b.slide", "B", config, 64, .65f, .55f, GamepadState.B, GamepadState.Y, GamepadState.A);
            var x = ScreenControlDefinition.lr(SlideControlLRDefinition.phaseSix(), 64, .15f, .55f,
                    GamepadState.X, GamepadState.DPAD_LEFT, GamepadState.DPAD_RIGHT, GamepadState.DPAD_UP);
            var definitions = List.of(b, x, mode);
            var store = new ScreenControlLayoutStore(file.toFile());
            var areas = store.load(definitions, 1200, 2670);
            rect(areas.get(mode.id), 513, 772, 264, 1881);
            rect(areas.get(x.id), 0, 636, 68, 2034);
            rect(areas.get(b.id), 1041, 506, 159, 2164);
            var router = new ScreenControlRouter();
            for (int pass = 0; pass < 20; pass++) {
                check(router.down(600, 900, 7, false, false, true, areas) == ScreenControlRouter.Owner.MODE,
                        "reserved DOWN consumed, never Mouse/Xbox");
                check(router.accepts(1, 7, false), "MOVE and arbitrary hold retain owner");
                check(router.owner() == ScreenControlRouter.Owner.MODE, "moving outside cannot transfer owner");
                check(router.modeUp(1, 7), "matching UP releases");
                check(router.owner() == ScreenControlRouter.Owner.NONE && router.profile() == ScreenControlRouter.Profile.NORMAL,
                        "no local selected mode exists; static M identity retained");
                check(!router.modeUp(1, 7), "duplicate UP has no action");
                router.down(600, 900, 7, false, false, true, areas); router.cancel();
                check(router.owner() == ScreenControlRouter.Owner.NONE && !router.modeUp(1, 7), "CANCEL cleans owner");
                router.down(600, 900, 7, false, false, true, areas);
                check(!router.accepts(2, 7, true) && router.owner() == ScreenControlRouter.Owner.NONE, "second pointer cleans owner");
                router.down(600, 900, 7, false, false, true, areas);
                check(!router.accepts(1, 8, false), "wrong pointer cannot act");
            }
            check(router.down(10, 900, 1, false, false, true, areas) == ScreenControlRouter.Owner.SCREEN_CONTROL
                    && x.id.equals(router.controlId()), "X at left retains routing");
            check(router.down(1100, 900, 1, false, false, true, areas) == ScreenControlRouter.Owner.SCREEN_CONTROL
                    && b.id.equals(router.controlId()), "B retains routing");
            check(router.down(900, 900, 1, false, false, true, areas) == ScreenControlRouter.Owner.MOUSE, "normal touchpad still admitted");
            var editor = new ScreenControlLayoutEditor(definitions, areas, 1200, 2670);
            editor.begin();
            check(editor.startDrag(645, 1700, 8) && mode.id.equals(editor.selectedId()), "same stable ID body drag");
            editor.drag(650, 1705); editor.endDrag();
            rect(editor.selectedRect(), 518, 777, 264, 1881);
            check(editor.numeric("513", "772", "260", "1800"), "same stable ID numeric resize");
            editor.cancel();
            rect(editor.layout().get(mode.id), 513, 772, 264, 1881);
            check(Arrays.equals(original, Files.readAllBytes(file)), "load/routing/edit-cancel never writes saved layout bytes");
            check(mode.buttonsFor(SlideControlGesture.Action.BASE) == 0 && mode.buttonsFor(SlideControlGesture.Action.SLIDE_UP) == 0,
                    "reserved definition still has no Xbox mapping");
            check(b.buttonsFor(SlideControlGesture.Action.BASE) == GamepadState.B && x.buttonsFor(SlideControlLRGesture.Action.BASE) == GamepadState.X,
                    "existing B/X mapping retained");
            Path source = Path.of(args[0]).resolve("app/src/main/java/com/rightpad/capture");
            String view = Files.readString(source.resolve("TouchCaptureView.java"));
            check(!view.contains("publishProfile("), "no production View profile publication");
            int start = view.indexOf("if (reservedContact)", view.indexOf("if (owner == ScreenControlRouter.Owner.SCREEN_CONTROL)"));
            String branch = view.substring(start, view.indexOf("if (action == MotionEvent.ACTION_CANCEL)", start + 80));
            check(!branch.contains("captureEvent(") && !branch.contains("controls.touch(") && branch.contains("modeEventHint()") && branch.contains("DIRECTION_COMMIT")
                    && branch.contains("modeUp()") && branch.contains("modeCancel()") && branch.contains("return true;"), "Mode MOVE/UP/CANCEL lifecycle reports only contact facts, no Mouse/gamepad, only bounded gesture preselection feedback");
            String controls = Files.readString(source.resolve("ScreenControls.java"));
            check(controls.contains("register(new ScreenControlDefinition(MODE_ID") && controls.contains("profile.label"), "production definition and visible M label preserved");
            check(controls.contains("if (MODE_ID.equals(id)) return;"), "reserved definition avoids gesture/haptic execution");
        } finally { Files.deleteIfExists(file); Files.delete(temp); }
        System.out.println("RESULT ReservedControlTests checks=" + checks + " failed=0");
    }
}
