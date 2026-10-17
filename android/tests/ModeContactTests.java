package com.rightpad.capture;
import java.nio.*;
public final class ModeContactTests {
    static int checks;
    static void check(boolean c,String m) {checks++;if(!c) throw new AssertionError(m);}
    static long seq(byte[] b) {return Integer.toUnsignedLong(ByteBuffer.wrap(b).order(ByteOrder.LITTLE_ENDIAN).getInt(18));}
    static int phase(byte[] b) {return b[26];}
    public static void main(String[] args) {
        var s=new ModeContactStream();long now=1_000_000_000L;
        check(!s.down(now) && s.claim(7,now)==null,"unready DOWN ignored");
        s.reply(new ModeContactStream.Reply(7,100,0,false),now);
        byte[] idle=s.claim(7,now);check(idle.length==32 && phase(idle)==0,"new generation sends Idle");
        check(!s.down(now),"must confirm Idle readiness");
        s.reply(new ModeContactStream.Reply(7,100,0,true),now);
        check(s.down(now),"new down");check(!s.down(now),"duplicate DOWN");
        byte[] down=s.claim(7,now);check(seq(down)==1 && phase(down)==1,"DOWN fact");
        s.up();s.up();check(s.pending()==2,"exactly one terminal UP");
        check(java.util.Arrays.equals(down,s.claim(7,now+20_000_000)),"lost DOWN retransmitted before UP");
        s.reply(new ModeContactStream.Reply(7,100,1,true),now+21_000_000);
        byte[] up=s.claim(7,now+21_000_000);check(seq(up)==2 && phase(up)==2,"UP after DOWN ack");
        check(java.util.Arrays.equals(up,s.claim(7,now+41_000_000)),"lost UP retry");
        s.reply(new ModeContactStream.Reply(7,100,2,true),now+42_000_000);check(s.pending()==0,"bounded retirement");
        byte[] refresh=s.claim(7,now+42_000_000);check(seq(refresh)==2,"UP liveness does not invent DOWN");
        s.reply(new ModeContactStream.Reply(7,100,2,true),now+43_000_000);
        check(s.claim(7,now+43_000_000)==null,"duplicate ack no busy loop");
        s.reply(new ModeContactStream.Reply(7,99,0,true),now+44_000_000);
        check(s.down(now+45_000_000),"old generation ignored");
        s.cancel();check(s.pending()==1 && phase(s.claim(7,now+46_000_000))==3,"priority cancel discards pending DOWN");
        check(!s.down(now+46_000_000),"cancel waits new generation");
        s.reply(new ModeContactStream.Reply(7,101,0,false),now+47_000_000);check(s.pending()==0,"safety token clears contact history");
        s.reply(new ModeContactStream.Reply(7,101,0,true),now+48_000_000);
        for(int i=0;i<17;i++) {s.down(now+50_000_000);s.up();}
        check(s.pending()==1 && phase(s.claim(7,now+51_000_000))==3,"bounded overflow cancels safely");
        var r=ByteBuffer.allocate(32).order(ByteOrder.LITTLE_ENDIAN);r.put(new byte[]{'R','P','M','D',1,1,0,0}).putLong(7).putLong(200).putInt(3).put((byte)1);
        check(ModeContactStream.decode(r.array(),32).ready(),"reply codec");
        for(int i=29;i<32;i++){r.array()[i]=1;check(ModeContactStream.decode(r.array(),32)==null,"reserved rejected");r.array()[i]=0;}
        for(int n=0;n<32;n++) check(ModeContactStream.decode(r.array(),n)==null,"truncation");
        var l=new ModeContactStream();l.reply(new ModeContactStream.Reply(7,300,0,true),now);l.down(now);
        byte[] expired=l.claim(7,now+600_000_001);check(phase(expired)==3,"lost receiver replies cancel");
        check(!l.down(now+600_000_002),"no resume old hold");
        System.out.println("PASS ModeContactTests checks="+checks);
    }
}
