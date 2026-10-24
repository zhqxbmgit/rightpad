package com.rightpad.capture;

import android.os.Handler;
import java.io.IOException;
import java.net.*;
import java.nio.*;
import java.util.*;
import java.util.concurrent.atomic.*;
import java.util.function.BooleanSupplier;

public final class InputReliabilityTests {
    static int groups;
    static void check(boolean ok,String reason){if(!ok)throw new AssertionError(reason);}
    static void until(BooleanSupplier condition) throws Exception {
        long deadline=System.nanoTime()+4_000_000_000L;
        while(!condition.getAsBoolean()){check(System.nanoTime()<deadline,"condition timeout");Thread.sleep(5);}
    }
    static byte[] next(DatagramSocket s,int type) throws Exception {return GamepadSenderTests.next(s,type);}
    static TouchCancellation.Ack identity(byte[] c){var b=ByteBuffer.wrap(c).order(ByteOrder.LITTLE_ENDIAN);return new TouchCancellation.Ack(b.getLong(2),Integer.toUnsignedLong(b.getInt(10)),Integer.toUnsignedLong(b.getInt(14)),Integer.toUnsignedLong(b.getInt(18)));}
    static void cancellation() throws Exception {
        try(var receiver=new DatagramSocket(0,InetAddress.getLoopbackAddress());var sender=new UdpTouchSender("127.0.0.1",receiver.getLocalPort())) {
            receiver.setSoTimeout(2500);sender.setForeground(true);next(receiver,4);long run=sender.getSenderRunId();
            sender.submit(UdpTouchSenderTest.touch(TouchSample.Action.DOWN));next(receiver,1);next(receiver,1);next(receiver,1);
            sender.cancelTouch();byte[] c=next(receiver,9);check(!sender.canCapture(),"cancel fences new capture");
            check(Arrays.equals(c,next(receiver,9)),"lost cancel or ACK retries same identity");
            var ack=identity(c);sender.cancelReply(new TouchCancellation.Ack(run+1,ack.session(),ack.down(),ack.sequence()));
            check(!sender.canCapture(),"wrong run ACK cannot unlock");sender.cancelReply(ack);until(sender::canCapture);
            sender.submit(UdpTouchSenderTest.touch(TouchSample.Action.DOWN));byte[] d=next(receiver,1);
            check(UdpTouchSenderTest.sequence(d)==ack.sequence()+1,"new DOWN after cancellation fence");next(receiver,1);next(receiver,1);
            sender.cancelReply(ack);check(sender.canCapture(),"late ACK cannot affect next contact");
            sender.cancelTouch();byte[] c2=next(receiver,9);check(identity(c2).down()>ack.sequence(),"new contact cancellation identity");
            // Drop every cancellation/ACK; worker must stop presence and request a new run.
            until(Handler::hasPending);check(!sender.canCapture(),"no replay before UI resync");
            Handler.drain();until(()->sender.getSenderRunId()!=run);check(sender.canCapture(),"fresh run resumes capture");
            byte[] hb;do {hb=next(receiver,4);}while(UdpTouchSenderTest.run(hb)==run);
            check(UdpTouchSenderTest.run(hb)==sender.getSenderRunId(),"resync heartbeat has new identity");
        }
        groups++;System.out.println("PASS cancellation actual Sender retries/ACK/barrier/exhaustion/fresh-run recovery");
    }
    static void heartbeat() throws Exception {
        // Force a heartbeat deadline specifically between dequeue and actual Touch send.
        for(TouchSample.Action action:new TouchSample.Action[]{TouchSample.Action.DOWN,TouchSample.Action.MOVE,TouchSample.Action.UP}) {
            AtomicBoolean delay=new AtomicBoolean(),fail=new AtomicBoolean();AtomicInteger attempts=new AtomicInteger();
            Runnable barrier=()->{if(delay.getAndSet(false))try{Thread.sleep(550);}catch(InterruptedException e){throw new AssertionError(e);}};
            try(var receiver=new DatagramSocket(0,InetAddress.getLoopbackAddress());var sender=new UdpTouchSender(new InetSocketAddress(InetAddress.getLoopbackAddress(),receiver.getLocalPort()),barrier,(socket,p)->{
                if(p.getData()[1]==4 && fail.get()){attempts.incrementAndGet();throw new IOException("periodic injected");}socket.send(p);
            })) {
                receiver.setSoTimeout(2500);sender.setForeground(true);next(receiver,4);
                if(action!=TouchSample.Action.DOWN){sender.submit(UdpTouchSenderTest.touch(TouchSample.Action.DOWN));next(receiver,1);next(receiver,1);next(receiver,1);}
                fail.set(true);delay.set(true);sender.submit(UdpTouchSenderTest.touch(action));
                int type=action==TouchSample.Action.DOWN?1:action==TouchSample.Action.MOVE?2:3;
                byte[] first=next(receiver,type);check(attempts.get()>0,"heartbeat failed at dequeue boundary");
                if(type!=2)check(Arrays.equals(first,next(receiver,type)) && Arrays.equals(first,next(receiver,type)),"triplicate preserved");
            }
        }
        AtomicInteger initial=new AtomicInteger(),touches=new AtomicInteger();
        try(var receiver=new DatagramSocket(0,InetAddress.getLoopbackAddress());var sender=new UdpTouchSender(new InetSocketAddress(InetAddress.getLoopbackAddress(),receiver.getLocalPort()),null,(socket,p)->{
            if(p.getData()[1]==4){initial.incrementAndGet();throw new IOException("initial injected");}
            if(p.getData()[1]>=1 && p.getData()[1]<=3)touches.incrementAndGet();socket.send(p);
        })) {
            sender.setForeground(true);until(()->initial.get()>0);sender.submit(UdpTouchSenderTest.touch(TouchSample.Action.DOWN));
            Thread.sleep(650);check(touches.get()==0,"no Touch before first heartbeat admission");check(initial.get()<=3,"initial failure bounded cadence");
            sender.setForeground(false);sender.setTarget(null);Handler.drain();check(!sender.canCapture(),"pause/route change fence resync");
        }
        groups++;System.out.println("PASS initial heartbeat safety abort and periodic failure attempts dequeued DOWN/MOVE/UP exact copies");
    }
    static void dispatch(DatagramSocket sender,int port,byte[] b) throws Exception {
        sender.send(new DatagramPacket(b,b.length,InetAddress.getLoopbackAddress(),port));until(Handler::hasPending);Handler.drain();
    }
    static byte[] click(){return ByteBuffer.allocate(24).order(ByteOrder.LITTLE_ENDIAN).put(new byte[]{'R','P','H','F',1,1,0,0}).putLong(7).putInt(1).putInt(1).array();}
    static void listener() throws Exception {
        AtomicInteger opens=new AtomicInteger();AtomicReference<DatagramSocket> active=new AtomicReference<>();int[] counts=new int[6];
        try(var holder=new DatagramSocket(0);var sender=new DatagramSocket()) {
            int port=holder.getLocalPort();
            try(var listener=new HapticFeedbackListener(()->counts[0]++,s->counts[1]++,(s,at)->counts[2]++,port,()->{
                opens.incrementAndGet();var socket=new DatagramSocket(null);active.set(socket);return socket;
            })) {
                listener.layouts=b->counts[3]++;listener.modes=r->counts[4]++;listener.cancellations=r->counts[5]++;
                listener.setActive(InetAddress.getLoopbackAddress(),7);Thread.sleep(650);
                check(opens.get()>=3 && opens.get()<=4,"bind failure exponential backoff");holder.close();
                until(()->active.get()!=null && active.get().isBound() && !active.get().isClosed());
                listener.expectUp(7,1,1);dispatch(sender,port,click());check(counts[0]==1,"first feedback");
                int previous=opens.get();final int firstOpen=previous;active.get().close();until(()->opens.get()>firstOpen && active.get().isBound() && !active.get().isClosed());
                dispatch(sender,port,click());check(counts[0]==1,"rebuild preserves consumed click dedupe");
                dispatch(sender,port,ControlConfigTests.v2(1));dispatch(sender,port,InputHealthTests.packet(7));
                dispatch(sender,port,RemoteLayoutProtocol.encode(new RemoteLayoutProtocol.Message(1,0,0,7,1,1,1,0,1,0,0,0,100,100,List.of())));
                byte[] mode=ByteBuffer.allocate(32).order(ByteOrder.LITTLE_ENDIAN).put(new byte[]{'R','P','M','D',1,1,0,0}).putLong(7).putLong(1).putInt(0).put((byte)1).array();
                dispatch(sender,port,mode);
                byte[] cancel=ByteBuffer.allocate(28).order(ByteOrder.LITTLE_ENDIAN).put(new byte[]{'R','P','T','C',1,1,0,0}).putLong(7).putInt(1).putInt(0).putInt(1).array();dispatch(sender,port,cancel);
                check(Arrays.equals(counts,new int[]{1,1,1,1,1,1}),"all five existing branches and cancellation ACK recover");
                // Pending UI callback from a failed socket must not execute after rebuild.
                listener.expectUp(7,2,2);byte[] queued=click();ByteBuffer.wrap(queued).order(ByteOrder.LITTLE_ENDIAN).putInt(16,2).putInt(20,2);
                sender.send(new DatagramPacket(queued,queued.length,InetAddress.getLoopbackAddress(),port));until(Handler::hasPending);
                previous=opens.get();active.get().close();final int before=previous;
                until(()->opens.get()>before && active.get().isBound() && !active.get().isClosed());Handler.drain();check(counts[0]==1,"failed socket callbacks fenced");
                listener.setActive(null,0);int stopped=opens.get();Thread.sleep(300);check(opens.get()==stopped,"pause stops retries");
                listener.setActive(InetAddress.getLoopbackAddress(),8);until(()->opens.get()>stopped);
                listener.setActive(null,0);
            }
        }
        // Persistent factory failures and identity replacement/close interrupt long waits.
        AtomicInteger failures=new AtomicInteger();
        var listener=new HapticFeedbackListener(()->{},s->{},(s,at)->{},0,()->{failures.incrementAndGet();throw new SecurityException("factory failure");});
        listener.setActive(InetAddress.getLoopbackAddress(),7);Thread.sleep(750);check(failures.get()<=4,"security failure backoff");
        listener.setActive(InetAddress.getByName("127.0.0.2"),8);until(()->failures.get()>=4);listener.close();int stopped=failures.get();Thread.sleep(250);check(failures.get()==stopped,"close cannot revive identity");
        groups++;System.out.println("PASS shared listener bind/receive/security failure recovery, all branches, dedupe and lifecycle fences");
    }
    public static void main(String[] args) throws Exception {
        android.util.Log.expectedNetworkFailures=true;
        try {cancellation();heartbeat();listener();System.out.println("RESULT InputReliability groups="+groups+" passed="+groups+" failed=0");}
        finally {android.util.Log.expectedNetworkFailures=false;}
    }
}
