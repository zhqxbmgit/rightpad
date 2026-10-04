using System.Net;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class NormalGuiStartupTests
{
    public static readonly (string Name, Func<Task> Run)[] Cases =
    [
        ("normal GUI bare entry selects M-R1", () => Sync(Bare)),
        ("normal GUI explicit M-R1 remains selected", () => Sync(ExplicitMr1)),
        ("normal GUI explicit historical M-F1 retains fixed configuration", () => Sync(Historical)),
        ("normal GUI runtime keeps direct mode with product settings flag", RuntimeConfiguration),
        ("normal GUI diagnostics M-R1 M8 Q0-C Tau/Support N/A", Diagnostics),
        ("normal GUI M to C-Z1 to M-R1 profile selection", () => Sync(Profiles)),
        ("normal GUI Saved Tau/Support do not shape direct reconstruction", () => Sync(SavedIndependence))
    ];

    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static Receiver.Program.LaunchOptions BareLaunch() => Receiver.Program.ParseLaunchArguments([]);

    private static ReceiverRuntime Runtime(RuntimeSettings settings, TextWriter? log = null)
    {
        var launch = BareLaunch();
        return new(new RuntimeSettingsStore(settings), log ?? TextWriter.Null, launch.Backend,
            endpoint: new(IPAddress.Loopback, 0),
            mouseFactory: () => new LibVirtualHidMouseOutput(new VirtualHidTests.FakeNative()),
            discoveryEndpoint: new(IPAddress.Loopback, 0),
            motionMode: Receiver.Program.ResolveGuiMotionMode(launch),
            useProductMotionSettings: !launch.MotionExplicit);
    }

    private static MotionConfiguration Configuration(RuntimeSettings settings)
    {
        var runtime = Runtime(settings);
        try { return runtime.ActiveMotionConfiguration; }
        finally { runtime.StopAsync().GetAwaiter().GetResult(); }
    }

    private static void Bare()
    {
        var launch = BareLaunch();
        Equal(Receiver.Program.LaunchMode.Gui, launch.Mode, "no-argument entry is GUI");
        Check(!launch.MotionExplicit, "bare entry carries no development override");
        Equal(MotionMode.M_R1, Receiver.Program.ResolveGuiMotionMode(launch), "normal GUI default");
    }

    private static void ExplicitMr1()
    {
        var launch = Receiver.Program.ParseLaunchArguments(["--dev-motion-mode", "M_R1"]);
        Check(launch.MotionExplicit, "explicit identity retained");
        Equal(MotionMode.M_R1, Receiver.Program.ResolveGuiMotionMode(launch), "explicit M-R1");
    }

    private static void Historical()
    {
        const MotionMode historical = MotionMode.RESAMPLED_1000HZ_FINITE_CRITICAL_K24_R5_SETTLE;
        var launch = Receiver.Program.ParseLaunchArguments(["--dev-motion-mode", historical.ToString()]);
        Equal(historical, Receiver.Program.ResolveGuiMotionMode(launch), "explicit M-F1 identity");
        Equal(historical, MotionModes.ProductionMode, "historical identity constant unchanged");
        var fixedHistorical = MotionModes.FixedConfiguration(historical);
        Check(fixedHistorical.IsFiniteCritical && fixedHistorical.IsEarnedSettle, "M-F1 filter/release retained");
        Equal((24, 120), (fixedHistorical.FiniteCriticalTauMs, fixedHistorical.FiniteCriticalSupportMs), "historical fixed kernel");
        var direct = MotionModes.FixedConfiguration(MotionMode.M_R1);
        Equal((24, 120), (direct.FiniteCriticalTauMs, direct.FiniteCriticalSupportMs), "M-R1 fixed history safety horizon unchanged");
    }

    private static async Task RuntimeConfiguration()
    {
        var settings = RuntimeSettings.Default with { SmoothingTauMs = 13, SmoothingSupportMs = 85 };
        var runtime = Runtime(settings);
        await runtime.StartAsync();
        try
        {
            var config = runtime.CaptureActiveRuntimeSnapshot()!.Motion;
            Equal(MotionMode.M_R1, config.Mode, "bare GUI runtime mode");
            Check(config.IsDirectReconstruction && !config.IsFiniteCritical && !config.IsEarnedSettle, "direct reconstruction, no position filter or Earned-Settle");
            Equal((13, 85), (config.FiniteCriticalTauMs, config.FiniteCriticalSupportMs), "saved history horizon carried without shaping");
            Equal(ReceiverState.Running, runtime.CaptureSnapshot().RuntimeState, "fake-backed runtime running");
        }
        finally { await runtime.StopAsync(); }
    }

    private static async Task Diagnostics()
    {
        var log = new StringWriter();
        var runtime = Runtime(RuntimeSettings.Default, log);
        await runtime.StartAsync();
        try
        {
            var stats = runtime.CaptureSnapshot();
            var vm = new RuntimeStatsViewModel();
            vm.Refresh(stats, 0);
            Equal(MotionModes.Mr1Algorithm, vm.MotionAlgorithm, "direct M-R1 diagnostics");
            Check(vm.MotionAlgorithm.Contains("8 ms") && vm.MotionAlgorithm.Contains("Q0-C") &&
                vm.MotionAlgorithm.Contains("1 ms / 1000 Hz") && vm.MotionAlgorithm.Contains("Reconstruction-completion / No glide"), "complete normal contract");
            Equal("N/A", vm.ActiveTau, "Tau not a position-shaping parameter");
            Equal("N/A", vm.ActiveSupport, "Support not a position-shaping parameter");
            Check(log.ToString().Contains("motion_mode: name=M_R1 ") && log.ToString().Contains("playoutDelayMs=8"), "actual normal GUI selected startup identity");
        }
        finally { await runtime.StopAsync(); }
    }

    private static void Profiles()
    {
        var config = Configuration(RuntimeSettings.Default);
        using var motion = new ResampledMotion((_, _) => { }, monotonicNow: () => 100_000,
            clockFrequency: 1_000_000, finiteCriticalMode: config.Mode, configuration: config);
        Equal(MotionProfile.Normal, motion.ActiveMotionProfile, "starts M");
        motion.RequestProfile(MotionProfile.Cinematic);
        Equal(MotionProfile.Cinematic, motion.ActiveMotionProfile, "M to C");
        Equal(12, motion.ReconstructionDelayMs, "C12 retained");
        Equal(4, motion.PeriodMs, "C fixed step retained");
        Check(motion.ActiveAlgorithm.Algorithm.StartsWith("C-Z1") && motion.ActiveAlgorithm.Algorithm.Contains("Amax 80000") &&
            motion.ActiveAlgorithm.Algorithm.Contains("Vmax 15000") && motion.ActiveAlgorithm.Algorithm.Contains("true glide"), "C-Z1 identity unchanged");
        Equal(35, motion.ActiveAlgorithm.TauMs, "C tau retained");
        motion.RequestProfile(MotionProfile.Normal);
        Equal(MotionModes.Mr1Algorithm, motion.ActiveAlgorithm.Algorithm, "C returns to direct M-R1");
        Equal(8, motion.ReconstructionDelayMs, "M8 retained");
        Equal(1, motion.PeriodMs, "M 1ms opportunities");
    }

    private static void SavedIndependence()
    {
        foreach (var (tau, support) in new[] { (8, 40), (13, 85), (24, 120), (60, 300) })
        {
            var config = Configuration(RuntimeSettings.Default with { SmoothingTauMs = tau, SmoothingSupportMs = support });
            long now = 100_000;
            var native = new List<(long At, int X, int Y)>();
            using var motion = new ResampledMotion((x, y) => native.Add((now, x, y)), sensitivityX: 1, sensitivityY: 1,
                monotonicNow: () => now, clockFrequency: 1_000_000, finiteCriticalMode: config.Mode, configuration: config);
            motion.Process(new(new(2, TouchEventType.Down, 1, 1, 0, 1), [new(0, 0, 0)]));
            now = 104_000;
            motion.Process(new(new(2, TouchEventType.Move, 1, 1, 1, 1), [new(4_000_000, 40, -20)]));
            now = 108_000;
            motion.Process(new(new(2, TouchEventType.Up, 1, 1, 2, 1), [new(8_000_000, 40, -20)]));
            for (int t = 8; t <= 16; t++)
            {
                now = 100_000 + t * 1000;
                motion.Tick(now, motion.Schedule.Generation);
                if (t < 16) Equal((Math.Min(40d, (t - 8) * 10d), Math.Max(-20d, -(t - 8) * 5d)), motion.Position, "independent direct interpolation");
            }
            Check(native.SequenceEqual(new[] { (109_000L, 10, -5), (110_000L, 10, -5), (111_000L, 10, -5), (112_000L, 10, -5) }), $"same native replay for Saved {tau}/{support}");
            Equal((40L, -20L), (motion.TotalDx, motion.TotalDy), "same earned endpoint");
            Check(motion.Schedule.Deadline is null, "parks at final reconstruction boundary");
            Equal(0, motion.KernelSegmentsIntegrated, "no Finite-Critical integration");
        }
    }
}
