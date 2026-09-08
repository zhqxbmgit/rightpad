namespace Rightpad.Receiver.Tests;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--sendinput-button-smoke" }) || args.SequenceEqual(new[] { "--android-tap-smoke" }))
        {
            try { await ButtonSmokeTests.Run(args[0] == "--android-tap-smoke"); return 0; }
            catch (Exception exception) { Console.WriteLine($"FAIL button smoke: {exception}"); return 1; }
        }
        if (args.SequenceEqual(new[] { "--sendinput-smoke" }))
        {
            try { WindowsMouseOutputTests.Smoke(); return 0; }
            catch (Exception exception) { Console.WriteLine($"FAIL SendInput smoke: {exception}"); return 1; }
        }
        if (args.SequenceEqual(new[] { "--android-mouse-smoke" }))
        {
            try { await WindowsMouseOutputTests.AndroidSmoke(); return 0; }
            catch (Exception exception) { Console.WriteLine($"FAIL Android mouse smoke: {exception}"); return 1; }
        }
        if (args.Length != 0) { Console.Error.WriteLine("Usage: Rightpad.Receiver.Tests [--sendinput-smoke | --android-mouse-smoke | --sendinput-button-smoke | --android-tap-smoke]"); return 1; }
        (string Name, Func<Task> Run)[] tests =
        [
            ("fixed bytes / endian / uint64 nanoseconds", Sync(PacketDecoderTests.FixedBytes)),
            ("DOWN, MOVE, UP decoding", Sync(PacketDecoderTests.EventTypes)),
            ("preserve order, equal timestamps, signed zero", Sync(PacketDecoderTests.PreserveSamples)),
            ("all truncated lengths rejected", Sync(PacketDecoderTests.TruncatedPackets)),
            ("version / event / count / trailing data rejection", Sync(PacketDecoderTests.MalformedFields)),
            ("NaN / infinity rejected, no partial packet", Sync(PacketDecoderTests.NonFiniteCoordinates)),
            ("sequence baseline / continuous / session change", Sync(PacketStatisticsTests.Continuous)),
            ("sequence gaps / duplicate / old", Sync(PacketStatisticsTests.GapsAndOldPackets)),
            ("uint32 ordinary ordering, no wraparound", Sync(PacketStatisticsTests.NoWraparound)),
            ("invalid packets preserve baseline", Sync(PacketStatisticsTests.InvalidPackets)),
            ("localhost UDP / malformed recovery / multiple source ports", UdpReceiverTests.Loopback),
            ("idle receive cancellation / shutdown / port release", UdpReceiverTests.CancelIdle),
            ("input timeout / resume / no sequence reset", UdpReceiverTests.TimeoutAndResume),
            ("occupied port fails explicitly", Sync(UdpReceiverTests.OccupiedPort)),
            ("session DOWN / MOVE / final UP", Sync(TouchSessionProcessorTests.DeltasAndUp)),
            ("session multi-sample order / timestamps ignored", Sync(TouchSessionProcessorTests.SampleOrder)),
            ("session invalid IDs do not mutate motion", Sync(TouchSessionProcessorTests.SessionIsolation)),
            ("session DOWN / UP / explicit reset isolation", Sync(TouchSessionProcessorTests.SessionReset)),
            ("session output / range failure cleanup", Sync(TouchSessionProcessorTests.FailureCleanup)),
            ("accumulator positive / negative symmetry", Sync(RawMotionProcessorTests.PositiveNegative)),
            ("accumulator alternating conservation", Sync(RawMotionProcessorTests.Alternating)),
            ("accumulator independent axes / sensitivity / reset", Sync(RawMotionProcessorTests.ScalingAndReset)),
            ("accumulator long-distance error bound", Sync(RawMotionProcessorTests.LongDistance)),
            ("accumulator invalid values / int32 overflow", Sync(RawMotionProcessorTests.InvalidNumbers)),
            ("SendInput native layout / fields / zero suppression", Sync(WindowsMouseOutputTests.LayoutAndFields)),
            ("SendInput failure / last error / no retry", Sync(WindowsMouseOutputTests.FailedCall)),
            ("Receiver minimal startup arguments", Sync(WindowsMouseOutputTests.Arguments)),
            ("UDP accepted-only motion / session isolation", UdpReceiverTests.MotionGate),
            ("UDP timeout retains session / position / residual", UdpReceiverTests.MotionSurvivesTimeout),
            ("UDP quiet logging / final statistics", UdpReceiverTests.QuietLogging),
            ("UDP output failure stops and cleans up", UdpReceiverTests.MotionFailure),
            ("tap stationary DOWN/UP", Sync(GestureProcessorTests.Stationary)),
            ("tap small movement / axis-inclusive boundary", Sync(GestureProcessorTests.SmallMovement)),
            ("tap X exceeded", Sync(GestureProcessorTests.XExceeded)),
            ("tap Y exceeded", Sync(GestureProcessorTests.YExceeded)),
            ("tap return to origin remains rejected", Sync(GestureProcessorTests.ReturnToOrigin)),
            ("tap duration / nanosecond boundary / backward time", Sync(GestureProcessorTests.Duration)),
            ("tap UP exceeded", Sync(GestureProcessorTests.UpExceeded)),
            ("tap wrong-session MOVE", Sync(GestureProcessorTests.WrongMove)),
            ("tap wrong-session UP", Sync(GestureProcessorTests.WrongUp)),
            ("tap new DOWN / explicit cleanup", Sync(GestureProcessorTests.NewDown)),
            ("tap middle historical sample exceeded", Sync(GestureProcessorTests.MiddleSample)),
            ("tap CLI defaults / overrides / invalid values", Sync(GestureProcessorTests.Arguments)),
            ("button native fields / return values / errors", Sync(LeftButtonControllerTests.NativeFields)),
            ("button asynchronous hold timing", LeftButtonControllerTests.Timing),
            ("button Dispose / stale timer cleanup", LeftButtonControllerTests.Cleanup),
            ("button overlapping clicks", LeftButtonControllerTests.Overlap),
            ("button failures / best-effort release", LeftButtonControllerTests.Failures),
            ("UDP gesture gate / exact motion independence", GestureIntegrationTests.GateAndMotionIndependence),
            ("UDP nonblocking click hold / timeout retention", GestureIntegrationTests.NonblockingAndTimeout),
            ("UDP normal / exceptional shutdown button cleanup", GestureIntegrationTests.ShutdownCleanup),
            ("UDP async LEFT UP failure stops idle receiver", GestureIntegrationTests.AsyncFailureStopsIdleReceiver)
        ];

        int failed = 0;
        foreach (var test in tests)
        {
            try
            {
                await test.Run();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception exception)
            {
                failed++;
                Console.WriteLine($"FAIL {test.Name}: {exception}");
            }
        }
        Console.WriteLine($"RESULT total={tests.Length} passed={tests.Length - failed} failed={failed}");
        return failed == 0 ? 0 : 1;
    }

    private static Func<Task> Sync(Action test) => () => { test(); return Task.CompletedTask; };

    public static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        Check(EqualityComparer<T>.Default.Equals(expected, actual),
            $"{message}: expected={expected}, actual={actual}");
    }
}
