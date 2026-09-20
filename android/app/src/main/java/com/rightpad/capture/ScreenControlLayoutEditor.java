package com.rightpad.capture;

import java.io.IOException;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/** One draft model; invalid text is UI input, never a second rectangle model. */
final class ScreenControlLayoutEditor {
    enum Handle { NONE, MOVE, LEFT, RIGHT, TOP, BOTTOM, TOP_LEFT, TOP_RIGHT, BOTTOM_LEFT, BOTTOM_RIGHT }
    final int width, height;
    private final List<ScreenControlDefinition> definitions;
    private Map<String, ControlRect> committed;
    private Map<String, ControlRect> draft;
    private String selected;
    private boolean numericValid = true;
    private Handle handle = Handle.NONE;
    private ControlRect dragStart;
    private float startX, startY;

    ScreenControlLayoutEditor(List<ScreenControlDefinition> definitions,
            Map<String, ControlRect> committed, int width, int height) {
        this.definitions = definitions;
        this.committed = new LinkedHashMap<>(committed);
        this.width = width;
        this.height = height;
    }
    void begin() {
        draft = new LinkedHashMap<>(committed);
        selected = definitions.get(0).id;
        numericValid = true;
    }
    boolean editing() { return draft != null; }
    Map<String, ControlRect> layout() {
        return java.util.Collections.unmodifiableMap(editing() ? draft : committed);
    }
    String selectedId() { return selected; }
    ControlRect selectedRect() { return draft.get(selected); }
    boolean canSave() { return editing() && numericValid && draft.values().stream().allMatch(r -> r.valid(width, height)); }
    boolean numeric(String x, String y, String w, String h) {
        try {
            ControlRect next = new ControlRect(Integer.parseInt(x), Integer.parseInt(y),
                    Integer.parseInt(w), Integer.parseInt(h));
            numericValid = next.valid(width, height);
            if (numericValid) draft.put(selected, next);
        } catch (NumberFormatException error) { numericValid = false; }
        return numericValid;
    }
    void save(ScreenControlLayoutStore store) throws IOException {
        if (!canSave()) throw new IllegalStateException("Invalid draft layout");
        store.save(draft, width, height);
        committed = new LinkedHashMap<>(draft);
        draft = null;
        endDrag();
    }
    void cancel() { draft = null; numericValid = true; endDrag(); }
    void reset() {
        for (ScreenControlDefinition d : definitions) draft.put(d.id, d.defaultRect(width, height));
        numericValid = true;
        endDrag();
    }
    Handle hitHandle(float x, float y, float size) {
        ControlRect r = selectedRect();
        size = Math.min(size, Math.min(r.width, r.height) / 3f);
        for (Handle h : Handle.values()) {
            if (h == Handle.NONE || h == Handle.MOVE) continue;
            float[] center = center(r, h);
            // Handle centers sit inside the Rect, so handles remain reachable at screen edges.
            float cx = Math.max(r.x + size / 2, Math.min(center[0], r.right() - size / 2));
            float cy = Math.max(r.y + size / 2, Math.min(center[1], r.bottom() - size / 2));
            if (Math.abs(x - cx) <= size / 2 && Math.abs(y - cy) <= size / 2) return h;
        }
        return r.contains(x, y) ? Handle.MOVE : Handle.NONE;
    }
    static float[] center(ControlRect r, Handle h) {
        String name = h.name();
        return new float[] {name.contains("LEFT") ? r.x : name.contains("RIGHT") ? r.right() : r.x + r.width / 2f,
                name.contains("TOP") ? r.y : name.contains("BOTTOM") ? r.bottom() : r.y + r.height / 2f};
    }
    boolean startDrag(float x, float y, float size) {
        handle = hitHandle(x, y, size);
        if (handle == Handle.NONE) {
            for (Map.Entry<String, ControlRect> entry : draft.entrySet()) {
                if (entry.getValue().contains(x, y)) {
                    selected = entry.getKey();
                    handle = hitHandle(x, y, size);
                    break;
                }
            }
        }
        dragStart = selectedRect();
        startX = x;
        startY = y;
        if (handle != Handle.NONE) numericValid = true;
        return handle != Handle.NONE;
    }
    void drag(float x, float y) {
        if (handle == Handle.NONE) return;
        int dx = Math.round(x - startX), dy = Math.round(y - startY);
        ControlRect r = dragStart;
        ControlRect next;
        if (handle == Handle.MOVE) {
            next = new ControlRect(r.x + dx, r.y + dy, r.width, r.height).clamp(width, height);
        } else {
            String name = handle.name();
            int l = r.x, t = r.y, right = r.right(), bottom = r.bottom();
            if (name.contains("LEFT")) l = Math.max(0, Math.min(r.x + dx, right - ControlRect.MIN_SIZE));
            if (name.contains("RIGHT")) right = Math.max(l + ControlRect.MIN_SIZE, Math.min(r.right() + dx, width));
            if (name.contains("TOP")) t = Math.max(0, Math.min(r.y + dy, bottom - ControlRect.MIN_SIZE));
            if (name.contains("BOTTOM")) bottom = Math.max(t + ControlRect.MIN_SIZE, Math.min(r.bottom() + dy, height));
            next = new ControlRect(l, t, right - l, bottom - t);
        }
        draft.put(selected, next);
        numericValid = true;
    }
    void endDrag() { handle = Handle.NONE; dragStart = null; }
}
