using System.IO;
using System.Net.Sockets;

namespace Rightpad.Receiver;

// Failure-boundary evidence only. Reporting must never replace the original exception.
internal static class RuntimeFailureDiagnostics
{
    internal sealed record Detail(string Type, string Message, int HResult,
        int? NativeErrorCode, string? SocketErrorCode, string Stack,
        Detail[] InnerExceptions);

    internal static Detail Capture(Exception error) => new(
        error.GetType().FullName ?? error.GetType().Name, error.Message, error.HResult,
        error is SocketException socket ? socket.NativeErrorCode : null,
        error is SocketException s ? s.SocketErrorCode.ToString() : null,
        error.ToString(),
        error is AggregateException aggregate
            ? aggregate.InnerExceptions.Select(Capture).ToArray()
            : error.InnerException is { } inner ? [Capture(inner)] : []);

    internal static void Report(FlightRecorder? recorder, TextWriter output, long runId,
        string operation, Exception error, bool cancellationRequested,
        bool? receiveCancellationRequested = null)
    {
        try
        {
            var detail = Capture(error);
            recorder?.Event("runtime_exception_detail", ("runtimeRunId", runId),
                ("operation", operation), ("cancellationRequested", cancellationRequested),
                ("receiveCancellationRequested", receiveCancellationRequested),
                ("exception", detail));
            output.WriteLine($"runtime_exception_detail: runId={runId} operation={operation} cancellationRequested={cancellationRequested} receiveCancellationRequested={receiveCancellationRequested} exception={error}");
        }
        catch { /* Preserve the original error and existing shutdown behavior. */ }
    }
}
