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
            Console.Error.WriteLine("Usage: Rightpad.Receiver [--raw-mouse [--sensitivity-x N] [--sensitivity-y N]]");
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
        try
        {
            Console.WriteLine(FormattableString.Invariant(
                $"startup: mode={(options.RawMouse ? "raw_mouse" : "diagnostic")} sensitivityX={options.SensitivityX} sensitivityY={options.SensitivityY} timeoutAction=diagnostic_only"));
            using var receiver = new UdpReceiver(new IPEndPoint(IPAddress.Any, UdpReceiver.Port), Console.Out,
                motion: motion, detailedLogging: !options.RawMouse);
            await receiver.RunAsync(cancellation.Token);
            return 0;
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or OverflowException)
        {
            Console.Error.WriteLine($"receiver_error: {exception.Message}");
            return 1;
        }
        finally
        {
            motion?.Reset();
            if (mouse is not null)
                Console.WriteLine($"mouse_stats: successfulEvents={mouse.SuccessfulEvents} failedCalls={mouse.FailedCalls}");
            Console.CancelKeyPress -= onCancel;
        }
    }

    internal readonly record struct Options(bool RawMouse, double SensitivityX, double SensitivityY);

    internal static Options ParseArguments(string[] args)
    {
        bool raw = false;
        double x = 7, y = 7;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            string option = args[i];
            if (!seen.Add(option)) throw new ArgumentException($"Repeated option: {option}");
            if (option == "--raw-mouse") { raw = true; continue; }
            if (option is not ("--sensitivity-x" or "--sensitivity-y"))
                throw new ArgumentException($"Unknown option: {option}");
            if (++i >= args.Length || !double.TryParse(args[i], NumberStyles.Float,
                CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value) || value <= 0)
                throw new ArgumentException($"{option} requires a finite positive number.");
            if (option == "--sensitivity-x") x = value; else y = value;
        }
        if (!raw && seen.Count != 0)
            throw new ArgumentException("Sensitivity options require --raw-mouse.");
        return new Options(raw, x, y);
    }
}
