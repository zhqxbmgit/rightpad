using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Net;
using Rightpad.Receiver;
using static Rightpad.Receiver.Tests.Program;
using static Rightpad.Receiver.Tests.RawMotionProcessorTests;

namespace Rightpad.Receiver.Tests;

internal static class WindowsMouseOutputTests
{
    public static void LayoutAndFields()
    {
        int expectedSize = IntPtr.Size == 8 ? 40 : 28;
        Equal(expectedSize, Marshal.SizeOf<WindowsMouseOutput.NativeInput>(), "native INPUT size");
        Equal(IntPtr.Size == 8 ? 32 : 24, Marshal.SizeOf<WindowsMouseOutput.MouseInput>(), "MOUSEINPUT size");
        Equal(new IntPtr(IntPtr.Size == 8 ? 8 : 4),
            Marshal.OffsetOf<WindowsMouseOutput.NativeInput>(nameof(WindowsMouseOutput.NativeInput.Data)), "union alignment");
        Equal(new IntPtr(IntPtr.Size == 8 ? 24 : 20),
            Marshal.OffsetOf<WindowsMouseOutput.MouseInput>(nameof(WindowsMouseOutput.MouseInput.ExtraInfo)), "ULONG_PTR alignment");
        int calls = 0;
        uint Send(uint count, ref WindowsMouseOutput.NativeInput input, int size)
        {
            calls++;
            Equal(1U, count, "one immediate event");
            Equal(expectedSize, size, "cbSize");
            Equal(0U, input.Type, "INPUT_MOUSE");
            Equal(-7, input.Data.Mouse.Dx, "signed dx");
            Equal(11, input.Data.Mouse.Dy, "signed dy");
            Equal(1U, input.Data.Mouse.Flags, "MOVE only, no absolute/button/wheel flags");
            Equal(0U, input.Data.Mouse.MouseData, "mouseData");
            Equal(0U, input.Data.Mouse.Time, "system time");
            Equal(UIntPtr.Zero, input.Data.Mouse.ExtraInfo, "extraInfo");
            return 1;
        }
        var mouse = new WindowsMouseOutput(Send, () => throw new Exception("no last error on success"));
        mouse.Move(0, 0);
        mouse.Move(-7, 11);
        Equal(1, calls, "skip zero delta");
        Equal(1L, mouse.SuccessfulEvents, "success accounting");
        Equal(0L, mouse.FailedCalls, "no failures");
    }

    public static void FailedCall()
    {
        foreach (uint inserted in new uint[] { 0, 2 })
        foreach (int error in new[] { 0, 5 })
        {
            int calls = 0;
            uint Send(uint count, ref WindowsMouseOutput.NativeInput input, int size) { calls++; return inserted; }
            var mouse = new WindowsMouseOutput(Send, () => error);
            try { mouse.Move(1, 0); throw new Exception("failure was swallowed"); }
            catch (Win32Exception exception)
            {
                Equal(error, exception.NativeErrorCode, "error captured even when zero");
                Check(exception.Message.Contains($"inserted={inserted}"), "return value diagnostic");
            }
            Equal(1, calls, "failure is not retried");
            Equal(0L, mouse.SuccessfulEvents, "failure not successful");
            Equal(1L, mouse.FailedCalls, "failure count");
        }
    }

    public static void Smoke()
    {
        Check(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000), "Windows 11 required");
        LayoutAndFields();
        var mouse = new WindowsMouseOutput();
        mouse.Move(8, 0);
        mouse.Move(-8, 0);
        Equal(2L, mouse.SuccessfulEvents, "two actual native calls returned 1");
        Equal(0L, mouse.FailedCalls, "native failure count");
        Console.WriteLine($"SENDINPUT_SMOKE inputSize={Marshal.SizeOf<WindowsMouseOutput.NativeInput>()} successfulEvents=2 failedCalls=0 cursorPositionIsNotAnAssertion=true");
    }

    public static async Task AndroidSmoke()
    {
        Check(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000), "Windows 11 required");
        var mouse = new WindowsMouseOutput();
        var motion = new TouchSessionProcessor(mouse.Move);
        using var receiver = new UdpReceiver(new IPEndPoint(IPAddress.Any, UdpReceiver.Port), Console.Out,
            motion: motion, detailedLogging: false);
        // Bounds a test run, not an output ticker or the diagnostic input timeout.
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await receiver.RunAsync(cancellation.Token);
        Check(receiver.Statistics.AcceptedPackets >= 3, "expected Android DOWN/MOVE/UP packets");
        Check(motion.ProcessedMotionSamples > 0 && mouse.SuccessfulEvents > 0, "no successful native movement events");
        Equal(motion.OutputEvents, mouse.SuccessfulEvents, "motion/native output accounting");
        Equal(0L, mouse.FailedCalls, "native output errors");
        Equal(0L, receiver.Statistics.InvalidPackets, "invalid packets");
        Equal(0L, receiver.Statistics.OldPackets, "old packets");
        Equal(0L, receiver.Statistics.SequenceGapEstimate, "sequence gaps");
        Equal(0L, motion.IgnoredSessionPackets, "invalid touch sessions");
        Console.WriteLine($"ANDROID_MOUSE_SMOKE acceptedPackets={receiver.Statistics.AcceptedPackets} " +
            $"acceptedSamples={receiver.Statistics.AcceptedSamples} successfulEvents={mouse.SuccessfulEvents} " +
            $"failedCalls={mouse.FailedCalls} cursorPositionIsNotAnAssertion=true");
    }

    public static void Arguments()
    {
        Equal(new Rightpad.Receiver.Program.Options(false, 7, 7), Rightpad.Receiver.Program.ParseArguments([]), "input option defaults (GUI entry tested separately)");
        Equal(new Rightpad.Receiver.Program.Options(true, 7, 7), Rightpad.Receiver.Program.ParseArguments(["--raw-mouse"]), "raw default");
        Equal(new Rightpad.Receiver.Program.Options(true, 5, 6),
            Rightpad.Receiver.Program.ParseArguments(["--sensitivity-y", "6", "--raw-mouse", "--sensitivity-x", "5"]), "explicit sensitivity overrides defaults");
        foreach (string[] args in new string[][] {
            ["--unknown"], ["--raw-mouse", "--raw-mouse"], ["--sensitivity-x", "1"],
            ["--raw-mouse", "--sensitivity-x"], ["--raw-mouse", "--sensitivity-y", "NaN"],
            ["--raw-mouse", "--sensitivity-y", "0"], ["--raw-mouse", "--sensitivity-x", "-1"] })
            Throws<ArgumentException>(() => Rightpad.Receiver.Program.ParseArguments(args));
    }
}
