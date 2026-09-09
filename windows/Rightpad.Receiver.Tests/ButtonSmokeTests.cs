using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using Rightpad.Receiver;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class ButtonSmokeTests
{
    // Exercise the already-running GUI's UDP/SendInput path. This owns only a
    // temporary inert click target, never a second Receiver or UDP listener.
    public static async Task GuiAndroid()
    {
        using var target = new ClickTarget();
        target.CheckPointer();
        async Task Adb(params string[] arguments)
        {
            var start = new ProcessStartInfo("adb") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("Could not start adb.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            catch { if (!process.HasExited) process.Kill(); throw; }
            Check(process.ExitCode == 0, $"adb failed: {await stderr}");
            Console.Write(await stdout);
        }
        await Adb("shell", "input", "swipe", "600", "1000", "610", "1000", "250");
        await Task.Delay(200);
        target.CheckPointer();
        await Adb("shell", "input", "tap", "600", "1000");
        await Task.Delay(200);
        Equal(1, target.InjectedDowns, "GUI emitted exactly one native LEFT DOWN");
        Equal(1, target.InjectedUps, "GUI emitted exactly one native LEFT UP");
        Check(target.InjectedMoves > 0, "GUI emitted native movement");
        Console.WriteLine($"GUI_ANDROID_E2E smallSwipe=10px tap=1 nativeMoves={target.InjectedMoves} nativeDowns={target.InjectedDowns} nativeUps={target.InjectedUps} target=inert");
    }

    // These entry points inject real buttons only when explicitly requested.
    public static async Task Run(bool android)
    {
        Check(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000), "Windows 11 required");
        using var target = new ClickTarget();
        var mouse = new WindowsMouseOutput();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        long downAt = 0, upAt = 0;
        using var button = new LeftButtonController(() =>
        {
            target.CheckPointer();
            mouse.LeftDown(); downAt = Stopwatch.GetTimestamp();
        }, () =>
        {
            mouse.LeftUp(); upAt = Stopwatch.GetTimestamp(); released.TrySetResult();
        }, Console.Error.WriteLine, cancel.Cancel);
        if (android)
        {
            var gesture = new GestureProcessor(button.Click);
            var motion = new TouchSessionProcessor(mouse.Move, 7, 7);
            using var receiver = new UdpReceiver(new IPEndPoint(IPAddress.Any, UdpReceiver.Port), Console.Out,
                motion: motion, detailedLogging: false, gesture: gesture);
            Task running = receiver.RunAsync(cancel.Token);
            Console.WriteLine("ANDROID_TAP_READY sensitivityX=7 sensitivityY=7 tapMaxDurationMs=300 tapMovementThresholdPx=8 clickHoldMs=25");
            try
            {
                await released.Task.WaitAsync(cancel.Token);
                await Task.Delay(100, cancel.Token); // Allow identical UP copies to reach the sequence gate.
            }
            finally { cancel.Cancel(); await running; button.Dispose(); }
            Equal(1L, gesture.ClicksTriggered, "one Android tap");
            Equal(2L, receiver.Statistics.AcceptedPackets, "Android DOWN and UP");
            Equal(2L, receiver.Statistics.AcceptedSamples, "Android samples");
            Equal(0L, receiver.Statistics.SequenceGapEstimate, "sequence gaps");
            Equal(0L, receiver.Statistics.OldPackets + receiver.Statistics.InvalidPackets, "packet errors");
            Console.WriteLine($"ANDROID_TAP_E2E clicksTriggered={gesture.ClicksTriggered} acceptedPackets={receiver.Statistics.AcceptedPackets} duplicatePackets={receiver.Statistics.DuplicatePackets}");
        }
        else
        {
            button.Click();
            Check(!released.Task.IsCompleted, "native UP is asynchronous");
            await released.Task.WaitAsync(cancel.Token);
            button.Dispose();
        }
        Check(button.Failure is null, "button controller failed");
        Equal(1L, mouse.LeftDownSuccess, "native LEFT DOWN returned 1");
        Equal(1L, mouse.LeftUpSuccess, "native LEFT UP returned 1");
        Equal(0L, mouse.LeftButtonFailures, "native button failures");
        double hold = Stopwatch.GetElapsedTime(downAt, upAt).TotalMilliseconds;
        Check(hold >= 15 && hold < 3000, "native local hold timing");
        Console.WriteLine($"BUTTON_SMOKE leftDownSuccess=1 leftUpSuccess=1 leftButtonFailures=0 holdMs={hold:F2} cursorPixelsNotAsserted=true");
    }

    // Test-only inert window at the existing cursor location; no cursor movement.
    private sealed class ClickTarget : IDisposable
    {
        private readonly Thread thread;
        private readonly nint window;
        private uint threadId;
        private int injectedDowns, injectedUps, injectedMoves;
        public int InjectedDowns => Volatile.Read(ref injectedDowns);
        public int InjectedUps => Volatile.Read(ref injectedUps);
        public int InjectedMoves => Volatile.Read(ref injectedMoves);
        private readonly HookProc hookProc;
        public ClickTarget()
        {
            hookProc = (code, message, data) =>
            {
                if (code >= 0 && (Marshal.PtrToStructure<MouseHook>(data).Flags & 1) != 0)
                {
                    if (message == 0x0201) Interlocked.Increment(ref injectedDowns);
                    if (message == 0x0202) Interlocked.Increment(ref injectedUps);
                    if (message == 0x0200) Interlocked.Increment(ref injectedMoves);
                }
                return CallNextHookEx(0, code, message, data);
            };
            var ready = new TaskCompletionSource<nint>(TaskCreationOptions.RunContinuationsAsynchronously);
            thread = new Thread(() =>
            {
                nint handle = 0;
                nint hook = 0;
                try
                {
                    threadId = GetCurrentThreadId();
                    if (!GetCursorPos(out var p)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    handle = CreateWindowExW(0x08000088, "STATIC", "rightpad button smoke target",
                        0x90000000, p.X - 150, p.Y - 80, 300, 160, 0, 0, 0, 0);
                    if (handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                    hook = SetWindowsHookExW(14, hookProc, GetModuleHandleW(null), 0);
                    if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                    ready.TrySetResult(handle);
                    while (GetMessageW(out var msg, 0, 0, 0) > 0)
                    {
                        TranslateMessage(ref msg);
                        DispatchMessageW(ref msg);
                    }
                }
                catch (Exception e) { ready.TrySetException(e); }
                finally { if (hook != 0) UnhookWindowsHookEx(hook); if (handle != 0) DestroyWindow(handle); }
            }) { IsBackground = true, Name = "RightpadSmokeTarget" };
            thread.Start();
            window = ready.Task.GetAwaiter().GetResult();
        }
        public void CheckPointer()
        {
            Check(GetCursorPos(out var p) && WindowFromPoint(p) == window, "cursor must be over inert smoke target");
        }
        public void Dispose()
        {
            PostThreadMessageW(threadId, 0x0012, 0, 0);
            thread.Join(TimeSpan.FromSeconds(5));
        }
        [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct MouseHook { public Point Point; public uint MouseData, Flags, Time; public nuint ExtraInfo; }
        private delegate nint HookProc(int code, nuint message, nint data);
        [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookExW(int id, HookProc callback, nint module, uint threadId);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
        [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nuint message, nint data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? name);
        [StructLayout(LayoutKind.Sequential)] private struct Msg
        {
            public nint Hwnd; public uint Message; public nuint WParam; public nint LParam;
            public uint Time; public Point Point; public uint Private;
        }
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern nint CreateWindowExW(uint ex, string cls, string text, uint style,
            int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
        [DllImport("user32.dll", ExactSpelling = true)] private static extern int GetMessageW(out Msg message, nint window, uint min, uint max);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg message);
        [DllImport("user32.dll", ExactSpelling = true)] private static extern nint DispatchMessageW(ref Msg message);
        [DllImport("user32.dll", ExactSpelling = true)] private static extern bool PostThreadMessageW(uint threadId, uint msg, nuint wParam, nint lParam);
    }
}
