package com.rightpad.capture;

import android.app.Instrumentation;
import android.content.Intent;
import android.os.Bundle;
import android.os.SystemClock;
import android.view.MotionEvent;
import java.io.File;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.atomic.AtomicReference;

/** Real View/store checks. The original file bytes are restored even after a failed assertion. */
public final class RemoteLayoutSmoke extends Instrumentation {
    private int checks;
    private void check(boolean v, String name) { checks++; if (!v) throw new AssertionError(name); }
    @Override public void onCreate(Bundle arguments) { super.onCreate(arguments); start(); }
    private void ui(Runnable r) {
        AtomicReference<Throwable> error = new AtomicReference<>();
        runOnMainSync(() -> { try { r.run(); } catch (Throwable e) { error.set(e); } });
        if (error.get()!=null) throw new AssertionError(error.get());
    }
    @Override public void onStart() {
        Bundle result = new Bundle(); int code = -1;
        File file = new File(getTargetContext().getFilesDir(), "screen-controls.properties");
        MainActivity activity = null;
        try (LayoutTestFileGuard guard = new LayoutTestFileGuard(file.toPath(), getTargetContext().getFilesDir().toPath())) {
            guard.capture(); check(guard.canMutate(), "backup required before mutation");
            byte[] originalBytes = java.nio.file.Files.readAllBytes(file.toPath());
            ActivityMonitor launch = addMonitor(MainActivity.class.getName(), null, false);
            // Match the existing smoke runner: HyperOS requires the authorized shell launch path.
            try (var pipe = getUiAutomation().executeShellCommand("am start -n com.rightpad.capture/.MainActivity");
                    var input = new java.io.FileInputStream(pipe.getFileDescriptor())) {
                while (input.read() != -1) { }
            }
            activity = (MainActivity)waitForMonitorWithTimeout(launch, 10000);
            removeMonitor(launch);
            check(activity != null, "bounded Activity launch");
            waitForIdleSync();
            var field = MainActivity.class.getDeclaredField("captureView"); field.setAccessible(true);
            TouchCaptureView view = (TouchCaptureView)field.get(activity);
            var remoteField = MainActivity.class.getDeclaredField("remoteLayout"); remoteField.setAccessible(true);
            RemoteLayoutBridge bridge = (RemoteLayoutBridge)remoteField.get(activity);
            var senderField = MainActivity.class.getDeclaredField("udpSender"); senderField.setAccessible(true);
            UdpTouchSender sender = (UdpTouchSender)senderField.get(activity);
            long end = SystemClock.elapsedRealtime()+7000;
            while (!sender.canCapture() && SystemClock.elapsedRealtime()<end) Thread.sleep(100);
            check(sender.canCapture(),"natural discovery and input route");
            var original = new java.util.LinkedHashMap<>(view.controls.rects());
            long run = sender.getSenderRunId();
            ui(() -> bridge.receive(RemoteLayoutProtocol.encode(new RemoteLayoutProtocol.Message(1,0,0,run,0,0,0,0,1,0,0,0,0,0,List.of()))));
            check(java.util.Arrays.equals(originalBytes,java.nio.file.Files.readAllBytes(file.toPath())),"real read does not write");
            var s = bridge.session.snapshot(run,2,SystemClock.elapsedRealtime(),0);
            var oldGeometry = s;
            var geometryTrial = new RemoteLayoutProtocol.Message(2,0,0,run,s.epoch(),s.revision(),s.geometry(),s.next(),20,0,0,0,s.width(),s.height(),
                    s.items().stream().filter(i -> i.flags()==0).toList());
            ui(() -> {
                int width=view.getWidth(), height=view.getHeight();
                view.layout(0,0,width,height-1);
                check(bridge.session.snapshot(run,0,SystemClock.elapsedRealtime(),0).geometry()>oldGeometry.geometry(),
                        "View resize fences geometry immediately before another DOWN or scheduled wake");
                check(bridge.session.handle(geometryTrial,SystemClock.elapsedRealtime(),true,false).error()==1,
                        "old complete candidate cannot enter a resized View");
                view.layout(0,0,width,height);
            });
            s = bridge.session.snapshot(run,2,SystemClock.elapsedRealtime(),0);
            List<RemoteLayoutProtocol.Item> candidate = new ArrayList<>();
            for (var item:s.items()) if(item.flags()==0) {
                var r=item.rect();
                if(item.id().equals("xbox.b.slide")) r=new ControlRect(r.x>10?r.x-10:r.x+10,r.y,r.width,r.height);
                candidate.add(new RemoteLayoutProtocol.Item(item.id(),item.label(),0,r));
            }
            var trial = new RemoteLayoutProtocol.Message(2,0,0,run,s.epoch(),s.revision(),s.geometry(),s.next(),3,0,0,0,s.width(),s.height(),candidate);
            // Hold a registered control while request arrives: its rectangle and ownership remain original.
            ControlRect b=original.get("xbox.b.slide");
            long time=SystemClock.uptimeMillis();
            ui(() -> { MotionEvent down=MotionEvent.obtain(time,time,MotionEvent.ACTION_DOWN,b.x+b.width/2f,b.y+100,0);
                view.onTouchEvent(down); down.recycle(); bridge.receive(RemoteLayoutProtocol.encode(trial));
                check(bridge.session.snapshot(run,0,SystemClock.elapsedRealtime(),0).state()==1,"wait for current contact");
                check(view.controls.rects().get("xbox.b.slide").x==b.x,"no mid-contact geometry change"); });
            ui(() -> { MotionEvent up=MotionEvent.obtain(time,SystemClock.uptimeMillis(),MotionEvent.ACTION_UP,b.x+b.width/2f,b.y+100,0);
                view.onTouchEvent(up); up.recycle(); bridge.session.tick(SystemClock.elapsedRealtime(),view.remoteIdle()); });
            check(bridge.session.snapshot(run,0,SystemClock.elapsedRealtime(),0).state()==2,"actual trial applied");
            ui(() -> { var live=view.controls.rects().get("xbox.b.slide");
                check(live.x==candidate.get(0).rect().x,"View draw and router share applied rect map");
                check(ScreenControlRouter.controlAt(live.x+1,live.y+1,view.controls.rects()).equals("xbox.b.slide"),"actual router hit uses same rect"); });
            check(java.util.Arrays.equals(originalBytes,java.nio.file.Files.readAllBytes(file.toPath())),"trial never persists");
            ui(() -> bridge.session.tick(SystemClock.elapsedRealtime()+60001,false));
            check(view.controls.rects().get("xbox.b.slide").x==b.x,"local expiry restores every original coordinate");
            // Real atomic Save, then load via the production store, all inside the file guard.
            s=bridge.session.snapshot(run,0,SystemClock.elapsedRealtime(),0);
            var second = new RemoteLayoutProtocol.Message(2,0,0,run,s.epoch(),s.revision(),s.geometry(),s.next(),4,0,0,0,s.width(),s.height(),candidate);
            ui(() -> bridge.receive(RemoteLayoutProtocol.encode(second)));
            s=bridge.session.snapshot(run,0,SystemClock.elapsedRealtime(),0);
            var keep=new RemoteLayoutProtocol.Message(3,0,0,run,s.epoch(),s.revision(),s.geometry(),s.transaction(),5,0,0,0,0,0,List.of());
            ui(() -> bridge.receive(RemoteLayoutProtocol.encode(keep)));
            check(bridge.session.snapshot(run,0,SystemClock.elapsedRealtime(),0).state()==3,"real store save confirmed");
            var reloaded=view.controls.store.load(view.controls.definitions,view.getWidth(),view.getHeight());
            check(reloaded.get("xbox.b.slide").x==candidate.get(0).rect().x,"saved ratios reload exact integers");
            // An actual filesystem failure must revert rather than report saved.
            File temporary=new File(file.getPath()+".tmp");
            check(!temporary.exists(),"no prior temporary data"); check(temporary.mkdir(),"inject directory at temporary file path");
            try {
                s=bridge.session.snapshot(run,0,SystemClock.elapsedRealtime(),0);
                var third=new RemoteLayoutProtocol.Message(2,0,0,run,s.epoch(),s.revision(),s.geometry(),s.next(),6,0,0,0,s.width(),s.height(),
                        original.entrySet().stream().map(e -> new RemoteLayoutProtocol.Item(e.getKey(),e.getKey(),0,e.getValue())).toList());
                ui(() -> bridge.receive(RemoteLayoutProtocol.encode(third)));
                s=bridge.session.snapshot(run,0,SystemClock.elapsedRealtime(),0);
                var failing=new RemoteLayoutProtocol.Message(3,0,0,run,s.epoch(),s.revision(),s.geometry(),s.transaction(),7,0,0,0,0,0,List.of());
                ui(() -> bridge.receive(RemoteLayoutProtocol.encode(failing)));
                var failure=bridge.session.snapshot(run,0,SystemClock.elapsedRealtime(),0);
                check(failure.state()==5 && failure.error()==4,"real IOException reports failure and restores preceding formal");
            } finally { check(temporary.delete(),"remove injected empty directory"); }
            ui(bridge::abort);
            ActivityMonitor recreate = addMonitor(MainActivity.class.getName(), null, false);
            MainActivity prior = activity;
            ui(prior::recreate);
            activity = (MainActivity)waitForMonitorWithTimeout(recreate, 5000);
            removeMonitor(recreate);
            check(activity != null, "Activity recreated");
            waitForIdleSync();
            TouchCaptureView restoredView = (TouchCaptureView)field.get(activity);
            ui(() -> check(restoredView.controls.rects().get("xbox.b.slide").x == candidate.get(0).rect().x,
                    "fresh Activity loads successfully saved formal file, no trial state"));
            guard.restore();
            check(java.util.Arrays.equals(originalBytes,java.nio.file.Files.readAllBytes(file.toPath())),"finally restores exact original bytes");
            result.putString("stream","PASS RemoteLayoutSmoke checks="+checks+"; actual View, router, atomic file Save, disk failure, byte restoration\n");
            code=-1;
        } catch(Throwable error) { code=0; result.putString("stream","FAIL RemoteLayoutSmoke checks="+checks+" "+android.util.Log.getStackTraceString(error)); }
        finally { if(activity!=null) { MainActivity ending=activity; runOnMainSync(ending::finish); } }
        finish(code,result);
    }
}
