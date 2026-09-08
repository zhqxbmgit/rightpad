using System.Net;
using System.ComponentModel;
using System.Globalization;

namespace Rightpad.Receiver;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Console.Error.WriteLine("rightpad Receiver requires Windows 11.");
            return 1;
        }

        Options options;
        try { options = ParseArguments(args); }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"argument_error: {exception.Message}");
            Console.Error.WriteLine("Usage: Rightpad.Receiver [--raw-mouse [--sensitivity-x N] [--sensitivity-y N] [--tap-max-duration-ms N] [--tap-movement-threshold-px N] [--click-hold-ms N]]");
            return 1;
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, args) =>
        {
            args.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += onCancel;
        WindowsMouseOutput? mouse = options.RawMouse ? new WindowsMouseOutput() : null;
        TouchSessionProcessor? motion = mouse is null ? null :
            new TouchSessionProcessor(mouse.Move, options.SensitivityX, options.SensitivityY);
        LeftButtonController? button = mouse is null ? null : new LeftButtonController(
            mouse.LeftDown, mouse.LeftUp, Console.Error.WriteLine, cancellation.Cancel, options.ClickHoldMs);
        GestureProcessor? gesture = button is null ? null : new GestureProcessor(
            button.Click, options.TapMaxDurationMs, options.TapMovementThresholdPx);
        int result = 0;
        try
        {
            Console.WriteLine(FormattableString.Invariant(
                $"startup: mode={(options.RawMouse ? "raw_mouse" : "diagnostic")} sensitivityX={options.SensitivityX} sensitivityY={options.SensitivityY} timeoutAction=diagnostic_only"));
            if (gesture is not null)
                Console.WriteLine(FormattableString.Invariant(
                    $"gesture: singleTap=enabled tapMaxDurationMs={options.TapMaxDurationMs} tapMovementThresholdPx={options.TapMovementThresholdPx} clickHoldMs={options.ClickHoldMs}"));
            using var receiver = new UdpReceiver(new IPEndPoint(IPAddress.Any, UdpReceiver.Port), Console.Out,
                motion: motion, detailedLogging: !options.RawMouse, gesture: gesture);
            await receiver.RunAsync(cancellation.Token);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or OverflowException)
        {
            Console.Error.WriteLine($"receiver_error: {exception.Message}");
            result = 1;
        }
        finally
        {
            button?.Dispose();
            gesture?.Reset();
            motion?.Reset();
            if (gesture is not null)
                Console.WriteLine($"gesture_stats: tapCandidates={gesture.TapCandidates} confirmedMoves={gesture.ConfirmedMoves} clicksTriggered={gesture.ClicksTriggered}");
            if (mouse is not null)
            {
                Console.WriteLine($"mouse_stats: successfulEvents={mouse.SuccessfulEvents} failedCalls={mouse.FailedCalls}");
                Console.WriteLine($"button_stats: leftDownSuccess={mouse.LeftDownSuccess} leftUpSuccess={mouse.LeftUpSuccess} leftButtonFailures={mouse.LeftButtonFailures}");
            }
            Console.CancelKeyPress -= onCancel;
        }
        return button?.Failure is null ? result : 1;
    }

    internal readonly record struct Options(bool RawMouse, double SensitivityX, double SensitivityY,
        int TapMaxDurationMs = 300, double TapMovementThresholdPx = 8, int ClickHoldMs = 25);

    internal static Options ParseArguments(string[] args)
    {
        bool raw = false;
        double x = 7, y = 7;
        int tapDuration = 300, hold = 25;
        double threshold = 8;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            string option = args[i];
            if (!seen.Add(option)) throw new ArgumentException($"Repeated option: {option}");
            if (option == "--raw-mouse") { raw = true; continue; }
            if (option is not ("--sensitivity-x" or "--sensitivity-y" or
                "--tap-max-duration-ms" or "--tap-movement-threshold-px" or "--click-hold-ms"))
                throw new ArgumentException($"Unknown option: {option}");
            if (++i >= args.Length || !double.TryParse(args[i], NumberStyles.Float,
                CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value) || value <= 0)
                throw new ArgumentException($"{option} requires a finite positive number.");
            if (option is "--tap-max-duration-ms" or "--click-hold-ms")
            {
                if (value < 1 || value > int.MaxValue || value != Math.Truncate(value))
                    throw new ArgumentException($"{option} requires a positive integer number of milliseconds (1..{int.MaxValue}).");
                if (option == "--tap-max-duration-ms") tapDuration = (int)value; else hold = (int)value;
            }
            else if (option == "--tap-movement-threshold-px") threshold = value;
            else if (option == "--sensitivity-x") x = value; else y = value;
        }
        if (!raw && seen.Count != 0)
            throw new ArgumentException("Sensitivity and tap options require --raw-mouse.");
        return new Options(raw, x, y, tapDuration, threshold, hold);
    }
}
