using System.IO;
using System.Net;

namespace Rightpad.Receiver;

internal sealed record ActiveRuntimeSnapshot(RuntimeSettings Settings, MotionConfiguration Motion);
internal sealed record ReceiverRestartResult(bool Succeeded, bool Restored, string? Error);

internal sealed class ReceiverRuntime(RuntimeSettingsStore settings, TextWriter output, MouseBackend backend,
    IPEndPoint? endpoint = null, Func<IMouseOutput>? mouseFactory = null, bool rawMouse = true,
    FlightRecorder? flightRecorder = null,
    IPEndPoint? discoveryEndpoint = null, Func<byte[]>? identityFactory = null,
    MotionMode motionMode = MotionMode.RAW, MotionTrace? motionTrace = null,
    bool useProductMotionSettings = false)
{
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private Run? current;
    private long nextRunId;
    private int restarting;
    public bool IsRestarting => Volatile.Read(ref restarting) != 0;
    public SensitivitySnapshot ActiveSensitivity => settings.Sensitivity.Current;
    public ActiveRuntimeSnapshot? CaptureActiveRuntimeSnapshot()
    {
        var run = Volatile.Read(ref current);
        if (run is null || (ReceiverState)Volatile.Read(ref run.State) != ReceiverState.Running) return null;
        var sensitivity = settings.Sensitivity.Current;
        return new(settings.Current with
        {
            SensitivityX = sensitivity.X, SensitivityY = sensitivity.Y,
            SmoothingTauMs = run.MotionConfiguration.FiniteCriticalTauMs,
            SmoothingSupportMs = run.MotionConfiguration.FiniteCriticalSupportMs
        }, run.MotionConfiguration);
    }
    public MotionMode ActiveMotionMode => motionMode;
    public MotionConfiguration ActiveMotionConfiguration => Volatile.Read(ref current)?.MotionConfiguration ??
        ResolveMotionConfiguration(settings.Current);
    public bool MotionTraceEnabled => motionTrace is not null;
    public IPEndPoint? LocalEndpoint => Volatile.Read(ref current)?.Receiver?.LocalEndpoint;
    public Task Completion => Volatile.Read(ref current)?.Task ?? Task.CompletedTask;
    public IPEndPoint? DiscoveryEndpoint => Volatile.Read(ref current)?.Discovery?.LocalEndpoint;

    private sealed class Run(long id, RuntimeSettings initialSettings, MotionConfiguration motionConfiguration)
    {
        public readonly long Id = id;
        public readonly RuntimeSettings InitialSettings = initialSettings;
        public readonly MotionConfiguration MotionConfiguration = motionConfiguration;
        public MotionMode MotionMode => MotionConfiguration.Mode;
        public readonly CancellationTokenSource Cancellation = new();
        public readonly TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Task = Task.CompletedTask;
        public UdpReceiver? Receiver;
        public DiscoveryResponder? Discovery;
        public IMouseOutput? Mouse;
        public ITouchMotion? Motion;
        public int State = (int)ReceiverState.Starting;
        public string? Error;
        public SensitivitySnapshot? ReportedSensitivity;
    }

    public async Task StartAsync()
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try { await StartLockedAsync().ConfigureAwait(false); }
        finally { lifecycle.Release(); }
    }

    private MotionConfiguration ResolveMotionConfiguration(RuntimeSettings snapshot) =>
        useProductMotionSettings && motionMode == MotionModes.ProductionMode
            ? MotionConfiguration.Product(snapshot)
            : MotionModes.FixedConfiguration(motionMode);

    public async Task StopAsync()
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try { await StopLockedAsync().ConfigureAwait(false); }
        finally { lifecycle.Release(); }
    }

    public async Task<ReceiverRestartResult> RestartAsync()
    {
        if (Interlocked.CompareExchange(ref restarting, 1, 0) != 0)
            return new(false, false, "Receiver restart is already in progress.");
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            var previous = CaptureActiveRuntimeSnapshot();
            if (previous is null) return new(false, false, "Receiver is not running. Use Start Receiver.");
            // Capture both target and recovery before stopping. Neither path publishes settings.
            var target = settings.Current;
            var errors = new List<string>();
            await StopLockedAsync().ConfigureAwait(false);
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                bool recovery = attempt == 3;
                try
                {
                    await StartLockedAsync(recovery ? previous.Settings : target,
                        recovery ? previous.Motion : ResolveMotionConfiguration(target)).ConfigureAwait(false);
                    if (CaptureSnapshot().RuntimeState == ReceiverState.Running)
                    {
                        if (!recovery) return new(true, false, null);
                        return new(false, true,
                            "Restart failed. Previous receiver configuration was restored. " + string.Join(" | ", errors));
                    }
                    errors.Add($"{(recovery ? "Recovery" : $"Target attempt {attempt}")}: {CaptureSnapshot().LastError}");
                }
                catch (Exception e) { errors.Add($"Attempt {attempt}: {e}"); }
                // Join failed initialization and release its socket/device before the next attempt.
                await StopLockedAsync().ConfigureAwait(false);
            }
            return new(false, false, "Receiver restart failed and automatic recovery failed. " + string.Join(" | ", errors));
        }
        catch (Exception e)
        {
            return new(false, false, "Receiver restart failed and automatic recovery failed. " + e);
        }
        finally { lifecycle.Release(); Volatile.Write(ref restarting, 0); }
    }

    private async Task StartLockedAsync(RuntimeSettings? snapshot = null, MotionConfiguration? configuration = null)
    {
        var previous = current;
        if (previous is not null)
        {
            if (!previous.Task.IsCompleted) return;
            await previous.Task.ConfigureAwait(false);
            previous.Cancellation.Dispose();
        }
        RuntimeSettings initial = snapshot ?? settings.Current;
        var run = new Run(++nextRunId, initial, configuration ?? ResolveMotionConfiguration(initial));
        Volatile.Write(ref current, run);
        run.Task = Task.Run(() => RunAsync(run));
        await run.Started.Task.ConfigureAwait(false);
        if ((ReceiverState)Volatile.Read(ref run.State) == ReceiverState.Error)
            await run.Task.ConfigureAwait(false);
    }

    private async Task StopLockedAsync()
    {
        var run = current;
        if (run is null) return;
        if (!run.Task.IsCompleted)
        {
            Volatile.Write(ref run.State, (int)ReceiverState.Stopping);
            run.Cancellation.Cancel();
            await run.Task.ConfigureAwait(false);
        }
        if (run.Error is null) Volatile.Write(ref run.State, (int)ReceiverState.Stopped);
    }

    private async Task RunAsync(Run run)
    {
        MotionMode motionMode = run.MotionMode;
        MotionConfiguration motionConfiguration = run.MotionConfiguration;
        IMouseOutput? mouse = null;
        LeftButtonController? buttons = null;
        ITouchMotion? motion = null;
        MotionClock? motionClock = null;
        CancellationTokenRegistration motionCancellation = default;
        GestureProcessor? gesture = null;
        HapticFeedbackSender? haptics = null;
        UdpReceiver? receiver = null;
        try
        {
            motionTrace?.BeginRuntimeRun(run.Id, motionConfiguration);
            var initial = run.InitialSettings;
            flightRecorder?.Event("runtime_start", ("runtimeRunId", run.Id));
            mouse = rawMouse ? (mouseFactory is not null ? mouseFactory() : MouseOutputFactory.Create(backend, flightRecorder, motionTrace)) : null;
            if (rawMouse && mouse is null) throw new InvalidOperationException("Mouse output factory returned no backend.");
            output.WriteLine($"mouse_backend: name={mouse?.BackendName ?? "None (diagnostics)"} device={mouse?.DeviceIdentity ?? "none"}");
            flightRecorder?.Event("mouse_backend", ("runtimeRunId", run.Id), ("mouseBackend", mouse?.BackendName), ("deviceIdentity", mouse?.DeviceIdentity));
            if (mouse is not null)
            {
                Action<int, int> nativeMove = mouse.Move;
                Action<int, int> move = motionTrace is null ? nativeMove : (x, y) => motionTrace.Move(nativeMove, x, y);
                motion = motionMode switch
                {
                    MotionMode.RAW => new TouchSessionProcessor(move, initial.SensitivityX, initial.SensitivityY),
                    MotionMode.RESAMPLED_250HZ or MotionMode.RESAMPLED_250HZ_BOXCAR_4MS or MotionMode.RESAMPLED_250HZ_BOXCAR_8MS or
                    MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5 or MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K35_R4 or
                    MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5_SETTLE or
                    MotionMode.RESAMPLED_500HZ_FINITE_CRITICAL_K24_R5_SETTLE or
                    MotionMode.RESAMPLED_1000HZ_FINITE_CRITICAL_K24_R5_SETTLE =>
                        new ResampledMotion(move, initial.SensitivityX, initial.SensitivityY, trace: motionTrace,
                            boxcarWindowMs: MotionModes.BoxcarWindowMs(motionMode),
                            finiteCriticalMode: MotionModes.IsFiniteCritical(motionMode) ? motionMode : MotionMode.RESAMPLED_250HZ,
                            configuration: MotionModes.IsFiniteCritical(motionMode) ? motionConfiguration : null,
                            liveSensitivity: settings.Sensitivity),
                    _ => throw new ArgumentOutOfRangeException(nameof(motionMode))
                };
                if (motion is ResampledMotion resampled)
                {
                    motionCancellation = run.Cancellation.Token.Register(resampled.Reset);
                    motionClock = new(resampled, run.Cancellation.Cancel);
                }
            }
            output.WriteLine($"motion_mode: name={motionMode} quantizer={MotionModes.QuantizerName(motionMode)} periodMs={MotionModes.PeriodMs(motionMode)} playoutDelayMs={(motionMode == MotionMode.RAW ? 0 : 12)} boxcarWindowMs={MotionModes.BoxcarWindowMs(motionMode)} trace={(motionTrace is null ? "off" : "on")}");
            flightRecorder?.Event("motion_configuration", ("runtimeRunId", run.Id), ("mode", motionMode.ToString()), ("quantizer", MotionModes.QuantizerName(motionMode)));
            if (MotionModes.IsFiniteCritical(motionMode))
            {
                output.WriteLine(FormattableString.Invariant($"finite_critical: tauMs={motionConfiguration.FiniteCriticalTauMs} supportMs={motionConfiguration.FiniteCriticalSupportMs} normalization={motionConfiguration.KernelNormalization:R} quantizer=Q0C sensitivity=live-committed-per-sample"));
                output.WriteLine($"motion_lifecycle: up={(MotionModes.IsEarnedSettle(motionMode) ? "earned_settle contacts=continuous_until_settled" : "instant_flush contacts=independent")}");
            }
            Volatile.Write(ref run.Mouse, mouse);
            Volatile.Write(ref run.Motion, motion);
            buttons = mouse is null ? null : new(mouse.LeftDown, mouse.LeftUp, output.WriteLine,
                run.Cancellation.Cancel, initial.ClickHoldMs);
            // Hold belongs to the request, which can occur after the packet's motion output.
            haptics = buttons is null ? null : new(output);
            gesture = buttons is null ? null : new((_, packet) =>
                {
                    // Capture the accepted packet's route now, not the presence at timer execution.
                    var address = IPAddress.Parse(receiver!.Presence.RemoteIp!);
                    var h = packet.Header;
                    var feedback = new ClickFeedback(h.SenderRunId, h.SessionId, h.Sequence);
                    buttons.Click(settings.Current.ClickHoldMs, () => haptics!.TryEnqueue(address, feedback));
                }, initial,
                () => { buttons.BeginDrag(); output.WriteLine("gesture: drag_start"); },
                () => { buttons.EndDrag(); output.WriteLine("gesture: drag_end"); });
            output.WriteLine(FormattableString.Invariant($"startup: runId={run.Id} mode={(rawMouse ? "raw_mouse" : "diagnostic")} sensitivityX={initial.SensitivityX} sensitivityY={initial.SensitivityY} timeoutAction=diagnostic_only"));
            if (gesture is not null)
                output.WriteLine(FormattableString.Invariant($"gesture: singleTap=enabled doubleTapDrag=enabled tapMaxDurationMs={initial.TapMaxDurationMs} tapMovementThresholdPx={initial.TapMovementThresholdPx} clickHoldMs={initial.ClickHoldMs} doubleTapIntervalMs={initial.DoubleTapIntervalMs}"));
            receiver = new UdpReceiver(endpoint ?? new(IPAddress.Any, UdpReceiver.Port), output,
                motion: motion, detailedLogging: !rawMouse, gesture: gesture, settings: settings,
                cancelButtons: buttons is null ? null : buttons.CancelPendingAndRelease, flightRecorder: flightRecorder, motionTrace: motionTrace);
            Volatile.Write(ref run.Receiver, receiver);
            // Explicit touch endpoints are test-only; opt into discovery there with its own endpoint.
            if (endpoint is null || discoveryEndpoint is not null)
            {
                byte[] id = identityFactory?.Invoke() ?? ReceiverIdentityStore.Load(ReceiverIdentityStore.DefaultPath, message =>
                {
                    output.WriteLine(message);
                    flightRecorder?.Event(message);
                });
                run.Discovery = new(discoveryEndpoint ?? new(IPAddress.Any, DiscoveryCodec.Port), id, output, flightRecorder);
            }
            Volatile.Write(ref run.State, (int)ReceiverState.Running);
            run.Started.TrySetResult(true);
            var touchTask = receiver.RunAsync(run.Cancellation.Token);
            if (run.Discovery is null) await touchTask.ConfigureAwait(false);
            else
            {
                var discoveryTask = run.Discovery.RunAsync(run.Cancellation.Token);
                await Task.WhenAny(touchTask, discoveryTask).ConfigureAwait(false);
                run.Cancellation.Cancel();
                await Task.WhenAll(touchTask, discoveryTask).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            Volatile.Write(ref run.Error, e.Message);
            output.WriteLine($"receiver_error: {e.Message}");
            flightRecorder?.Event("runtime_error", ("runtimeRunId", run.Id), ("error", e.Message));
        }
        finally
        {
            // Join the motion writer before releasing buttons or disposing the shared native device.
            motionCancellation.Dispose();
            motionClock?.Dispose();
            if (motionClock?.Failure is { } clockFailure)
            {
                Volatile.Write(ref run.Error, clockFailure.Message);
                output.WriteLine($"motion_clock_error: {clockFailure.Message}");
            }
            (motion as IDisposable)?.Dispose();
            run.Discovery?.Dispose();
            buttons?.Dispose();
            if (buttons?.Failure is { } failure) Volatile.Write(ref run.Error, failure.Message);
            gesture?.Reset();
            motion?.Reset();
            run.Receiver?.Dispose();
            if (haptics is not null) await haptics.DisposeAsync().ConfigureAwait(false);
            try { mouse?.Dispose(); }
            catch (Exception e)
            {
                Volatile.Write(ref run.Error, e.Message);
                output.WriteLine($"mouse_cleanup_error: {e.Message}");
                flightRecorder?.Event("mouse_cleanup_error", ("runtimeRunId", run.Id), ("error", e.Message));
            }
            if (gesture is not null)
                output.WriteLine($"gesture_stats: tapCandidates={gesture.TapCandidates} confirmedMoves={gesture.ConfirmedMoves} clicksTriggered={gesture.ClicksTriggered} dragStarts={gesture.DragStarts} dragEnds={gesture.DragEnds}");
            if (mouse is not null)
            {
                var stats = mouse.Stats;
                output.WriteLine($"mouse_stats: backend={mouse.BackendName} successfulEvents={stats.MoveSuccesses} failedCalls={stats.MoveFailures}");
                output.WriteLine($"button_stats: leftDownSuccess={stats.LeftDownSuccesses} leftUpSuccess={stats.LeftUpSuccesses} leftButtonFailures={stats.ButtonFailures}");
            }
            Volatile.Write(ref run.State, (int)(run.Error is null ? ReceiverState.Stopped : ReceiverState.Error));
            flightRecorder?.Event("runtime_stop", ("runtimeRunId", run.Id), ("error", Volatile.Read(ref run.Error)));
            run.Started.TrySetResult(false);
        }
    }

    public RuntimeStatsSnapshot CaptureSnapshot()
    {
        var run = Volatile.Read(ref current);
        string selectedName = rawMouse ? MouseOutputFactory.Name(backend) : "None (diagnostics)";
        if (run is null) return new(0, ReceiverState.Stopped, MouseBackend: selectedName,
            MotionModeName: ActiveMotionMode.ToString());
        var state = (ReceiverState)Volatile.Read(ref run.State);
        var live = settings.Sensitivity.Current;
        if (state == ReceiverState.Running && Interlocked.Exchange(ref run.ReportedSensitivity, live) != live)
        {
            output.WriteLine(FormattableString.Invariant($"live_sensitivity: runtimeRunId={run.Id} sensitivityX={live.X} sensitivityY={live.Y}"));
            flightRecorder?.Event("live_sensitivity", ("runtimeRunId", run.Id), ("x", live.X), ("y", live.Y));
        }
        var r = Volatile.Read(ref run.Receiver);
        if (r is null) return new(run.Id, state, MouseBackend: Volatile.Read(ref run.Mouse)?.BackendName ?? selectedName,
            LastError: Volatile.Read(ref run.Error), MotionModeName: run.MotionMode.ToString());
        var s = r.Statistics;
        var motion = Volatile.Read(ref run.Motion);
        var resampled = motion as ResampledMotion;
        var mouse = Volatile.Read(ref run.Mouse);
        var stats = mouse?.Stats ?? default;
        // Historical SendInput fields remain specific to that implementation.
        var sendInput = mouse is WindowsMouseOutput ? stats : default;
        return new(run.Id, state, r.LastAcceptedAtTicks, s.ReceivedPackets, s.AcceptedPackets,
            s.AcceptedSamples, s.SequenceGapEstimate, s.OldPackets, s.DuplicatePackets,
            s.InvalidPackets, r.InputTimeouts, state == ReceiverState.Running ? r.ActiveTouchSessionId : -1,
            r.LastRemoteIp, mouse?.BackendName ?? selectedName, Volatile.Read(ref run.Error),
            r.Presence, s.HeartbeatPackets, s.OutdatedRunPackets, r.PresenceTimeouts,
            r.LastHeartbeatAtTicks, r.LastTouchDatagramAtTicks, r.LastAcceptedSampleAtTicks,
            motion?.OutputEvents ?? 0, motion?.LastOutputAtTicks ?? 0,
            sendInput.Successes, sendInput.Failures, sendInput.LastSuccessAtTicks, sendInput.LastFailureAtTicks,
            stats.RelativeDx, stats.RelativeDy, stats.AbsDx, stats.AbsDy,
            stats.Successes, stats.Failures, stats.LastSuccessAtTicks, stats.LastFailureAtTicks,
            run.MotionMode.ToString(), resampled?.TickCount ?? 0, resampled?.MissedTicks ?? 0);
    }
}
