package com.rightpad.capture;

/** Fixed View rectangles. Only the original Mode rectangle admits a DOWN. */
final class ModeFixedRegions {
    record Geometry(long identity, int width, int height, float density, ControlRect mode,
            int left, int top, int right, int bottom) {
        boolean valid() {
            return identity != 0 && width >= 20 && width <= 32768 && height >= 20 && height <= 32768 &&
                    Float.isFinite(density) && density > 0 && density <= 16 && mode != null && mode.valid(width,height) &&
                    left >= 0 && top >= 0 && right <= width && bottom <= height && right > left && bottom > top;
        }
        boolean fits(float firstDp, float secondDp) {
            return error(firstDp,secondDp)==null;
        }
        String error(float firstDp,float secondDp) {
            if(!valid() || !widthValid(firstDp) || !widthValid(secondDp)) return "Mode 输入坐标空间或区域宽度无效";
            var sides=new java.util.ArrayList<String>();
            if(mode.x<left)sides.add("左侧");if(mode.y<top)sides.add("上侧");
            if(mode.right()+firstDp*density+secondDp*density>right)sides.add("右侧");
            if(mode.bottom()>bottom)sides.add("下侧");
            return sides.isEmpty()?null:"Mode 三区域超出手机可用输入范围："+String.join("、",sides);
        }
    }
    static boolean widthValid(float dp) { return Float.isFinite(dp) && dp >= 1 && dp <= 50 && dp*2 == Math.floor(dp*2); }
    private final Geometry geometry;
    private final float firstEnd, secondEnd;
    private boolean entered, invalid, eventFeedback;
    private int eventStartSelection;
    private int selection = 1; // 0 cancelled/invalid, 1 origin, 2 first, 3 second
    ModeFixedRegions(Geometry g, float firstDp, float secondDp) {
        if (g == null || !g.fits(firstDp,secondDp)) throw new IllegalArgumentException("Mode regions outside input space");
        geometry=g; firstEnd=g.mode().right()+firstDp*g.density(); secondEnd=firstEnd+secondDp*g.density();
    }
    void beginEvent() { eventStartSelection=selection;eventFeedback=false; }
    void sample(float x, float y, boolean feedback) {
        if (invalid) return;
        var r=geometry.mode();
        if (!Float.isFinite(x) || !Float.isFinite(y) || x<r.x || x>=secondEnd || y<r.y || y>=r.bottom()) {
            invalid=true;selection=0;return;
        }
        eventFeedback=feedback;
        if (x<r.right()) { selection=entered?0:1;return; }
        entered=true;
        if (x<firstEnd) {
            selection=2;
        } else {
            selection=3;
        }
    }
    // Compare only the event endpoints after all ordered history has updated the safety facts.
    boolean eventHint() { return eventFeedback && !invalid && selection>=2 && selection!=eventStartSelection; }
    int selection() { return selection; }
    boolean invalid() { return invalid; }
}
