package com.rightpad.capture;
import java.nio.*;
import java.util.*;

/** Exercises production fixed geometry and the real bounded contact encoder. */
public final class ModeFixedRegionsTests {
    static int checks;
    static void check(boolean value,String message) {checks++;if(!value)throw new AssertionError(message);}
    static ModeFixedRegions.Geometry geometry(float density,ControlRect rect) {
        return new ModeFixedRegions.Geometry(1,1200,2670,density,rect,0,90,1200,2670);
    }
    static ModeFixedRegions regions() {return new ModeFixedRegions(geometry(3,new ControlRect(600,772,264,1881)),8,10);}
    static void at(ModeFixedRegions r,float x,float y,int expected) {r.sample(x,y,false);check(r.selection()==expected,"selection at "+x+","+y+" expected "+expected);}
    static ModeContactStream.Reply reply(long generation,long ack,long revision,long geometry) {
        return new ModeContactStream.Reply(7,generation,ack,true,2,2,8,10,revision,geometry);
    }
    static ModeContactStream stream() {
        var s=new ModeContactStream();s.geometry(geometry(3,new ControlRect(600,772,264,1881)));s.reply(reply(100,0,1,1),1_000_000_000L);return s;
    }
    static void geometryTests() {
        for(float density:new float[]{1,1.25f,2,2.75f,3,4}) {
            for(var rect:List.of(new ControlRect(100,100,20,20),new ControlRect(300,300,100,600),new ControlRect(600,772,264,1881))) {
                var g=geometry(density,rect);check(g.fits(8,10),"actual View geometry and density valid");
                float edge=rect.right(), first=edge+8*density,end=first+10*density;
                at(new ModeFixedRegions(g,8,10),rect.x,rect.y,1);
                at(new ModeFixedRegions(g,8,10),Math.nextDown(edge),rect.y,1);
                at(new ModeFixedRegions(g,8,10),edge,rect.y,2);
                at(new ModeFixedRegions(g,8,10),Math.nextDown(first),rect.y,2);
                at(new ModeFixedRegions(g,8,10),first,rect.y,3);
                at(new ModeFixedRegions(g,8,10),Math.nextDown(end),Math.nextDown((float)rect.bottom()),3);
                at(new ModeFixedRegions(g,8,10),end,rect.y,0);
                at(new ModeFixedRegions(g,8,10),rect.x,rect.bottom(),0);
            }
        }
        check(!geometry(3,new ControlRect(1150,100,20,20)).fits(8,10),"right overflow rejected without clamping");
        check(!geometry(3,new ControlRect(600,80,200,200)).fits(8,10),"cutout inset top rejected in View coordinates");
        check(!geometry(3,new ControlRect(600,772,264,1881)).fits(Float.NaN,10),"NaN width rejected");
        for(float width:new float[]{0,0.5f,50.5f,Float.POSITIVE_INFINITY,1.1f}) check(!ModeFixedRegions.widthValid(width),"invalid width/step");
        check(geometry(3,new ControlRect(600,100,300,200)).fits(10,8),"widths independent, second may be smaller");
        for(float x:new float[]{600,650,863.9f}) {
            var g=geometry(3,new ControlRect(600,772,264,1881));var s=new ModeFixedRegions(g,8,10);
            s.sample(x,900,false);at(s,870,900,2);
        }
    }
    static void trajectories() {
        var r=regions();at(r,870,900,2);at(r,900,900,3);at(r,870,900,2);at(r,620,900,0);at(r,900,900,3);
        for(float x:new float[]{599,918}) {r=regions();at(r,x,900,0);at(r,870,900,0);check(r.invalid(),"invalid latches");}
        for(float y:new float[]{771,2653}) {r=regions();at(r,870,y,0);at(r,870,900,0);}
        r=regions();at(r,700,900,1);at(r,860,2000,1); // No relative 3dp tolerance.
        r=regions();at(r,888,900,3);at(r,887.99f,900,2);at(r,888,900,3); // No hysteresis.
        r=regions();at(r,Float.NaN,900,0);at(r,870,900,0);
    }
    static void hints() {
        var r=regions();r.beginEvent();r.sample(870,900,true);check(r.eventHint(),"first hint");
        r.beginEvent();r.sample(865,900,true);check(!r.eventHint(),"same first no repeat");
        r.beginEvent();r.sample(900,900,true);check(r.eventHint(),"first outward second hint");
        for(float x:new float[]{870,900,870,900,620,870,620,900}) {
            r.beginEvent();r.sample(x,900,true);check(r.eventHint()==(x!=620),"cross-tier/reentry hint, origin silent");
        }
        r=regions();r.beginEvent();r.sample(900,900,true);check(r.eventHint(),"direct second once");
        r.beginEvent();r.sample(870,900,true);check(r.eventHint(),"direct second retracts to first with hint");
        r=regions();r.beginEvent();r.sample(870,900,true);r.sample(900,900,true);check(r.eventHint(),"historical multiple entries coalesce");
        r.beginEvent();r.sample(620,900,true);r.sample(870,900,true);r.sample(900,900,true);check(!r.eventHint(),"history returns to same second, no hint");
        r.beginEvent();r.sample(870,900,true);r.sample(900,900,true);r.sample(870,900,true);check(r.eventHint(),"history final first differs from prior second");
        r.beginEvent();r.sample(900,900,true);r.sample(870,900,true);check(!r.eventHint(),"first through second back to first silent");
        r.beginEvent();r.sample(900,900,true);r.sample(620,900,true);check(!r.eventHint() && r.selection()==0,"history final origin silent, entered retained");
        r.beginEvent();r.sample(870,900,true);check(r.eventHint(),"reentry after cancelled origin hints");
        r.beginEvent();r.sample(900,900,false);check(!r.eventHint() && r.selection()==3,"UP changes tier without hint");
        r=regions();r.beginEvent();r.sample(900,900,false);check(!r.eventHint() && r.selection()==3,"UP selection without haptic");
        r=regions();r.beginEvent();r.sample(870,900,true);r.sample(918,900,true);check(!r.eventHint(),"invalid event suppresses hint");
        r.sample(900,900,true);check(!r.eventHint() && r.invalid(),"historical invalid persists despite final inside");
        // Exhaustive event endpoints/history against production state, including invalid and origin.
        for(float start:new float[]{620,870,900}) for(float middle:new float[]{620,870,900,918})
            for(float end:new float[]{620,870,900}) {
                r=regions();r.sample(start,900,false);int before=r.selection();
                r.beginEvent();r.sample(middle,900,true);r.sample(end,900,true);
                check(r.eventHint()==(middle!=918 && end>=870 && r.selection()!=before),"history endpoint comparison");
                if(middle==918) check(r.invalid() && r.selection()==0,"history invalid fact retained");
            }
    }
    static void transport() {
        long now=1_000_000_000L;var s=stream();
        check(!s.down(now,870,900),"first area not a DOWN button");check(!s.down(now,900,900),"second not a DOWN button");
        check(s.down(now,610,900),"original Mode admits");byte[] down=s.claim(7,now);check(down.length==96 && down[1]==10 && down[28]==0,"v2 down no selection/output");
        s.beginEvent();s.sample(870,900,true);s.sample(900,900,true);s.sample(870,900,true);
        s.reply(reply(100,1,2,1),now+1); // mid-contact Save retains old geometry/revision.
        s.sample(870,900,false);s.up();byte[] up=s.claim(7,now+2);
        var b=ByteBuffer.wrap(up).order(ByteOrder.LITTLE_ENDIAN);
        check(up[28]==2 && b.getLong(32)==1,"UP keeps old config identity and final first selection");
        s.reply(reply(100,2,2,1),now+3);check(s.down(now+4,860,900),"next DOWN latest config");
        byte[] next=s.claim(7,now+4);check(ByteBuffer.wrap(next).order(ByteOrder.LITTLE_ENDIAN).getLong(32)==2,"new revision");
        s.geometry(new ModeFixedRegions.Geometry(2,1200,2670,3,new ControlRect(500,772,264,1881),0,90,1200,2670));
        s.sample(800,900,false);s.up();check(s.claim(7,now+5)[26]==3,"layout boundary cancels not reinterprets");
        s.reply(reply(101,0,2,2),now+6);check(!s.down(now+6,900,900),"sync does not replay old physical contact");
        s=stream();s.down(now,610,900);s.sample(900,900,false);s.up();s.reply(reply(100,1,1,1),now+1);
        check(s.claim(7,now+1)[28]==3,"DOWN then UP without MOVE selects final second");
        s=stream();s.down(now,610,900);s.sample(870,900,false);s.sample(599,900,false);s.sample(900,900,false);s.up();s.reply(reply(100,1,1,1),now+1);
        check(s.claim(7,now+1)[28]==0,"historical invalid survives final inside UP");
        s=stream();s.down(now,610,900);s.sample(870,900,false);s.sample(650,900,false);s.up();s.reply(reply(100,1,1,1),now+1);
        check(s.claim(7,now+1)[28]==0,"entered fact cancels original on retract");
        byte[] reply=new byte[56];var v=ByteBuffer.wrap(reply).order(ByteOrder.LITTLE_ENDIAN);
        v.put(new byte[]{'R','P','M','D',2,1,0,0}).putLong(7).putLong(100).putInt(0).put((byte)1).put((byte)2);v.position(32);v.putShort((short)16).putShort((short)20);v.putLong(40,1);v.putLong(48,1);
        check(ModeContactStream.decode(reply,56)!=null,"v2 reply exact codec");
        for(int index:new int[]{6,7,30,31,36,37,38,39}) {reply[index]=1;check(ModeContactStream.decode(reply,56)==null,"reserved remains zero");reply[index]=0;}
        for(int n=0;n<60;n++) if(n!=56 && n!=32) check(ModeContactStream.decode(Arrays.copyOf(reply,60),n)==null,"precise v2 length");
        s=stream();s.reply(new ModeContactStream.Reply(7,100,0,true),now);check(s.claim(7,now)[1]==10,"no v1 downgrade after v2");
        s=stream();s.down(now,610,900);
        s.reply(new ModeContactStream.Reply(7,100,1,true,2,2,16,10,2,1),now+1);
        s.sample(900,900,false);s.up();check(s.claim(7,now+2)[28]==3,"mid-contact width Save retains old fixed boundary");
        s.reply(new ModeContactStream.Reply(7,100,2,true,2,2,16,10,2,1),now+3);
        s.down(now+4,610,900);s.sample(900,900,false);s.up();
        s.reply(new ModeContactStream.Reply(7,100,3,true,2,2,16,10,2,1),now+5);
        check(s.claim(7,now+6)[28]==2,"next contact uses new fixed boundary at same final coordinate");
        System.out.println("Mode v2 golden DOWN="+HexFormat.of().formatHex(down));
    }
    public static void main(String[] args) {geometryTests();trajectories();hints();transport();System.out.println("PASS ModeFixedRegionsTests checks="+checks);}
}
