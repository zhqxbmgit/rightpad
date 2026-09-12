using System.Buffers.Binary;

namespace Rightpad.Receiver;

internal static class DiscoveryCodec
{
    public const int Port = 50001, DiscoverSize = 16, OfferSize = 40;
    private static bool Header(ReadOnlySpan<byte> data, int size, byte type) =>
        data.Length == size && data[..4].SequenceEqual("RPAD"u8) &&
        data[4] == 1 && data[5] == type && data[6] == 0 && data[7] == 0;

    public static bool TryDiscover(ReadOnlySpan<byte> data, out ulong nonce)
    {
        nonce = 0;
        if (!Header(data, DiscoverSize, 1)) return false;
        nonce = BinaryPrimitives.ReadUInt64LittleEndian(data[8..]);
        return true;
    }

    public static byte[] Discover(ulong nonce)
    {
        var data = new byte[DiscoverSize];
        WriteHeader(data, 1, nonce);
        return data;
    }

    public static byte[] Offer(ulong nonce, ReadOnlySpan<byte> receiverId)
    {
        if (receiverId.Length != 16) throw new ArgumentException("Identity must be 16 opaque bytes.");
        var data = new byte[OfferSize];
        WriteHeader(data, 2, nonce);
        receiverId.CopyTo(data.AsSpan(16));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(32), UdpReceiver.Port);
        data[34] = 2;
        return data;
    }

    public static bool TryOffer(ReadOnlySpan<byte> data, ulong nonce, out byte[] receiverId)
    {
        receiverId = [];
        if (!Header(data, OfferSize, 2) || data[35] != 0 || data[34] != 2 ||
            BinaryPrimitives.ReadUInt16LittleEndian(data[32..]) != UdpReceiver.Port ||
            BinaryPrimitives.ReadUInt64LittleEndian(data[8..]) != nonce) return false;
        receiverId = data.Slice(16, 16).ToArray();
        return true; // Capabilities are informational; unknown bits do not change v1 behavior.
    }

    private static void WriteHeader(Span<byte> data, byte type, ulong nonce)
    {
        "RPAD"u8.CopyTo(data);
        data[4] = 1;
        data[5] = type;
        BinaryPrimitives.WriteUInt64LittleEndian(data[8..], nonce);
    }
}
