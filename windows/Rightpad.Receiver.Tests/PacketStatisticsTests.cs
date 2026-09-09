using Rightpad.Receiver;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class PacketStatisticsTests
{
    private static SequenceObservation Receive(PacketStatistics stats, uint sequence, uint session = 1, ushort count = 1)
    {
        stats.RecordReceived();
        return stats.Observe(new PacketHeader(2, TouchEventType.Move, count, session, sequence));
    }

    public static void Continuous()
    {
        var stats = new PacketStatistics();
        var first = Receive(stats, 1000);
        Equal("baseline", first.Status, "first packet");
        Check(first.Delta is null && first.PreviousSequence is null, "baseline not empty");
        Equal("in_order", Receive(stats, 1001, 1, 3).Status, "consecutive");
        Equal("in_order", Receive(stats, 1002, 2).Status, "new session does not reset sequence");
        Equal(3L, stats.AcceptedPackets, "packet count");
        Equal(5L, stats.AcceptedSamples, "sample count");
        Equal(0L, stats.SequenceGapEstimate, "no initial loss assumption");
    }

    public static void GapsAndOldPackets()
    {
        var stats = new PacketStatistics();
        Receive(stats, 10);
        var gap = Receive(stats, 13);
        Equal("gap", gap.Status, "jump");
        Equal(3L, gap.Delta!.Value, "jump size");
        Check(!Receive(stats, 13).Accepted, "duplicate accepted");
        var old = Receive(stats, 11);
        Equal("old", old.Status, "old packet");
        Equal(-2L, old.Delta!.Value, "old delta");
        Equal(13U, stats.LastSequence!.Value, "old packet moved baseline");
        Equal(2L, stats.SequenceGapEstimate, "late packet must not erase cumulative gap");
        Equal("in_order", Receive(stats, 14).Status, "resume");
        Equal(5L, stats.ReceivedPackets, "total includes ignored packets");
        Equal(3L, stats.AcceptedSamples, "ignored packets do not add samples");
        Equal(1L, stats.DuplicatePackets, "duplicates");
        Equal(1L, stats.OldPackets, "old packets");
    }

    public static void NoWraparound()
    {
        var stats = new PacketStatistics();
        Receive(stats, uint.MaxValue - 1);
        Equal("in_order", Receive(stats, uint.MaxValue).Status, "large uint");
        var old = Receive(stats, 0);
        Equal("old", old.Status, "wrap deliberately unsupported");
        Equal(-4294967295L, old.Delta!.Value, "signed diagnostic delta");
        Equal(uint.MaxValue, stats.LastSequence!.Value, "baseline retained");
        var newRun = new PacketStatistics();
        Equal("baseline", Receive(newRun, 0).Status, "restart baseline");
    }

    public static void InvalidPackets()
    {
        var stats = new PacketStatistics();
        Receive(stats, 10);
        stats.RecordReceived(); stats.RecordInvalid();
        Equal("in_order", Receive(stats, 11).Status, "bad packet changed sequence");
        Equal(3L, stats.ReceivedPackets, "received");
        Equal(1L, stats.InvalidPackets, "invalid");
        Equal(2L, stats.AcceptedPackets, "accepted");
    }
}
