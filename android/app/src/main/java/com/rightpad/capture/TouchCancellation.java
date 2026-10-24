package com.rightpad.capture;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;

// One bounded barrier on the existing Sender worker; no Touch packet is an ACK.
final class TouchCancellation {
    static final long RETRY_NS = 100_000_000L, LIMIT_NS = 1_500_000_000L;
    record Ack(long run, long session, long down, long sequence) { }
    final long run, session, down, sequence, started;
    private long next;
    TouchCancellation(long run, long session, long down, long sequence, long now) {
        this.run=run; this.session=session; this.down=down; this.sequence=sequence; started=now; next=now;
    }
    byte[] claim(long now) {
        if (now < next || expired(now)) return null;
        next=now+RETRY_NS;
        return ByteBuffer.allocate(22).order(ByteOrder.LITTLE_ENDIAN).put((byte)2).put((byte)9)
                .putLong(run).putInt((int)session).putInt((int)down).putInt((int)sequence).array();
    }
    long waitNanos(long now) { return Math.max(0, Math.min(next, started+LIMIT_NS)-now); }
    boolean expired(long now) { return now-started>=LIMIT_NS; }
    boolean matches(Ack ack) { return ack!=null && ack.run==run && ack.session==session && ack.down==down && ack.sequence==sequence; }
    static boolean matches(byte[] bytes, int length) {
        return length>=4 && bytes[0]=='R' && bytes[1]=='P' && bytes[2]=='T' && bytes[3]=='C';
    }
    static Ack decode(byte[] bytes, int length) {
        if (length!=28 || !matches(bytes,length) || bytes[4]!=1 || bytes[5]!=1 || bytes[6]!=0 || bytes[7]!=0) return null;
        ByteBuffer b=ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN); b.position(8);
        Ack ack=new Ack(b.getLong(),Integer.toUnsignedLong(b.getInt()),Integer.toUnsignedLong(b.getInt()),Integer.toUnsignedLong(b.getInt()));
        return ack.down<ack.sequence ? ack : null;
    }
}
