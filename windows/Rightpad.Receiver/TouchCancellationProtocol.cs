using System.Buffers.Binary;

namespace Rightpad.Receiver;

internal readonly record struct TouchCancellation(ulong Run, uint Session, uint Down, uint Sequence);
internal static class TouchCancellationProtocol
{
    public static bool TryDecode(ReadOnlySpan<byte> b, out TouchCancellation cancel)
    {
        cancel=default;
        if(b.Length!=22 || b[0]!=2 || b[1]!=9) return false;
        cancel=new(BinaryPrimitives.ReadUInt64LittleEndian(b[2..]),BinaryPrimitives.ReadUInt32LittleEndian(b[10..]),
            BinaryPrimitives.ReadUInt32LittleEndian(b[14..]),BinaryPrimitives.ReadUInt32LittleEndian(b[18..]));
        return cancel.Down<cancel.Sequence;
    }
    public static byte[] Acknowledge(TouchCancellation cancel)
    {
        byte[] b=new byte[28]; "RPTC"u8.CopyTo(b); b[4]=1;b[5]=1;
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(8),cancel.Run);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(16),cancel.Session);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(20),cancel.Down);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(24),cancel.Sequence);
        return b;
    }
}
