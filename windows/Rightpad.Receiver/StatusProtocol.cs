using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;

namespace Rightpad.Receiver;

[Flags]
internal enum StatusFlags : byte
{
    MouseInputHealthy = 1, GamepadAvailable = 2, RuntimeErrorPresent = 4,
    MouseOutputFailurePresent = 8, GamepadFailurePresent = 16
}

internal readonly record struct StatusSnapshot(ulong SenderRunId, ulong ConfigEpoch,
    ulong ConfigRevision, ReceiverState State, StatusFlags Flags);
internal sealed record StatusDelivery(IPAddress Address, StatusSnapshot Snapshot);

internal static class StatusProtocol
{
    public const int Size = 36;
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(500);

    public static byte[] Encode(StatusSnapshot value)
    {
        if ((uint)value.State > 4 || ((byte)value.Flags & 0xE0) != 0 || value.ConfigEpoch == 0 || value.ConfigRevision == 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        byte[] bytes = new byte[Size];
        "RPST"u8.CopyTo(bytes);
        bytes[4] = bytes[5] = 1;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), value.SenderRunId);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), value.ConfigEpoch);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(24), value.ConfigRevision);
        bytes[32] = (byte)value.State;
        bytes[33] = (byte)value.Flags;
        return bytes;
    }

    public static bool TryDecode(ReadOnlySpan<byte> bytes, out StatusSnapshot value)
    {
        value = default;
        if (bytes.Length != Size || !bytes[..4].SequenceEqual("RPST"u8) || bytes[4] != 1 || bytes[5] != 1
            || bytes[6] != 0 || bytes[7] != 0 || bytes[34] != 0 || bytes[35] != 0
            || bytes[32] > 4 || (bytes[33] & 0xE0) != 0) return false;
        ulong epoch = BinaryPrimitives.ReadUInt64LittleEndian(bytes[16..]);
        ulong revision = BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..]);
        if (epoch == 0 || revision == 0) return false;
        value = new(BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]), epoch, revision,
            (ReceiverState)bytes[32], (StatusFlags)bytes[33]);
        return true;
    }

    // Read-only observation: status never admits/renews presence or changes any input lease.
    public static StatusDelivery? Capture(RuntimeStatsSnapshot runtime, ulong epoch, ulong revision, long now)
    {
        var p = runtime.Presence;
        if (p is not { Connected: true, RunId: ulong run } || epoch == 0 || revision == 0
            || Stopwatch.GetElapsedTime(p.LastSeenAtTicks, now) >= UdpReceiver.PresenceTimeout
            || !IPAddress.TryParse(p.RemoteIp, out var address)) return null;
        StatusFlags flags = 0;
        bool runtimeError = runtime.RuntimeState == ReceiverState.Error || runtime.LastError is not null;
        if (runtimeError) flags |= StatusFlags.RuntimeErrorPresent;
        if (runtime.MouseOutputFailures > 0) flags |= StatusFlags.MouseOutputFailurePresent;
        if (runtime.RuntimeState == ReceiverState.Running && runtime.MouseAvailable
            && !runtimeError && runtime.MouseOutputFailures == 0) flags |= StatusFlags.MouseInputHealthy;
        if (runtime.GamepadAvailable) flags |= StatusFlags.GamepadAvailable;
        if (runtime.GamepadFailures > 0 || runtime.LastGamepadError is not null) flags |= StatusFlags.GamepadFailurePresent;
        return new(address, new(run, epoch, revision, runtime.RuntimeState, flags));
    }
}
