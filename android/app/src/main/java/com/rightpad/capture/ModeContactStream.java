package com.rightpad.capture;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.ArrayDeque;

/** Contact facts only. Receiver owns binding, toggle state and output. sendGate guards this object. */
final class ModeContactStream {
    static final int IDLE=0, DOWN=1, UP=2, CANCEL=3, CAPACITY=32;
    record Event(long sequence,long contact,int phase) { }
    record Reply(long run,long generation,long ack,boolean ready) { }
    private final ArrayDeque<Event> queue=new ArrayDeque<>();
    private long generation,sequence,contact,ack,sendAt,lastReplyAt;
    private boolean ready,touching,cancelling;
    private Event latest=new Event(0,0,IDLE);
    static boolean matches(byte[] b,int n) { return n>=4 && b[0]=='R' && b[1]=='P' && b[2]=='M' && b[3]=='D'; }
    static Reply decode(byte[] b,int n) {
        if(n!=32 || !matches(b,n) || b[4]!=1 || b[5]!=1 || b[6]!=0 || b[7]!=0 || b[28]<0 || b[28]>1 || b[29]!=0 || b[30]!=0 || b[31]!=0) return null;
        var v=ByteBuffer.wrap(b).order(ByteOrder.LITTLE_ENDIAN);
        long g=v.getLong(16);
        return g==0?null:new Reply(v.getLong(8),g,Integer.toUnsignedLong(v.getInt(24)),b[28]==1);
    }
    void reply(Reply r,long now) {
        if(r==null || Long.compareUnsigned(r.generation(),generation)<0) return;
        boolean changed=r.generation()!=generation || r.ack()!=ack || (r.ready() && !ready && !cancelling);
        if(r.generation()!=generation) {
            generation=r.generation();sequence=contact=ack=0;queue.clear();latest=new Event(0,0,IDLE);
            ready=touching=cancelling=false;sendAt=0;
        }
        if(r.ack()<ack || r.ack()>sequence) return;
        ack=r.ack();lastReplyAt=now;
        while(!queue.isEmpty() && queue.peekFirst().sequence()<=ack) latest=queue.removeFirst();
        ready=r.ready() && !cancelling;
        if(changed) sendAt=0;
    }
    boolean down(long now) {
        if(!ready || cancelling || touching || now-lastReplyAt>=600_000_000L) return false;
        touching=true;contact++;return add(DOWN);
    }
    void up() { if(touching) {touching=false;add(UP);} }
    void cancel() {
        touching=false;
        if(generation==0 || cancelling) return;
        queue.clear();cancelling=true;ready=false;
        latest=new Event(++sequence,contact,CANCEL);queue.add(latest);sendAt=0;
    }
    private boolean add(int phase) {
        if(queue.size()>=CAPACITY || sequence>=0xfffffffeL || contact>=0xfffffffeL) { cancel();return false; }
        queue.add(new Event(++sequence,contact,phase));sendAt=0;return true;
    }
    byte[] claim(long run,long now) {
        if(generation==0 || now<sendAt) return null;
        if(now-lastReplyAt>=600_000_000L && !cancelling) cancel();
        Event e=queue.isEmpty()?latest:queue.peekFirst();
        sendAt=now+(queue.isEmpty()?100_000_000L:20_000_000L);
        var b=ByteBuffer.allocate(32).order(ByteOrder.LITTLE_ENDIAN);
        b.put((byte)2).put((byte)8).putLong(run).putLong(generation).putInt((int)e.sequence()).putInt((int)e.contact()).put((byte)e.phase());
        return b.array();
    }
    long waitNanos(long now) { return generation==0?Long.MAX_VALUE:Math.max(0,sendAt-now); }
    int pending() {return queue.size();}
}
