using System.IO;
using System.Net;

namespace Rightpad.Receiver;

internal sealed class ReceiverRuntime(RuntimeSettingsStore settings, TextWriter output,
    IPEndPoint? endpoint = null, Func<WindowsMouseOutput>? mouseFactory = null, bool rawMouse = true)
{
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private Run? current;
    private long nextRunId;
    public IPEndPoint? LocalEndpoint => Volatile.Read(ref current)?.Receiver?.LocalEndpoint;
    public Task Completion => Volatile.Read(ref current)?.Task ?? Task.CompletedTask;

    private sealed class Run(long id)
    {
        public readonly long Id = id;
        public readonly CancellationTokenSource Cancellation = new();
        public readonly TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Task = Task.CompletedTask;
        public UdpReceiver? Receiver;
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
        WindowsMouseOutput? mouse = null;
        LeftButtonController? buttons = null;
        TouchSessionProcessor? motion = null;
        GestureProcessor? gesture = null;
        try
        {
            var initial = settings.Current;
            mouse = rawMouse ? (mouseFactory?.Invoke() ?? new WindowsMouseOutput()) : null;
            motion = mouse is null ? null : new(mouse.Move, initial.SensitivityX, initial.SensitivityY);
            buttons = mouse is null ? null : new(mouse.LeftDown, mouse.LeftUp, output.WriteLine,
                run.Cancellation.Cancel, initial.ClickHoldMs);
            // Hold belongs to the request, which can occur after the packet's motion output.
            gesture = buttons is null ? null : new(_ => buttons.Click(settings.Current.ClickHoldMs), initial);
            output.WriteLine(FormattableString.Invariant($"startup: runId={run.Id} mode={(rawMouse ? "raw_mouse" : "diagnostic")} sensitivityX={initial.SensitivityX} sensitivityY={initial.SensitivityY} timeoutAction=diagnostic_only"));
            if (gesture is not null)
                output.WriteLine(FormattableString.Invariant($"gesture: singleTap=enabled tapMaxDurationMs={initial.TapMaxDurationMs} tapMovementThresholdPx={initial.TapMovementThresholdPx} clickHoldMs={initial.ClickHoldMs}"));
            var receiver = new UdpReceiver(endpoint ?? new(IPAddress.Any, UdpReceiver.Port), output,
                motion: motion, detailedLogging: !rawMouse, gesture: gesture, settings: settings,
                cancelButtons: buttons is null ? null : buttons.CancelPendingAndRelease);
            Volatile.Write(ref run.Receiver, receiver);
            Volatile.Write(ref run.State, (int)ReceiverState.Running);
            run.Started.TrySetResult(true);
            await receiver.RunAsync(run.Cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Volatile.Write(ref run.Error, e.Message);
            output.WriteLine($"receiver_error: {e.Message}");
        }
        finally
        {
            buttons?.Dispose();
            if (buttons?.Failure is { } failure) Volatile.Write(ref run.Error, failure.Message);
            gesture?.Reset();
            motion?.Reset();
            run.Receiver?.Dispose();
            if (gesture is not null)
                output.WriteLine($"gesture_stats: tapCandidates={gesture.TapCandidates} confirmedMoves={gesture.ConfirmedMoves} clicksTriggered={gesture.ClicksTriggered}");
            if (mouse is not null)
            {
                output.WriteLine($"mouse_stats: successfulEvents={mouse.SuccessfulEvents} failedCalls={mouse.FailedCalls}");
                output.WriteLine($"button_stats: leftDownSuccess={mouse.LeftDownSuccess} leftUpSuccess={mouse.LeftUpSuccess} leftButtonFailures={mouse.LeftButtonFailures}");
            }
            Volatile.Write(ref run.State, (int)(run.Error is null ? ReceiverState.Stopped : ReceiverState.Error));
            run.Started.TrySetResult(false);
        }
    }

    public RuntimeStatsSnapshot CaptureSnapshot()
    {
        var run = Volatile.Read(ref current);
        if (run is null) return new(0, ReceiverState.Stopped);
        var state = (ReceiverState)Volatile.Read(ref run.State);
        var r = Volatile.Read(ref run.Receiver);
        if (r is null) return new(run.Id, state, LastError: Volatile.Read(ref run.Error));
        var s = r.Statistics;
        return new(run.Id, state, r.LastAcceptedAtTicks, s.ReceivedPackets, s.AcceptedPackets,
            s.AcceptedSamples, s.SequenceGapEstimate, s.OldPackets, s.DuplicatePackets,
            s.InvalidPackets, r.InputTimeouts, state == ReceiverState.Running ? r.ActiveTouchSessionId : -1,
            r.LastRemoteIp, rawMouse ? "SendInput" : "None (diagnostics)", Volatile.Read(ref run.Error),
            r.Presence, s.HeartbeatPackets, s.OutdatedRunPackets, r.PresenceTimeouts);
    }
}
