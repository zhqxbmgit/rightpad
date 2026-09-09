namespace Rightpad.Receiver;

internal sealed class PacketStatistics
{
    private long receivedPackets, invalidPackets, acceptedPackets, acceptedSamples;
    private long sequenceGapEstimate, duplicatePackets, oldPackets;
    public long ReceivedPackets => Interlocked.Read(ref receivedPackets);
    public long InvalidPackets => Interlocked.Read(ref invalidPackets);
    public long AcceptedPackets => Interlocked.Read(ref acceptedPackets);
    public long AcceptedSamples => Interlocked.Read(ref acceptedSamples);
    public long SequenceGapEstimate => Interlocked.Read(ref sequenceGapEstimate);
    public long DuplicatePackets => Interlocked.Read(ref duplicatePackets);
    public long OldPackets => Interlocked.Read(ref oldPackets);
    public uint? LastSequence { get; private set; }

    public void RecordReceived() => Interlocked.Increment(ref receivedPackets);
    public void RecordInvalid() => Interlocked.Increment(ref invalidPackets);

    public SequenceObservation Observe(PacketHeader header)
    {
        uint? previous = LastSequence;
        long? delta = previous.HasValue ? (long)header.Sequence - previous.Value : null;
        if (delta == 0)
        {
            Interlocked.Increment(ref duplicatePackets);
            return new("duplicate", previous, delta, false);
        }
        if (delta < 0)
        {
            Interlocked.Increment(ref oldPackets);
            return new("old", previous, delta, false);
        }

        // Ordinary uint32 ordering only. Restart the receiver after a sender restart.
        if (delta > 1) Interlocked.Add(ref sequenceGapEstimate, delta.Value - 1);
        LastSequence = header.Sequence;
        Interlocked.Increment(ref acceptedPackets);
        Interlocked.Add(ref acceptedSamples, header.SampleCount);
        return new(previous.HasValue ? (delta > 1 ? "gap" : "in_order") : "baseline",
            previous, delta, true);
    }
}
