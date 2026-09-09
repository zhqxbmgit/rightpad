package com.rightpad.capture;

import java.util.Arrays;
import java.util.HexFormat;

public final class ProtocolV2EncoderTest {
    private static int checks;

    public static void main(String[] args) {
        // Literal fixture also decoded by Windows PacketDecoderTests.
        byte[] expected = HexFormat.of().parseHex(
                "020208070605040302810200785634122A00000008070605040302010000C03F000010C0"
                + "FFFFFFFFFFFFFFFF000000800000803E");
        check(HexFormat.of().formatHex(ProtocolV2Encoder.heartbeat(0x8102030405060708L)).equals("02040807060504030281"), "heartbeat high-bit bytes");
        TouchSample[] move = {
                sample(1.5f, -2.25f, 0x0102030405060708L, TouchSample.Action.MOVE, true),
                sample(-0.0f, 0.25f, -1L, TouchSample.Action.MOVE, false)
        };
        check(Arrays.equals(expected, ProtocolV2Encoder.encode(move, 42, 0x8102030405060708L)), "fixed MOVE bytes");
        for (TouchSample.Action action : new TouchSample.Action[]{TouchSample.Action.DOWN, TouchSample.Action.UP}) {
            byte[] single = ProtocolV2Encoder.encode(new TouchSample[]{sample(1.5f, -2.25f,
                    0x0102030405060708L, action, false)}, 42, 0x8102030405060708L);
            byte[] singleExpected = Arrays.copyOf(expected, 36);
            singleExpected[1] = (byte) (action == TouchSample.Action.DOWN ? 1 : 3);
            singleExpected[10] = 1;
            check(Arrays.equals(singleExpected, single), action + " fixed bytes");
        }
        TouchSample repeated = sample(1.5f, -2.25f, 123456789L, TouchSample.Action.MOVE, true);
        byte[] duplicateTimes = ProtocolV2Encoder.encode(new TouchSample[]{repeated, repeated, repeated}, 0, 0x8102030405060708L);
        check(duplicateTimes.length == 68 && duplicateTimes[10] == 3
                && Arrays.equals(Arrays.copyOfRange(duplicateTimes, 20, 36),
                        Arrays.copyOfRange(duplicateTimes, 52, 68)), "repeated samples preserved");
        byte[] maxSequence = ProtocolV2Encoder.encode(move, 0xffffffffL, 0x8102030405060708L);
        check(HexFormat.of().formatHex(maxSequence, 16, 20).equals("ffffffff"), "uint32 sequence");
        rejects(new TouchSample[0], 0);
        rejects(new TouchSample[]{sample(0, 0, 1, TouchSample.Action.CANCEL, false)}, 0);
        TouchSample down = sample(0, 0, 1, TouchSample.Action.DOWN, false);
        rejects(new TouchSample[]{down, down}, 0);
        rejects(new TouchSample[]{move[0], down}, 0);
        rejects(new TouchSample[]{sample(Float.NaN, 0, 1, TouchSample.Action.MOVE, false)}, 0);
        rejects(new TouchSample[]{sample(0, Float.POSITIVE_INFINITY, 1, TouchSample.Action.MOVE, false)}, 0);
        rejects(move, -1);
        rejects(move, 0x100000000L);
        TouchSample[] tooLarge = new TouchSample[4094];
        Arrays.fill(tooLarge, repeated);
        rejects(tooLarge, 0);
        rejects(new TouchSample[]{new TouchSample(0, 0, 0, TouchSample.Action.DOWN, 0,
                0x100000000L, false)}, 0);
        System.out.println("PASS " + checks + " encoder checks; fixed MOVE=" + HexFormat.of().formatHex(expected));
    }

    private static TouchSample sample(float x, float y, long time, TouchSample.Action action, boolean history) {
        return new TouchSample(x, y, time, action, 0, 0x12345678L, history);
    }

    private static void rejects(TouchSample[] samples, long sequence) {
        try {
            ProtocolV2Encoder.encode(samples, sequence, 0x8102030405060708L);
            throw new AssertionError("Expected rejection");
        } catch (IllegalArgumentException expected) {
            checks++;
        }
    }

    private static void check(boolean condition, String label) {
        if (!condition) throw new AssertionError(label);
        checks++;
    }
}
