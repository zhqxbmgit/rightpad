package com.rightpad.capture;

import android.app.Instrumentation;
import android.os.Bundle;
import android.os.SystemClock;
import android.view.MotionEvent;
import java.util.concurrent.atomic.AtomicReference;

/** Uses current production rectangles. Never writes or replaces the user's layout. */
public final class ModeDeviceSmoke extends Instrumentation {
    private String stage;
    private TouchCaptureView view;
    private long downTime;
    @Override public void onCreate(Bundle args) {super.onCreate(args);stage=args.getString("stage","mode");start();}
    private void ui(Runnable r) {
        AtomicReference<Throwable> error=new AtomicReference<>();runOnMainSync(()->{try{r.run();}catch(Throwable e){error.set(e);}});
        if(error.get()!=null)throw new AssertionError(error.get());
    }
    private void event(int action,float x,float y) {
        ui(()-> {
            long now=SystemClock.uptimeMillis();if(action==MotionEvent.ACTION_DOWN)downTime=now;
            var p=new MotionEvent.PointerProperties();p.id=0;p.toolType=MotionEvent.TOOL_TYPE_FINGER;
            var c=new MotionEvent.PointerCoords();c.x=x;c.y=y;c.pressure=action==MotionEvent.ACTION_UP?0:1;c.size=.1f;
            MotionEvent e=MotionEvent.obtain(downTime,now,action,1,new MotionEvent.PointerProperties[]{p},new MotionEvent.PointerCoords[]{c},0,0,1,1,0,0,android.view.InputDevice.SOURCE_TOUCHSCREEN,0);
            try {view.onTouchEvent(e);} finally {e.recycle();}
        });
    }
    private void pause(long ms) throws Exception {Thread.sleep(ms);}
    private void tap(float x,float y) throws Exception {event(MotionEvent.ACTION_DOWN,x,y);pause(65);event(MotionEvent.ACTION_UP,x,y);}
    @Override public void onStart() {
        Bundle result=new Bundle();int code=0;
        try {
            var monitor=addMonitor(MainActivity.class.getName(),null,false);
            try(var pipe=getUiAutomation().executeShellCommand("am start -n com.rightpad.capture/.MainActivity");var input=new java.io.FileInputStream(pipe.getFileDescriptor())) {while(input.read()!=-1){}}
            MainActivity activity=(MainActivity)waitForMonitorWithTimeout(monitor,10000);removeMonitor(monitor);
            if(activity==null)throw new AssertionError("Activity launch");waitForIdleSync();
            var f=MainActivity.class.getDeclaredField("captureView");f.setAccessible(true);view=(TouchCaptureView)f.get(activity);
            var sf=MainActivity.class.getDeclaredField("udpSender");sf.setAccessible(true);UdpTouchSender sender=(UdpTouchSender)sf.get(activity);
            long until=SystemClock.elapsedRealtime()+10000;while(!sender.canCapture() && SystemClock.elapsedRealtime()<until)pause(100);
            if(!sender.canCapture())throw new AssertionError("discovery / sender");pause(1500);
            var rect=view.controls.rects().get(ScreenControlRouter.MODE_ID);float x=rect.x+rect.width/2f,y=rect.y+rect.height/2f;
            android.util.Log.i("RightpadModeTest","stage="+stage+" run="+Long.toUnsignedString(sender.getSenderRunId(),16)+" modeRect="+rect.x+","+rect.y+","+rect.width+","+rect.height);
            switch(stage) {
                case "mode":
                    tap(x,y);pause(500);
                    event(MotionEvent.ACTION_DOWN,x,y);pause(1500);event(MotionEvent.ACTION_MOVE,view.getWidth()*.78f,y);pause(200);event(MotionEvent.ACTION_UP,view.getWidth()*.78f,y);pause(700);
                    tap(x,y);pause(700);break;
                case "toggle":
                    tap(x,y);event(MotionEvent.ACTION_UP,x,y);event(MotionEvent.ACTION_MOVE,x,y);pause(1600);tap(x,y);pause(700);break;
                case "cancel":
                    tap(x,y);pause(500);event(MotionEvent.ACTION_DOWN,x,y);pause(300);event(MotionEvent.ACTION_CANCEL,x,y);pause(700);break;
                case "bx":
                    for(String id:new String[]{"xbox.b.slide","xbox.x.slide_lr"}) {
                        var r=view.controls.rects().get(id);float cx=r.x+r.width/2f,cy=r.y+r.height/2f;
                        tap(cx,cy);pause(350);event(MotionEvent.ACTION_DOWN,cx,cy);pause(700);event(MotionEvent.ACTION_UP,cx,cy);pause(350);
                    }break;
                case "merge":
                    tap(x,y);pause(200);
                    for(String id:new String[]{"xbox.b.slide","xbox.x.slide_lr"}) {
                        var r=view.controls.rects().get(id);float cx=r.x+r.width/2f,cy=r.y+r.height/2f;
                        tap(cx,cy);pause(200);event(MotionEvent.ACTION_DOWN,cx,cy);pause(700);event(MotionEvent.ACTION_UP,cx,cy);pause(200);
                    }
                    tap(x,y);pause(500);break;
                case "mouse":
                    // A continuous mouse corridor in the live layout, away from Mode/B/X.
                    x=view.getWidth()*.78f;y=view.getHeight()*.55f;
                    if(ScreenControlRouter.controlAt(x,y,view.controls.rects())!=null)throw new AssertionError("mouse corridor occupied");
                    event(MotionEvent.ACTION_DOWN,x,y);for(int i=1;i<=8;i++){pause(20);event(MotionEvent.ACTION_MOVE,x+i*6,y+i*2);}event(MotionEvent.ACTION_UP,x+48,y+16);pause(700);break;
                case "click":
                    x=view.getWidth()*.78f;y=view.getHeight()*.55f;tap(x,y);pause(500);break;
                case "drag":
                    x=view.getWidth()*.78f;y=view.getHeight()*.55f;tap(x,y);pause(40);event(MotionEvent.ACTION_DOWN,x,y);pause(100);
                    for(int i=1;i<=12;i++){pause(20);event(MotionEvent.ACTION_MOVE,x+i*4,y);}event(MotionEvent.ACTION_UP,x+48,y);pause(500);break;
                default: throw new AssertionError("unknown stage");
            }
            result.putString("result","PASS stage="+stage+" current layout retained; injected MotionEvents, not human acceptance");code=-1;
        } catch(Throwable e) {result.putString("result","FAIL "+e);android.util.Log.e("RightpadModeTest","failure",e);}
        finally {if(view!=null)ui(()->view.stopCapture("mode_test_end"));}
        finish(code,result);
    }
}
