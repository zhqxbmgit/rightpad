using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class RuntimeFailureDiagnosticsTests
{
    private sealed class Storage : IFlightRecordStorage
    {
        public readonly ConcurrentQueue<string> Lines = new();
        public void WriteLine(string line) => Lines.Enqueue(line);
        public void Dispose() { }
    }
    private sealed class BrokenWriter : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException("diagnostic writer failed");
    }
    public static void ExceptionTree()
    {
        Exception socket;
        try { throw new SocketException(995); } catch (Exception e) { socket = e; }
        var detail = RuntimeFailureDiagnostics.Capture(new AggregateException("outer", socket, new IOException("second")));
        Equal(2, detail.InnerExceptions.Length, "all aggregate branches retained");
        Equal(995, detail.InnerExceptions[0].NativeErrorCode!.Value, "native abort code retained");
        Equal("OperationAborted", detail.InnerExceptions[0].SocketErrorCode!, "socket code retained");
        Check(detail.InnerExceptions[0].Stack.Contains(nameof(ExceptionTree)), "throw-site stack retained");
        Equal("second", detail.InnerExceptions[1].Message, "secondary exception retained");
        RuntimeFailureDiagnostics.Report(null, new BrokenWriter(), 1, "test", socket, false);
    }
    public static async Task ReceiveFailure()
    {
        var storage = new Storage();
        using var recorder = new FlightRecorder(storage);
        using var receiver = new UdpReceiver(new(IPAddress.Loopback, 0), TextWriter.Null,
            flightRecorder: recorder, runtimeRun: 17);
        var socket = (UdpClient)typeof(UdpReceiver).GetField("socket",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(receiver)!;
        socket.Client.Shutdown(SocketShutdown.Receive);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await receiver.RunAsync(cancellation.Token); throw new Exception("unexpected successful receive"); }
        catch (SocketException e) { Equal(SocketError.Shutdown, e.SocketErrorCode, "unexpected receive failure still propagates"); }
        await RuntimeTests.Until(() => storage.Lines.Any(line => line.Contains("runtime_exception_detail")));
        using var json = JsonDocument.Parse(storage.Lines.First(line => line.Contains("runtime_exception_detail")));
        var root = json.RootElement;
        Equal("touch_receive", root.GetProperty("operation").GetString()!, "operation before cleanup");
        Equal(17L, root.GetProperty("runtimeRunId").GetInt64(), "runtime identity");
        Check(!root.GetProperty("cancellationRequested").GetBoolean(), "not hidden as expected cancellation");
        Equal("Shutdown", root.GetProperty("exception").GetProperty("SocketErrorCode").GetString()!, "real socket error captured");
        using var rebound = new UdpClient(receiver.LocalEndpoint);
    }
    public static async Task NormalCancellation()
    {
        var storage = new Storage();
        var recorder = new FlightRecorder(storage);
        using var receiver = new UdpReceiver(new(IPAddress.Loopback, 0), TextWriter.Null, flightRecorder: recorder);
        using var responder = new DiscoveryResponder(new(IPAddress.Loopback, 0), new byte[16], TextWriter.Null, recorder);
        using var cancellation = new CancellationTokenSource();
        var receive = receiver.RunAsync(cancellation.Token);
        var discover = responder.RunAsync(cancellation.Token, 18);
        cancellation.Cancel();
        await Task.WhenAll(receive, discover);
        recorder.Dispose();
        Check(!storage.Lines.Any(line => line.Contains("runtime_exception_detail")), "ordinary stop remains non-error");
    }
    public static async Task CleanupDoesNotEraseEvidence()
    {
        var storage = new Storage();
        using var recorder = new FlightRecorder(storage);
        var secondary = new IOException("secondary cleanup failure");
        using var receiver = new UdpReceiver(new(IPAddress.Loopback, 0), TextWriter.Null,
            flightRecorder: recorder, cancelButtons: reason => { if (reason == "receiver_stop") throw secondary; });
        var socket = (UdpClient)typeof(UdpReceiver).GetField("socket",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(receiver)!;
        socket.Client.Shutdown(SocketShutdown.Receive);
        try { await receiver.RunAsync(CancellationToken.None); throw new Exception("unexpected success"); }
        catch (IOException e) { Check(ReferenceEquals(secondary, e), "existing cleanup exception semantics unchanged"); }
        await RuntimeTests.Until(() => storage.Lines.Any(line => line.Contains("touch_loop_before_cleanup")));
        using var json = JsonDocument.Parse(storage.Lines.First(line => line.Contains("touch_loop_before_cleanup")));
        Equal("Shutdown", json.RootElement.GetProperty("exception").GetProperty("SocketErrorCode").GetString()!,
            "original socket failure survives secondary cleanup error in evidence");
    }
    public static async Task TaskExitEvidence()
    {
        var storage = new Storage();
        using var recorder = new FlightRecorder(storage);
        var settings = new RuntimeSettingsStore();
        var failure = new SocketException(995);
        using var sender = new UdpClient(AddressFamily.InterNetwork);
        var runtime = new ReceiverRuntime(settings, TextWriter.Null, MouseBackend.SendInput,
            new(IPAddress.Loopback, 0), () => new FailingMouse(failure), flightRecorder: recorder,
            discoveryEndpoint: new(IPAddress.Loopback, 0), identityFactory: () => new byte[16]);
        try
        {
            await runtime.StartAsync();
            await sender.SendAsync(PacketDecoderTests.Encode(TouchEventType.Down, 1, 0, [new(0, 0, 0)]), runtime.LocalEndpoint!);
            await sender.SendAsync(PacketDecoderTests.Encode(TouchEventType.Move, 1, 1, [new(1, 5, 0)]), runtime.LocalEndpoint!);
            await RuntimeTests.Until(() => runtime.Completion.IsCompleted);
            Equal(ReceiverState.Error, runtime.CaptureSnapshot().RuntimeState, "error behavior unchanged");
            await RuntimeTests.Until(() => storage.Lines.Any(line => line.Contains("touch_task_before_shutdown")));
            using var json = JsonDocument.Parse(storage.Lines.First(line => line.Contains("runtime_task_completed")));
            Equal("touch", json.RootElement.GetProperty("firstObservedTask").GetString()!, "first observed completed task");
            Check(!json.RootElement.GetProperty("cancellationRequestedBeforeShutdown").GetBoolean(), "records state before coordination cancel");
            Check(storage.Lines.Any(line => line.Contains("touch_task_before_shutdown") && line.Contains("995")), "original task exception captured");
        }
        finally { await runtime.StopAsync(); }
    }
    private sealed class FailingMouse(Exception failure) : IMouseOutput
    {
        public string BackendName => "fake";
        public MouseOutputStats Stats => default;
        public void Move(int x, int y) => throw failure;
        public void LeftDown() { }
        public void LeftUp() { }
        public void SetButton(ModeMouseButton button, bool down) { }
        public void Dispose() { }
    }
}
