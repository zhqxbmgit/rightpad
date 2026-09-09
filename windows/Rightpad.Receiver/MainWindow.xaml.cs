using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Rightpad.Receiver;

public partial class MainWindow : Window
{
    private readonly MainViewModel model;
    private readonly ReceiverRuntime runtime;
    private readonly SettingsFileStore settingsFile;
    private readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly UserControl[] pages;
    private bool closing, closed;

    internal MainWindow(ReceiverRuntime runtime, SettingsViewModel settings, SettingsFileStore settingsFile)
    {
        this.runtime = runtime;
        this.settingsFile = settingsFile;
        model = new(runtime, settings);
        InitializeComponent();
        pages = [new OverviewView(), new MotionView(), new TapView(), new DiagnosticsView()];
        DataContext = model;
        PageContent.Content = pages[(int)ReceiverPage.Motion];
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        timer.Tick += (_, _) => model.Refresh();
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
    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (closed) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        IsEnabled = false;
        timer.Stop();
        await runtime.StopAsync();
        await settingsFile.FlushAsync();
        closed = true;
        Close();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);
}
