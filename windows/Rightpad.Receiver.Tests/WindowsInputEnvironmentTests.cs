using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using static Rightpad.Receiver.Tests.Program;
using static Rightpad.Receiver.Tests.RawMotionProcessorTests;

namespace Rightpad.Receiver.Tests;

internal static class WindowsInputEnvironmentTests
{
    private sealed class Reads : WindowsInputEnvironmentSnapshot.Reads
    {
        public uint Pid = 10;
        public int ProcessReads, CursorReads;
        public bool CursorFails, ClipFails, DesktopFails, ProcessFails;
        public override (int X, int Y) Cursor()
        { CursorReads++; if (CursorFails) throw new Win32Exception(5); return (-30, 40); }
        public override (int Left, int Top, int Right, int Bottom) Clip()
        { if (ClipFails) throw new Win32Exception(6); return (-100, 0, 900, 700); }
        public override (int Left, int Top, int Width, int Height) VirtualScreen() => (-100, 0, 1000, 700);
        public override uint ForegroundPid() => Pid;
        public override WindowsInputEnvironmentSnapshot.ForegroundProcess ProcessInfo(uint pid)
        { ProcessReads++; if (ProcessFails) throw new Win32Exception(5); return new($"p{pid}", $"C:/p{pid}.exe", "Medium"); }
        public override string InputDesktop()
        { if (DesktopFails) throw new Win32Exception(5); return "Default"; }
        public override uint ActiveConsoleSession() => 2;
    }
    private sealed class Storage : IFlightRecordStorage
    {
        public readonly ConcurrentQueue<string> Lines = new();
        public void WriteLine(string line) => Lines.Enqueue(line);
        public void Dispose() { }
    }
    private static JsonElement Parse(string line) { using var doc = JsonDocument.Parse(line); return doc.RootElement.Clone(); }

    public static void IntendedCounts()
    {
        var inputs = new List<(uint Flags, int X, int Y)>();
        uint result = 1;
        var mouse = new WindowsMouseOutput((uint count, ref WindowsMouseOutput.NativeInput input, int size) =>
        { inputs.Add((input.Data.Mouse.Flags, input.Data.Mouse.Dx, input.Data.Mouse.Dy)); return result; }, () => 5);
        mouse.Move(5, -3); mouse.Move(-2, 8);
        Equal(3L, mouse.IntendedRelativeDxTotal, "signed X"); Equal(5L, mouse.IntendedRelativeDyTotal, "signed Y");
        Equal(7L, mouse.IntendedAbsDxTotal, "absolute X"); Equal(11L, mouse.IntendedAbsDyTotal, "absolute Y");
        Check(inputs.SequenceEqual(new[] { (WindowsMouseOutput.MouseMove, 5, -3), (WindowsMouseOutput.MouseMove, -2, 8) }), "native counts/flags unchanged");
        result = 0;
        Throws<Win32Exception>(() => mouse.Move(int.MinValue, int.MinValue));
        Equal(7L + 2147483648L, mouse.IntendedAbsDxTotal, "int.MinValue magnitude and attempted failure");
        Equal(11L + 2147483648L, mouse.IntendedAbsDyTotal, "Y magnitude avoids int overflow");
        Equal(2L, mouse.SuccessfulCalls, "failed attempt is not success");
        Equal(1L, mouse.AllFailedCalls, "failure behavior unchanged");
    }
    public static void RoundTripCounts()
    {
        var mouse = new WindowsMouseOutput((uint count, ref WindowsMouseOutput.NativeInput input, int size) => 1, () => 0);
        for (int i = 0; i < 100; i++) { mouse.Move(7, -9); mouse.Move(-7, 9); }
        Equal(0L, mouse.IntendedRelativeDxTotal, "round trip net X"); Equal(0L, mouse.IntendedRelativeDyTotal, "round trip net Y");
        Equal(1400L, mouse.IntendedAbsDxTotal, "round trip X does not cancel"); Equal(1800L, mouse.IntendedAbsDyTotal, "round trip Y does not cancel");
    }
    public static void ZeroAndButtons()
    {
        int calls = 0;
        var mouse = new WindowsMouseOutput((uint count, ref WindowsMouseOutput.NativeInput input, int size) => { calls++; return 1; }, () => 0);
        mouse.Move(0, 0); Equal(0, calls, "zero does not call SendInput");
        mouse.LeftDown(); mouse.LeftUp();
        Equal(0L, mouse.IntendedRelativeDxTotal, "no intended X"); Equal(0L, mouse.IntendedRelativeDyTotal, "no intended Y");
        Equal(0L, mouse.IntendedAbsDxTotal, "no absolute X"); Equal(0L, mouse.IntendedAbsDyTotal, "no absolute Y");
        Equal(2L, mouse.SuccessfulCalls, "buttons retain existing success semantics");
    }
    public static async Task SnapshotFields()
    {
        var reads = new Reads(); var environment = new WindowsInputEnvironmentSnapshot(reads); var storage = new Storage();
        using var recorder = new FlightRecorder(storage, captureEnvironment: environment.Capture);
        recorder.Event("test");
        Equal(0, reads.CursorReads, "events do not read Windows environment");
        recorder.Snapshot(new(1, ReceiverState.Running, IntendedRelativeDxTotal: -5, IntendedRelativeDyTotal: 6,
            IntendedAbsDxTotal: 15, IntendedAbsDyTotal: 16));
        await RuntimeTests.Until(() => storage.Lines.Count == 2);
        var j = Parse(storage.Lines.Last());
        Check(j.GetProperty("cursorReadSucceeded").GetBoolean(), "cursor success");
        Equal(-30, j.GetProperty("cursorX").GetInt32(), "negative screen cursor X"); Equal(40, j.GetProperty("cursorY").GetInt32(), "cursor Y");
        Check(j.GetProperty("clipReadSucceeded").GetBoolean(), "clip success");
        Equal(-100, j.GetProperty("clipLeft").GetInt32(), "clip left"); Equal(0, j.GetProperty("clipTop").GetInt32(), "clip top");
        Equal(900, j.GetProperty("clipRight").GetInt32(), "clip right"); Equal(700, j.GetProperty("clipBottom").GetInt32(), "clip bottom");
        Equal(-100, j.GetProperty("virtualScreenLeft").GetInt32(), "virtual left"); Equal(0, j.GetProperty("virtualScreenTop").GetInt32(), "virtual top");
        Equal(1000, j.GetProperty("virtualScreenWidth").GetInt32(), "virtual width"); Equal(700, j.GetProperty("virtualScreenHeight").GetInt32(), "virtual height");
        Equal("p10", j.GetProperty("foregroundProcessName").GetString(), "foreground name"); Equal("C:/p10.exe", j.GetProperty("foregroundProcessPath").GetString(), "foreground path");
        Equal("Medium", j.GetProperty("foregroundIntegrity").GetString(), "foreground integrity");
        Equal("Default", j.GetProperty("inputDesktopName").GetString(), "desktop"); Check(j.GetProperty("inputDesktopReadSucceeded").GetBoolean(), "desktop success");
        Equal(2, j.GetProperty("activeConsoleSessionId").GetInt32(), "console session");
        Equal(-5L, j.GetProperty("intendedRelativeDxTotal").GetInt64(), "intended X JSON"); Equal(6L, j.GetProperty("intendedRelativeDyTotal").GetInt64(), "intended Y JSON");
        Equal(15L, j.GetProperty("intendedAbsDxTotal").GetInt64(), "absolute X JSON"); Equal(16L, j.GetProperty("intendedAbsDyTotal").GetInt64(), "absolute Y JSON");
        Check(j.GetProperty("cursorReadError").ValueKind == JsonValueKind.Null && j.GetProperty("clipReadError").ValueKind == JsonValueKind.Null, "successful reads clear errors");
        Check(!j.TryGetProperty("cursorVisible", out _), "no unrequested visibility field");
    }
    public static void CursorFailure()
    {
        var reads = new Reads { CursorFails = true }; var environment = new WindowsInputEnvironmentSnapshot(reads);
        var v = environment.Capture();
        Equal(false, v["cursorReadSucceeded"], "failed cursor read"); Equal(5, v["cursorReadError"], "cursor Win32 error");
        Check(v["cursorX"] is null && v["cursorY"] is null, "no invented failure coordinates");
        Equal(true, v["clipReadSucceeded"], "clip still read"); Equal("Default", v["inputDesktopName"], "desktop still read");
        reads.CursorFails = false;
        v = environment.Capture(); Equal(true, v["cursorReadSucceeded"], "next scheduled snapshot reads normally"); Check(v["cursorReadError"] is null, "no stale error");
    }
    public static void ClipFailure()
    {
        var v = new WindowsInputEnvironmentSnapshot(new Reads { ClipFails = true }).Capture();
        Equal(false, v["clipReadSucceeded"], "failed clip read"); Equal(6, v["clipReadError"], "clip Win32 error");
        Check(new[] { "clipLeft", "clipTop", "clipRight", "clipBottom" }.All(k => v[k] is null), "no invented clip");
        Equal(true, v["cursorReadSucceeded"], "cursor still read"); Equal(1000, v["virtualScreenWidth"], "bounds still read");
    }
    public static void ForegroundCache()
    {
        var reads = new Reads(); var environment = new WindowsInputEnvironmentSnapshot(reads);
        environment.Capture(); environment.Capture(); Equal(1, reads.ProcessReads, "same PID cached");
        reads.Pid = 11; reads.ProcessFails = true;
        var v = environment.Capture(); environment.Capture(); Equal(2, reads.ProcessReads, "failed details cached too");
        Equal("Unavailable", v["foregroundIntegrity"], "no stale prior identity");
        Equal("Unavailable", v["foregroundProcessPath"], "path unavailable");
        reads.Pid = 0; v = environment.Capture(); Check(v["foregroundPid"] is null, "no foreground window");
        Equal("Unavailable", v["foregroundProcessName"], "no stale name"); Equal(2, reads.ProcessReads, "PID zero not opened");
        reads.Pid = 10; reads.ProcessFails = false; v = environment.Capture();
        Equal(3, reads.ProcessReads, "returning PID refreshed"); Equal("p10", v["foregroundProcessName"], "new cached name");
    }
    public static Task DesktopFailureDoesNotAffectInput() => FailureDoesNotAffectInput(false);
    public static Task EnvironmentFailureDoesNotAffectInput() => FailureDoesNotAffectInput(true);
    private static async Task FailureDoesNotAffectInput(bool throwAll)
    {
        var storage = new Storage(); var environment = new WindowsInputEnvironmentSnapshot(new Reads { DesktopFails = true });
        using var recorder = new FlightRecorder(storage, captureEnvironment: () => throwAll ? throw new IOException("environment unavailable") : environment.Capture());
        var mouse = new WindowsMouseOutput((uint c, ref WindowsMouseOutput.NativeInput i, int s) => 1, () => 0);
        var runtime = new ReceiverRuntime(new(), TextWriter.Null, MouseBackend.SendInput, new IPEndPoint(IPAddress.Loopback, 0), () => mouse, flightRecorder: recorder);
        using var sender = new UdpClient();
        try
        {
            await runtime.StartAsync();
            recorder.StartSnapshots(runtime.CaptureSnapshot, TimeSpan.FromMilliseconds(10));
            await sender.SendAsync(PacketDecoderTests.Encode(TouchEventType.Down, 1, 0, new TouchSample(0, 0, 0)), runtime.LocalEndpoint!);
            await sender.SendAsync(PacketDecoderTests.Encode(TouchEventType.Move, 1, 1, new TouchSample(1, 2, -3)), runtime.LocalEndpoint!);
            await RuntimeTests.Until(() => storage.Lines.Any(l => { var j = Parse(l); return j.GetProperty("type").GetString() == "snapshot" && j.GetProperty("sendInputSuccesses").GetInt64() > 0; }));
            var snapshot = Parse(storage.Lines.Last(l => Parse(l).GetProperty("type").GetString() == "snapshot"));
            Check(!snapshot.GetProperty("inputDesktopReadSucceeded").GetBoolean(), "desktop failure recorded");
            Check(snapshot.GetProperty("inputDesktopReadError").ValueKind != JsonValueKind.Null, "desktop error retained");
            if (throwAll) Check(!snapshot.GetProperty("cursorReadSucceeded").GetBoolean(), "whole-read failure keeps missing fields");
            Check(snapshot.GetProperty("intendedAbsDxTotal").GetInt64() > 0, "runtime forwards intended counts");
            Equal(ReceiverState.Running, runtime.CaptureSnapshot().RuntimeState, "runtime stays running");
            Equal(0L, mouse.AllFailedCalls, "input succeeds"); Check(!recorder.Disabled, "recorder keeps recording");
        }
        finally { await runtime.StopAsync(); }
    }
}
