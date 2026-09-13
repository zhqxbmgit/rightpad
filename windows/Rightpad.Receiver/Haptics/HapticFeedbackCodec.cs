using System.Buffers.Binary;

namespace Rightpad.Receiver;

internal readonly record struct ClickFeedback(ulong SenderRunId, uint SessionId, uint UpSequence);

internal static class HapticFeedbackCodec
{
    public const int Port = 50002;
    public const int Size = 24;

    public static byte[] Encode(ClickFeedback click)
    {
        byte[] bytes = new byte[Size];
        "RPHF"u8.CopyTo(bytes);
        bytes[4] = 1;
        bytes[5] = 1; // CLICK; reserved bytes 6..7 remain zero.
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), click.SenderRunId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), click.SessionId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), click.UpSequence);
        return bytes;
    }

    public static bool TryDecode(ReadOnlySpan<byte> bytes, out ClickFeedback click)
    {
        click = default;
        if (bytes.Length != Size || !bytes[..4].SequenceEqual("RPHF"u8) ||
            bytes[4] != 1 || bytes[5] != 1 || bytes[6] != 0 || bytes[7] != 0) return false;
        click = new(BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..]));
        return true;
    }
}
