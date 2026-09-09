using System.Buffers.Binary;

namespace Rightpad.Receiver;

internal static class PacketDecoder
{
    public const int HeaderSize = 20;
    public const int HeartbeatSize = 10;
    public const int SampleSize = 16;

    public static bool TryDecode(ReadOnlySpan<byte> data, out TouchPacket? packet, out string error)
    {
        packet = null;
        error = "";
        if (data.Length < HeartbeatSize) { error = "short_header"; return false; }
        if (data[0] != 2) { error = "unsupported_version"; return false; }
        if (data[1] is < 1 or > 4) { error = "unsupported_event"; return false; }
        ulong runId = BinaryPrimitives.ReadUInt64LittleEndian(data[2..]);
        if (data[1] == 4)
        {
            if (data.Length != HeartbeatSize) { error = "heartbeat_length"; return false; }
            // No touch fields exist on the heartbeat wire; zeros are local model defaults only.
            packet = new(new(2, TouchEventType.Heartbeat, 0, 0, 0, runId), []);
            return true;
        }
        if (data.Length < HeaderSize) { error = "short_header"; return false; }

        var type = (TouchEventType)data[1];
        ushort count = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]);
        if (count == 0 || (type != TouchEventType.Move && count != 1))
        {
            error = "invalid_sample_count";
            return false;
        }
        if (data.Length != HeaderSize + count * SampleSize)
        {
            error = "length_mismatch";
            return false;
        }

        var samples = new TouchSample[count];
        for (int i = 0; i < count; i++)
        {
            var sample = data.Slice(HeaderSize + i * SampleSize, SampleSize);
            float x = BinaryPrimitives.ReadSingleLittleEndian(sample[8..]);
            float y = BinaryPrimitives.ReadSingleLittleEndian(sample[12..]);
            if (!float.IsFinite(x) || !float.IsFinite(y))
            {
                error = "non_finite_coordinate";
                return false;
            }
            samples[i] = new TouchSample(BinaryPrimitives.ReadUInt64LittleEndian(sample), x, y);
        }

        packet = new TouchPacket(new PacketHeader(data[0], type, count,
            BinaryPrimitives.ReadUInt32LittleEndian(data[12..]),
            BinaryPrimitives.ReadUInt32LittleEndian(data[16..]), runId), samples);
        return true;
    }
}
