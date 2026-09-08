using System.Globalization;
using System.Net;

namespace Rightpad.Receiver;

internal sealed class RawSampleLogger(TextWriter output, bool detailed = true)
{
    private static string Number<T>(T value) where T : IFormattable =>
        value.ToString(null, CultureInfo.InvariantCulture);

    public void Listening(IPEndPoint endpoint) => output.WriteLine($"listening: udp={endpoint}");

    public void Invalid(double receiveElapsedMs, IPEndPoint remote, int bytes, string reason) =>
        output.WriteLine($"packet: receiveElapsedMs={Number(receiveElapsedMs)} remote={remote} " +
            $"bytes={bytes} status=invalid reason={reason}");

    public void Packet(double receiveElapsedMs, IPEndPoint remote, TouchPacket packet,
        SequenceObservation observation)
    {
        if (!detailed && observation.Status is not ("gap" or "old")) return;
        var h = packet.Header;
        output.WriteLine($"packet: receiveElapsedMs={Number(receiveElapsedMs)} remote={remote} " +
            $"version={h.Version} eventType={h.EventType.ToString().ToUpperInvariant()} " +
            $"sessionId={h.SessionId} sequence={h.Sequence} sampleCount={h.SampleCount} " +
            $"previousSequence={observation.PreviousSequence?.ToString(CultureInfo.InvariantCulture) ?? "NA"} " +
            $"sequenceDelta={observation.Delta?.ToString(CultureInfo.InvariantCulture) ?? "NA"} " +
            $"status={observation.Status}");
        if (!detailed || !observation.Accepted) return;
        for (int i = 0; i < packet.Samples.Length; i++)
        {
            var sample = packet.Samples[i];
            output.WriteLine($"sample: sessionId={h.SessionId} sequence={h.Sequence} sampleIndex={i} " +
                $"timestampNs={sample.TimestampNs} x={Number(sample.X)} y={Number(sample.Y)}");
        }
    }

    public void Timeout(double receiveElapsedMs) =>
        output.WriteLine($"receiver: status=input_timeout receiveElapsedMs={Number(receiveElapsedMs)}");

    public void Stats(PacketStatistics stats) => output.WriteLine(
        $"stats: receivedPackets={stats.ReceivedPackets} invalidPackets={stats.InvalidPackets} " +
        $"acceptedPackets={stats.AcceptedPackets} acceptedSamples={stats.AcceptedSamples} " +
        $"sequenceGapEstimate={stats.SequenceGapEstimate} duplicatePackets={stats.DuplicatePackets} " +
        $"oldPackets={stats.OldPackets}");

    public void Stopped() => output.WriteLine("receiver: status=stopped");

    public void Summary(long timeouts, TouchSessionProcessor? motion)
    {
        output.WriteLine($"receiver_stats: inputTimeouts={timeouts}");
        if (motion is null) return;
        output.WriteLine($"motion_stats: processedSamples={motion.ProcessedMotionSamples} " +
            $"ignoredSessionPackets={motion.IgnoredSessionPackets} outputEvents={motion.OutputEvents} " +
            $"totalDx={motion.TotalDx} totalDy={motion.TotalDy}");
    }
}
