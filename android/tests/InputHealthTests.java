package com.rightpad.capture;

import android.os.Handler;
import java.net.*;
import java.nio.*;
import java.util.*;
import static com.rightpad.capture.InputHealthStatus.Health.*;

public final class InputHealthTests {
    static int checks;
    static void check(boolean ok, String message) { checks++; if (!ok) throw new AssertionError(message); }
    static byte[] golden() { return HexFormat.of().parseHex("525053540101000001020304050607081112131415161718212223242526272802030000"); }
    static byte[] packet(long run) {
        byte[] b = golden(); ByteBuffer.wrap(b).order(ByteOrder.LITTLE_ENDIAN).putLong(8, run).putLong(16, 1).putLong(24, 1); return b;
    }
    static void protocol() {
        byte[] b = golden(); var s = StatusProtocol.decode(b, b.length);
        check(b.length == 36 && s != null, "exact size/shared golden");
        check(s.runId() == 0x0807060504030201L && s.epoch() == 0x1817161514131211L
                && s.revision() == 0x2827262524232221L && s.state() == 2 && s.flags() == 3, "golden semantics");
        check(StatusProtocol.decode(null, 36) == null, "null");
        for (int n = 0; n < 36; n++) check(StatusProtocol.decode(Arrays.copyOf(b,n), n) == null, "truncation " + n);
        check(StatusProtocol.decode(Arrays.copyOf(b,37),37) == null, "oversize");
        check(StatusProtocol.decode(new byte[2],36) == null, "invalid supplied length");
        for (int offset : new int[]{0,1,2,3,4,5,6,7,32,33,34,35}) {
            byte[] bad = b.clone(); bad[offset] = (byte)255;
            check(StatusProtocol.decode(bad,36) == null, "strict field " + offset);
        }
        ByteBuffer.wrap(b).order(ByteOrder.LITTLE_ENDIAN).putLong(8,-1).putLong(16,-1).putLong(24,-1);
        s = StatusProtocol.decode(b,36);
        check(s.runId() == -1 && s.epoch() == -1 && s.revision() == -1, "unsigned max bit patterns");
        for (int offset : new int[]{16,24}) {
            byte[] bad = b.clone(); ByteBuffer.wrap(bad).order(ByteOrder.LITTLE_ENDIAN).putLong(offset,0);
            check(StatusProtocol.decode(bad,36) == null, "nonzero config identity");
        }
    }
    static void health() {
        var tracker = new InputHealthTracker(); var cache = new ControlConfigCache();
        check(tracker.display(cache,0).equals(InputHealthStatus.OFFLINE), "disconnected all gray");
        tracker.configure(true,7);
        check(tracker.display(cache,0).equals(new InputHealthStatus(true,PENDING,OFFLINE,PENDING)), "first status pending/input, unavailable xbox");
        check(!tracker.accept(StatusProtocol.decode(packet(8),36),0), "wrong run rejected");
        var healthy = StatusProtocol.decode(packet(7),36);
        tracker.accept(healthy,0);
        check(tracker.display(cache,0).input() == GOOD && tracker.display(cache,0).xbox() == GOOD
                && tracker.display(cache,0).config() == PENDING, "healthy idle backend and empty config");
        cache.accept(ControlConfigTests.snapshot(1,1,7));
        check(tracker.display(cache,0).config() == PENDING, "v1 never complete");
        byte[] cfg = ControlConfigTests.v2(1); cache.accept(ControlConfigProtocol.decode(cfg,cfg.length));
        check(tracker.display(cache,0).equals(new InputHealthStatus(true,GOOD,GOOD,GOOD)), "complete matching v2");
        tracker.accept(new StatusProtocol.Snapshot(7,1,2,2,3),10);
        check(tracker.display(cache,10).config() == PENDING, "lost RPCT push/status newer");
        cfg = ControlConfigTests.v2(2); cache.accept(ControlConfigProtocol.decode(cfg,cfg.length));
        check(tracker.display(cache,10).config() == GOOD, "RPCT recovery recalculates without status change");
        tracker.accept(new StatusProtocol.Snapshot(7,2,2,2,3),20);
        check(tracker.display(cache,20).config() == PENDING, "epoch mismatch");
        tracker.accept(new StatusProtocol.Snapshot(7,1,2,2,1),30);
        check(tracker.display(cache,30).input() == GOOD && tracker.display(cache,30).xbox() == ERROR, "xbox unavailable independent of input");
        tracker.accept(new StatusProtocol.Snapshot(7,1,2,2,11),30);
        check(tracker.display(cache,30).input() == ERROR, "mouse failure overrides healthy flag");
        tracker.accept(new StatusProtocol.Snapshot(7,1,2,2,19),30);
        check(tracker.display(cache,30).xbox() == ERROR, "gamepad failure overrides available flag");
        for (int state : new int[]{0,1,2,3,4}) {
            tracker.accept(new StatusProtocol.Snapshot(7,1,2,state,7),40);
            check(tracker.display(cache,40).input() == ERROR && tracker.display(cache,40).xbox() != GOOD, "runtime fatal never green " + state);
        }
        for (int state : new int[]{1,3}) {
            tracker.accept(new StatusProtocol.Snapshot(7,1,2,state,3),40);
            check(tracker.display(cache,40).input() == PENDING && tracker.display(cache,40).xbox() == PENDING, "transition");
        }
        tracker.accept(new StatusProtocol.Snapshot(7,1,2,0,0),40);
        check(tracker.display(cache,40).input() == OFFLINE && tracker.display(cache,40).xbox() == OFFLINE, "stopped");
        tracker.accept(new StatusProtocol.Snapshot(7,1,2,2,3),50);
        check(tracker.display(cache,50+InputHealthTracker.STALE_NS).input() == GOOD, "timeout inclusive boundary");
        check(tracker.display(cache,51+InputHealthTracker.STALE_NS).equals(new InputHealthStatus(true,PENDING,PENDING,PENDING)), "stale all pending");
        tracker.configure(true,8);
        check(!tracker.accept(healthy,60) && tracker.display(cache,60).input() == PENDING, "new sender resets/rejects old");
        tracker.configure(false,0);
        check(tracker.display(cache,60).equals(InputHealthStatus.OFFLINE), "pause/disconnect immediate gray");
        tracker.configure(true,8);
        check(tracker.display(cache,60).input() == PENDING, "resume same sender does not reuse green");
    }
    static void receive(DatagramSocket sender, InetAddress destination, byte[] bytes) throws Exception {
        sender.send(new DatagramPacket(bytes,bytes.length,destination,50002));
        long deadline = System.nanoTime()+2_000_000_000L;
        while (!Handler.hasPending()) { Thread.sleep(5); check(System.nanoTime()<deadline,"listener callback"); }
    }
    static void listener() throws Exception {
        InetAddress local = InetAddress.getByName("127.0.0.1"); int[] status = {0}, configs = {0}, clicks = {0};
        Thread ui = Thread.currentThread();
        try (var sender = new DatagramSocket(); var other = new DatagramSocket(new InetSocketAddress("127.0.0.2",0));
             var listener = new HapticFeedbackListener(() -> clicks[0]++, ignored -> configs[0]++, (s, at) -> {
                 check(Thread.currentThread() == ui && at <= System.nanoTime(), "UI dispatch and receive timestamp"); status[0]++;
             })) {
            listener.setActive(local,7);
            long deadline = System.nanoTime()+2_000_000_000L;
            while (!Handler.hasPending()) {
                sender.send(new DatagramPacket(packet(7),36,local,50002)); Thread.sleep(10);
                check(System.nanoTime()<deadline,"bind");
            }
            Handler.drain(); check(status[0]>0,"actual UDP RPST"); int accepted = status[0];
            receive(sender,local,packet(8)); Handler.drain(); check(status[0]==accepted,"old run reject");
            receive(other,local,packet(7)); Handler.drain(); check(status[0]==accepted,"wrong source reject");
            byte[] bad=packet(7); bad[33]=(byte)128;
            receive(sender,local,bad); Handler.drain(); check(status[0]==accepted,"malformed status isolated");
            receive(sender,local,ControlConfigTests.v2(1)); Handler.drain(); check(configs[0]==1,"RPCT still works");
            listener.expectUp(7,1,1);
            byte[] click=ByteBuffer.allocate(24).order(ByteOrder.LITTLE_ENDIAN).put(new byte[]{'R','P','H','F',1,1,0,0}).putLong(7).putInt(1).putInt(1).array();
            receive(sender,local,click); Handler.drain(); check(clicks[0]==1,"RPHF still works");
            receive(sender,local,packet(7)); listener.setActive(local,8); Handler.drain();
            check(status[0]==accepted,"queued status invalidated on sender transition");
            listener.setActive(null,0);
        }
    }
    public static void main(String[] args) throws Exception {
        protocol(); health(); listener(); System.out.println("RESULT InputHealthTests checks="+checks+" failed=0");
    }
}
