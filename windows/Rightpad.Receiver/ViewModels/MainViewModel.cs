using System.Diagnostics;

namespace Rightpad.Receiver;

internal enum ReceiverPage { Overview, Motion, Tap, Diagnostics }

internal sealed class MainViewModel(ReceiverRuntime runtime, SettingsViewModel settings, StartupViewModel startup) : ObservableModel
{
    public string MotionQuantizerName => $"quantizer={MotionModes.QuantizerName(runtime.ActiveMotionMode)}";
    public string MotionModeName => runtime.ActiveMotionMode switch
    {
        MotionMode.RESAMPLED_250HZ_BOXCAR_4MS => "RESAMPLED_250HZ / BOXCAR 4 ms / quantizer=Q0I",
        MotionMode.RESAMPLED_250HZ_BOXCAR_8MS => "RESAMPLED_250HZ / BOXCAR 8 ms / quantizer=Q0I",
        MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5 => "RESAMPLED_250HZ / FINITE CRITICAL K24-r5 / quantizer=Q0C",
        MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5_SETTLE => "RESAMPLED_250HZ / FINITE CRITICAL K24-r5 SETTLE / quantizer=Q0C",
        MotionMode.RESAMPLED_500HZ_FINITE_CRITICAL_K24_R5_SETTLE => "RESAMPLED_500HZ / FINITE CRITICAL K24-r5 SETTLE / quantizer=Q0C",
        MotionMode.RESAMPLED_1000HZ_FINITE_CRITICAL_K24_R5_SETTLE => "RESAMPLED_1000HZ / FINITE CRITICAL K24-r5 SETTLE / quantizer=Q0C",
        MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K35_R4 => "RESAMPLED_250HZ / FINITE CRITICAL K35-r4 / quantizer=Q0C",
        _ => $"{runtime.ActiveMotionMode} / quantizer=Q0I"
    };
    public string MotionExplanation => runtime.ActiveMotionMode switch
    {
        MotionMode.RAW => "RAW applies a fixed gain without filtering.",
        MotionMode.RESAMPLED_250HZ => "Experimental motion: 250 Hz output opportunities with a fixed 12 ms playback delay.",
        MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5 => "Experimental fixed K24-r5: 250 Hz, 12 ms playback, tau 24 ms, support 120 ms, Q0C. Sensitivity changes apply on the next Receiver run.",
        MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5_SETTLE => "Research K24-r5 SETTLE: 250 Hz, 12 ms playback, tau 24 ms, support 120 ms, Q0C. Earned movement settles after release; the next contact continues the unfinished movement. Sensitivity changes apply on the next Receiver run.",
        MotionMode.RESAMPLED_500HZ_FINITE_CRITICAL_K24_R5_SETTLE => "Research K24-r5 SETTLE: 500 Hz, 12 ms playback, tau 24 ms, support 120 ms, Q0C. Earned movement settles after release; the next contact continues the unfinished movement. Sensitivity changes apply on the next Receiver run.",
        MotionMode.RESAMPLED_1000HZ_FINITE_CRITICAL_K24_R5_SETTLE => "Production K24-r5 SETTLE: fixed 1000 Hz, 12 ms playback, tau 24 ms, support 120 ms, Q0C. Earned movement settles after release; the next contact continues the unfinished movement. Sensitivity changes apply on the next Receiver run.",
        MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K35_R4 => "Experimental fixed K35-r4: 250 Hz, 12 ms playback, tau 35 ms, support 140 ms, Q0C. Sensitivity changes apply on the next Receiver run.",
        _ => $"Experimental motion: 250 Hz output opportunities, fixed 12 ms playback delay and fixed {MotionModes.BoxcarWindowMs(runtime.ActiveMotionMode)} ms causal boxcar position average."
    };
    private ReceiverPage currentPage = ReceiverPage.Motion;
    private bool busy;
    private bool running;
    private bool canToggle = true;
    public ReceiverPage CurrentPage { get => currentPage; set => Set(ref currentPage, value); }
    public SettingsViewModel Settings { get; } = settings;
    public StartupViewModel Startup { get; } = startup;
    public RuntimeStatsViewModel Stats { get; } = new();
    public string ActionText => running ? "Stop Receiver" : "Start Receiver";
    public bool CanToggle => canToggle;
    public bool IsStopped => !running;
    public void Refresh()
    {
        var snapshot = runtime.CaptureSnapshot();
        Stats.Refresh(snapshot, Stopwatch.GetTimestamp());
        bool wasRunning = running;
        running = snapshot.RuntimeState == ReceiverState.Running;
        if (running != wasRunning) { Changed(nameof(ActionText)); Changed(nameof(IsStopped)); }
        Set(ref canToggle, !busy && Stats.RuntimeState is not ("Starting" or "Stopping"), nameof(CanToggle));
        Settings.RefreshNotice();
    }
    public async Task StartAsync()
    {
        busy = true; Refresh();
        try { await runtime.StartAsync(); }
        finally { busy = false; Refresh(); }
    }
    public async Task ToggleAsync()
    {
        if (!CanToggle) return;
        busy = true; Refresh();
        try
        {
            if (running) await runtime.StopAsync(); else await runtime.StartAsync();
        }
        finally { busy = false; Refresh(); }
    }
}
