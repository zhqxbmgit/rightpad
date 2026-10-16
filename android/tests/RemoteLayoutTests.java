package com.rightpad.capture;

import java.nio.file.Files;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

public final class RemoteLayoutTests {
    static int checks;
    static void ok(boolean value, String name) { checks++; if (!value) throw new AssertionError(name); }
    static String geometry(Map<String, ControlRect> map) {
        String s = ""; for (var e : map.entrySet()) { var r = e.getValue(); s += e.getKey()+":"+r.x+","+r.y+","+r.width+","+r.height+";"; } return s;
    }
    static final class Fixture {
        Map<String, ControlRect> live = new LinkedHashMap<>();
        int writes, publications; boolean fail;
        long[] realClock = { Long.MIN_VALUE };
        RemoteLayoutSession session;
        List<RemoteLayoutProtocol.Item> descriptions;
        Fixture() {
            live.put("xbox.b.slide", new ControlRect(700, 500, 150, 900));
            live.put("xbox.x.slide_lr", new ControlRect(0, 400, 60, 1200));
            live.put("rightpad.input.mode", new ControlRect(400, 500, 200, 900));
            descriptions = new ArrayList<>();
            for (var e : live.entrySet()) descriptions.add(new RemoteLayoutProtocol.Item(e.getKey(), e.getKey(), 0, e.getValue()));
            session = new RemoteLayoutSession(123, map -> { writes++; if (fail) throw new java.io.IOException("disk"); },
                    map -> { live = map; publications++; }, () -> realClock[0]);
            session.geometry(1000, 2000, live, descriptions, List.of(new RemoteLayoutProtocol.Item("settings", "设置", 1, new ControlRect(800,20,40,40))));
        }
        RemoteLayoutProtocol.Message query(long now) { return session.handle(command(1, 0, List.of()), now, true, false); }
        RemoteLayoutProtocol.Message command(int kind, long txn, List<RemoteLayoutProtocol.Item> items) {
            var s = session.snapshot(99, 1, 0, 0);
            return new RemoteLayoutProtocol.Message(kind,0,0,99,123,s.revision(),s.geometry(),txn,8,0,0,0,1000,2000,items);
        }
        List<RemoteLayoutProtocol.Item> candidate() {
            var result = new ArrayList<>(descriptions); var b = result.get(0);
            result.set(0,new RemoteLayoutProtocol.Item(b.id(),"B",0,new ControlRect(650,550,160,800))); return result;
        }
        RemoteLayoutProtocol.Message begin(long now, boolean idle) {
            return session.handle(command(2,1,candidate()),now,idle,false);
        }
    }
    public static void main(String[] args) throws Exception {
        var f = new Fixture(); String original = geometry(f.live); int before = f.publications;
        var q = f.query(10); var candidate = f.candidate();
        ok(f.writes == 0 && f.publications == before && geometry(f.live).equals(original), "query and draft isolation");
        var m = f.begin(100, false);
        ok(m.state()==1 && geometry(f.live).equals(original),"active contact keeps original, pending only");
        f.session.tick(500, false); ok(f.publications == before,"no mid-contact publication");
        f.session.tick(1000, true); m=f.query(1000);
        ok(m.state()==2 && m.remaining()==60000 && f.writes==0,"full trial only in memory at idle");
        ok(f.publications==before+1 && !geometry(f.live).equals(original),"one atomic map publication");
        f.session.handle(f.command(2,1,candidate),2000,true,false);
        ok(f.query(2000).remaining()==59000,"duplicate trial never extends deadline");
        f.session.tick(61000,false); m=f.query(61000);
        ok(m.state()==4 && geometry(f.live).equals(original) && f.writes==0,"local expiry without receiver and even during contact");
        f.session.handle(f.command(2,1,candidate),62000,true,false);
        f.session.handle(f.command(3,1,List.of()),62000,true,false);
        ok(f.query(62000).state()==4 && f.writes==0,"late trial and keep cannot resurrect");
        f = new Fixture(); f.begin(0,true); var keep = f.command(3,1,List.of());
        m=f.session.handle(keep,59999,true,false);
        ok(m.state()==3 && f.writes==1,"valid keep persisted before success");
        String saved = geometry(f.live); f.session.tick(70000,true);
        m=f.session.handle(keep,71000,true,false);
        ok(m.state()==3 && f.writes==1 && geometry(f.live).equals(saved),"lost result retry idempotent, late expiry cannot revert saved");
        f = new Fixture(); f.begin(0,true); f.fail=true;
        m=f.session.handle(f.command(3,1,List.of()),1,true,false);
        ok(m.state()==5 && m.error()==4 && geometry(f.live).equals(original),"failed disk restores formal");
        f = new Fixture(); f.begin(0,true);
        m=f.session.handle(f.command(3,1,List.of()),60000,true,false);
        ok(m.state()==4 && m.error()==5 && f.writes==0,"expiry wins exactly at deadline");
        f = new Fixture(); f.begin(0,true); f.realClock[0]=60000;
        m=f.session.handle(f.command(3,1,List.of()),59999,true,false);
        ok(m.state()==4 && f.writes==0,"recheck actual clock at commit entry, not only request processing time");
        f = new Fixture(); f.session.handle(f.command(4,1,List.of()),0,true,false); f.begin(1,true);
        ok(f.query(1).state()==4,"cancel overtakes delayed first trial and fences token");
        f = new Fixture(); var delayed = f.command(2,1,f.candidate());
        f.session.handle(f.command(1,1,List.of()),0,true,false);
        m=f.session.handle(delayed,1,true,false);
        ok(m.state()==4 && geometry(f.live).equals(original),"uncertain result query fences an unseen Try before reporting not applied");
        f = new Fixture(); f.begin(0,true); f.session.abort();
        ok(geometry(f.live).equals(original) && f.query(1).state()==4,"pause/target/run abort independently restores");
        f = new Fixture(); f.begin(0,false); f.session.tick(10000,false);
        ok(f.query(10000).state()==4,"pending idle boundary has bounded lifetime");
        f = new Fixture(); var stale = f.command(2,1,f.candidate()); f.session.localSaved(f.live);
        ok(f.session.handle(stale,0,true,false).error()==1,"local save rejects old computer draft");
        stale=f.command(2,1,f.candidate()); f.session.geometry(1000,2000,f.live,f.descriptions,List.of());
        ok(f.session.handle(stale,0,true,false).error()==1,"stale geometry rejected");
        f = new Fixture(); ok(f.session.handle(f.command(2,1,f.candidate()),0,true,true).error()==3,"local editor and remote trial serialized");
        var malformed = new ArrayList<>(f.candidate()); malformed.remove(0);
        ok(f.session.handle(f.command(2,1,malformed),0,true,false).error()==2,"missing known ID rejected whole");
        f = new Fixture(); malformed=new ArrayList<>(f.candidate()); var b=malformed.get(0);
        malformed.set(0,new RemoteLayoutProtocol.Item(b.id(),"B",0,new ControlRect(Integer.MAX_VALUE,0,200,100)));
        ok(f.session.handle(f.command(2,1,malformed),0,true,false).error()==2,"overflow/outside rejected whole");
        f = new Fixture(); malformed=new ArrayList<>(f.candidate());
        for (int i=0;i<malformed.size();i++) { b=malformed.get(i); malformed.set(i,new RemoteLayoutProtocol.Item(b.id(),b.label(),0,new ControlRect(0,0,1000,2000))); }
        ok(f.session.handle(f.command(2,1,malformed),0,true,false).error()==2,"union may not cover entire mouse area");
        // Shared strict codec: all truncated prefixes, trailing bytes, duplicates, malformed UTF8.
        f = new Fixture(); byte[] wire=RemoteLayoutProtocol.encode(f.query(0));
        var decoded=RemoteLayoutProtocol.decode(wire,wire.length);
        ok(decoded!=null && decoded.items().get(3).label().equals("设置"),"UTF8 geometry roundtrip");
        for(int i=0;i<wire.length;i++) ok(RemoteLayoutProtocol.decode(wire,i)==null,"truncated prefix "+i);
        var extra=java.util.Arrays.copyOf(wire,wire.length+1); ok(RemoteLayoutProtocol.decode(extra,extra.length)==null,"trailing");
        byte[] corrupt=wire.clone(); corrupt[4]=2; ok(RemoteLayoutProtocol.decode(corrupt,corrupt.length)==null,"version");
        corrupt=wire.clone(); corrupt[86]=1; ok(RemoteLayoutProtocol.decode(corrupt,corrupt.length)==null,"reserved");
        var dup=new ArrayList<>(f.candidate()); dup.set(1,dup.get(0));
        byte[] duplicated=RemoteLayoutProtocol.encode(f.command(2,1,dup));
        ok(RemoteLayoutProtocol.decode(duplicated,duplicated.length)==null,"duplicate ID");
        // Existing ratio format and unknown stable-ID data survive the real atomic store.
        var directory=Files.createTempDirectory("rightpad-layout-test"); var file=directory.resolve("screen-controls.properties");
        try {
            Files.writeString(file,"version=1\ncontrols.future.id.xRatio=0.314\nfuture.extension=kept\n");
            byte[] exact=Files.readAllBytes(file); var store=new ScreenControlLayoutStore(file.toFile());
            ok(java.util.Arrays.equals(exact,Files.readAllBytes(file)),"read no writes");
            store.save(f.live,1000,2000); var p=new java.util.Properties(); p.load(Files.newInputStream(file));
            ok(p.getProperty("future.extension").equals("kept") && p.getProperty("controls.future.id.xRatio").equals("0.314"),"unknown fields preserved");
            ok(p.getProperty("controls.rightpad.input.mode.xRatio").equals("0.4"),"reserved ID retained");
        } finally { Files.deleteIfExists(file); Files.deleteIfExists(directory); }
        var golden = new RemoteLayoutProtocol.Message(1,0,0,0x0807060504030201L,0,0,0,0,9,0,0,0,0,0,List.of());
        for(long runId : new long[] {0,-1}) {
            var unsigned = new RemoteLayoutProtocol.Message(1,0,0,runId,0,0,0,0,9,0,0,0,0,0,List.of());
            byte[] bytes=RemoteLayoutProtocol.encode(unsigned);
            ok(RemoteLayoutProtocol.decode(bytes,bytes.length).run()==runId,"all existing uint64 Sender run bit patterns remain legal");
        }
        System.out.println("RPLY query golden="+java.util.HexFormat.of().formatHex(RemoteLayoutProtocol.encode(golden)));
        System.out.println("RESULT RemoteLayoutTests checks="+checks+" failed=0");
    }
}
