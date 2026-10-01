using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class HitchTraceTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> Cases =>
    [
        ("H1 bounded ring overwrites / 60 second retention / empty", () => Run(Retention)),
        ("H1 exact frozen boundary survives multiple concurrent overwrites", ConcurrentSnapshot),
        ("H1 zero per-record allocations / bounded capacity / measured cost", () => Run(Cost)),
        ("H1 accepted rejected sample and receive identity", () => Run(Admission)),
        ("H1 native success failure and zero-output isolation", () => Run(Native)),
        ("H1 clocks percentiles all gap thresholds and empty export", () => Run(Export)),
        ("H1 frozen 1000Hz Q0-C Earned-Settle replay equals uninstrumented", () => Run(MotionUnchanged)),
        ("H1 manual export failure leaves live Receiver and input running", RuntimeIsolation)
    ];

    private static Task Run(Action action) { action(); return Task.CompletedTask; }
    private static void Retention()
    {
        long now = 0;
        var r = new HitchTraceRecorder(4, () => now, 1000);
        Equal(0, r.Snapshot().Count, "empty safe");
        for (int i = 0; i < 10; i++) { now = i; r.Write(new(HitchKind.Tick, i, Dx: i)); }
        var s = r.Snapshot();
        Equal(4, s.Count, "bounded"); Equal(6L, s.FirstOrdinal, "old overwritten");
        Check(s.Records.Take(s.Count).Select(v => v.Dx).SequenceEqual(new[] { 6, 7, 8, 9 }), "oldest first");
        now = 60009; Equal(1, r.Snapshot().Count, "60s inclusive boundary");
        now++; Equal(0, r.Snapshot().Count, "older data never exported");
        r.Write(new(HitchKind.Tick, now)); Equal(1, r.Snapshot().Count, "recording continues");
    }

    private static async Task ConcurrentSnapshot()
    {
        var r = new HitchTraceRecorder(257);
        for (int i = 0; i < 257; i++) r.Write(new(HitchKind.Tick, i, Dx: i, Dy: -i));
        var c = r.BeginSnapshot();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
        {
            for (int i = 0; i < 20000; i++) r.Write(new(HitchKind.NativeSubmit, i, Dx: worker, Dy: i));
        })));
        var s = r.CompleteSnapshot(c);
        Equal(257, s.Count, "all pre-boundary records preserved while export paused");
        for (int i = 0; i < s.Count; i++)
        { Equal(i, s.Records[i].Dx, "exact frozen record"); Equal(-i, s.Records[i].Dy, "no torn fields"); }
        using var stop = new CancellationTokenSource();
        Task writer = Task.Run(() => { int n = 0; while (!stop.IsCancellationRequested) { r.Write(new(HitchKind.Tick, n, Dx: n, Dy: -n)); n++; } });
        try
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                var snapshot = r.Snapshot();
                foreach (var v in snapshot.Records.Take(snapshot.Count).Where(v => v.Kind == HitchKind.Tick))
                { Equal(v.At, (long)v.Dx, "concurrent exact fields"); Equal(-v.Dx, v.Dy, "concurrent coherent pair"); }
                Check(snapshot.Records.Take(snapshot.Count).All(v => v.RecordedAt <= snapshot.FrozenAt), "no post-freeze event");
            }
        }
        finally { stop.Cancel(); await writer; }
    }

    private static void Cost()
    {
        Check(!RuntimeHelpers.IsReferenceOrContainsReferences<HitchRecord>(), "fixed scalar struct");
        var r = new HitchTraceRecorder();
        var record = new HitchRecord(HitchKind.Tick, 123, QueueDepth: 4, Deadline: 122);
        for (int i = 0; i < 20000; i++) r.Write(record);
        long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
        for (int i = 0; i < 1000000; i++) r.Write(record);
        double ns = Stopwatch.GetElapsedTime(start).TotalNanoseconds / 1000000;
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - bytes, "no per-record GC allocation");
        Equal(HitchTraceRecorder.DefaultCapacity, r.Snapshot().Count, "capacity never grows");
        Console.WriteLine($"H1_COST recordBytes={Marshal.SizeOf<HitchRecord>()} ringBytes={Marshal.SizeOf<HitchRecord>() * (long)r.Capacity} writes=1000000 meanNs={ns:F1} allocatedBytes=0");
        long now = 0;
        var minute = new HitchTraceRecorder(clock: () => now, clockFrequency: 1000);
        for (int ms = 0; ms <= 60000; ms++)
        {
            now = ms;
            for (int i = 0; i < 4; i++) minute.Write(new(HitchKind.Tick, now));
        }
        Equal(240004, minute.Snapshot().Count, "full minute at 4000 records/sec fits");
    }

    private static void Admission()
    {
        var trace = new HitchTraceRecorder(100);
        using var r = new UdpReceiver(new(IPAddress.Loopback, 0), TextWriter.Null, detailedLogging: false, hitchTrace: trace, runtimeRun: 9);
        var remote = new IPEndPoint(IPAddress.Loopback, 12345);
        void Send(byte[] b) => r.ProcessDatagram(b, remote, Stopwatch.GetTimestamp());
        Send(PresenceTests.Touch(1, TouchEventType.Move, 0));
        Send(PresenceTests.Touch(1, TouchEventType.Down, 0, time: 100));
        Send(PresenceTests.Touch(1, TouchEventType.Move, 2, time: 200));
        Send(PresenceTests.Touch(1, TouchEventType.Move, 2));
        Send(PresenceTests.Touch(1, TouchEventType.Move, 1));
        Send(PresenceTests.Heartbeat(2)); Send(PresenceTests.Heartbeat(1)); Send([2, 1]);
        var s = trace.Snapshot();
        var receives = s.Records.Take(s.Count).Where(v => v.Kind == HitchKind.Receive).ToArray();
        Check(receives.Select(v => v.Status).SequenceEqual(new[] { HitchStatus.UnknownRun, HitchStatus.Accepted,
            HitchStatus.Accepted, HitchStatus.Duplicate, HitchStatus.Old, HitchStatus.Accepted, HitchStatus.RetiredRun, HitchStatus.Invalid }), "all admission outcomes");
        Equal(9L, receives[0].RuntimeRun, "runtime identity");
        Equal(2u, receives[2].Sequence, "wire sequence");
        var sample = s.Records.Take(s.Count).Single(v => v.Kind == HitchKind.Sample && v.AndroidNs == 200);
        Equal(1, sample.SampleCount, "wire count"); Equal(0, sample.SampleIndex, "wire sample index");
        Equal(2L, r.Statistics.AcceptedPackets, "admission unchanged");
    }

    private static void Native()
    {
        var trace = new HitchTraceRecorder(20);
        var native = new VirtualHidTests.FakeNative();
        using var mouse = new LibVirtualHidMouseOutput(native, hitchTrace: trace, runtimeRun: 3);
        mouse.Move(0, 0); mouse.Move(11, -9);
        native.FailMove = true;
        try { mouse.Move(2, 4); throw new InvalidOperationException("expected native failure"); }
        catch (IOException) { }
        var s = trace.Snapshot();
        Equal(2, s.Count, "zero move does not submit");
        Equal(HitchStatus.Success, s.Records[0].Status, "native success");
        Equal(HitchStatus.Failure, s.Records[1].Status, "native failure");
        Equal(11, s.Records[0].Dx, "dx unchanged"); Equal(-9, s.Records[0].Dy, "dy unchanged");
        Check(s.Records.Take(s.Count).All(v => v.End >= v.At && v.RuntimeRun == 3), "submit bounds and identity");
        uint inserted = 1;
        using var send = new WindowsMouseOutput((uint n, ref WindowsMouseOutput.NativeInput input, int size) => inserted,
            () => 5, hitchTrace: trace);
        send.Move(3, -4); inserted = 0;
        try { send.Move(3, -4); } catch (System.ComponentModel.Win32Exception e) { Equal(5, e.NativeErrorCode, "last error preserved"); }
        Equal(HitchStatus.Failure, trace.Snapshot().Records[3].Status, "SendInput failure");
    }

    private static string TestRoot() => Path.Combine(HitchTraceExport.DefaultRoot(), "tests-" + Guid.NewGuid().ToString("N"));
    private static void Export()
    {
        long now = 0;
        var r = new HitchTraceRecorder(100, () => now, 1000);
        ulong android = 9000000000000000000;
        uint seq = 0;
        foreach (int interval in new[] { 0, 5, 6, 11, 13, 21, 51 })
        {
            now += interval; android += (ulong)interval * 1000000;
            r.Packet(new(new(2, TouchEventType.Move, 1, 1, seq++, 1), [new(android, 0, 0)]), now, 1, HitchStatus.Accepted);
            r.Write(new(HitchKind.Tick, now, RuntimeRun: 1));
            r.Write(new(HitchKind.Output, now, RuntimeRun: 1));
            r.Write(new(HitchKind.NativeSubmit, now, RuntimeRun: 1));
        }
        var s = r.Snapshot();
        var intervals = HitchTraceExport.Intervals(s);
        Equal(30, intervals.Count, "six intervals times five independent series");
        Check(intervals.All(v => v.Milliseconds <= 51), "no mixed-clock latency");
        string summary = HitchTraceExport.Summary(s, intervals);
        Check(summary.Contains("p50=11 p95=51 p99=51 max=51"), "nearest-rank percentiles");
        foreach (string text in new[] { ">5ms: 5", ">10ms: 4", ">12ms: 3", ">20ms: 2", ">50ms: 1" }) Check(summary.Contains(text), text);
        var result = HitchTraceExport.Save(s, TestRoot());
        Check(result.Error is null, "export succeeds");
        Equal(26, File.ReadAllLines(Path.Combine(result.Directory!, "gaps.csv")).Length, "every >5ms interval listed");
        Equal(36, File.ReadAllLines(Path.Combine(result.Directory!, "trace.csv")).Length, "all structured rows");
        var empty = HitchTraceExport.Save(new HitchTraceRecorder(2).Snapshot(), TestRoot());
        Check(empty.Error is null && File.ReadAllText(Path.Combine(empty.Directory!, "summary.txt")).Contains("p50=N/A"), "empty export safe");
    }

    private static void MotionUnchanged()
    {
        long now = 100000;
        var trace = new HitchTraceRecorder(4096);
        List<(long, int, int)> a = [], b = [];
        const MotionMode mode = MotionMode.RESAMPLED_1000HZ_FINITE_CRITICAL_K24_R5_SETTLE;
        using var control = new ResampledMotion((x, y) => a.Add((now, x, y)), 9, 9, () => now, 1000000, finiteCriticalMode: mode);
        using var observed = new ResampledMotion((x, y) => b.Add((now, x, y)), 9, 9, () => now, 1000000, finiteCriticalMode: mode, hitchTrace: trace);
        uint sequence = 0;
        for (int ms = 0; ms < 500; ms++)
        {
            now = 100000 + ms * 1000;
            foreach (var m in new[] { control, observed })
                if (m.Schedule.Deadline is long deadline && deadline <= now) m.Tick(now, m.Schedule.Generation);
            if (ms <= 100 && ms % 4 == 0)
            {
                var p = new TouchPacket(new(2, ms == 0 ? TouchEventType.Down : ms == 100 ? TouchEventType.Up : TouchEventType.Move,
                    1, 1, sequence++, 1), [new((ulong)ms * 1000000, (float)(Math.Sin(ms / 13.0) * 10), ms)]);
                control.Process(p); observed.Process(p);
            }
            if (ms % 7 == 0) _ = trace.Snapshot();
            Equal(control.Schedule, observed.Schedule, "freeze keeps deadlines/generation");
            Equal(control.Position, observed.Position, "freeze keeps filtered position");
            Equal(control.Pending, observed.Pending, "freeze keeps earned target");
        }
        Check(a.SequenceEqual(b) && a.Count > 0, "exact timestamp and integer output replay");
        Equal((long?)null, observed.Schedule.Deadline, "settle still parks");
        var s = trace.Snapshot();
        Check(s.Records.Take(s.Count).Any(v => v.Kind == HitchKind.Output && v.Dx == 0 && v.Dy == 0), "zero output opportunities recorded");
    }

    private static async Task RuntimeIsolation()
    {
        var native = new VirtualHidTests.FakeNative();
        var settings = new RuntimeSettingsStore(RuntimeSettings.Default);
        var runtime = new ReceiverRuntime(settings, TextWriter.Null, MouseBackend.VirtualHid,
            endpoint: new(IPAddress.Loopback, 0), discoveryEndpoint: new(IPAddress.Loopback, 0),
            identityFactory: () => new byte[16], mouseFactory: () => new LibVirtualHidMouseOutput(native));
        await runtime.StartAsync();
        using var sender = new UdpClient();
        async Task Send(byte[] bytes) { await sender.SendAsync(bytes, runtime.LocalEndpoint!); }
        async Task WaitAccepted(long count)
        {
            var timeout = Stopwatch.StartNew();
            while (runtime.CaptureSnapshot().AcceptedPackets < count && timeout.ElapsedMilliseconds < 3000) await Task.Delay(5);
            Equal(count, runtime.CaptureSnapshot().AcceptedPackets, "live UDP accepted");
        }
        string root = TestRoot();
        try
        {
            await Send(PresenceTests.Touch(7, TouchEventType.Down, 0));
            await Send(PresenceTests.Touch(7, TouchEventType.Move, 1, 1));
            await WaitAccepted(2);
            Check(!Directory.Exists(root), "capture does not create export directories or per-tick files");
            var before = runtime.CaptureSnapshot(); var endpoint = runtime.LocalEndpoint; var committed = settings.Current;
            var ok = await runtime.HitchTrace.FreezeAsync(root);
            Check(ok.Error is null, "manual freeze succeeds");
            string blocker = Path.Combine(root, "not-a-directory"); File.WriteAllText(blocker, "block");
            var failed = await runtime.HitchTrace.FreezeAsync(blocker);
            Check(failed.Error is not null, "export failure contained");
            await Send(PresenceTests.Touch(7, TouchEventType.Move, 2, 3)); await WaitAccepted(3);
            var after = runtime.CaptureSnapshot();
            Equal(before.RunId, after.RunId, "same Runtime"); Equal(endpoint, runtime.LocalEndpoint, "same socket");
            Equal(ReceiverState.Running, after.RuntimeState, "running after failed export");
            Equal(committed, settings.Current, "settings unchanged");
            Check(after.MouseOutputSuccesses > before.MouseOutputSuccesses, "input continues after freeze/failure");
            Equal(1, Directory.GetFiles(root, "trace.csv", SearchOption.AllDirectories).Length, "only manual successful export");
        }
        finally { await runtime.StopAsync(); }
    }
}
