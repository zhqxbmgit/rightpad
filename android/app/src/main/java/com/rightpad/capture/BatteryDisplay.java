package com.rightpad.capture;

final class BatteryDisplay {
    static final int UNKNOWN_PERCENT = -1;
    static final int COLOR_LOW = 0;
    static final int COLOR_MEDIUM = 1;
    static final int COLOR_HIGH = 2;

    private BatteryDisplay() {}

    static int percent(int level, int scale) {
        if (level < 0 || scale <= 0) return UNKNOWN_PERCENT;
        long rounded = ((long) level * 100L + scale / 2L) / scale;
        return (int) Math.max(0L, Math.min(100L, rounded));
    }

    static String text(int percent) {
        if (percent == UNKNOWN_PERCENT) return "--%";
        return Math.max(0, Math.min(100, percent)) + "%";
    }

    static int colorBand(int percent) {
        if (percent != UNKNOWN_PERCENT && percent < 15) return COLOR_LOW;
        if (percent != UNKNOWN_PERCENT && percent <= 30) return COLOR_MEDIUM;
        return COLOR_HIGH;
    }
}
