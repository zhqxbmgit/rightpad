using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class StatusTests
{
    public static void Codec()
    {
        var value = new StatusSnapshot(0x0807060504030201, 0x1817161514131211,
            0x2827262524232221, ReceiverState.Running, (StatusFlags)3);
        byte[] golden = Convert.FromHexString("525053540101000001020304050607081112131415161718212223242526272802030000");
        Equal(36, golden.Length, "fixed packet size");
        Check(StatusProtocol.Encode(value).SequenceEqual(golden), "shared Java/C# golden");
        Check(StatusProtocol.TryDecode(golden, out var decoded), "decode golden");
        Equal(value, decoded, "golden fields");
        for (int length = 0; length < 36; length++)
            Check(!StatusProtocol.TryDecode(golden.AsSpan(0, length), out _), "truncation");
        Check(!StatusProtocol.TryDecode([.. golden, 0], out _), "trailing bytes");
        foreach (int offset in new[] {0,1,2,3,4,5,6,7,32,33,34,35})
        {
            byte[] bad = (byte[])golden.Clone(); bad[offset] = 255;
            Check(!StatusProtocol.TryDecode(bad, out _), $"strict field {offset}");
        }
        foreach (ulong run in new[] {0UL, ulong.MaxValue})
        {
            var max = value with { SenderRunId = run, ConfigEpoch = ulong.MaxValue, ConfigRevision = ulong.MaxValue };
            Check(StatusProtocol.TryDecode(StatusProtocol.Encode(max), out decoded), "unsigned max");
            Equal(max, decoded, "unsigned bit preservation");
        }
    }

    private static RuntimeStatsSnapshot Running(long now) => new(1, ReceiverState.Running,
        Presence: new(7, now, true, "127.0.0.1"), GamepadAvailable: true, MouseAvailable: true);

    public static void Health()
    {
        long now = Stopwatch.GetTimestamp();
        var state = Running(now);
        StatusFlags Flags(RuntimeStatsSnapshot s) => StatusProtocol.Capture(s, 1, 2, now)!.Snapshot.Flags;
        Equal((StatusFlags)3, Flags(state), "both backends healthy with zero outputs");
        Check((Flags(state with { MouseAvailable = false }) & StatusFlags.MouseInputHealthy) == 0, "diagnostic/no mouse never healthy");
        Check((Flags(state with { MouseOutputFailures = 1 }) & StatusFlags.MouseOutputFailurePresent) != 0, "mouse failure");
        Check((Flags(state with { LastError = "fatal" }) & StatusFlags.MouseInputHealthy) == 0, "runtime fatal clears mouse healthy");
        Check((Flags(state with { RuntimeState = ReceiverState.Error }) & StatusFlags.RuntimeErrorPresent) != 0, "runtime error");
        Check((Flags(state with { GamepadAvailable = false, LastGamepadError = "create" }) & StatusFlags.GamepadFailurePresent) != 0, "gamepad create failure");
        Check((Flags(state with { GamepadFailures = 1 }) & StatusFlags.GamepadFailurePresent) != 0, "gamepad output failure");
    }

    public static void Presence()
    {
        long now = Stopwatch.GetTimestamp();
        var state = Running(now);
        Check(StatusProtocol.Capture(state with { Presence = null }, 1, 1, now) is null, "no sender");
        Check(StatusProtocol.Capture(state with { Presence = new(7, now, false, "127.0.0.1") }, 1, 1, now) is null, "disconnected");
        Check(StatusProtocol.Capture(state, 1, 1, now + Stopwatch.Frequency * 2) is null, "expired presence even before maintenance");
        Equal(7UL, StatusProtocol.Capture(state, 1, 1, now)!.Snapshot.SenderRunId, "current run");
        var changed = state with { Presence = new(8, now, true, "127.0.0.2") };
        var delivery = StatusProtocol.Capture(changed, 2, 3, now)!;
        Equal(8UL, delivery.Snapshot.SenderRunId, "new run");
        Equal("127.0.0.2", delivery.Address.ToString(), "new route");
        Equal(new SenderPresence(7, now, true, "127.0.0.1"), state.Presence!, "observation cannot renew presence");
    }

    public static async Task Worker()
    {
        var messages = new ConcurrentQueue<byte[]>();
        StatusDelivery? current = null;
        await using (var worker = new HapticFeedbackSender(TextWriter.Null, sendForTest: (bytes, target, token) =>
        { Equal(50002, target.Port, "existing reverse port"); messages.Enqueue(bytes); return ValueTask.CompletedTask; }))
        {
            worker.SetStatusSource(() => Volatile.Read(ref current));
            await Task.Delay(550);
            Equal(0, messages.Count, "no active sender no status");
            current = new(IPAddress.Loopback, new(7, 1, 1, ReceiverState.Running, (StatusFlags)3));
            await RuntimeTests.Until(() => messages.Count >= 1);
            var first = messages.Count;
            await Task.Delay(100);
            Equal(first, messages.Count, "not per-frame refresh");
            current = new(IPAddress.Loopback, new(8, 1, 2, ReceiverState.Running, (StatusFlags)3));
            await RuntimeTests.Until(() => messages.Any(b => StatusProtocol.TryDecode(b, out var s) && s.SenderRunId == 8 && s.ConfigRevision == 2));
            current = null;
            int stopped = messages.Count;
            await Task.Delay(550);
            Equal(stopped, messages.Count, "no historical route after disconnect");
            worker.TryEnqueue(IPAddress.Loopback, new(8, 4, 5));
            worker.TryEnqueueConfig(IPAddress.Loopback, ControlConfigProtocol.Encode(8, 1, 2, VirtualControlsSettings.Default));
            await RuntimeTests.Until(() => messages.Count == stopped + 2);
            Check(messages.Any(b => b.Length == 24 && b[3] == 'F'), "RPHF unchanged");
            Check(messages.Any(b => b.Length == 62 && b[3] == 'T' && b[2] == 'C'), "RPCT v2 unchanged");
        }
        int final = messages.Count;
        await Task.Delay(550);
        Equal(final, messages.Count, "disposed worker stops periodic refresh");
    }
}
