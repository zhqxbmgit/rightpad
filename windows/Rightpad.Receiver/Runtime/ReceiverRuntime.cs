using System.IO;
using System.Net;

namespace Rightpad.Receiver;

internal sealed class ReceiverRuntime(RuntimeSettingsStore settings, TextWriter output,
    IPEndPoint? endpoint = null, Func<IMouseOutput>? mouseFactory = null, bool rawMouse = true,
    FlightRecorder? flightRecorder = null, MouseBackend backend = MouseBackend.SendInput,
    IPEndPoint? discoveryEndpoint = null, Func<byte[]>? identityFactory = null)
{
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private Run? current;
    private long nextRunId;
    public IPEndPoint? LocalEndpoint => Volatile.Read(ref current)?.Receiver?.LocalEndpoint;
    public Task Completion => Volatile.Read(ref current)?.Task ?? Task.CompletedTask;
    public IPEndPoint? DiscoveryEndpoint => Volatile.Read(ref current)?.Discovery?.LocalEndpoint;

    private sealed class Run(long id)
    {
        public readonly long Id = id;
        public readonly CancellationTokenSource Cancellation = new();
        public readonly TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Task = Task.CompletedTask;
        public UdpReceiver? Receiver;
        public DiscoveryResponder? Discovery;
        public IMouseOutput? Mouse;
        public TouchSessionProcessor? Motion;
        public int State = (int)ReceiverState.Starting;
        public string? Error;
    }

    public async Task StartAsync()
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            var previous = current;
            if (previous is not null)
            {
                if (!previous.Task.IsCompleted) return;
                await previous.Task.ConfigureAwait(false);
                previous.Cancellation.Dispose();
            }
            var run = new Run(++nextRunId);
            Volatile.Write(ref current, run);
            run.Task = Task.Run(() => RunAsync(run));
            await run.Started.Task.ConfigureAwait(false);
            if ((ReceiverState)Volatile.Read(ref run.State) == ReceiverState.Error)
                await run.Task.ConfigureAwait(false);
        }
        finally { lifecycle.Release(); }
    }

    public async Task StopAsync()
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
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
        finally { lifecycle.Release(); }
    }

    private async Task RunAsync(Run run)
    {
        IMouseOutput? mouse = null;
        LeftButtonController? buttons = null;
        TouchSessionProcessor? motion = null;
        GestureProcessor? gesture = null;
        try
        {
            var initial = settings.Current;
            flightRecorder?.Event("runtime_start", ("runtimeRunId", run.Id));
            mouse = rawMouse ? (mouseFactory is not null ? mouseFactory() : MouseOutputFactory.Create(backend, flightRecorder)) : null;
            if (rawMouse && mouse is null) throw new InvalidOperationException("Mouse output factory returned no backend.");
            output.WriteLine($"mouse_backend: name={mouse?.BackendName ?? "None (diagnostics)"} device={mouse?.DeviceIdentity ?? "none"}");
            flightRecorder?.Event("mouse_backend", ("runtimeRunId", run.Id), ("mouseBackend", mouse?.BackendName), ("deviceIdentity", mouse?.DeviceIdentity));
            motion = mouse is null ? null : new(mouse.Move, initial.SensitivityX, initial.SensitivityY);
            Volatile.Write(ref run.Mouse, mouse);
            Volatile.Write(ref run.Motion, motion);
            buttons = mouse is null ? null : new(mouse.LeftDown, mouse.LeftUp, output.WriteLine,
                run.Cancellation.Cancel, initial.ClickHoldMs);
            // Hold belongs to the request, which can occur after the packet's motion output.
            gesture = buttons is null ? null : new(_ => buttons.Click(settings.Current.ClickHoldMs), initial);
            output.WriteLine(FormattableString.Invariant($"startup: runId={run.Id} mode={(rawMouse ? "raw_mouse" : "diagnostic")} sensitivityX={initial.SensitivityX} sensitivityY={initial.SensitivityY} timeoutAction=diagnostic_only"));
            if (gesture is not null)
                output.WriteLine(FormattableString.Invariant($"gesture: singleTap=enabled tapMaxDurationMs={initial.TapMaxDurationMs} tapMovementThresholdPx={initial.TapMovementThresholdPx} clickHoldMs={initial.ClickHoldMs}"));
            var receiver = new UdpReceiver(endpoint ?? new(IPAddress.Any, UdpReceiver.Port), output,
                motion: motion, detailedLogging: !rawMouse, gesture: gesture, settings: settings,
                cancelButtons: buttons is null ? null : buttons.CancelPendingAndRelease, flightRecorder: flightRecorder);
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
            run.Discovery?.Dispose();
            buttons?.Dispose();
            if (buttons?.Failure is { } failure) Volatile.Write(ref run.Error, failure.Message);
            gesture?.Reset();
            motion?.Reset();
            run.Receiver?.Dispose();
            try { mouse?.Dispose(); }
            catch (Exception e)
            {
                Volatile.Write(ref run.Error, e.Message);
                output.WriteLine($"mouse_cleanup_error: {e.Message}");
                flightRecorder?.Event("mouse_cleanup_error", ("runtimeRunId", run.Id), ("error", e.Message));
            }
            if (gesture is not null)
                output.WriteLine($"gesture_stats: tapCandidates={gesture.TapCandidates} confirmedMoves={gesture.ConfirmedMoves} clicksTriggered={gesture.ClicksTriggered}");
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
        if (run is null) return new(0, ReceiverState.Stopped, MouseBackend: selectedName);
        var state = (ReceiverState)Volatile.Read(ref run.State);
        var r = Volatile.Read(ref run.Receiver);
        if (r is null) return new(run.Id, state, MouseBackend: Volatile.Read(ref run.Mouse)?.BackendName ?? selectedName, LastError: Volatile.Read(ref run.Error));
        var s = r.Statistics;
        var motion = Volatile.Read(ref run.Motion);
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
            stats.Successes, stats.Failures, stats.LastSuccessAtTicks, stats.LastFailureAtTicks);
    }
}
