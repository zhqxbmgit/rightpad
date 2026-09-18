using System.Net;
using System.Text.Json;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class TauSupportMotionTests
{
    private const long Frequency = 1_000_000, Origin = 100_000;
    private static readonly MotionMode Production = MotionModes.ProductionMode;
    private static readonly (int Tau, int Support)[] Combinations =
    [
        (8, 40), (12, 60), (18, 90), (24, 120), (24, 80),
        (24, 160), (30, 150), (40, 200), (60, 300)
    ];

    public static IEnumerable<(string Name, Func<Task> Run)> Cases
    {
        get
        {
            yield return ("product Tau24/Support120 exact paired replay against fixed K24-r5", () => Sync(DefaultRegression));
            yield return ("product legal Tau/Support combinations conserve endpoint and settle", () => Sync(CombinationCorrectness));
            yield return ("product Support300 fixed history capacity boundary", () => Sync(Support300Capacity));
            yield return ("product trace metadata freezes actual Tau/Support", () => Sync(TraceMetadata));
            yield return ("runtime product Tau/Support freezes per run and applies next run", RuntimeLifecycle);
            yield return ("explicit dev mode overrides product Tau/Support", DevOverride);
        }
    }

    private sealed class Harness : IDisposable
    {
        public long Now = Origin;
        public readonly List<(int X, int Y)> Moves = [];
        public readonly ResampledMotion Motion;
        private uint sequence;
        public Harness(MotionConfiguration? configuration = null)
        {
            Motion = new((x, y) => Moves.Add((x, y)), 9, 9, () => Now, Frequency,
                finiteCriticalMode: Production, configuration: configuration);
        }
        public void Advance(int ms)
        {
            long end = Origin + ms * 1000L;
            while (Motion.Schedule.Deadline is long tick && tick <= end)
            {
                Now = tick;
                Motion.Tick(tick, Motion.Schedule.Generation);
            }
            Now = end;
        }
        public void SkipTo(int ms)
        {
            Now = Origin + ms * 1000L;
            Motion.Tick(Now, Motion.Schedule.Generation);
        }
        public void Send(TouchEventType type, int ms, float x, float y = 0)
        {
            Advance(ms);
            Motion.Process(new(new(2, type, 1, 1, sequence++, 1), [new((ulong)ms * 1_000_000, x, y)]));
        }
        public (int X, int Y) Total => (Moves.Sum(m => m.X), Moves.Sum(m => m.Y));
        public void Dispose() => Motion.Dispose();
    }

    private static Task Sync(Action action) { action(); return Task.CompletedTask; }

    private static void DefaultRegression()
    {
        using var legacy = new Harness();
        using var product = new Harness(new(Production, 24, 120));
        void Same(string stage)
        {
            Check(legacy.Moves.SequenceEqual(product.Moves), $"integer dx/dy differ at {stage}");
            Equal(legacy.Motion.Position, product.Motion.Position, $"continuous position at {stage}");
            Equal(legacy.Motion.Pending, product.Motion.Pending, $"pending canonical displacement at {stage}");
            Equal(legacy.Motion.Schedule, product.Motion.Schedule, $"cadence/generation at {stage}");
        }
        foreach (var sample in new[]
        {
            (TouchEventType.Down, 0, 0f, 0f),
            (TouchEventType.Move, 3, 7f, -2f),
        })
        {
            legacy.Send(sample.Item1, sample.Item2, sample.Item3, sample.Item4);
            product.Send(sample.Item1, sample.Item2, sample.Item3, sample.Item4);
            Same($"{sample.Item1}@{sample.Item2}");
        }
        legacy.SkipTo(25); product.SkipTo(25); Same("skipped wake at 25");
        Equal(13L, legacy.Motion.MissedTicks, "legacy skipped-tick count");
        Equal(legacy.Motion.MissedTicks, product.Motion.MissedTicks, "skipped-tick policy identical");
        foreach (var sample in new[]
        {
            (TouchEventType.Move, 30, 18f, -6f),
            (TouchEventType.Move, 35, 4f, 3f),
            (TouchEventType.Move, 43, -5f, 1f),
            (TouchEventType.Up, 49, -2f, 0f)
        })
        {
            legacy.Send(sample.Item1, sample.Item2, sample.Item3, sample.Item4);
            product.Send(sample.Item1, sample.Item2, sample.Item3, sample.Item4);
            Same($"{sample.Item1}@{sample.Item2}");
        }
        for (int t = 50; t <= 320; t++)
        {
            legacy.Advance(t); product.Advance(t); Same($"tick {t}");
        }
        Equal((-18, 0), legacy.Total, "legacy endpoint");
        Equal(legacy.Total, product.Total, "parameterized endpoint bit-equivalent");
        Check(product.Motion.Schedule.Deadline is null, "settle complete");
        Equal((0d, 0d), product.Motion.Pending, "pending canonical displacement zero");
        long oldLegacy = legacy.Motion.Schedule.Generation, oldProduct = product.Motion.Schedule.Generation;
        legacy.Motion.Reset(); product.Motion.Reset();
        legacy.Motion.Tick(Origin + 500_000, oldLegacy); product.Motion.Tick(Origin + 500_000, oldProduct);
        Same("reset and stale tick");
        Equal(1, product.Motion.PeriodMs, "production cadence remains 1 ms");
        Equal("Q0C", MotionModes.QuantizerName(Production), "Q0-C unchanged");
        Equal(ResampledMotion.PlayoutDelayMs, 12, "playout unchanged");
    }

    private static void CombinationCorrectness()
    {
        foreach (var (tau, support) in Combinations)
        {
            var configuration = new MotionConfiguration(Production, tau, support);
            Check(double.IsFinite(configuration.KernelNormalization) && configuration.KernelNormalization > 0,
                $"finite positive normalization {tau}/{support}");
            using var h = new Harness(configuration);
            h.Send(TouchEventType.Down, 0, 0, 0);
            h.Send(TouchEventType.Move, 4, 10, -2);
            h.Send(TouchEventType.Move, 9, -5, 4);
            h.Send(TouchEventType.Up, 14, 3, -1);
            h.Advance(14 + support + 40);
            Equal((27, -9), h.Total, $"endpoint conservation {tau}/{support}");
            Equal((0d, 0d), h.Motion.Pending, $"pending cleared {tau}/{support}");
            Check(h.Motion.Schedule.Deadline is null, $"settle completes {tau}/{support}");
            Equal(0L, h.Motion.UpFlushCount, $"Earned-Settle retained {tau}/{support}");
            Check(h.MovesWithinEarnedEnvelope(), $"no artificial distance {tau}/{support}");
            long generation = h.Motion.Schedule.Generation;
            h.Motion.Reset(); h.Motion.Tick(h.Now + 1_000_000, generation);
            Equal((27, -9), h.Total, $"reset cancels stale output {tau}/{support}");
        }
        DisconnectCancelsCustomTail();
    }

    private static void DisconnectCancelsCustomTail()
    {
        long now = System.Diagnostics.Stopwatch.Frequency * 10;
        var moves = new List<(int X, int Y)>();
        using var motion = new ResampledMotion((x, y) => moves.Add((x, y)), 9, 9, () => now,
            configuration: new(Production, 60, 300), finiteCriticalMode: Production);
        using var receiver = new UdpReceiver(new(IPAddress.Loopback, 0), TextWriter.Null,
            motion: motion, detailedLogging: false);
        var remote = new IPEndPoint(IPAddress.Loopback, 1234);
        receiver.ProcessDatagram(PresenceTests.Touch(1, TouchEventType.Down, 0), remote, now);
        now += System.Diagnostics.Stopwatch.Frequency / 250;
        receiver.ProcessDatagram(PresenceTests.Touch(1, TouchEventType.Up, 1, 100, 4_000_000), remote, now);
        long generation = motion.Schedule.Generation;
        Check(motion.Schedule.Deadline is not null, "custom Support300 tail armed");
        now += 2 * System.Diagnostics.Stopwatch.Frequency;
        receiver.CheckTimeouts(now);
        Check(!receiver.Presence.Connected, "custom run presence disconnected");
        motion.Tick(now, generation);
        Equal(0, moves.Count, "disconnect cancels custom tail without stale output");
        Check(motion.Schedule.Deadline is null, "disconnect clears custom run state");
    }

    private static bool MovesWithinEarnedEnvelope(this Harness harness)
    {
        int x = 0, y = 0;
        foreach (var move in harness.Moves)
        {
            x += move.X; y += move.Y;
            if (x is < -45 or > 90 || y is < -18 or > 36) return false;
        }
        return double.IsFinite(harness.Motion.Position.X) && double.IsFinite(harness.Motion.Position.Y) &&
            harness.Motion.BufferOverflows == 0;
    }

    private static void Support300Capacity()
    {
        var configuration = new MotionConfiguration(Production, 60, 300);
        using var h = new Harness(configuration);
        h.Send(TouchEventType.Down, 0, 0);
        for (int t = 1; t <= 1000; t++) h.Send(TouchEventType.Move, t, t / 10f);
        h.Send(TouchEventType.Up, 1001, 100);
        h.Advance(1400);
        Equal(900, h.Total.X, "Support300 endpoint");
        Equal(0L, h.Motion.BufferOverflows, "point history did not overflow");
        Check(h.Motion.KernelSegmentsIntegrated <= CausalFiniteCritical.Capacity, "kernel remains within fixed capacity");
        Check(h.Motion.Schedule.Deadline is null, "Support300 settles");
    }

    private static void TraceMetadata()
    {
        string directory = Path.Combine(Path.GetTempPath(), "rightpad-tau-trace-" + Guid.NewGuid().ToString("N"));
        try
        {
            var configuration = new MotionConfiguration(Production, 18, 90);
            using (var trace = new MotionTrace(directory, Production))
            {
                trace.BeginRuntimeRun(7, configuration);
                trace.Write(MotionEventKind.Tick, Origin);
            }
            using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "metadata.json")));
            Equal(18, metadata.RootElement.GetProperty("TauMs").GetInt32(), "trace Tau");
            Equal(90, metadata.RootElement.GetProperty("SupportMs").GetInt32(), "trace Support");
            Equal(1, metadata.RootElement.GetProperty("PeriodMs").GetInt32(), "trace 1000 Hz");
            Equal(12, metadata.RootElement.GetProperty("PlayoutDelayMs").GetInt32(), "trace 12 ms");
            Equal("Q0C", metadata.RootElement.GetProperty("Quantizer").GetString()!, "trace Q0-C");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static async Task RuntimeLifecycle()
    {
        var store = new RuntimeSettingsStore(RuntimeSettings.Default);
        var native = new VirtualHidTests.FakeNative();
        var runtime = new ReceiverRuntime(store, TextWriter.Null, MouseBackend.VirtualHid,
            new(IPAddress.Loopback, 0), () => new LibVirtualHidMouseOutput(native),
            motionMode: Production, useProductMotionSettings: true);
        try
        {
            await runtime.StartAsync();
            Equal((24, 120), (runtime.ActiveMotionConfiguration.FiniteCriticalTauMs,
                runtime.ActiveMotionConfiguration.FiniteCriticalSupportMs), "initial run baseline");
            store.Publish(RuntimeSettings.Default with { SmoothingTauMs = 18, SmoothingSupportMs = 90 });
            Equal((24, 120), (runtime.ActiveMotionConfiguration.FiniteCriticalTauMs,
                runtime.ActiveMotionConfiguration.FiniteCriticalSupportMs), "Save does not hot-switch active run");
            await runtime.StopAsync(); await runtime.StartAsync();
            Equal((18, 90), (runtime.ActiveMotionConfiguration.FiniteCriticalTauMs,
                runtime.ActiveMotionConfiguration.FiniteCriticalSupportMs), "next run applies committed values");
            store.Publish(RuntimeSettings.Default with { SmoothingTauMs = 60, SmoothingSupportMs = 300 });
            Equal((18, 90), (runtime.ActiveMotionConfiguration.FiniteCriticalTauMs,
                runtime.ActiveMotionConfiguration.FiniteCriticalSupportMs), "second active run remains frozen");
            await runtime.StopAsync(); await runtime.StartAsync();
            Equal((60, 300), (runtime.ActiveMotionConfiguration.FiniteCriticalTauMs,
                runtime.ActiveMotionConfiguration.FiniteCriticalSupportMs), "subsequent run applies maximum values");
        }
        finally { await runtime.StopAsync(); }
    }

    private static async Task DevOverride()
    {
        var settings = RuntimeSettings.Default with { SmoothingTauMs = 18, SmoothingSupportMs = 90 };
        var runtime = new ReceiverRuntime(new(settings), TextWriter.Null, MouseBackend.VirtualHid,
            new(IPAddress.Loopback, 0), () => new LibVirtualHidMouseOutput(new VirtualHidTests.FakeNative()),
            motionMode: Production, useProductMotionSettings: false);
        try
        {
            await runtime.StartAsync();
            Equal((24, 120), (runtime.ActiveMotionConfiguration.FiniteCriticalTauMs,
                runtime.ActiveMotionConfiguration.FiniteCriticalSupportMs), "explicit dev mode retains fixed K24-r5");
        }
        finally { await runtime.StopAsync(); }
    }
}
