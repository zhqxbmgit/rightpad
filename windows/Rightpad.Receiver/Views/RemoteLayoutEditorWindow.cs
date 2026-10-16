using System.ComponentModel;
using System.Windows;

namespace Rightpad.Receiver;

internal sealed class RemoteLayoutEditorWindow : Window
{
    private readonly RemoteLayoutView editor;
    private readonly Action returnToReceiver;
    private bool shuttingDown;

    internal RemoteLayoutEditorWindow(Func<RemoteLayoutClient?> getClient, Action returnToReceiver)
    {
        this.returnToReceiver = returnToReceiver;
        SetResourceReference(StyleProperty, typeof(Window));
        Title = "rightpad · 手机布局编辑器";
        MinWidth = Math.Min(800, SystemParameters.WorkArea.Width);
        MinHeight = Math.Min(560, SystemParameters.WorkArea.Height);
        Width = Math.Min(1100, SystemParameters.WorkArea.Width);
        Height = Math.Min(800, SystemParameters.WorkArea.Height);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowState = WindowState.Maximized;
        editor = new(getClient, ReturnToReceiver);
        Content = editor;
        IsVisibleChanged += (_, _) => UpdatePolling();
        StateChanged += (_, _) => UpdatePolling();
        Closing += OnClosing;
    }

    internal void Open()
    {
        WindowState = WindowState.Maximized;
        Show(); Activate(); UpdatePolling();
    }

    private void UpdatePolling() => editor.SetPollingActive(
        !shuttingDown && IsVisible && WindowState != WindowState.Minimized);

    private void ReturnToReceiver()
    {
        Hide();
        returnToReceiver();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (shuttingDown) return;
        // Keep the sole draft/view; closing is never Keep, Revert or a Runtime operation.
        e.Cancel = true;
        ReturnToReceiver();
    }

    internal void CloseForShutdown()
    {
        shuttingDown = true;
        editor.SetPollingActive(false);
        Close();
    }
}
