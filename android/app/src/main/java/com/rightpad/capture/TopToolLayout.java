package com.rightpad.capture;

final class TopToolLayout {
    final float settingsCenterX;
    final float powerCenterX;
    final float percentTextX;
    final float batteryBodyLeft;
    final float batteryBodyRight;

    private TopToolLayout(float settingsCenterX, float powerCenterX, float percentTextX,
            float batteryBodyLeft, float batteryBodyRight) {
        this.settingsCenterX = settingsCenterX;
        this.powerCenterX = powerCenterX;
        this.percentTextX = percentTextX;
        this.batteryBodyLeft = batteryBodyLeft;
        this.batteryBodyRight = batteryBodyRight;
    }

    static TopToolLayout create(float width, float safeRight, float density, float toolRadius,
            float fixedPercentSlotWidth) {
        float rightMargin = 17f * density;
        float toolGap = 8f * density;
        float batteryTextGap = 8f * density;
        float batteryWidth = 24f * density;

        float powerCenterX = width - safeRight - rightMargin - toolRadius;
        float settingsCenterX = powerCenterX - toolRadius * 2f - toolGap;
        float percentSlotRight = settingsCenterX - toolRadius - toolGap;
        float percentTextX = percentSlotRight - fixedPercentSlotWidth;
        float batteryBodyRight = percentTextX - batteryTextGap;
        return new TopToolLayout(settingsCenterX, powerCenterX, percentTextX,
                batteryBodyRight - batteryWidth, batteryBodyRight);
    }
}
