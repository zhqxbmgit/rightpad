using System.Diagnostics;

namespace Rightpad.Receiver;

internal enum ReceiverPage { Overview, Motion, Tap, Diagnostics }

internal sealed class MainViewModel(ReceiverRuntime runtime, SettingsViewModel settings) : ObservableModel
{
    private ReceiverPage currentPage = ReceiverPage.Motion;
    private bool busy;
    private bool running;
    private bool canToggle = true;
    public ReceiverPage CurrentPage { get => currentPage; set => Set(ref currentPage, value); }
    public SettingsViewModel Settings { get; } = settings;
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
