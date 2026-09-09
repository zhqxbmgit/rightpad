using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Rightpad.Receiver;

public partial class MainWindow : Window
{
    private readonly MainViewModel model;
    private readonly ReceiverRuntime runtime;
    private readonly SettingsFileStore settingsFile;
    private readonly TrayApplicationBehavior trayBehavior = new();
    private readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly UserControl[] pages;
    private readonly Forms.NotifyIcon trayIcon;
    private readonly System.Drawing.Icon? trayIconImage;

    internal MainWindow(ReceiverRuntime runtime, SettingsViewModel settings, StartupViewModel startup,
        SettingsFileStore settingsFile)
    {
        this.runtime = runtime;
        this.settingsFile = settingsFile;
        model = new(runtime, settings, startup);
        InitializeComponent();
        pages = [new OverviewView(), new MotionView(), new TapView(), new DiagnosticsView()];
        DataContext = model;
        PageContent.Content = pages[(int)ReceiverPage.Motion];
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        timer.Tick += (_, _) => model.Refresh();

        trayIconImage = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        trayIcon = new Forms.NotifyIcon
        {
            Icon = trayIconImage,
            Text = "rightpad Receiver",
            ContextMenuStrip = CreateTrayMenu(),
            Visible = true
        };
        trayIcon.DoubleClick += (_, _) => Dispatcher.InvokeAsync(RestoreWindow);
    }
    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        timer.Start();
        await model.StartAsync();
    }
    private void WindowSourceInitialized(object? sender, EventArgs e)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        int enabled = 1;
        if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
    }
    private void PageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (pages is not null && Navigation.SelectedValue is ReceiverPage page)
            PageContent.Content = pages[(int)page];
    }
    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = trayBehavior.HandleClosing(Hide);
    }

    private Forms.ContextMenuStrip CreateTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        var open = new Forms.ToolStripMenuItem("Open rightpad Receiver");
        open.Click += (_, _) => Dispatcher.InvokeAsync(RestoreWindow);
        var exit = new Forms.ToolStripMenuItem("Exit");
        exit.Click += (_, _) => Dispatcher.InvokeAsync(RequestExitAsync);
        menu.Items.Add(open);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exit);
        return menu;
    }

    private void RestoreWindow() => trayBehavior.Restore(
        IsVisible,
        WindowState == WindowState.Minimized,
        Show,
        () => WindowState = WindowState.Normal,
        () => Activate());

    private async Task RequestExitAsync()
    {
        if (trayBehavior.IsExitRequested) return;
        IsEnabled = false;
        timer.Stop();
        await trayBehavior.ExitAsync(
            runtime.StopAsync,
            settingsFile.FlushAsync,
            DisposeTray,
            () => Application.Current.Shutdown());
    }

    private void DisposeTray()
    {
        trayIcon.Visible = false;
        trayIcon.ContextMenuStrip?.Dispose();
        trayIcon.Dispose();
        trayIconImage?.Dispose();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);
}
