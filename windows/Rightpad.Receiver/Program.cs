using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace Rightpad.Receiver;

internal static class Program
{
    internal enum LaunchMode { Gui, Diagnostics, RawMouse }
    internal sealed record LaunchOptions(LaunchMode Mode, Options Input, string? LogDirectory, string? SettingsPath,
        MouseBackend Backend, MotionMode Motion = MotionMode.RAW, string? MotionTraceDirectory = null,
        bool MotionExplicit = false);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    [STAThread]
    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            MessageBox.Show("rightpad Receiver requires Windows 11.", "rightpad Receiver");
            return 1;
        }
        LaunchOptions launch;
        try { launch = ParseLaunchArguments(args); }
        catch (ArgumentException e)
        {
            AttachConsole(uint.MaxValue);
            Console.Error.WriteLine($"argument_error: {e.Message}");
            return 1;
        }
        string? logDirectory = launch.LogDirectory;
        if (launch.Mode != LaunchMode.Gui)
        {
            AttachConsole(uint.MaxValue);
            logDirectory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "rightpad", "diagnostics");
        }
        using TextWriter log = OpenLog(logDirectory);
        if (launch.Mode != LaunchMode.Gui) return RunDevelopmentAsync(launch, log).GetAwaiter().GetResult();
        using var instance = new Mutex(initiallyOwned: true, @"Local\rightpad.Receiver.Gui", out bool firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("rightpad Receiver is already running.", "rightpad Receiver");
            return 0;
        }
        try { return RunGui(launch, log); }
        finally { instance.ReleaseMutex(); }
    }

    private static int RunGui(LaunchOptions launch, TextWriter log)
    {
        using var flightRecorder = FlightRecorder.CreateDefault(log.WriteLine);
        flightRecorder.Event("receiver_process_start", ("mode", "gui"));
        string path = launch.SettingsPath ?? SettingsFileStore.DefaultPath;
        var loaded = SettingsFileStore.Load(path);
        var file = new SettingsFileStore(path);
        var store = new RuntimeSettingsStore(loaded.Settings);
        MotionMode selectedMotion = ResolveGuiMotionMode(launch);
        using var motionTrace = launch.MotionTraceDirectory is null ? null : new MotionTrace(launch.MotionTraceDirectory, selectedMotion);
        var runtime = new ReceiverRuntime(store, log, flightRecorder: flightRecorder, backend: launch.Backend,
            motionMode: selectedMotion, motionTrace: motionTrace);
        flightRecorder.StartSnapshots(runtime.CaptureSnapshot);
        var app = new App();
        app.InitializeComponent();
        log.WriteLine("application: mode=gui defaultPage=Motion");
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Current executable path is unavailable.");
        var startup = new StartupViewModel(new(new WindowsStartupValueStore(), executable));
        try { return app.Run(new MainWindow(runtime, new SettingsViewModel(store, file, loaded.Warning), startup, file)); }
        finally { flightRecorder.Event("receiver_shutdown", ("mode", "gui")); }
    }

    private static TextWriter OpenLog(string? directory)
    {
        if (directory is null) return TextWriter.Null;
        Directory.CreateDirectory(directory);
        return TextWriter.Synchronized(new StreamWriter(Path.Combine(directory, "receiver.log"), append: true) { AutoFlush = true });
    }

    internal static MotionMode ResolveGuiMotionMode(LaunchOptions launch) =>
        launch.MotionExplicit ? launch.Motion : MotionModes.ProductionMode;

    private static async Task<int> RunDevelopmentAsync(LaunchOptions launch, TextWriter log)
    {
        using var flightRecorder = FlightRecorder.CreateDefault(log.WriteLine);
        flightRecorder.Event("receiver_process_start", ("mode", launch.Mode.ToString()));
        var o = launch.Input;
        var store = new RuntimeSettingsStore(new(o.SensitivityX, o.SensitivityY, o.TapMaxDurationMs, o.TapMovementThresholdPx, o.ClickHoldMs, o.DoubleTapIntervalMs));
        using var motionTrace = launch.MotionTraceDirectory is null ? null : new MotionTrace(launch.MotionTraceDirectory, launch.Motion);
        var runtime = new ReceiverRuntime(store, log, rawMouse: launch.Mode == LaunchMode.RawMouse,
            flightRecorder: flightRecorder, backend: launch.Backend, motionMode: launch.Motion, motionTrace: motionTrace);
        flightRecorder.StartSnapshots(runtime.CaptureSnapshot);
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            await runtime.StartAsync().ConfigureAwait(false);
            await Task.WhenAny(runtime.Completion, Task.Delay(Timeout.Infinite, cancellation.Token)).ConfigureAwait(false);
            await runtime.StopAsync().ConfigureAwait(false);
            return runtime.CaptureSnapshot().RuntimeState == ReceiverState.Error ? 1 : 0;
        }
        finally
        {
            flightRecorder.Event("receiver_shutdown", ("mode", launch.Mode.ToString()));
            Console.CancelKeyPress -= cancel;
        }
    }

    internal static LaunchOptions ParseLaunchArguments(string[] args)
    {
        var input = new List<string>();
        string? log = null, settings = null;
        bool diagnostics = false;
        var backend = MouseBackendDefaults.Production;
        var motion = MotionMode.RAW;
        string? motionTrace = null;
        var seen = new HashSet<string>();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "--dev-log-dir" or "--dev-settings-path" or "--diagnostics" or "--dev-mouse-backend" or "--dev-motion-mode" or "--dev-motion-trace-dir")
            {
                if (!seen.Add(arg)) throw new ArgumentException($"Repeated option: {arg}");
                if (arg == "--diagnostics") { diagnostics = true; continue; }
                if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith("--"))
                    throw new ArgumentException($"{arg} requires a value.");
                if (arg == "--dev-mouse-backend") backend = args[i] switch
                {
                    "sendinput" => MouseBackend.SendInput,
                    "virtualhid" => MouseBackend.VirtualHid,
                    _ => throw new ArgumentException("--dev-mouse-backend requires sendinput or virtualhid.")
                };
                else if (arg == "--dev-motion-mode") motion = args[i] switch
                {
                    "RAW" => MotionMode.RAW,
                    "RESAMPLED_250HZ" => MotionMode.RESAMPLED_250HZ,
                    "RESAMPLED_250HZ_BOXCAR_4MS" => MotionMode.RESAMPLED_250HZ_BOXCAR_4MS,
                    "RESAMPLED_250HZ_BOXCAR_8MS" => MotionMode.RESAMPLED_250HZ_BOXCAR_8MS,
                    "RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5" => MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5,
                    "RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5_SETTLE" => MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5_SETTLE,
                    "RESAMPLED_500HZ_FINITE_CRITICAL_K24_R5_SETTLE" => MotionMode.RESAMPLED_500HZ_FINITE_CRITICAL_K24_R5_SETTLE,
                    "RESAMPLED_1000HZ_FINITE_CRITICAL_K24_R5_SETTLE" => MotionMode.RESAMPLED_1000HZ_FINITE_CRITICAL_K24_R5_SETTLE,
                    "RESAMPLED_250HZ_FINITE_CRITICAL_K35_R4" => MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K35_R4,
                    _ => throw new ArgumentException("--dev-motion-mode requires one of the supported fixed development modes.")
                };
                else if (arg == "--dev-motion-trace-dir") motionTrace = args[i];
                else if (arg == "--dev-log-dir") log = args[i]; else settings = args[i];
            }
            else input.Add(arg);
        }
        var options = ParseArguments(input.ToArray());
        if (diagnostics && input.Count != 0) throw new ArgumentException("--diagnostics cannot use RAW options.");
        var mode = diagnostics ? LaunchMode.Diagnostics : options.RawMouse ? LaunchMode.RawMouse : LaunchMode.Gui;
        if (settings is not null && mode != LaunchMode.Gui) throw new ArgumentException("--dev-settings-path is GUI-only.");
        if (diagnostics && seen.Contains("--dev-mouse-backend"))
            throw new ArgumentException("--dev-mouse-backend requires mouse output, not --diagnostics.");
        if (diagnostics && (seen.Contains("--dev-motion-mode") || motionTrace is not null))
            throw new ArgumentException("Motion experiments require mouse output.");
        return new(mode, options, log, settings, backend, motion, motionTrace, seen.Contains("--dev-motion-mode"));
    }
    internal readonly record struct Options(bool RawMouse, double SensitivityX, double SensitivityY,
        int TapMaxDurationMs = 300, double TapMovementThresholdPx = 8, int ClickHoldMs = 25, int DoubleTapIntervalMs = RuntimeSettings.DefaultDoubleTapIntervalMs);

    internal static Options ParseArguments(string[] args)
    {
        bool raw = false;
        double x = 7, y = 7;
        int tapDuration = 300, hold = 25, doubleTapInterval = RuntimeSettings.Default.DoubleTapIntervalMs;
        double threshold = 8;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            string option = args[i];
            if (!seen.Add(option)) throw new ArgumentException($"Repeated option: {option}");
            if (option == "--raw-mouse") { raw = true; continue; }
            if (option is not ("--sensitivity-x" or "--sensitivity-y" or
                "--tap-max-duration-ms" or "--tap-movement-threshold-px" or "--click-hold-ms" or "--double-tap-interval-ms"))
                throw new ArgumentException($"Unknown option: {option}");
            if (++i >= args.Length || !double.TryParse(args[i], NumberStyles.Float,
                CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value) || value <= 0)
                throw new ArgumentException($"{option} requires a finite positive number.");
            if (option is "--tap-max-duration-ms" or "--click-hold-ms" or "--double-tap-interval-ms")
            {
                if (value < 1 || value > int.MaxValue || value != Math.Truncate(value))
                    throw new ArgumentException($"{option} requires a positive integer number of milliseconds (1..{int.MaxValue}).");
                if (option == "--double-tap-interval-ms")
                {
                    if (!RuntimeSettings.InRange(value, 50, 1000))
                        throw new ArgumentException("--double-tap-interval-ms requires 50–1000 whole milliseconds.");
                    doubleTapInterval = (int)value;
                }
                else if (option == "--tap-max-duration-ms") tapDuration = (int)value; else hold = (int)value;
            }
            else if (option == "--tap-movement-threshold-px") threshold = value;
            else if (option == "--sensitivity-x") x = value; else y = value;
        }
        if (!raw && seen.Count != 0)
            throw new ArgumentException("Sensitivity and tap options require --raw-mouse.");
        return new Options(raw, x, y, tapDuration, threshold, hold, doubleTapInterval);
    }
}
