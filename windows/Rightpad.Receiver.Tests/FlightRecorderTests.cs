using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using static Rightpad.Receiver.Tests.Program;
using static Rightpad.Receiver.Tests.RawMotionProcessorTests;

namespace Rightpad.Receiver.Tests;

internal static class FlightRecorderTests
{
    private sealed class MemoryStorage(Action<string>? write = null) : IFlightRecordStorage
    {
        public readonly ConcurrentQueue<string> Lines = new();
        public void WriteLine(string line) { write?.Invoke(line); Lines.Enqueue(line); }
        public void Dispose() { }
    }

    private static JsonElement Json(string line) => JsonDocument.Parse(line).RootElement.Clone();

    public static async Task FormattingAndTimestamps()
    {
        var storage = new MemoryStorage(); long now = Stopwatch.Frequency * 20;
        using var recorder = new FlightRecorder(storage, monotonicNow: () => now,
            wallNow: () => new DateTimeOffset(2026, 9, 9, 18, 50, 0, TimeSpan.FromHours(8)));
        recorder.Snapshot(new RuntimeStatsSnapshot(3, ReceiverState.Running, now - Stopwatch.Frequency,
            ReceivedPackets: 11, AcceptedPackets: 7, AcceptedSamples: 9, Presence: new(0xAB, now, true, "127.0.0.1"),
            HeartbeatPackets: 4, LastHeartbeatAtTicks: now, LastTouchDatagramAtTicks: now - 2 * Stopwatch.Frequency,
            MotionOutputEvents: 5, SendInputSuccesses: 6));
        await RuntimeTests.Until(() => storage.Lines.Count == 1);
        var j = Json(storage.Lines.Single());
        Equal("snapshot", j.GetProperty("type").GetString(), "snapshot type");
        Equal("Connected", j.GetProperty("connectionState").GetString(), "connection state");
        Equal("00000000000000AB", j.GetProperty("senderRunId").GetString(), "run formatting");
        Equal(2000d, j.GetProperty("lastTouchDatagramAgeMs").GetDouble(), "monotonic age");
        Equal(5L, j.GetProperty("motionOutputEvents").GetInt64(), "existing motion counter");
    }

    public static async Task PeriodicSnapshot()
    {
        var storage = new MemoryStorage(); int captures = 0;
        using var recorder = new FlightRecorder(storage);
        recorder.StartSnapshots(() => { Interlocked.Increment(ref captures); return new(1, ReceiverState.Running, Presence: new(1, 1, true)); }, TimeSpan.FromMilliseconds(10));
        await RuntimeTests.Until(() => storage.Lines.Count >= 2);
        Check(captures >= 2, "periodic snapshots use capture delegate");
    }

    public static async Task EventFormatting()
    {
        var storage = new MemoryStorage();
        using var recorder = new FlightRecorder(storage, monotonicNow: () => 123,
            wallNow: () => DateTimeOffset.UnixEpoch);
        recorder.Event("presence_connected", ("senderRunId", "01"));
        await RuntimeTests.Until(() => storage.Lines.Count == 1);
        var j = Json(storage.Lines.Single());
        Equal("event", j.GetProperty("type").GetString(), "event type");
        Equal("presence_connected", j.GetProperty("event").GetString(), "event name");
        Equal(123L, j.GetProperty("monotonicTicks").GetInt64(), "event monotonic time");
    }

    public static void Rotation()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"rightpad-flight-{Guid.NewGuid():N}");
        try
        {
            using (var storage = new RollingFlightRecordStorage(directory, 80))
                for (int i = 0; i < 8; i++) storage.WriteLine(new string((char)('a' + i), 30));
            string current = Path.Combine(directory, RollingFlightRecordStorage.CurrentFileName);
            string previous = Path.Combine(directory, RollingFlightRecordStorage.PreviousFileName);
            Check(File.Exists(current) && File.Exists(previous), "two rolling files exist");
            Check(new FileInfo(current).Length <= 80 && new FileInfo(previous).Length <= 80, "size cap respected");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    public static async Task WriterFailureDoesNotAffectRuntime()
    {
        var storage = new MemoryStorage(_ => throw new IOException("disk unavailable"));
        using var recorder = new FlightRecorder(storage);
        recorder.Event("force_failure");
        await RuntimeTests.Until(() => recorder.Disabled);
        var runtime = new ReceiverRuntime(new(), TextWriter.Null, new IPEndPoint(IPAddress.Loopback, 0),
            rawMouse: false, flightRecorder: recorder);
        await runtime.StartAsync();
        Equal(ReceiverState.Running, runtime.CaptureSnapshot().RuntimeState, "diagnostic failure does not stop runtime");
        await runtime.StopAsync();
    }

    public static async Task QueueOverflowIsNonblocking()
    {
        using var release = new ManualResetEventSlim();
        var storage = new MemoryStorage(_ => release.Wait());
        var recorder = new FlightRecorder(storage, queueCapacity: 1);
        try
        {
            recorder.Event("block_writer"); await Task.Delay(20);
            var elapsed = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++) recorder.Event("overflow", ("i", i));
            Check(elapsed.Elapsed < TimeSpan.FromSeconds(1), "queue overflow never blocks producer");
            Check(recorder.DroppedRecords > 0, "overflow drops diagnostic records");
        }
        finally { release.Set(); recorder.Dispose(); }
    }

    public static void MotionCounters()
    {
        long now = 321; var motion = new TouchSessionProcessor((_, _) => { }, monotonicNow: () => now);
        motion.Process(TouchSessionProcessorTests.Packet(TouchEventType.Down, 1, new TouchSample(0, 0, 0)));
        motion.Process(TouchSessionProcessorTests.Packet(TouchEventType.Move, 1, new TouchSample(1, 2, 0)));
        Equal(1L, motion.OutputEvents, "motion output count"); Equal(now, motion.LastOutputAtTicks, "motion output time");
    }

    public static void SendInputCounters()
    {
        long now = 100; uint result = 1;
        var mouse = new WindowsMouseOutput((uint c, ref WindowsMouseOutput.NativeInput i, int s) => result,
            () => 5, () => now);
        mouse.Move(1, 0); mouse.LeftDown();
        Equal(2L, mouse.SuccessfulCalls, "all successful SendInput calls"); Equal(100L, mouse.LastSuccessfulAtTicks, "success time");
        result = 0; now = 200; Throws<System.ComponentModel.Win32Exception>(() => mouse.Move(1, 0));
        Equal(1L, mouse.AllFailedCalls, "all failed SendInput calls"); Equal(200L, mouse.LastFailedAtTicks, "failure time");
    }

    public static async Task RuntimeRestartBoundary()
    {
        var storage = new MemoryStorage(); using var recorder = new FlightRecorder(storage);
        var runtime = new ReceiverRuntime(new(), TextWriter.Null, new IPEndPoint(IPAddress.Loopback, 0), rawMouse: false, flightRecorder: recorder);
        await runtime.StartAsync(); await runtime.StopAsync(); await runtime.StartAsync(); await runtime.StopAsync();
        await RuntimeTests.Until(() => storage.Lines.Count(l => l.Contains("runtime_start")) == 2);
        Equal(2, storage.Lines.Count(l => l.Contains("runtime_stop")), "two clear runtime stop boundaries");
    }
}
