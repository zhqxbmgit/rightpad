namespace Rightpad.Receiver;

internal sealed class TrayApplicationBehavior
{
    public bool IsExitRequested { get; private set; }

    public bool HandleClosing(Action hide)
    {
        if (IsExitRequested) return false;
        hide();
        return true;
    }

    public void Restore(bool isVisible, bool isMinimized, Action show, Action restoreNormal, Action activate)
    {
        if (!isVisible) show();
        if (isMinimized) restoreNormal();
        activate();
    }

    public async Task ExitAsync(Func<Task> stopRuntime, Func<Task> flushSettings,
        Action disposeTray, Action shutdown)
    {
        if (IsExitRequested) return;
        IsExitRequested = true;
        try
        {
            await stopRuntime();
            await flushSettings();
        }
        finally
        {
            disposeTray();
            shutdown();
        }
    }
}
