package com.rightpad.capture;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;

/** Independent RPLY framing on the existing two UDP channels. Never Touch presence. */
final class RemoteLayoutProtocol {
    static final int HEADER = 88, MAX = 4096;
    static final int QUERY = 1, TRY = 2, KEEP = 3, REVERT = 4, SNAPSHOT = 128;
    record Item(String id, String label, int flags, ControlRect rect) { }
    record Message(int kind, int state, int error, long run, long epoch, long revision,
            long geometry, long transaction, long request, long next, long serial,
            int remaining, int width, int height, List<Item> items) { }
    static boolean matches(byte[] b, int n) {
        return n >= 4 && b[0] == 'R' && b[1] == 'P' && b[2] == 'L' && b[3] == 'Y';
    }
    static Message decode(byte[] bytes, int length) {
        if (length < HEADER || length > MAX || !matches(bytes, length)) return null;
        try {
            ByteBuffer b = ByteBuffer.wrap(bytes, 0, length).order(ByteOrder.LITTLE_ENDIAN);
            b.position(4);
            if (b.get() != 1) return null;
            int kind = Byte.toUnsignedInt(b.get()), state = Byte.toUnsignedInt(b.get()), error = Byte.toUnsignedInt(b.get());
            if ((kind < QUERY || kind > REVERT) && kind != SNAPSHOT) return null;
            if (state > 5 || error > 6) return null;
            long run = b.getLong(), epoch = b.getLong(), revision = b.getLong(), geometry = b.getLong();
            long txn = b.getLong(), request = b.getLong(), next = b.getLong(), serial = b.getLong();
            int remaining = b.getInt(), width = b.getInt(), height = b.getInt();
            int count = Short.toUnsignedInt(b.getShort());
            if (b.getShort() != 0 || count > 16 || remaining < 0 || remaining > 60000) return null;
            List<Item> items = new ArrayList<>();
            var ids = new HashSet<String>();
            for (int i = 0; i < count; i++) {
                int idLength = Byte.toUnsignedInt(b.get()), labelLength = Byte.toUnsignedInt(b.get());
                int flags = Short.toUnsignedInt(b.getShort());
                ControlRect r = new ControlRect(b.getInt(), b.getInt(), b.getInt(), b.getInt());
                if (idLength < 1 || idLength > 64 || labelLength > 64 || flags > 1) return null;
                byte[] id = new byte[idLength], label = new byte[labelLength]; b.get(id); b.get(label);
                String name = new String(id, StandardCharsets.UTF_8);
                if (!java.util.Arrays.equals(id, name.getBytes(StandardCharsets.UTF_8)) || !ids.add(name)) return null;
                items.add(new Item(name, new String(label, StandardCharsets.UTF_8), flags, r));
            }
            if (b.position() != length || ((kind == QUERY || kind == KEEP || kind == REVERT) && count != 0)) return null;
            return new Message(kind, state, error, run, epoch, revision, geometry, txn, request,
                    next, serial, remaining, width, height, List.copyOf(items));
        } catch (java.nio.BufferUnderflowException | IllegalArgumentException e) { return null; }
    }
    static byte[] encode(Message m) {
        ByteBuffer b = ByteBuffer.allocate(MAX).order(ByteOrder.LITTLE_ENDIAN);
        b.put(new byte[] {'R','P','L','Y',1,(byte)m.kind(),(byte)m.state(),(byte)m.error()});
        b.putLong(m.run()).putLong(m.epoch()).putLong(m.revision()).putLong(m.geometry())
                .putLong(m.transaction()).putLong(m.request()).putLong(m.next()).putLong(m.serial())
                .putInt(m.remaining()).putInt(m.width()).putInt(m.height()).putShort((short)m.items().size()).putShort((short)0);
        for (Item item : m.items()) {
            byte[] id = item.id().getBytes(StandardCharsets.UTF_8), label = item.label().getBytes(StandardCharsets.UTF_8);
            ControlRect r = item.rect();
            b.put((byte)id.length).put((byte)label.length).putShort((short)item.flags())
                    .putInt(r.x).putInt(r.y).putInt(r.width).putInt(r.height).put(id).put(label);
        }
        return java.util.Arrays.copyOf(b.array(), b.position());
    }
}
