using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using static Rightpad.Receiver.Tests.Program;
namespace Rightpad.Receiver.Tests;

internal static class ControlConfigTests
{
    public static readonly (string Name, Func<Task> Run)[] Cases = [
        Sync("legacy settings defaults and per-field fallback", Load),
        Sync("numeric ranges precision and steps", Numeric),
        ("controls explicit Save disk/publish/push/Escape", Save),
        ("controls failed Save retains draft without publish/push", Failure),
        Sync("generic config golden and request codec", Codec),
        Sync("epoch revision changes only for committed controls", Revisions),
        Sync("request admission has no presence or sequence side effects", Admission),
        Sync("lost Save push and reconnect recover through requests", Recovery),
        Sync("disposed config channel unsubscribes", Dispose)
    ];
    static (string, Func<Task>) Sync(string name, Action action) => ("controls " + name, () => { action(); return Task.CompletedTask; });
    static long T(int ms) => ms * Stopwatch.Frequency / 1000;
    static readonly IPAddress Ip = IPAddress.Loopback;
    static readonly RuntimeSettings Changed = RuntimeSettings.Default with { Controls = new() { B = new(1.5, 3, 25, 400) } };
    sealed class Files : IDisposable
    {
        public readonly string Dir = Path.Combine(Path.GetTempPath(), "rightpad-controls-" + Guid.NewGuid().ToString("N"));
        public string PathName => Path.Combine(Dir, "settings.json");
        public Files() => Directory.CreateDirectory(Dir);
        public void Dispose() { foreach (var f in Directory.GetFiles(Dir)) File.Delete(f); Directory.Delete(Dir); }
    }
    static void Load()
    {
        using var f = new Files();
        File.WriteAllText(f.PathName, """{"sensitivityX":9,"sensitivityY":8,"tapMaxDurationMs":300,"tapMovementThresholdPx":8,"clickHoldMs":25}""");
        var value = SettingsFileStore.Load(f.PathName); Equal(VirtualControlsSettings.Default, value.Settings.Controls, "legacy defaults");
        Equal(9d, value.Settings.SensitivityX, "old fields preserved"); Check(value.Warning is null, "no migration required");
        File.WriteAllText(f.PathName, """{"controls":{"b":{"slideUpThresholdDp":1.55,"slideDownThresholdDp":4.2,"tapHoldMs":201,"longPressMs":413}}}""");
        Equal(new SlideControlSettings(.7, 4.2, 25, 413), SettingsFileStore.Load(f.PathName).Settings.Controls.B, "per-field fallback, arbitrary integer long press");
        foreach (string bad in new[] {"null", "[]", "5", "\"bad\""}) {
            File.WriteAllText(f.PathName, "{\"controls\":" + bad + "}");
            Equal(VirtualControlsSettings.Default, SettingsFileStore.Load(f.PathName).Settings.Controls, "invalid object fallback");
        }
    }
    static void Numeric()
    {
        using var f = new Files(); var store = new RuntimeSettingsStore(); var vm = new SettingsViewModel(store, new(f.PathName)); var c = vm.Controls[0];
        foreach (var bad in new[] {"0.09", "50.1", "1.55", "1.50"}) { c.Up.Text = bad; Check(!vm.CanSave, "no silent rounding " + bad); }
        c.Up.Restore(); c.Up.Step(1); Equal("0.8", c.Up.Text, "up step/F1");
        c.Down!.Step(-1); Equal("2.9", c.Down.Text, "down step/F1");
        c.TapHold.Text = "1"; c.TapHold.Step(1); Equal("2", c.TapHold.Text, "hold step");
        c.LongPress.Text = "413"; c.LongPress.Step(1); Equal("423", c.LongPress.Text, "arbitrary integer plus ten");
        c.TapHold.Text = "201"; Check(!vm.CanSave, "hold upper bound"); c.TapHold.Restore();
        c.LongPress.Text = "49"; Check(!vm.CanSave, "long lower bound");
        Equal(RuntimeSettings.Default, store.Current, "draft not published"); Check(!File.Exists(f.PathName), "draft not persisted");
    }
    static async Task Save()
    {
        using var f = new Files(); var store = new RuntimeSettingsStore(); var file = new SettingsFileStore(f.PathName);
        var vm = new SettingsViewModel(store, file); var messages = new List<byte[]>();
        using var channel = new ControlConfigChannel(store, () => new(1, 0, true, Ip.ToString()), (_, b) => messages.Add(b), TextWriter.Null);
        vm.Controls[0].Up.Text = "1.5"; Equal(0, messages.Count, "draft does not push");
        Check(await vm.SaveAsync(), "Save succeeds"); Equal(Changed, store.Current, "publish committed controls");
        Equal(Changed, SettingsFileStore.Load(f.PathName).Settings, "disk roundtrip"); Equal(1, messages.Count, "Save pushes once without restart");
        using var json = JsonDocument.Parse(File.ReadAllText(f.PathName));
        Equal(1.5, json.RootElement.GetProperty("controls").GetProperty("b").GetProperty("slideUpThresholdDp").GetDouble(), "nested JSON");
        vm.Controls[0].Up.Text = "2.0"; vm.Controls[0].Up.Restore(); Equal("1.5", vm.Controls[0].Up.Text, "Escape restores last saved");
        Check(!vm.CanSave, "restored draft clean"); await file.FlushAsync();
    }
    static async Task Failure()
    {
        using var f = new Files(); var store = new RuntimeSettingsStore(); var file = new SettingsFileStore(f.Dir);
        var vm = new SettingsViewModel(store, file); int sent = 0;
        using var channel = new ControlConfigChannel(store, () => new(1, 0, true, Ip.ToString()), (_, _) => sent++, TextWriter.Null);
        vm.Controls[0].Up.Text = "1.5"; Check(!await vm.SaveAsync(), "directory target causes real disk failure");
        Equal(RuntimeSettings.Default, store.Current, "failed Save cannot publish"); Equal(0, sent, "cannot change Android");
        Equal(1UL, channel.Revision, "no revision on failed Save"); Equal("1.5", vm.Controls[0].Up.Text, "draft preserved");
        Check(vm.CanSave, "retry available"); await file.FlushAsync(); Equal(0, sent, "Flush cannot publish failed draft");
    }
    internal static byte[] Request(ulong run, ulong epoch = 0, ulong revision = 0)
    {
        byte[] b = new byte[26]; b[0] = 2; b[1] = 6;
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(2), run); BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(10), epoch);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(18), revision); return b;
    }
    static void Codec()
    {
        var b = ControlConfigProtocol.Encode(0x0807060504030201, 0x1817161514131211, 0x2827262524232221, VirtualControlsSettings.Default);
        Equal("5250435402010000010203040506070811121314151617182122232425262728020000000100010C07001E00190090010200020E78001E00140019009001", Convert.ToHexString(b), "shared Android v2 golden");
        Check(ControlConfigProtocol.TryDecodeRequest(Request(7, 8, 9), out var r) && r == new ControlConfigRequest(7, 8, 9), "request fields");
        for(int length = 0; length < 26; length++) Check(!ControlConfigProtocol.TryDecodeRequest(Request(7).AsSpan(0, length), out _), "short request");
        Check(!ControlConfigProtocol.TryDecodeRequest(new byte[27], out _), "trailing request");
        Check(!ControlConfigProtocol.TryDecodeRequest(Request(7, 0, 1), out _), "inconsistent known config");
    }
    static void Revisions()
    {
        var store = new RuntimeSettingsStore();
        using var c = new ControlConfigChannel(store, () => new(), (_, _) => { }, TextWriter.Null);
        using var d = new ControlConfigChannel(store, () => new(), (_, _) => { }, TextWriter.Null);
        Check(c.Epoch != 0 && c.Epoch != d.Epoch, "new runtime epoch");
        store.Publish(RuntimeSettings.Default with { SensitivityX = 8 }); Equal(1UL, c.Revision, "non-controls publish no new revision");
        store.Publish(Changed); Equal(2UL, c.Revision, "changed Controls committed"); store.Publish(Changed); Equal(2UL, c.Revision, "unchanged idempotent");
    }
    static void Admission()
    {
        var sent = new List<byte[]>(); using var r = new UdpReceiver(new(Ip, 0), TextWriter.Null,
            settings: new RuntimeSettingsStore(), controlSend: (_, b) => sent.Add(b));
        var remote = new IPEndPoint(Ip, 5555);
        r.ProcessDatagram(Request(1), remote, T(0)); Check(!r.Presence.Connected && sent.Count == 0, "request cannot admit");
        r.ProcessDatagram(PresenceTests.Heartbeat(1), remote, T(1));
        r.ProcessDatagram(Request(2), remote, T(2)); r.ProcessDatagram(Request(1), new(IPAddress.Parse("127.0.0.2"), 5555), T(3));
        Equal(0, sent.Count, "invalid run/source rejected"); r.ProcessDatagram(Request(1), remote, T(4)); Equal(1, sent.Count, "current admitted");
        r.ProcessDatagram(Request(1), remote, T(5)); Equal(1, sent.Count, "requests throttled");
        Equal(0L, r.Statistics.AcceptedPackets, "no Touch samples"); Equal(T(1), r.Presence.LastSeenAtTicks, "no presence renewal");
        r.CheckTimeouts(T(2002)); Check(!r.Presence.Connected, "request cannot keep presence alive");
    }
    static void Recovery()
    {
        var store = new RuntimeSettingsStore(); var p = new SenderPresence(1, 0, true, Ip.ToString());
        var delivered = new List<byte[]>(); bool drop = true;
        using var c = new ControlConfigChannel(store, () => p, (_, b) => { if (!drop) delivered.Add(b); }, TextWriter.Null);
        store.Publish(Changed); Equal(0, delivered.Count, "Save push lost"); drop = false;
        c.Request(new(1, c.Epoch, 1), Ip, T(1000)); Equal(2UL, BinaryPrimitives.ReadUInt64LittleEndian(delivered[0].AsSpan(24)), "request recovers lost push without Save");
        p = new(2, 0, true, Ip.ToString()); c.Request(new(2, 0, 0), Ip, T(2000));
        Equal(2UL, BinaryPrimitives.ReadUInt64LittleEndian(delivered[1].AsSpan(8)), "recreated sender gets current config");
        Equal(15, (int)BinaryPrimitives.ReadUInt16LittleEndian(delivered[1].AsSpan(40)), "complete latest record");
    }
    static void Dispose()
    {
        var store = new RuntimeSettingsStore(); int sent = 0; var c = new ControlConfigChannel(store, () => new(1,0,true,Ip.ToString()), (_,_) => sent++, TextWriter.Null);
        c.Dispose(); store.Publish(Changed); Equal(0, sent, "old runtime unsubscribed");
    }
}
