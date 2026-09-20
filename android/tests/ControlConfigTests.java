package com.rightpad.capture;

import android.os.Handler;
import java.net.*;
import java.nio.*;
import java.util.*;

public final class ControlConfigTests {
    static int checks;
    static void check(boolean ok, String message) { checks++; if (!ok) throw new AssertionError(message); }
    static byte[] golden() { return HexFormat.of().parseHex("5250435401010000010203040506070811121314151617182122232425262728010000000100010007001e0019009001"); }
    static byte[] packet(long run, long epoch, long revision, int up) {
        byte[] b = golden(); ByteBuffer p=ByteBuffer.wrap(b).order(ByteOrder.LITTLE_ENDIAN);
        p.putLong(8,run).putLong(16,epoch).putLong(24,revision).putShort(40,(short)up); return b;
    }
    static ControlConfigProtocol.Snapshot snapshot(long epoch, long rev, int up) {
        byte[] b=packet(7,epoch,rev,up); return ControlConfigProtocol.decode(b,b.length);
    }
    public static void main(String[] args) throws Exception {
        byte[] b=golden(); var s=ControlConfigProtocol.decode(b,b.length);
        check(s!=null && s.epoch()==0x1817161514131211L && s.revision()==0x2827262524232221L, "shared Windows golden");
        check(s.records().get(1).equals(new ControlConfigProtocol.Slide(7,30,25,400)), "fixed point fields");
        for(int n=0;n<48;n++) check(ControlConfigProtocol.decode(b,n)==null,"truncated "+n);
        check(ControlConfigProtocol.decode(Arrays.copyOf(b,49),49)==null,"extra bytes");
        for(int at:new int[]{4,5,6,7,33,34,35,38,39}) {
            byte[] bad=b.clone(); bad[at]=99; check(ControlConfigProtocol.decode(bad,bad.length)==null,"invalid field "+at);
        }
        for(int up:new int[]{0,501}) check(ControlConfigProtocol.decode(packet(7,1,1,up),48)==null,"range");
        byte[] duplicate=Arrays.copyOf(b,60); duplicate[32]=2; System.arraycopy(b,36,duplicate,48,12);
        check(ControlConfigProtocol.decode(duplicate,60)==null,"duplicate IDs");
        duplicate[48]=99; var two=ControlConfigProtocol.decode(duplicate,60);
        check(two!=null && two.records().size()==2,"generic records independent of B");
        var cache=new ControlConfigCache();
        check(cache.epoch()==0 && cache.gesture(1,3,SlideControlGesture.Config.phaseOne(3)).tapHoldMs==25,"defaults before config");
        check(cache.accept(snapshot(1,2,7)),"new epoch");
        check(!cache.accept(snapshot(1,2,15)) && cache.revision()==2,"duplicate idempotent even different payload");
        check(!cache.accept(snapshot(1,1,15)),"stale revision rejected");
        List<SlideControlGesture.Action> actions=new ArrayList<>(); var gesture=new SlideControlGesture(actions::add);
        gesture.down(100,0,cache.gesture(1,3,SlideControlGesture.Config.phaseOne(3)));
        check(cache.accept(snapshot(1,3,15)),"newer accepted mid-gesture");
        gesture.move(97,10); check(gesture.action()==SlideControlGesture.Action.SLIDE_UP,"current gesture retains 0.7dp snapshot");
        gesture.cancel(); gesture.down(100,20,cache.gesture(1,3,SlideControlGesture.Config.phaseOne(3))); gesture.move(97,30);
        check(gesture.action()==SlideControlGesture.Action.NEUTRAL,"next gesture uses 1.5dp");
        gesture.move(95,40); check(gesture.action()==SlideControlGesture.Action.SLIDE_UP,"new threshold reached");
        check(cache.accept(snapshot(2,1,9)),"new epoch resets revision"); check(!cache.accept(snapshot(1,99,7)),"retired epoch cannot roll back");
        cache.reset(); check(cache.epoch()==0 && cache.revision()==0,"new target defaults/reset");
        b=ControlConfigProtocol.request(0x0807060504030201L,0x1817161514131211L,0x2827262524232221L);
        check(HexFormat.of().formatHex(b).equals("0206010203040506070811121314151617182122232425262728"),"type6 exact26 golden");
        versionTwo(); requests(); listener();
        System.out.println("RESULT ControlConfigTests checks="+checks+" failed=0");
    }
    static byte[] v2(long revision) {
        byte[] b = HexFormat.of().parseHex("5250435402010000010203040506070811121314151617182122232425262728020000000100010c07001e00190090010200020e78001e00140019009001");
        ByteBuffer.wrap(b).order(ByteOrder.LITTLE_ENDIAN).putLong(8,7).putLong(16,1).putLong(24,revision);
        return b;
    }
    static ControlConfigProtocol.Snapshot decode(byte[] b) { return ControlConfigProtocol.decode(b,b.length); }
    static byte[] append(byte[] b, byte[] record) {
        byte[] next=Arrays.copyOf(b,b.length+record.length); System.arraycopy(record,0,next,b.length,record.length);
        next[32]++; return next;
    }
    static void versionTwo() {
        byte[] b=v2(2); var s=decode(b);
        check(b.length==62 && s!=null && s.version()==2,"v2 62-byte golden");
        check(s.records().get(1).equals(new ControlConfigProtocol.Slide(7,30,25,400)),"B 12-byte record unchanged values");
        check(s.lrRecords().get(2).equals(new ControlConfigProtocol.SlideLR(120,30,20,25,400)),"X 14-byte LR record");
        for(int n=0;n<62;n++) check(ControlConfigProtocol.decode(b,n)==null,"v2 truncate "+n);
        check(decode(Arrays.copyOf(b,63))==null,"trailing garbage");
        for(int at:new int[]{0,4,5,6,7,32,33,34,35,38,50}) {
            byte[] bad=b.clone(); bad[at]=(byte)99; check(decode(bad)==null,"v2 wrong header/kind "+at);
        }
        for(int at:new int[]{39,51}) for(int size:new int[]{0,1,3,4,11,13,15,255}) {
            byte[] bad=b.clone(); bad[at]=(byte)size; check(decode(bad)==null,"record length bounds/exact "+at+"/"+size);
        }
        check(decode(append(b,Arrays.copyOfRange(b,36,48)))==null,"duplicate B");
        check(decode(append(b,Arrays.copyOfRange(b,48,62)))==null,"duplicate X");
        byte[] onlyB=Arrays.copyOf(b,48); onlyB[32]=1; check(decode(onlyB)==null,"v2 missing X rejects");
        byte[] onlyX=new byte[50]; System.arraycopy(b,0,onlyX,0,36); System.arraycopy(b,48,onlyX,36,14); onlyX[32]=1;
        check(decode(onlyX)==null,"v2 missing B rejects");
        byte[] unknown=Arrays.copyOfRange(b,36,48); unknown[0]=99;
        check(decode(append(b,unknown))!=null,"unknown ID known kind skip");
        check(decode(append(b,new byte[]{99,0,77,4}))!=null,"unknown ID and kind safe skip");
        byte[] wrongUnknown=unknown.clone(); wrongUnknown[3]=4;
        check(decode(append(b,wrongUnknown))==null,"known kind requires exact length even unknown ID");
        for(int at:new int[]{40,42,52,54,56,44,58,46,60}) {
            int min=(at==46||at==60)?50:1, max=(at==46||at==60)?2000:(at==44||at==58)?200:500;
            for(int value:new int[]{min-1,max+1}) {
                byte[] bad=b.clone(); ByteBuffer.wrap(bad).order(ByteOrder.LITTLE_ENDIAN).putShort(at,(short)value);
                check(decode(bad)==null,"range rejects "+at+"/"+value);
            }
            for(int value:new int[]{min,max}) {
                byte[] good=b.clone(); ByteBuffer.wrap(good).order(ByteOrder.LITTLE_ENDIAN).putShort(at,(short)value);
                check(decode(good)!=null,"range accepts "+at+"/"+value);
            }
        }
        for(int at:new int[]{16,24}) { byte[] bad=b.clone(); ByteBuffer.wrap(bad).putLong(at,0); check(decode(bad)==null,"zero epoch/revision"); }
        var cache=new ControlConfigCache(); var defaults=SlideControlLRGesture.Config.defaults();
        check(cache.lrGesture(2,defaults).equals(defaults),"LR defaults before config");
        check(cache.accept(snapshot(1,2,7)),"v1 during upgrade");
        check(cache.lrGesture(2,defaults).equals(defaults),"v1 only B, LR defaults");
        check(cache.accept(s),"same epoch/revision v1->v2 completeness upgrade");
        check(!cache.accept(snapshot(2,99,15)),"no v1 downgrade after v2");
        List<SlideControlLRGesture.Action> actions=new ArrayList<>(); var g=new SlideControlLRGesture(actions::add);
        g.down(0,0,0,cache.lrGesture(2,defaults),1);
        byte[] changed=v2(3); var p=ByteBuffer.wrap(changed).order(ByteOrder.LITTLE_ENDIAN);
        p.putShort(40,(short)15).putShort(52,(short)80).putShort(54,(short)60).putShort(56,(short)40).putShort(58,(short)30).putShort(60,(short)500);
        byte[] bad=changed.clone(); bad[51]=0;
        check(!cache.accept(decode(bad)) && cache.revision()==2 && cache.gesture(1,1,null).upThresholdPx==.7f,"malformed X cannot partially update B/revision");
        check(cache.accept(decode(changed)),"valid B+X snapshot atomically accepted");
        g.move(3,0,10); check(g.action()==SlideControlLRGesture.Action.SLIDE_RIGHT,"active gesture retains right3");
        g.cancel(); g.down(0,0,20,cache.lrGesture(2,defaults),1); g.move(3,0,30);
        check(g.action()==SlideControlLRGesture.Action.NEUTRAL && g.deadline()==520,"next DOWN right6/long500");
        g.move(6,0,40); check(g.action()==SlideControlLRGesture.Action.SLIDE_RIGHT,"new right threshold6");
        g.cancel(); g.down(0,0,50,cache.lrGesture(2,defaults),1); g.move(-8,0,60); check(g.action()==SlideControlLRGesture.Action.SLIDE_LEFT,"left8");
        g.cancel(); g.down(0,0,70,cache.lrGesture(2,defaults),1); g.move(0,-4,80); check(g.action()==SlideControlLRGesture.Action.SLIDE_UP,"up4");
        g.cancel(); g.down(0,0,90,cache.lrGesture(2,defaults),1); g.up(100,100);
        check(g.minimumHoldMs()==30 && g.deadline()==130,"tap30 from new snapshot");
        check(!cache.accept(s) && !cache.accept(decode(changed)),"old and duplicate v2 cannot alter snapshot");
        check(cache.gesture(1,1,null).upThresholdPx==1.5f,"B updated alongside LR");
        cache.reset(); check(cache.lrGesture(2,defaults).equals(defaults),"new target LR resets to defaults");
    }
    static void requests() throws Exception {
        try(DatagramSocket socket=new DatagramSocket(0,InetAddress.getLoopbackAddress());
            DatagramSocket target=new DatagramSocket(0,InetAddress.getLoopbackAddress());
            UdpTouchSender sender=new UdpTouchSender("127.0.0.1",socket.getLocalPort())) {
            socket.setSoTimeout(2500); target.setSoTimeout(2500); sender.enableControlRequests(); sender.setForeground(true);
            byte[] first=GamepadSenderTests.next(socket,6);
            check(first.length==26 && ByteBuffer.wrap(first).order(ByteOrder.LITTLE_ENDIAN).getLong(10)==0,"new run requests unknown config");
            byte[] retry=GamepadSenderTests.next(socket,6); check(Arrays.equals(first,retry),"lost initial response retried");
            sender.knownControlConfig(9,3); sender.setForeground(false); sender.setForeground(true);
            byte[] resume=GamepadSenderTests.next(socket,6);
            check(ByteBuffer.wrap(resume).order(ByteOrder.LITTLE_ENDIAN).getLong(10)==9,"resume immediately requests known config");
            sender.submit(UdpTouchSenderTest.touch(TouchSample.Action.DOWN));
            check(UdpTouchSenderTest.sequence(GamepadSenderTests.next(socket,1))==0,"request leaves Touch sequence zero");
            sender.setTarget(new InetSocketAddress("127.0.0.1",target.getLocalPort()));
            byte[] changed=GamepadSenderTests.next(target,6);
            check(UdpTouchSenderTest.run(changed)!=UdpTouchSenderTest.run(first) && ByteBuffer.wrap(changed).order(ByteOrder.LITTLE_ENDIAN).getLong(10)==0,"target/run replacement requests defaults");
        }
    }
    static void listener() throws Exception {
        List<ControlConfigProtocol.Snapshot> received=new ArrayList<>();
        try(var socket=new DatagramSocket(); var listener=new HapticFeedbackListener(()->{},received::add)) {
            listener.setActive(InetAddress.getLoopbackAddress(),7);
            long until=System.nanoTime()+2_000_000_000L;
            while(received.isEmpty()) {
                byte[] b=packet(7,1,1,15); socket.send(new DatagramPacket(b,b.length,InetAddress.getLoopbackAddress(),50002));
                Thread.sleep(10); Handler.drain(); check(System.nanoTime()<until,"shared listener receive");
            }
            check(received.get(0).records().get(1).upTenthsDp()==15,"same50002 config dispatch");
            Thread.sleep(20); Handler.drain(); int count=received.size();
            byte[] wrong=packet(8,1,2,7); socket.send(new DatagramPacket(wrong,wrong.length,InetAddress.getLoopbackAddress(),50002));
            Thread.sleep(30); Handler.drain(); check(received.size()==count,"wrong senderRun rejected");
            listener.setActive(null,0);
        }
    }
}
