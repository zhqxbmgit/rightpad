package com.rightpad.capture;

/** Integer View pixels shared by drawing, routing, editing and persistence. */
final class ControlRect {
    static final int MIN_SIZE = 20;
    final int x, y, width, height;

    ControlRect(int x, int y, int width, int height) {
        this.x = x;
        this.y = y;
        this.width = width;
        this.height = height;
    }

    int right() { return x + width; }
    int bottom() { return y + height; }
    boolean contains(float px, float py) {
        return px >= x && py >= y && px < right() && py < bottom();
    }
    boolean valid(int viewWidth, int viewHeight) {
        return x >= 0 && y >= 0 && width >= MIN_SIZE && height >= MIN_SIZE
                && (long) x + width <= viewWidth && (long) y + height <= viewHeight;
    }
    ControlRect clamp(int viewWidth, int viewHeight) {
        if (viewWidth < MIN_SIZE || viewHeight < MIN_SIZE) {
            throw new IllegalArgumentException("View too small for controls");
        }
        int w = Math.max(MIN_SIZE, Math.min(width, viewWidth));
        int h = Math.max(MIN_SIZE, Math.min(height, viewHeight));
        return new ControlRect(Math.max(0, Math.min(x, viewWidth - w)),
                Math.max(0, Math.min(y, viewHeight - h)), w, h);
    }
}
