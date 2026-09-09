namespace Rightpad.Receiver;

internal enum TouchEventType : byte { Down = 1, Move = 2, Up = 3, Heartbeat = 4 }

internal readonly record struct PacketHeader(
    byte Version, TouchEventType EventType, ushort SampleCount, uint SessionId, uint Sequence,
    ulong SenderRunId = 0);

internal readonly record struct TouchSample(ulong TimestampNs, float X, float Y);

internal sealed record TouchPacket(PacketHeader Header, TouchSample[] Samples);

// Receiver-local diagnostics; these fields are never added to the wire format.
internal readonly record struct SequenceObservation(
    string Status, uint? PreviousSequence, long? Delta, bool Accepted);
