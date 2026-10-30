package com.rightpad.capture;

import android.os.SystemClock;
import java.util.ArrayList;
import java.util.List;

/** Uses the View dispatcher and the existing Sender worker; no transport thread. */
final class RemoteLayoutBridge {
    private final TouchCaptureView view;
    private final UdpTouchSender sender;
    final RemoteLayoutSession session;
    private String environment = "";
    private long localRevision;
    private int lastWidth, lastHeight;
    private final Runnable wake = this::tick;

    RemoteLayoutBridge(TouchCaptureView view, UdpTouchSender sender) {
        this.view = view; this.sender = sender;
        long random = java.util.concurrent.ThreadLocalRandom.current().nextLong(1, Long.MAX_VALUE);
        session = new RemoteLayoutSession(random,
                layout -> view.controls.store.save(layout, view.getWidth(), view.getHeight()),
                layout -> {
                    view.stopCapture("remote_layout_changed");
                    view.controls.layout = new ScreenControlLayoutEditor(view.controls.definitions,
                            layout, view.getWidth(), view.getHeight());
                    view.refreshModeGeometry(true);
                    view.invalidate();
                }, SystemClock::elapsedRealtime);
        session.geometryAllowed = layout -> view.modeLayoutAllowed(layout.get(ScreenControlRouter.MODE_ID));
        view.beforeRemoteDown = () -> session.tick(SystemClock.elapsedRealtime(), view.remoteIdle());
        view.remoteGeometryChanged = this::sync;
    }
    private boolean sync() {
        var controls = view.controls;
        if (controls.layout == null) return false;
        List<RemoteLayoutProtocol.Item> tools = view.remoteTools();
        String key = view.getWidth() + ":" + view.getHeight();
        for (var t : tools) { var r = t.rect(); key += ":" + r.x + ":" + r.y + ":" + r.width + ":" + r.height; }
        if (!key.equals(environment)) {
            if (lastWidth == view.getWidth() && lastHeight == view.getHeight()) session.abort();
            var descriptions = new ArrayList<RemoteLayoutProtocol.Item>();
            for (var d : controls.definitions) descriptions.add(new RemoteLayoutProtocol.Item(d.id, d.label, 0,
                    controls.layout.committedLayout().get(d.id)));
            session.geometry(view.getWidth(), view.getHeight(), controls.layout.committedLayout(), descriptions, tools);
            environment = key; localRevision = controls.localLayoutRevision;
            lastWidth = view.getWidth(); lastHeight = view.getHeight();
        } else if (localRevision != controls.localLayoutRevision) {
            session.localSaved(controls.layout.committedLayout()); localRevision = controls.localLayoutRevision;
        }
        return true;
    }
    void receive(byte[] bytes) {
        var m = RemoteLayoutProtocol.decode(bytes, bytes.length);
        if (m == null || m.kind() == RemoteLayoutProtocol.SNAPSHOT || m.run() != sender.getSenderRunId() || !sync()) return;
        if(m.kind()!=RemoteLayoutProtocol.QUERY) {view.stopCapture("layout_transaction");view.refreshModeGeometry(true);}
        var result = session.handle(m, SystemClock.elapsedRealtime(), view.remoteIdle(), view.controls.editing());
        sender.submitLayout(RemoteLayoutProtocol.encode(result));
        schedule();
    }
    void abort() { view.removeCallbacks(wake); session.abort(); }
    private void tick() {
        if (!sync()) { abort(); return; }
        session.tick(SystemClock.elapsedRealtime(), view.remoteIdle());
        if (sender.canCapture()) sender.submitLayout(RemoteLayoutProtocol.encode(
                session.snapshot(sender.getSenderRunId(), 0, SystemClock.elapsedRealtime(), 0)));
        schedule();
    }
    private void schedule() {
        view.removeCallbacks(wake);
        if (session.active()) view.postDelayed(wake, session.waitMillis(SystemClock.elapsedRealtime()));
    }
}
