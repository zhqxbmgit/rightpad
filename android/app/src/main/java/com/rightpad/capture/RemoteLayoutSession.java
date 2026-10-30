package com.rightpad.capture;

import java.io.IOException;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/** UI-thread transaction authority. The clock, persistence and publication are injectable. */
final class RemoteLayoutSession {
    static final int READY = 0, WAITING = 1, TRIAL = 2, SAVED = 3, RESTORED = 4, FAILED = 5;
    interface Save { void write(Map<String, ControlRect> layout) throws IOException; }
    private final long epoch;
    private final Save save;
    private final java.util.function.Consumer<Map<String, ControlRect>> publish;
    private final java.util.function.LongSupplier clock;
    private Map<String, ControlRect> formal, candidate;
    private List<RemoteLayoutProtocol.Item> descriptions, tools;
    private int width, height, state, error;
    private long geometry, revision = 1, transaction, serial, deadline, pendingDeadline;
    java.util.function.Predicate<Map<String,ControlRect>> geometryAllowed = ignored -> true;

    RemoteLayoutSession(long epoch, Save save, java.util.function.Consumer<Map<String, ControlRect>> publish) {
        this(epoch, save, publish, () -> Long.MIN_VALUE);
    }
    RemoteLayoutSession(long epoch, Save save, java.util.function.Consumer<Map<String, ControlRect>> publish,
            java.util.function.LongSupplier clock) {
        if (epoch == 0) throw new IllegalArgumentException("epoch");
        this.epoch = epoch; this.save = save; this.publish = publish;
        this.clock = clock;
    }
    void geometry(int width, int height, Map<String, ControlRect> layout,
            List<RemoteLayoutProtocol.Item> descriptions, List<RemoteLayoutProtocol.Item> tools) {
        // The View already loaded the formal file at its new size. Never publish old pixels here.
        candidate = null; deadline = 0;
        this.width = width; this.height = height; this.formal = new LinkedHashMap<>(layout);
        this.descriptions = List.copyOf(descriptions); this.tools = List.copyOf(tools);
        geometry++; revision++; state = READY; error = 0;
    }
    void localSaved(Map<String, ControlRect> layout) {
        // Local editor entry aborts the remote trial first. Its Save shares this revision domain.
        abort(); formal = new LinkedHashMap<>(layout); revision++; state = READY; error = 0;
    }
    boolean active() { return state == WAITING || state == TRIAL; }
    long waitMillis(long now) { return state == TRIAL ? Math.min(100, Math.max(1, deadline-now)) : 100; }
    void abort() { if (active()) restore(6); }
    private void restore(int reason) {
        if (state == TRIAL) publish.accept(new LinkedHashMap<>(formal));
        candidate = null; state = RESTORED; error = reason; deadline = 0;
    }
    void tick(long now, boolean idle) {
        if (state == WAITING && now >= pendingDeadline) restore(5);
        if (state == WAITING && idle) {
            if(!geometryAllowed.test(candidate)) {restore(2);state=FAILED;return;}
            publish.accept(new LinkedHashMap<>(candidate));
            state = TRIAL; deadline = now + 60000;
        }
        if (state == TRIAL && now >= deadline) restore(5);
    }
    RemoteLayoutProtocol.Message handle(RemoteLayoutProtocol.Message m, long now, boolean idle, boolean editor) {
        tick(now, idle);
        int responseError = 0;
        // A timed-out first Try may still be in the network. Reconciliation consumes its
        // unused token before reporting "not applied", so a late copy cannot apply later.
        if (m.kind() == RemoteLayoutProtocol.QUERY && m.transaction() == transaction + 1 &&
                m.epoch() == epoch && m.revision() == revision && m.geometry() == geometry && !active()) {
            transaction = m.transaction(); state = RESTORED; error = 6;
        }
        if (m.kind() != RemoteLayoutProtocol.QUERY) {
            if (m.epoch() != epoch || m.geometry() != geometry || m.revision() != revision) {
                // A duplicate KEEP after a successful commit must still report the committed result.
                if (!(m.kind() == RemoteLayoutProtocol.KEEP && m.epoch() == epoch &&
                        m.transaction() == transaction && state == SAVED)) responseError = 1;
            } else if (m.transaction() == transaction && transaction != 0) {
                if (m.kind() == RemoteLayoutProtocol.REVERT && active()) restore(6);
                else if (m.kind() == RemoteLayoutProtocol.KEEP && state == TRIAL) {
                    if (Math.max(now, clock.getAsLong()) >= deadline) {
                        restore(5);
                        return snapshot(m.run(), m.request(), Math.max(now, clock.getAsLong()), 0);
                    }
                    // Entry to synchronous atomic Save and timeout are serialized on the UI thread.
                    // No timeout callback may run between successful replacement and state publication.
                    try { save.write(candidate); formal = new LinkedHashMap<>(candidate); candidate = null;
                        revision++; state = SAVED; error = 0; deadline = 0;
                    } catch (IOException | RuntimeException failed) { restore(4); state = FAILED; }
                }
            } else if (m.transaction() == transaction + 1 && !active() && !editor &&
                    (m.kind() == RemoteLayoutProtocol.TRY || m.kind() == RemoteLayoutProtocol.REVERT)) {
                transaction = m.transaction();
                if (m.kind() == RemoteLayoutProtocol.REVERT) { state = RESTORED; error = 6; }
                else if (!valid(m)) { state = FAILED; error = 2; }
                else {
                    candidate = new LinkedHashMap<>();
                    for (var item : m.items()) candidate.put(item.id(), item.rect());
                    state = WAITING; error = 0; pendingDeadline = now + 10000;
                    tick(now, idle);
                }
            } else responseError = editor || active() ? 3 : 1;
        }
        return snapshot(m.run(), m.request(), now, responseError);
    }
    private boolean valid(RemoteLayoutProtocol.Message m) {
        if (m.width() != width || m.height() != height || m.items().size() != formal.size()) return false;
        var rects = new ArrayList<ControlRect>();
        for (var item : m.items()) {
            if (item.flags() != 0 || !formal.containsKey(item.id()) || !item.rect().valid(width, height)) return false;
            rects.add(item.rect());
        }
        var proposed=new LinkedHashMap<String,ControlRect>();
        for(var item:m.items()) proposed.put(item.id(),item.rect());
        if(!geometryAllowed.test(proposed)) return false;
        for (var tool : tools) rects.add(tool.rect());
        // Exact union coverage, allowing overlaps and preserving existing priority.
        var xs = new java.util.TreeSet<Integer>(); xs.add(0); xs.add(width);
        for (var r : rects) { xs.add(r.x); xs.add(r.right()); }
        Integer previous = null;
        for (int x : xs) {
            if (previous != null && x > previous) {
                final int left = previous, right = x;
                var spans = rects.stream().filter(r -> r.x <= left && r.right() >= right)
                        .sorted(java.util.Comparator.comparingInt(r -> r.y)).toList();
                int covered = 0;
                for (var r : spans) { if (r.y > covered) break; covered = Math.max(covered, r.bottom()); }
                if (covered < height) return true;
            }
            previous = x;
        }
        return false;
    }
    RemoteLayoutProtocol.Message snapshot(long run, long request, long now, int responseError) {
        var items = new ArrayList<RemoteLayoutProtocol.Item>();
        if (formal != null) {
            Map<String, ControlRect> effective = state == TRIAL ? candidate : formal;
            for (var d : descriptions) items.add(new RemoteLayoutProtocol.Item(d.id(), d.label(), 0, effective.get(d.id())));
            items.addAll(tools);
        }
        return new RemoteLayoutProtocol.Message(RemoteLayoutProtocol.SNAPSHOT, state,
                responseError == 0 ? error : responseError, run, epoch, revision, geometry,
                transaction, request, transaction + 1, ++serial,
                state == TRIAL ? (int)Math.max(0, deadline - now) : 0, width, height, List.copyOf(items));
    }
}
