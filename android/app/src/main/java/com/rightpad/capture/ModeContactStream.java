package com.rightpad.capture;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.ArrayDeque;

/** Bounded ordered contact facts. Receiver alone owns bindings and output. sendGate guards this object. */
final class ModeContactStream {
    static final int IDLE=0, DOWN=1, UP=2, CANCEL=3, CAPACITY=32;
    record Event(long sequence,long contact,int phase,long revision,int selection,ModeFixedRegions.Geometry geometry) {
        Event(long sequence,long contact,int phase) { this(sequence,contact,phase,0,0,null); }
    }
    record Reply(long run,long generation,long ack,boolean ready,int version,int purpose,float firstDp,float secondDp,long revision,long geometry) {
        Reply(long run,long generation,long ack,boolean ready) { this(run,generation,ack,ready,1,0,8,10,0,0); }
    }
    private final ArrayDeque<Event> queue=new ArrayDeque<>();
    private long generation,sequence,contact,ack,sendAt,lastReplyAt,activeRevision;
    private boolean ready,touching,cancelling,extended;
    private Event latest=new Event(0,0,IDLE);
    private Reply config;
    private ModeFixedRegions.Geometry geometry;
    private ModeFixedRegions regions;
    static boolean matches(byte[] b,int n) { return n>=4 && b[0]=='R' && b[1]=='P' && b[2]=='M' && b[3]=='D'; }
    static Reply decode(byte[] b,int n) {
        if(!matches(b,n) || (n!=32 && n!=56) || b[5]!=1 || b[6]!=0 || b[7]!=0 || b[28]<0 || b[28]>1) return null;
        var v=ByteBuffer.wrap(b).order(ByteOrder.LITTLE_ENDIAN);
        long g=v.getLong(16);
        if(g==0) return null;
        if(n==32) {
            if(b[4]!=1 || b[29]!=0 || b[30]!=0 || b[31]!=0) return null;
            return new Reply(v.getLong(8),g,Integer.toUnsignedLong(v.getInt(24)),b[28]==1);
        }
        float first=Short.toUnsignedInt(v.getShort(32))/2f,second=Short.toUnsignedInt(v.getShort(34))/2f;
        long rev=v.getLong(40);
        if(b[4]!=2 || b[29]<0 || b[29]>2 || b[30]!=0 || b[31]!=0 || v.getInt(36)!=0 ||
                rev==0 || !ModeFixedRegions.widthValid(first) || !ModeFixedRegions.widthValid(second)) return null;
        return new Reply(v.getLong(8),g,Integer.toUnsignedLong(v.getInt(24)),b[28]==1,2,b[29],first,second,rev,v.getLong(48));
    }
    void reply(Reply r,long now) {
        if(r==null || extended && r.version()==1 || Long.compareUnsigned(r.generation(),generation)<0) return;
        if(config!=null && r.generation()==generation && Long.compareUnsigned(r.revision(),config.revision())<0) return;
        boolean changed=r.generation()!=generation || r.ack()!=ack || (r.ready() && !ready && !cancelling);
        if(r.generation()!=generation) {
            generation=r.generation();sequence=contact=ack=0;queue.clear();latest=new Event(0,0,IDLE);
            ready=touching=cancelling=false;regions=null;sendAt=0;
        }
        if(r.ack()<ack || r.ack()>sequence) return;
        boolean upgrade=r.version()==2 && !extended;
        if(r.version()==2) { extended=true;config=r; }
        if(upgrade && sequence!=0) cancel();
        ack=r.ack();lastReplyAt=now;
        while(!queue.isEmpty() && queue.peekFirst().sequence()<=ack) latest=queue.removeFirst();
        ready=r.ready() && !cancelling;
        if(changed) sendAt=0;
    }
    void geometry(ModeFixedRegions.Geometry next) {
        if(java.util.Objects.equals(geometry,next)) return;
        if(extended) cancel();
        geometry=next;
    }
    boolean layoutAllowed(ModeFixedRegions.Geometry next) {
        return config==null || config.purpose()!=2 || next!=null && next.fits(config.firstDp(),config.secondDp());
    }
    String layoutError(ModeFixedRegions.Geometry next) {
        return config==null || config.purpose()!=2 ? null : next==null ? "Mode 输入坐标空间未就绪" : next.error(config.firstDp(),config.secondDp());
    }
    boolean down(long now,float x,float y) {
        if(extended && (geometry==null || !geometry.valid() || config==null || config.geometry()!=geometry.identity() ||
                config.purpose()==2 && (!geometry.fits(config.firstDp(),config.secondDp()) || !geometry.mode().contains(x,y)))) return false;
        boolean accepted=down(now);
        if(accepted && extended && config.purpose()==2) regions=new ModeFixedRegions(geometry,config.firstDp(),config.secondDp());
        return accepted;
    }
    boolean down(long now) {
        if(!ready || cancelling || touching || now-lastReplyAt>=600_000_000L) return false;
        if(extended && (config==null || geometry==null || config.geometry()!=geometry.identity() || !layoutAllowed(geometry))) return false;
        activeRevision=extended?config.revision():0;
        touching=true;contact++;return add(DOWN);
    }
    void beginEvent() { if(regions!=null) regions.beginEvent(); }
    void sample(float x,float y,boolean feedback) { if(touching && regions!=null) regions.sample(x,y,feedback); }
    boolean eventHint() { return touching && regions!=null && regions.eventHint(); }
    void up() { if(touching) {touching=false;add(UP);regions=null;} }
    void cancel() {
        touching=false;regions=null;
        if(generation==0 || cancelling) return;
        queue.clear();cancelling=true;ready=false;
        latest=new Event(++sequence,contact,CANCEL,config==null?0:config.revision(),0,geometry);queue.add(latest);sendAt=0;
    }
    private boolean add(int phase) {
        if(queue.size()>=CAPACITY || sequence>=0xfffffffeL || contact>=0xfffffffeL) { cancel();return false; }
        queue.add(new Event(++sequence,contact,phase,activeRevision,phase==UP && regions!=null?regions.selection():0,geometry));
        sendAt=0;return true;
    }
    byte[] claim(long run,long now) {
        if(generation==0 || now<sendAt) return null;
        if(now-lastReplyAt>=600_000_000L && !cancelling) cancel();
        Event e=queue.isEmpty()?latest:queue.peekFirst();
        sendAt=now+(queue.isEmpty()?100_000_000L:20_000_000L);
        if(extended && (geometry==null || !geometry.valid() || config==null)) return null;
        if(extended && e.phase()==IDLE) e=new Event(0,0,IDLE,config.revision(),0,geometry);
        var b=ByteBuffer.allocate(extended?96:32).order(ByteOrder.LITTLE_ENDIAN);
        b.put((byte)2).put((byte)(extended?10:8)).putLong(run).putLong(generation).putInt((int)e.sequence()).putInt((int)e.contact()).put((byte)e.phase());
        if(extended) {
            var g=e.phase()==CANCEL?geometry:e.geometry();
            b.put((byte)2).put((byte)e.selection());b.position(32);
            b.putLong(e.revision()).putLong(g.identity()).putInt(g.width()).putInt(g.height()).putFloat(g.density());
            b.putInt(g.mode().x).putInt(g.mode().y).putInt(g.mode().width).putInt(g.mode().height).putInt(0);
            b.putInt(g.left()).putInt(g.top()).putInt(g.right()).putInt(g.bottom());
        }
        return b.array();
    }
    long waitNanos(long now) { return generation==0?Long.MAX_VALUE:Math.max(0,sendAt-now); }
    int pending() {return queue.size();}
}
