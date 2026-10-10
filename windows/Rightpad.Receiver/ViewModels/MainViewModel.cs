using System.Diagnostics;

namespace Rightpad.Receiver;

internal enum ReceiverPage { Overview, Motion, Tap, Diagnostics, Controls }

internal sealed class MainViewModel(ReceiverRuntime runtime, SettingsViewModel settings, StartupViewModel startup) : ObservableModel
{
    private bool CinematicActive => Stats.ActiveMotionProfile == "C";
    private bool DirectReconstruction => runtime.ActiveMotionConfiguration.IsDirectReconstruction;
    public string MotionQuantizerName => CinematicActive ? "quantizer=Java-compatible rounding" : $"quantizer={MotionModes.QuantizerName(runtime.ActiveMotionMode)}";
    private string CParametersText => (runtime.CaptureCz1State()?.Active ?? Settings.CommittedSettings.Cz1).Description;
    public string MotionModeName => CinematicActive ? "C-Z1 / Reconstruction 12 ms / zhq-derived servo / " + CParametersText + " / 4 ms / 250 Hz nominal / true glide" : runtime.ActiveMotionMode switch
    {
        MotionMode.M_R1 => MotionModes.Mr1Algorithm,
        MotionMode.RESAMPLED_250HZ_BOXCAR_4MS => "RESAMPLED_250HZ / BOXCAR 4 ms / quantizer=Q0I",
        MotionMode.RESAMPLED_250HZ_BOXCAR_8MS => "RESAMPLED_250HZ / BOXCAR 8 ms / quantizer=Q0I",
        MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5 => "RESAMPLED_250HZ / FINITE CRITICAL K24-r5 / quantizer=Q0C",
        MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5_SETTLE => "RESAMPLED_250HZ / FINITE CRITICAL K24-r5 SETTLE / quantizer=Q0C",
        MotionMode.RESAMPLED_500HZ_FINITE_CRITICAL_K24_R5_SETTLE => "RESAMPLED_500HZ / FINITE CRITICAL K24-r5 SETTLE / quantizer=Q0C",
        MotionMode.RESAMPLED_1000HZ_FINITE_CRITICAL_K24_R5_SETTLE => "M-F1 / Reconstruction 8 ms / RESAMPLED_1000HZ / FINITE CRITICAL SETTLE / quantizer=Q0C",
        MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K35_R4 => "RESAMPLED_250HZ / FINITE CRITICAL K35-r4 / quantizer=Q0C",
        _ => $"{runtime.ActiveMotionMode} / quantizer=Q0I"
    };
    public string MotionExplanation
    {
        get
        {
            if (CinematicActive) return "C-Z1: fixed zhq-derived servo, " + CParametersText + ". 4 ms / 250 Hz nominal dynamics, Java-compatible rounding and true glide after the UP playout marker. C parameter changes wait for an idle chain; Sensitivity applies after Save.";
            MotionConfiguration configuration = runtime.ActiveMotionConfiguration;
            return runtime.ActiveMotionMode switch
            {
        MotionMode.M_R1 => MotionModes.Mr1Algorithm + ". Sensitivity changes apply to new real displacement after Save. Saved Tau/Support are retained for the filtered baseline; they do not shape M-R1.",
        MotionMode.RAW => "RAW applies a fixed gain without filtering.",
        MotionMode.RESAMPLED_250HZ => "Experimental motion: 250 Hz output opportunities with a fixed 12 ms playback delay.",
        MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5 => "Experimental fixed K24-r5: 250 Hz, 12 ms playback, tau 24 ms, support 120 ms, Q0C. Sensitivity changes apply immediately after Save.",
        MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5_SETTLE => "Research K24-r5 SETTLE: 250 Hz, 12 ms playback, tau 24 ms, support 120 ms, Q0C. Earned movement settles after release; the next contact continues the unfinished movement. Sensitivity changes apply immediately after Save.",
        MotionMode.RESAMPLED_500HZ_FINITE_CRITICAL_K24_R5_SETTLE => "Research K24-r5 SETTLE: 500 Hz, 12 ms playback, tau 24 ms, support 120 ms, Q0C. Earned movement settles after release; the next contact continues the unfinished movement. Sensitivity changes apply immediately after Save.",
        MotionMode.RESAMPLED_1000HZ_FINITE_CRITICAL_K24_R5_SETTLE => $"M-F1 finite-critical SETTLE: fixed 1000 Hz, 8 ms reconstruction/playback, tau {configuration.FiniteCriticalTauMs} ms, support {configuration.FiniteCriticalSupportMs} ms, Q0C. Earned movement settles after release; the next contact continues the unfinished movement. Sensitivity changes apply immediately after Save. Tau and Support require a Receiver restart.",
        MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K35_R4 => "Experimental fixed K35-r4: 250 Hz, 12 ms playback, tau 35 ms, support 140 ms, Q0C. Sensitivity changes apply immediately after Save.",
        _ => $"Experimental motion: 250 Hz output opportunities, fixed 12 ms playback delay and fixed {MotionModes.BoxcarWindowMs(runtime.ActiveMotionMode)} ms causal boxcar position average."
            };
        }
    }
    private ReceiverPage currentPage = ReceiverPage.Motion;
    private bool freezingHitchTrace;
    private string hitchTraceStatus = "Keeps up to 60 seconds in memory. Export only on manual freeze.";
    public bool CanFreezeHitchTrace => !freezingHitchTrace;
    public string HitchTraceStatus => hitchTraceStatus;
    public async Task FreezeHitchTraceAsync()
    {
        if (freezingHitchTrace) return;
        freezingHitchTrace = true;
        Changed(nameof(CanFreezeHitchTrace));
        Set(ref hitchTraceStatus, "Exporting hitch trace...", nameof(HitchTraceStatus));
        try
        {
            var result = await runtime.HitchTrace.FreezeAsync();
            Set(ref hitchTraceStatus, result.Error is null ? $"Saved: {result.Directory}" :
                $"Hitch export failed: {result.Error}", nameof(HitchTraceStatus));
        }
        catch (Exception e) { Set(ref hitchTraceStatus, $"Hitch export failed: {e.Message}", nameof(HitchTraceStatus)); }
        finally { freezingHitchTrace = false; Changed(nameof(CanFreezeHitchTrace)); }
    }
    private bool busy;
    private bool running;
    private bool canToggle = true;
    private bool isRestarting;
    private string restartError = "";
    public bool IsRestarting => isRestarting;
    public string RestartText => IsRestarting ? "Restarting..." : "Restart Receiver";
    public string RestartError => restartError;
    public int? ActiveTauMs => DirectReconstruction ? null : runtime.CaptureActiveRuntimeSnapshot()?.Motion.FiniteCriticalTauMs;
    public int? ActiveSupportMs => DirectReconstruction ? null : runtime.CaptureActiveRuntimeSnapshot()?.Motion.FiniteCriticalSupportMs;
    public string ActiveMotionText => CinematicActive ? "Active C-Z1: Reconstruction 12 ms · zhq-derived servo · " + CParametersText + " · 4 ms / 250 Hz nominal · Java-compatible rounding · true glide" : ActiveTauMs is int tau
        ? $"Active: Tau {tau} ms · Support {ActiveSupportMs} ms" : DirectReconstruction && running ? "Active: " + MotionModes.Mr1Algorithm + " · Tau N/A · Support N/A" : "Active: Receiver stopped";
    public bool RestartRequired => running && !DirectReconstruction && (ActiveTauMs != Settings.CommittedSettings.SmoothingTauMs ||
        ActiveSupportMs != Settings.CommittedSettings.SmoothingSupportMs);
    public bool CanRestart => RestartRequired && !Settings.IsSaving && !busy && !IsRestarting;
    public ReceiverPage CurrentPage { get => currentPage; set => Set(ref currentPage, value); }
    public SettingsViewModel Settings { get; } = settings;
    public StartupViewModel Startup { get; } = startup;
    public RuntimeStatsViewModel Stats { get; } = new();
    public string ActionText => running ? "Stop Receiver" : "Start Receiver";
    public bool CanToggle => canToggle;
    public bool IsStopped => !running;
    public bool CanSelectTouchMode => runtime.UsesLocalTouchMode && !Settings.IsSaving && !busy && !IsRestarting;
    public string SelectedTouchModeText => "Selected：" + TouchModes.Label(Settings.CommittedSettings.TouchMode);
    private string activeTouchModeText = "Active：Receiver stopped", pendingTouchModeText = "";
    public string ActiveTouchModeText => activeTouchModeText;
    public string PendingTouchModeText => pendingTouchModeText;
    public string TouchModeNotice => runtime.UsesLocalTouchMode
        ? "选择后自动保存；触摸或未完成运动期间，将在安全空闲时生效。"
        : "当前使用显式开发模式覆盖；生产触控模式选择器未启用。";
    public async Task<bool> SelectTouchModeAsync(TouchMode mode)
    {
        if (!CanSelectTouchMode) return false;
        try { return await Settings.SelectTouchModeAsync(mode); }
        finally { Refresh(); }
    }
    public void Refresh()
    {
        var snapshot = runtime.CaptureSnapshot();
        Stats.Refresh(snapshot, Stopwatch.GetTimestamp());
        Set(ref activeTouchModeText, snapshot.ActiveTouchMode is { } active
            ? "Active：" + TouchModes.Label(active) : "Active：" + (runtime.UsesLocalTouchMode ? snapshot.RuntimeState.ToString() : "开发模式 " + snapshot.MotionModeName), nameof(ActiveTouchModeText));
        Set(ref pendingTouchModeText, snapshot.PendingTouchMode is { } pending
            ? "Pending：" + TouchModes.Label(pending) + "（等待当前运动完成）" : "", nameof(PendingTouchModeText));
        bool wasRunning = running;
        running = snapshot.RuntimeState == ReceiverState.Running;
        if (running != wasRunning) { Changed(nameof(ActionText)); Changed(nameof(IsStopped)); }
        Set(ref canToggle, !busy && Stats.RuntimeState is not ("Starting" or "Stopping"), nameof(CanToggle));
        Settings.RefreshNotice();
        Settings.Cz1.Refresh(runtime.CaptureCz1State());
        foreach (string name in new[] { nameof(ActiveTauMs), nameof(ActiveSupportMs), nameof(ActiveMotionText),
            nameof(RestartRequired), nameof(CanRestart),
            nameof(IsRestarting), nameof(RestartText), nameof(RestartError), nameof(MotionExplanation),
            nameof(MotionQuantizerName), nameof(MotionModeName), nameof(CanSelectTouchMode),
            nameof(SelectedTouchModeText), nameof(TouchModeNotice) }) Changed(name);
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
    public async Task RestartAsync()
    {
        if (!CanRestart) return;
        busy = isRestarting = true;
        restartError = "";
        Settings.SetRestarting(true);
        Refresh();
        try
        {
            var result = await runtime.RestartAsync();
            restartError = result.Error ?? "";
        }
        finally
        {
            busy = isRestarting = false;
            Settings.SetRestarting(false);
            Refresh();
        }
    }
}
