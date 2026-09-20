using System.Buffers.Binary;
namespace Rightpad.Receiver;

internal readonly record struct ControlConfigRequest(ulong SenderRunId, ulong Epoch, ulong Revision);
internal static class ControlConfigProtocol
{
    public const int HeaderSize = 36, SlideRecordSize = 12, LRRecordSize = 14, MaxRecords = 16, RequestSize = 26;
    private static int RecordSize(ControlBehavior behavior) => behavior switch {
        ControlBehavior.Slide => SlideRecordSize, ControlBehavior.SlideLR => LRRecordSize,
        _ => throw new ArgumentOutOfRangeException(nameof(behavior))
    };
    public static byte[] Encode(ulong run, ulong epoch, ulong revision, VirtualControlsSettings settings)
    {
        if (epoch == 0 || revision == 0 || !settings.IsValid || ControlDefinitions.All.Count > MaxRecords)
            throw new ArgumentOutOfRangeException(nameof(settings));
        byte[] bytes = new byte[HeaderSize + ControlDefinitions.All.Sum(d => RecordSize(d.Behavior))];
        "RPCT"u8.CopyTo(bytes); bytes[4] = 2; bytes[5] = 1;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), run);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), epoch);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(24), revision);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(32), (ushort)ControlDefinitions.All.Count);
        int offset = HeaderSize;
        foreach (var d in ControlDefinitions.All)
        {
            int size = RecordSize(d.Behavior);
            var r = bytes.AsSpan(offset, size);
            BinaryPrimitives.WriteUInt16LittleEndian(r, d.ProtocolId); r[2] = (byte)d.Behavior; r[3] = (byte)size;
            switch (d.Get(settings)) {
                case SlideControlSettings s:
                    Write(r[4..], s.SlideUpThresholdDp); Write(r[6..], s.SlideDownThresholdDp);
                    BinaryPrimitives.WriteUInt16LittleEndian(r[8..], (ushort)s.TapHoldMs);
                    BinaryPrimitives.WriteUInt16LittleEndian(r[10..], (ushort)s.LongPressMs);
                    break;
                case SlideControlLRSettings s:
                    Write(r[4..], s.SlideLeftThresholdDp); Write(r[6..], s.SlideRightThresholdDp); Write(r[8..], s.SlideUpThresholdDp);
                    BinaryPrimitives.WriteUInt16LittleEndian(r[10..], (ushort)s.TapHoldMs);
                    BinaryPrimitives.WriteUInt16LittleEndian(r[12..], (ushort)s.LongPressMs);
                    break;
            }
            offset += size;
        }
        return bytes;
    }
    private static void Write(Span<byte> destination, double dp) =>
        BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)((decimal)dp * 10));
    public static bool TryDecodeRequest(ReadOnlySpan<byte> bytes, out ControlConfigRequest request)
    {
        request = default;
        if (bytes.Length != RequestSize || bytes[0] != 2 || bytes[1] != 6) return false;
        request = new(BinaryPrimitives.ReadUInt64LittleEndian(bytes[2..]),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[10..]), BinaryPrimitives.ReadUInt64LittleEndian(bytes[18..]));
        return (request.Epoch == 0) == (request.Revision == 0);
    }
}
