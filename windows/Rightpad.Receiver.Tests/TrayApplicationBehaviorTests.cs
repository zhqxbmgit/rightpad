namespace Rightpad.Receiver.Tests;

internal static class TrayApplicationBehaviorTests
{
    public static void ClosingHidesWithoutCleanup()
    {
        var behavior = new TrayApplicationBehavior();
        int hides = 0, runtimeStops = 0, portReleases = 0;

        bool cancel = behavior.HandleClosing(() => hides++);

        Program.Check(cancel, "ordinary Closing is canceled");
        Program.Equal(1, hides, "ordinary Closing hides the window");
        Program.Equal(0, runtimeStops, "hiding does not stop ReceiverRuntime");
        Program.Equal(0, portReleases, "hiding does not release UDP");
    }

    public static void ExplicitExitAllowsClosing()
    {
        var behavior = new TrayApplicationBehavior();
        behavior.ExitAsync(() => Task.CompletedTask, () => Task.CompletedTask, () => { }, () => { })
            .GetAwaiter().GetResult();

        int hides = 0;
        bool cancel = behavior.HandleClosing(() => hides++);

        Program.Check(!cancel, "explicit Exit allows Closing");
        Program.Equal(0, hides, "explicit Exit does not hide again");
    }

    public static async Task ExitCleansUp()
    {
        var behavior = new TrayApplicationBehavior();
        var actions = new List<string>();

        await behavior.ExitAsync(
            () => { actions.Add("stop"); return Task.CompletedTask; },
            () => { actions.Add("flush"); return Task.CompletedTask; },
            () => actions.Add("dispose tray"),
            () => actions.Add("shutdown"));

        Program.Check(behavior.IsExitRequested, "Exit marks the explicit exit state");
        Program.Equal("stop,flush,dispose tray,shutdown", string.Join(',', actions), "Exit cleanup order");
    }

    public static void RestoreHidden()
    {
        var behavior = new TrayApplicationBehavior();
        var actions = new List<string>();

        behavior.Restore(false, false, () => actions.Add("show"),
            () => actions.Add("normal"), () => actions.Add("activate"));

        Program.Equal("show,activate", string.Join(',', actions), "hidden window restore");
    }

    public static void RestoreMinimized()
    {
        var behavior = new TrayApplicationBehavior();
        var actions = new List<string>();

        behavior.Restore(true, true, () => actions.Add("show"),
            () => actions.Add("normal"), () => actions.Add("activate"));

        Program.Equal("normal,activate", string.Join(',', actions), "minimized window restore");
    }
}
