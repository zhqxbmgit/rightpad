namespace Rightpad.Receiver;

internal sealed class PacketStatistics
{
    public long ReceivedPackets { get; private set; }
    public long InvalidPackets { get; private set; }
    public long AcceptedPackets { get; private set; }
    public long AcceptedSamples { get; private set; }
    public long SequenceGapEstimate { get; private set; }
    public long DuplicatePackets { get; private set; }
    public long OldPackets { get; private set; }
    public uint? LastSequence { get; private set; }

    public void RecordReceived() => ReceivedPackets++;
    public void RecordInvalid() => InvalidPackets++;

    public SequenceObservation Observe(PacketHeader header)
    {
        uint? previous = LastSequence;
        long? delta = previous.HasValue ? (long)header.Sequence - previous.Value : null;
        if (delta == 0)
        {
            DuplicatePackets++;
            return new("duplicate", previous, delta, false);
        }
        if (delta < 0)
        {
            OldPackets++;
            return new("old", previous, delta, false);
        }

        // Ordinary uint32 ordering only. Restart the receiver after a sender restart.
        if (delta > 1) SequenceGapEstimate += delta.Value - 1;
        LastSequence = header.Sequence;
        AcceptedPackets++;
        AcceptedSamples += header.SampleCount;
        return new(previous.HasValue ? (delta > 1 ? "gap" : "in_order") : "baseline",
            previous, delta, true);
    }
}
