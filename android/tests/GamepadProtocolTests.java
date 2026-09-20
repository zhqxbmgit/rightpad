package com.rightpad.capture;
import java.nio.*;
import java.util.*;

public final class GamepadProtocolTests {
    static int checks;
    static void check(boolean ok, String message) { checks++; if (!ok) throw new AssertionError(message); }
    public static void main(String[] args) {
        long run = 0xfedcba9876543210L;
        byte[] bytes = GamepadProtocol.encode(run, 0x87654321L,
                new GamepadState(11, 17, 255, (short)-32768, (short)32767, (short)-1234, (short)5678));
        check(bytes.length == 30, "exact size");
        check(HexFormat.of().formatHex(bytes).equals("02051032547698badcfe214365870b0011ff0080ff7f2efb2e1600000000"), "golden LE all fields");
        ByteBuffer b = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN);
        check(b.getLong(2) == run && Integer.toUnsignedLong(b.getInt(10)) == 0x87654321L, "unsigned identities");
        for (int button : new int[] {0, GamepadState.B, GamepadState.Y, GamepadState.A}) {
            bytes = GamepadProtocol.encode(0, 0, new GamepadState(button));
            check(bytes[14] == button && bytes[15] == 0, "logical button " + button);
            check(Arrays.equals(Arrays.copyOfRange(bytes, 16, 26), new byte[10]), "analog neutral");
        }
        for (int invalid : new int[] {-1, 0x8000, 0x10000}) {
            try { new GamepadState(invalid); throw new AssertionError("unknown bits allowed"); }
            catch (IllegalArgumentException expected) { checks++; }
        }
        check(GamepadState.NEUTRAL.neutral() && !new GamepadState(GamepadState.B).neutral(), "neutral equality");
        bytes = GamepadProtocol.encode(1, 0, new GamepadStateSubmission(new GamepadState(GamepadState.X), 65535, false));
        check((bytes[26] & 255) == 255 && (bytes[27] & 255) == 255 && bytes[28] == 0 && bytes[29] == 0, "unsigned dwell, reserved zero");
        bytes = GamepadProtocol.encode(1, 1, GamepadStateSubmission.safety());
        check(bytes[26] == 0 && bytes[27] == 0 && bytes[28] == 1 && bytes[29] == 0, "FORCE_NEUTRAL encoded");
        try { new GamepadStateSubmission(new GamepadState(GamepadState.X), 65536, false); throw new AssertionError("oversize dwell"); }
        catch (IllegalArgumentException expected) { checks++; }
        System.out.println("RESULT GamepadProtocolTests checks=" + checks + " failed=0");
    }
}
