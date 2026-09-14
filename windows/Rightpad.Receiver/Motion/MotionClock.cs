using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Rightpad.Receiver;

internal sealed class MotionClock : IDisposable
{
    private readonly ResampledMotion motion;
    private readonly AutoResetEvent changed = new(false);
    private readonly ManualResetEvent stop = new(false);
    private readonly EventWaitHandle timer = new(false, EventResetMode.AutoReset);
    private readonly Thread thread;
    private Exception? failure;
    public Exception? Failure => Volatile.Read(ref failure);

    public MotionClock(ResampledMotion motion, Action onFailure)
    {
        this.motion = motion;
        var handle = CreateWaitableTimerExW(0, null, 2, 0x1F0003);
        if (handle.IsInvalid) { handle.Dispose(); timer.Dispose(); changed.Dispose(); stop.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        timer.SafeWaitHandle.Dispose();
        timer.SafeWaitHandle = handle;
        motion.Changed = () => changed.Set();
        thread = new(() =>
        {
            try
            {
                WaitHandle[] waits = [stop, changed, timer];
                while (true)
                {
                    var schedule = motion.Schedule;
                    if (schedule.Deadline is long deadline)
                    {
                        long remaining = deadline - Stopwatch.GetTimestamp();
                        if (remaining <= 0)
                        {
                            if (stop.WaitOne(0)) break;
                            motion.Tick(Stopwatch.GetTimestamp(), schedule.Generation);
                            continue;
                        }
                        long due = -Math.Max(1, (long)Math.Ceiling(remaining * (10_000_000.0 / Stopwatch.Frequency)));
                        if (!SetWaitableTimer(handle, ref due, 0, 0, 0, false)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                    else CancelWaitableTimer(handle);
                    int signaled = WaitHandle.WaitAny(waits);
                    if (signaled == 0) break;
                    if (signaled == 2) motion.Tick(Stopwatch.GetTimestamp(), schedule.Generation);
                }
            }
            catch (Exception e) { Volatile.Write(ref failure, e); motion.Reset(); onFailure(); }
        }) { IsBackground = true, Name = "rightpad motion 250Hz" };
        thread.Start();
    }
    public void Dispose()
    {
        stop.Set(); thread.Join();
        motion.Changed = null; motion.Reset();
        timer.Dispose(); changed.Dispose(); stop.Dispose();
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeWaitHandle CreateWaitableTimerExW(nint attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long due, int period, nint callback, nint argument, [MarshalAs(UnmanagedType.Bool)] bool resume);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelWaitableTimer(SafeWaitHandle timer);
}
