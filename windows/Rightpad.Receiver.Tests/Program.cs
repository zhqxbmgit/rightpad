namespace Rightpad.Receiver.Tests;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--gui-android-smoke" }))
        {
            try { await ButtonSmokeTests.GuiAndroid(); return 0; }
            catch (Exception exception) { Console.WriteLine($"FAIL GUI Android smoke: {exception}"); return 1; }
        }
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
            ("UDP async LEFT UP failure stops idle receiver", GestureIntegrationTests.AsyncFailureStopsIdleReceiver),
            ("settings no file", Sync(SettingsTests.NoFile)),
            ("settings valid", Sync(SettingsTests.Valid)),
            ("settings malformed root", Sync(SettingsTests.Malformed)),
            ("settings missing fields", Sync(SettingsTests.Missing)),
            ("settings invalid fields", Sync(SettingsTests.Invalid)),
            ("settings product range", Sync(SettingsTests.Range)),
            ("settings unknown fields", Sync(SettingsTests.Unknown)),
            ("settings five-field round trip", SettingsTests.RoundTrip),
            ("settings 500ms debounce", SettingsTests.Debounce),
            ("settings quick updates / latest wins", SettingsTests.LatestWins),
            ("settings save failure retains runtime", SettingsTests.SaveFailure),
            ("settings exit flush", SettingsTests.ExitFlush),
            ("settings atomic full snapshot", SettingsTests.AtomicSnapshot),
            ("numeric draft / publish / precision", SettingsTests.NumericDraft),
            ("GUI default / explicit dev entries", Sync(SettingsTests.Entry)),
            ("runtime Start / Stop / release", RuntimeTests.StartStop),
            ("runtime repeated Start / Stop / fresh run", RuntimeTests.Repeated),
            ("runtime occupied port / retry", RuntimeTests.Occupied),
            ("runtime output error / restart", RuntimeTests.ErrorRestart),
            ("runtime counters / run isolation", RuntimeTests.CountersAndRuns),
            ("input without UI snapshot reads", RuntimeTests.NoUiReads),
            ("runtime old timer isolation", RuntimeTests.OldTimerIsolation),
            ("stats activity / actual elapsed rates", Sync(RuntimeTests.ActivityAndRates)),
            ("settings accepted packet boundary", RuntimeSettingsTests.PacketBoundary),
            ("settings residual / position retained", Sync(RuntimeSettingsTests.Residual)),
            ("settings Tap DOWN snapshot", Sync(RuntimeSettingsTests.TapDownSnapshot)),
            ("settings queued Click Hold durations", RuntimeSettingsTests.QueuedHold),
            ("v2 heartbeat bytes / high-bit run", Sync(PresenceTests.HeartbeatBytes)),
            ("v2 malformed heartbeat", Sync(PresenceTests.MalformedHeartbeat)),
            ("first heartbeat establishes run", Sync(PresenceTests.FirstHeartbeat)),
            ("first DOWN establishes run", Sync(PresenceTests.FirstDown)),
            ("unknown MOVE / UP ignored", Sync(PresenceTests.UnknownMoveUp)),
            ("new run sequence zero / motion reset", Sync(PresenceTests.RestartBaseline)),
            ("retired run never returns", Sync(PresenceTests.RetiredNeverReturns)),
            ("malformed run cannot switch", Sync(PresenceTests.MalformedCannotSwitch)),
            ("touch renews presence", Sync(PresenceTests.TouchRenews)),
            ("heartbeat idle / separate touch timeout", Sync(PresenceTests.HeartbeatIdleAndTouchTimeout)),
            ("exact presence timeout / recovery", Sync(PresenceTests.ExactTimeoutAndRecovery)),
            ("disconnect clears session / residual / gesture", Sync(PresenceTests.DisconnectResetsInput)),
            ("old / duplicate cannot renew presence", Sync(PresenceTests.OldDuplicateDoNotRenew)),
            ("heartbeat statistics independence", Sync(PresenceTests.Stats)),
            ("run cancels candidate", Sync(PresenceTests.RunCancelsCandidate)),
            ("connection UI / Last Seen", Sync(PresenceTests.Ui)),
            ("presence expires without UI / traffic", PresenceTests.IdleLoopExpiresWithoutUi),
            ("button cancel queue / release / stale callback", LeftButtonControllerTests.CancelAndRace),
            ("button cancel failure propagates", Sync(LeftButtonControllerTests.CancelFailure)),
            ("run/disconnect held and queued button cleanup", PresenceTests.ButtonCleanupIntegration),
            ("run cleanup LEFT UP failure becomes Runtime Error", PresenceTests.CleanupFailureIsRuntimeError),
            ("startup absent is Off", Sync(StartupTests.AbsentIsOff)),
            ("startup current executable is On", Sync(StartupTests.CurrentExecutableIsOn)),
            ("startup stale or wrong executable is Off", Sync(StartupTests.StaleOrWrongIsOff)),
            ("startup enable writes current command", Sync(StartupTests.EnableWritesCurrentCommand)),
            ("startup repeated enable is idempotent", Sync(StartupTests.EnableIsIdempotent)),
            ("startup enable replaces stale path", Sync(StartupTests.EnableReplacesStalePath)),
            ("startup disable deletes only rightpad value", Sync(StartupTests.DisableDeletesOnlyRightpadValue)),
            ("startup disable absent is safe", Sync(StartupTests.DisableAbsentIsSafe)),
            ("startup write failure leaves runtime running", StartupTests.WriteFailureDoesNotAffectRuntime),
            ("startup command quoting and Unicode path", Sync(StartupTests.CommandQuotingHandlesSpacesAndUnicode)),
            ("tray Closing hides without runtime/UDP cleanup", Sync(TrayApplicationBehaviorTests.ClosingHidesWithoutCleanup)),
            ("tray explicit Exit allows Closing", Sync(TrayApplicationBehaviorTests.ExplicitExitAllowsClosing)),
            ("tray Exit runtime/settings/resource cleanup", TrayApplicationBehaviorTests.ExitCleansUp),
            ("tray restore hidden window", Sync(TrayApplicationBehaviorTests.RestoreHidden)),
            ("tray restore minimized window", Sync(TrayApplicationBehaviorTests.RestoreMinimized))
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
