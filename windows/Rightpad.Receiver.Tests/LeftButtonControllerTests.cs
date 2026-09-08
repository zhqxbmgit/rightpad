using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Rightpad.Receiver;
using static Rightpad.Receiver.Tests.Program;
using static Rightpad.Receiver.Tests.RawMotionProcessorTests;

namespace Rightpad.Receiver.Tests;

internal static class LeftButtonControllerTests
{
    public static void NativeFields()
    {
        var flags = new List<uint>();
        var mouse = new WindowsMouseOutput((uint count, ref WindowsMouseOutput.NativeInput input, int size) =>
        {
            Equal(1u, count, "single button event");
            Equal(Marshal.SizeOf<WindowsMouseOutput.NativeInput>(), size, "INPUT size");
            Equal(0u, input.Type, "INPUT_MOUSE");
            var m = input.Data.Mouse;
            Equal(0, m.Dx, "button dx"); Equal(0, m.Dy, "button dy");
            Equal(0u, m.MouseData, "button data"); Equal(0u, m.Time, "system timestamp");
            Equal(UIntPtr.Zero, m.ExtraInfo, "extra info");
            flags.Add(m.Flags);
            return 1;
        }, () => 0);
        mouse.LeftDown(); mouse.LeftUp();
        Check(flags.SequenceEqual(new uint[] { 2, 4 }), "LEFTDOWN then LEFTUP only");
        Equal(1L, mouse.LeftDownSuccess, "down stats"); Equal(1L, mouse.LeftUpSuccess, "up stats");
        Equal(0L, mouse.SuccessfulEvents, "movement counters unchanged");
        foreach (uint result in new uint[] { 0, 2 })
        {
            var fail = new WindowsMouseOutput((uint n, ref WindowsMouseOutput.NativeInput i, int s) => result, () => 5);
            foreach (Action call in new Action[] { fail.LeftDown, fail.LeftUp })
            {
                try { call(); throw new InvalidOperationException("missing failure"); }
                catch (Win32Exception e) { Equal(5, e.NativeErrorCode, "button last error"); }
            }
            Equal(2L, fail.LeftButtonFailures, "button failure stats");
            Equal(0L, fail.LeftDownSuccess + fail.LeftUpSuccess, "failed calls not successful");
        }
    }

    public static async Task Timing()
    {
        var clock = Stopwatch.StartNew();
        var released = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
        double downAt = -1;
        using var button = new LeftButtonController(() => downAt = clock.Elapsed.TotalMilliseconds,
            () => released.TrySetResult(clock.Elapsed.TotalMilliseconds), _ => { }, () => { }, 120);
        button.Click();
        Check(downAt >= 0, "DOWN is immediate");
        Check(!released.Task.IsCompleted, "UP must not be synchronous");
        double upAt = await released.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(upAt - downAt >= 90 && upAt - downAt < 3000, "timer roughly honors hold without exact timing assertion");
        Console.WriteLine($"BUTTON_TIMING requestedMs=120 observedMs={upAt - downAt:F2}");
    }

    public static async Task Cleanup()
    {
        int downs = 0, ups = 0;
        var button = new LeftButtonController(() => downs++, () => ups++, _ => { }, () => { }, 200);
        button.Click(); button.Dispose(); button.Dispose();
        Equal(1, downs, "one down"); Equal(1, ups, "Dispose releases held button");
        await Task.Delay(260);
        Equal(1, ups, "no stale timer UP after Dispose");
        Throws<ObjectDisposedException>(button.Click);
    }

    public static async Task Overlap()
    {
        var events = new List<string>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int ups = 0;
        using var button = new LeftButtonController(() => events.Add("D"), () =>
        {
            events.Add("U"); if (++ups == 3) done.TrySetResult();
        }, _ => { }, () => { }, 40);
        button.Click(); button.Click(); button.Click();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        button.Dispose();
        Check(events.SequenceEqual(new[] { "D", "U", "D", "U", "D", "U" }), "overlapping taps serialized");
    }

    public static async Task Failures()
    {
        var logs = new List<string>();
        int ups = 0, wakeups = 0;
        using (var button = new LeftButtonController(() => throw new Win32Exception(5), () => ups++, logs.Add, () => wakeups++))
        {
            Throws<Win32Exception>(button.Click);
            Check(button.Failure is not null, "DOWN failure recorded");
        }
        Equal(1, ups, "failed DOWN gets best-effort UP"); Equal(1, wakeups, "failure wakes receiver");
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ups = 0;
        using (var button = new LeftButtonController(() => { }, () =>
        {
            if (++ups < 3) throw new Win32Exception(5);
        }, logs.Add, () => failed.TrySetResult(), 20))
        {
            button.Click();
            await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            button.Dispose();
            Equal(3, ups, "timer failure, best-effort failure, then Dispose release");
            Check(button.Failure is not null, "async failure remains visible after recovery");
        }
        Check(logs.Any(s => s.StartsWith("left_button_error:")) &&
              logs.Any(s => s.StartsWith("left_button_cleanup_error:")), "all failures logged");
    }
}
