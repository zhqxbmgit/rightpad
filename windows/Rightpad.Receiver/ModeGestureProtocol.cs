using System.Buffers.Binary;
namespace Rightpad.Receiver;

internal static class ModeGestureProtocol
{
    public const int ContactSize = 96, ReplySize = 56;
    public static bool TryDecode(ReadOnlySpan<byte> b, out ModeContact contact)
    {
        contact=default;
        if (b.Length!=ContactSize || b[0]!=2 || b[1]!=10 || b[26]>3 || b[27]!=2 || b[28]>3 ||
            b[29..32].IndexOfAnyExcept((byte)0)>=0 || b[76..80].IndexOfAnyExcept((byte)0)>=0 ||
            b[26]!=(byte)ModeContactPhase.Up && b[28]!=0) return false;
        return DecodeFields(b, out contact);
    }
    private static bool DecodeFields(ReadOnlySpan<byte> b, out ModeContact contact)
    {
        // Keep framing allocation-free on the Receiver input worker.
        var g=new ModeGeometry(BinaryPrimitives.ReadUInt64LittleEndian(b[40..]),
            BinaryPrimitives.ReadInt32LittleEndian(b[48..]),BinaryPrimitives.ReadInt32LittleEndian(b[52..]),
            BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(b[56..])),
            new(BinaryPrimitives.ReadInt32LittleEndian(b[60..]),BinaryPrimitives.ReadInt32LittleEndian(b[64..]),
                BinaryPrimitives.ReadInt32LittleEndian(b[68..]),BinaryPrimitives.ReadInt32LittleEndian(b[72..])),
            BinaryPrimitives.ReadInt32LittleEndian(b[80..]),BinaryPrimitives.ReadInt32LittleEndian(b[84..]),
            BinaryPrimitives.ReadInt32LittleEndian(b[88..]),BinaryPrimitives.ReadInt32LittleEndian(b[92..]));
        contact=new(BinaryPrimitives.ReadUInt64LittleEndian(b[2..]),BinaryPrimitives.ReadUInt64LittleEndian(b[10..]),
            BinaryPrimitives.ReadUInt32LittleEndian(b[18..]),BinaryPrimitives.ReadUInt32LittleEndian(b[22..]),(ModeContactPhase)b[26],
            BinaryPrimitives.ReadUInt64LittleEndian(b[32..]),b[28],g);
        return contact.Generation!=0 && contact.Revision!=0 && g.IsValid &&
            (contact.Phase==ModeContactPhase.Idle ? contact.Sequence==0 && contact.Contact==0 : contact.Sequence!=0);
    }
    public static byte[] Reply(ulong run, ulong generation, uint ack, bool ready, ModeBinding binding, ulong revision, ulong geometry)
    {
        byte[] b=new byte[ReplySize];"RPMD"u8.CopyTo(b);b[4]=2;b[5]=1;
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(8),run);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(16),generation);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(24),ack);b[28]=ready?(byte)1:(byte)0;b[29]=(byte)binding.Purpose;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(32),(ushort)(binding.Gesture.FirstWidthDp*2));
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(34),(ushort)(binding.Gesture.SecondWidthDp*2));
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(40),revision);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(48),geometry);return b;
    }
}
