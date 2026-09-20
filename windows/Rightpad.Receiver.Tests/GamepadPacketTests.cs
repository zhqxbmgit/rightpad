using System.Buffers.Binary;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class GamepadPacketTests
{
    public static readonly (string Name, Action Run)[] Cases =
    [
        ("gamepad packet exact golden LE full state", Golden),
        ("gamepad packet all truncated/oversize lengths", Lengths),
        ("gamepad packet version/type/reserved-bit validation", InvalidFields),
        ("gamepad packet Neutral/A/B/Y analog zeros", Buttons),
        ("gamepad packet independent Touch decoder", TouchUnchanged)
    ];
    internal static byte[] Encode(ulong run, uint sequence, XboxGamepadState state, ushort dwell = 0, bool force = false)
    {
        var bytes = new byte[30]; bytes[0] = 2; bytes[1] = 5;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(2), run);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(10), sequence);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), (ushort)state.Buttons);
        bytes[16] = state.LeftTrigger; bytes[17] = state.RightTrigger;
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(18), state.LeftThumbX);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20), state.LeftThumbY);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22), state.RightThumbX);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(24), state.RightThumbY);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(26), dwell);
        bytes[28] = force ? (byte)1 : (byte)0;
        return bytes;
    }
    private static void Golden()
    {
        var bytes = Convert.FromHexString("02051032547698badcfe214365870b0011ff0080ff7f2efb2e1600000000");
        Check(PacketDecoder.TryDecodeGamepad(bytes, out var p, out var error), error);
        Equal(30, bytes.Length, "size"); Equal(0xfedcba9876543210UL, p.SenderRunId, "all 64 run bits");
        Equal(0x87654321U, p.Sequence, "unsigned sequence");
        Equal(new XboxGamepadState(XboxGamepadButtons.A | XboxGamepadButtons.B | XboxGamepadButtons.Y,
            17, 255, short.MinValue, short.MaxValue, -1234, 5678), p.State, "full fields");
        Check(Encode(p.SenderRunId, p.Sequence, p.State).SequenceEqual(bytes), "same Android golden");
    }
    private static void Lengths()
    {
        var valid = Encode(1, 0, default);
        for (int size = 0; size < 30; size++) Check(!PacketDecoder.TryDecodeGamepad(valid.AsSpan(0, size), out _, out _), "short " + size);
        foreach (int size in new[] {31, 36, 65507}) {
            var extra = new byte[size]; valid.CopyTo(extra, 0);
            Check(!PacketDecoder.TryDecodeGamepad(extra, out _, out _), "trailing " + size);
        }
    }
    private static void InvalidFields()
    {
        var p = Encode(0, uint.MaxValue, default);
        Check(PacketDecoder.TryDecodeGamepad(p, out var packet, out _), "zero run is valid bit pattern");
        Equal(uint.MaxValue, packet.Sequence, "max sequence valid");
        p[0] = 1; Check(!PacketDecoder.TryDecodeGamepad(p, out _, out _), "old version"); p[0] = 2;
        p[1] = 4; Check(!PacketDecoder.TryDecodeGamepad(p, out _, out _), "not gamepad"); p[1] = 5;
        p[15] = 0x80; Check(!PacketDecoder.TryDecodeGamepad(p, out _, out _), "reserved bit rejected");
        p[14] = 0xff; p[15] = 0x7f; Check(PacketDecoder.TryDecodeGamepad(p, out _, out _), "all defined bits supported");
        p[28] = 2; Check(!PacketDecoder.TryDecodeGamepad(p, out _, out _), "unknown flag"); p[28] = 0;
        p[29] = 1; Check(!PacketDecoder.TryDecodeGamepad(p, out _, out _), "reserved byte"); p[29] = 0;
        Check(PacketDecoder.TryDecodeGamepad(Encode(1, 0, new(XboxGamepadButtons.X), 65535), out packet, out _)
            && packet.MinimumDwellMs == 65535, "full uint16 generic dwell");
        Check(PacketDecoder.TryDecodeGamepad(Encode(1, 0, default, force: true), out packet, out _) && packet.ForceNeutral, "force Neutral");
        Check(!PacketDecoder.TryDecodeGamepad(Encode(1, 0, default, 25), out _, out _), "Neutral cannot request dwell");
        Check(!PacketDecoder.TryDecodeGamepad(Encode(1, 0, new(XboxGamepadButtons.X), force: true), out _, out _), "force must carry Neutral");
    }
    private static void Buttons()
    {
        foreach (var button in new[] { XboxGamepadButtons.None, XboxGamepadButtons.A, XboxGamepadButtons.B, XboxGamepadButtons.Y })
        {
            Check(PacketDecoder.TryDecodeGamepad(Encode(7, 42, new(button)), out var p, out _), "decode");
            Equal(new XboxGamepadState(button), p.State, "only logical button, analog zero");
        }
    }
    private static void TouchUnchanged()
    {
        var touch = PacketDecoderTests.Encode(TouchEventType.Down, 1, 0, new TouchSample(123, 10, 20));
        Check(PacketDecoder.TryDecode(touch, out var decoded, out _), "Touch still decodes");
        Equal(0U, decoded!.Header.Sequence, "Touch sequence still at offset 16");
        Check(!PacketDecoder.TryDecodeGamepad(touch, out _, out _), "no cross decode");
        Check(!PacketDecoder.TryDecode(Encode(1, 0, default), out _, out _), "type5 never sent to Touch processor");
    }
}
