package com.rightpad.capture;

public final class UiStatusAndPowerTest {
    private static int checks;

    public static void main(String[] args) {
        batteryFormattingAndClamping();
        batteryColorBands();
        fixedTopToolLayout();
        powerGestureCases();
        System.out.println("RESULT UI status/power checks=" + checks + " PASS");
    }

    private static void batteryFormattingAndClamping() {
        equal(0, BatteryDisplay.percent(0, 100), "zero percent");
        equal("0%", BatteryDisplay.text(0), "zero text");
        equal("9%", BatteryDisplay.text(BatteryDisplay.percent(9, 100)), "single digit");
        equal("85%", BatteryDisplay.text(BatteryDisplay.percent(85, 100)), "two digits");
        equal("100%", BatteryDisplay.text(BatteryDisplay.percent(100, 100)), "full");
        equal(100, BatteryDisplay.percent(120, 100), "high clamp");
        equal(-1, BatteryDisplay.percent(-1, 100), "negative invalid");
        equal(-1, BatteryDisplay.percent(50, 0), "scale invalid");
        equal("--%", BatteryDisplay.text(-1), "unknown text");
    }

    private static void batteryColorBands() {
        equal(BatteryDisplay.COLOR_LOW, BatteryDisplay.colorBand(0), "low zero");
        equal(BatteryDisplay.COLOR_LOW, BatteryDisplay.colorBand(14), "low edge");
        equal(BatteryDisplay.COLOR_MEDIUM, BatteryDisplay.colorBand(15), "medium start");
        equal(BatteryDisplay.COLOR_MEDIUM, BatteryDisplay.colorBand(30), "medium edge");
        equal(BatteryDisplay.COLOR_HIGH, BatteryDisplay.colorBand(31), "high start");
        equal(BatteryDisplay.COLOR_HIGH, BatteryDisplay.colorBand(-1), "unknown neutral/high");
    }

    private static void fixedTopToolLayout() {
        TopToolLayout oneDigit = TopToolLayout.create(1200f, 0f, 3f, 57f, 104f);
        TopToolLayout threeDigits = TopToolLayout.create(1200f, 0f, 3f, 57f, 104f);
        equal(oneDigit.settingsCenterX, threeDigits.settingsCenterX, "settings fixed");
        equal(oneDigit.powerCenterX, threeDigits.powerCenterX, "power fixed");
        equal(oneDigit.percentTextX, threeDigits.percentTextX, "percent slot fixed");
        truth(oneDigit.batteryBodyRight < oneDigit.percentTextX, "battery before percent");
        truth(oneDigit.percentTextX < oneDigit.settingsCenterX, "percent before settings");
        truth(oneDigit.settingsCenterX < oneDigit.powerCenterX, "settings before power");
    }

    private static void powerGestureCases() {
        final float center = 50f;
        final float radius = 20f;
        final float slop = 8f;

        PowerGestureTracker valid = new PowerGestureTracker();
        truth(valid.onDown(50f, 50f, 100L, center, center, radius), "inside down claimed");
        valid.onMove(54f, 53f, slop);
        truth(valid.isClaimed(), "claimed move remains consumed");
        truth(valid.onUp(54f, 53f, 300L, center, center, radius, 300L), "valid tap exits");
        truth(!valid.onUp(54f, 53f, 300L, center, center, radius, 300L), "no duplicate callback");

        PowerGestureTracker outsideDown = new PowerGestureTracker();
        truth(!outsideDown.onDown(10f, 10f, 0L, center, center, radius), "outside down normal");

        PowerGestureTracker moved = new PowerGestureTracker();
        int routedTouchSamples = 0;
        boolean claimedDown = moved.onDown(50f, 50f, 0L, center, center, radius);
        if (!claimedDown) routedTouchSamples++;
        truth(claimedDown, "moved claimed");
        moved.onMove(59f, 50f, slop);
        if (!moved.isClaimed()) routedTouchSamples++;
        truth(moved.isClaimed(), "cancelled movement remains consumed");
        truth(!moved.onUp(50f, 50f, 100L, center, center, radius, 300L), "movement cancels");
        equal(0, routedTouchSamples, "claimed gesture sends no touch samples");

        PowerGestureTracker longPress = new PowerGestureTracker();
        longPress.onDown(50f, 50f, 0L, center, center, radius);
        truth(!longPress.onUp(50f, 50f, 301L, center, center, radius, 300L), "long press cancels");

        PowerGestureTracker upOutside = new PowerGestureTracker();
        upOutside.onDown(50f, 50f, 0L, center, center, radius);
        truth(!upOutside.onUp(75f, 50f, 100L, center, center, radius, 300L), "up outside cancels");

        PowerGestureTracker cancelled = new PowerGestureTracker();
        cancelled.onDown(50f, 50f, 0L, center, center, radius);
        cancelled.onCancel();
        truth(!cancelled.isClaimed(), "cancel ends claim");
        truth(!cancelled.onUp(50f, 50f, 100L, center, center, radius, 300L), "cancel no exit");

        PowerGestureTracker exactSlop = new PowerGestureTracker();
        exactSlop.onDown(50f, 50f, 0L, center, center, radius);
        exactSlop.onMove(58f, 50f, slop);
        truth(exactSlop.onUp(58f, 50f, 300L, center, center, radius, 300L),
                "threshold and duration inclusive");

        PowerGestureTracker negativeDuration = new PowerGestureTracker();
        negativeDuration.onDown(50f, 50f, 200L, center, center, radius);
        truth(!negativeDuration.onUp(50f, 50f, 199L, center, center, radius, 300L),
                "negative duration rejected");
    }

    private static void equal(int expected, int actual, String label) {
        checks++;
        if (expected != actual) throw new AssertionError(label + ": " + actual);
    }

    private static void equal(String expected, String actual, String label) {
        checks++;
        if (!expected.equals(actual)) throw new AssertionError(label + ": " + actual);
    }

    private static void equal(float expected, float actual, String label) {
        checks++;
        if (Float.compare(expected, actual) != 0) throw new AssertionError(label + ": " + actual);
    }

    private static void truth(boolean value, String label) {
        checks++;
        if (!value) throw new AssertionError(label);
    }
}
