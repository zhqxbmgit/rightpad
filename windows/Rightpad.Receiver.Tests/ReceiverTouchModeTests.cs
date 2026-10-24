using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Xml.Linq;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class ReceiverTouchModeTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> Cases =>
    [
        ("Receiver mode defaults / strict fallback / settings preservation", Schema),
        ("Receiver mode disk-first GUI Save / failure / unsaved draft isolation", Save),
        ("Receiver GUI M/C actual runtime / restart / type7 / reconnect / device lifetime", Runtime),
        ("Receiver GUI touching and release Pending/Active truth", Pending),
        ("Receiver M/C shared raw Touch produces native Mouse / X-B full state unchanged", InputAndXbox),
        ("Receiver mode safe M-R1 to C-Z1 exact old chain / fresh state", () => Boundary(TouchMode.M_R1)),
        ("Receiver mode safe C-Z1 to M-R1 exact glide / fresh state", () => Boundary(TouchMode.C_Z1)),
        ("Receiver mode GUI binding / Selected-Pending-Active contract", Markup)
    ];

    private sealed class Files : IDisposable
    {
        public readonly string DirectoryPath = Path.Combine(Path.GetTempPath(), "rightpad-local-mode-" + Guid.NewGuid().ToString("N"));
        public string PathName => Path.Combine(DirectoryPath, "settings.json");
        public Files() => Directory.CreateDirectory(DirectoryPath);
        public void Dispose() => Directory.Delete(DirectoryPath, true);
    }
    private static Task Schema()
    {
        using var files = new Files();
        Equal(TouchMode.M_R1, SettingsFileStore.Load(files.PathName).Settings.TouchMode, "first launch M-R1");
        foreach (string value in new[] { "\"M_F1\"", "\"unknown\"", "1", "null", "true", "\"C\"", "{}" })
        {
            File.WriteAllText(files.PathName, "{\"sensitivityX\":6,\"sensitivityY\":5,\"tapMaxDurationMs\":300,\"tapMovementThresholdPx\":8,\"clickHoldMs\":25,\"smoothingTauMs\":13,\"smoothingSupportMs\":85,\"touchMode\":" + value + "}");
            var loaded = SettingsFileStore.Load(files.PathName);
            Equal(TouchMode.M_R1, loaded.Settings.TouchMode, "invalid mode safely falls back to direct M-R1");
            Check(loaded.Warning is not null, "invalid mode diagnostic");
            Equal((6.0, 5.0, 13, 85), (loaded.Settings.SensitivityX, loaded.Settings.SensitivityY, loaded.Settings.SmoothingTauMs, loaded.Settings.SmoothingSupportMs), "other saved values retained");
        }
        Check(!(RuntimeSettings.Default with { TouchMode = (TouchMode)99 }).IsProductValid, "only two product modes");
        Equal(MotionMode.M_R1, Receiver.Program.NormalGuiMotionMode, "no historical ProductionMode reuse");
        return Task.CompletedTask;
    }
    private static async Task Save()
    {
        using var files = new Files();
        var initial = RuntimeSettings.Default with { SensitivityX = 6, SmoothingTauMs = 13, SmoothingSupportMs = 85 };
        var store = new RuntimeSettingsStore(initial);
        var file = new SettingsFileStore(files.PathName);
        var vm = new SettingsViewModel(store, file);
        vm.SensitivityX.Text = "8";
        foreach (var mode in new[] { TouchMode.C_Z1, TouchMode.M_R1 })
        {
            bool sawDurable = false;
            void Published() => sawDurable = SettingsFileStore.Load(files.PathName).Settings == store.Current;
            store.Published += Published;
            Check(await vm.SelectTouchModeAsync(mode), "direct mode click saves");
            store.Published -= Published;
            Check(sawDurable, "disk success precedes publication");
            Equal(initial with { TouchMode = mode }, store.Current, "mode changes only committed mode");
            Equal(mode, SettingsFileStore.Load(files.PathName).Settings.TouchMode, "restart restores last selected");
            Equal(8.0, vm.DraftSettings.SensitivityX, "other draft retained without applying");
            Check(vm.HasUnsavedChanges && vm.CanSave, "unrelated draft still dirty");
            using var json = JsonDocument.Parse(File.ReadAllText(files.PathName));
            Equal(mode.ToString(), json.RootElement.GetProperty("touchMode").GetString(), "explicit M_R1/C_Z1 JSON identity");
        }
        Check(await vm.SaveAsync(), "subsequent ordinary Settings Save");
        Equal(TouchMode.M_R1, store.Current.TouchMode, "ordinary Save cannot revert selected mode");
        Equal(8.0, store.Current.SensitivityX, "only explicit Save commits other draft");
        await file.FlushAsync();
        string blocked = Path.Combine(files.DirectoryPath, "blocked"); Directory.CreateDirectory(blocked);
        var badFile = new SettingsFileStore(blocked);
        var badVm = new SettingsViewModel(store, badFile);
        Check(!await badVm.SelectTouchModeAsync(TouchMode.C_Z1), "failed mode Save reported");
        Equal(TouchMode.M_R1, store.Current.TouchMode, "failed Save cannot select or apply mode");
        Equal(TouchMode.M_R1, badVm.CommittedSettings.TouchMode, "failed GUI remains truthful");
        Check(badVm.Notice.Length > 0, "visible save error");
        Check(!await badVm.SelectTouchModeAsync((TouchMode)123), "illegal GUI mode rejected");
        await badFile.FlushAsync();
    }
    private sealed class StartupMemory : IStartupValueStore
    {
        public string? Read(string name) => null;
        public void Write(string name, string command) => throw new InvalidOperationException("no startup mutation in mode tests");
        public void Delete(string name) => throw new InvalidOperationException("no startup mutation in mode tests");
    }
    private sealed class FakePad : IVirtualGamepad
    {
        public readonly ConcurrentQueue<XboxGamepadState> States = new();
        public string BackendName => "fake-xbox";
        public string DeviceIdentity => "fake-local-mode";
        public bool Available => true;
        public string? LastError => null;
        public GamepadOutputStats Stats => new(States.Count, 0);
        public bool SetState(XboxGamepadState state) { States.Enqueue(state); return true; }
        public void Dispose() { }
    }
    private sealed class Harness : IAsyncDisposable
    {
        public readonly Files Files = new();
        public readonly RuntimeSettingsStore Store;
        public readonly SettingsFileStore File;
        public readonly ReceiverRuntime R;
        public readonly MainViewModel Gui;
        public readonly UdpClient Sender = new();
        public readonly FakePad Pad = new();
        public readonly VirtualHidTests.FakeNative Mouse = new();
        public int MouseCreates, PadCreates;
        public Harness(TouchMode mode)
        {
            Store = new(RuntimeSettings.Default with { TouchMode = mode, SensitivityX = 1, SensitivityY = 1 });
            File = new(Files.PathName);
            R = new(Store, TextWriter.Null, MouseBackend.VirtualHid, new(IPAddress.Loopback, 0),
                () => { MouseCreates++; return new LibVirtualHidMouseOutput(Mouse); }, motionMode: MotionMode.M_R1,
                useProductMotionSettings: true, gamepadFactory: () => { PadCreates++; return Pad; });
            Gui = new(R, new(Store, File), new(new(new StartupMemory(), @"C:\rightpad\fake.exe")));
        }
        public async Task Send(byte[] bytes) => await Sender.SendAsync(bytes, R.LocalEndpoint!);
        public async Task Profile(ulong run, uint seq, MotionProfile profile)
        {
            byte[] bytes = new byte[16]; bytes[0] = 2; bytes[1] = 7;
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(2), run);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(10), seq); bytes[14] = (byte)profile;
            await Send(bytes);
        }
        public async ValueTask DisposeAsync()
        {
            await R.StopAsync(); await File.FlushAsync(); Sender.Dispose(); Files.Dispose();
        }
    }
    private static void Actual(Harness h, TouchMode mode)
    {
        h.Gui.Refresh(); var s = h.R.CaptureSnapshot();
        Equal(mode, s.SelectedTouchMode, "local selection"); Equal((TouchMode?)mode, s.ActiveTouchMode, "actual runtime mode");
        Check(s.PendingTouchMode is null, "idle has no pending request");
        Equal(TouchModes.Profile(mode), s.ActiveMotionProfile, "actual engine profile");
        Check(h.Gui.SelectedTouchModeText.Contains(mode == TouchMode.C_Z1 ? "C-Z1" : "M-R1")
            && h.Gui.ActiveTouchModeText.Contains(mode == TouchMode.C_Z1 ? "C-Z1" : "M-R1"), "GUI projection matches runtime");
        if (mode == TouchMode.C_Z1) { Equal(35, s.ActiveMotionTauMs, "real C servo tau"); Equal("250 Hz", s.NativeOutputCadence, "real C step cadence"); }
        else { Equal(MotionModes.Mr1Algorithm, s.ActiveMotionAlgorithm, "real direct M-R1 algorithm"); Equal("1000 Hz", s.NativeOutputCadence, "real M opportunities"); }
    }
    private static async Task Runtime()
    {
        await using var h = new Harness(TouchMode.M_R1); await h.Gui.StartAsync();
        foreach (var mode in new[] { TouchMode.C_Z1, TouchMode.M_R1 })
        {
            int mouseCreates = h.MouseCreates, padCreates = h.PadCreates;
            Check(await h.Gui.SelectTouchModeAsync(mode), "GUI mode click"); Actual(h, mode);
            await h.Send(PresenceTests.Heartbeat(1)); await RuntimeTests.Until(() => h.R.CaptureSnapshot().Presence?.Connected == true);
            for (uint seq = 0; seq < 4; seq++) await h.Profile(1, seq, mode == TouchMode.C_Z1 ? MotionProfile.Normal : MotionProfile.Cinematic);
            // Wait for a later Touch packet on the same receive loop; this proves all prior type7 packets were handled.
            long accepted = h.R.CaptureSnapshot().AcceptedPackets;
            await h.Send(PresenceTests.Touch(1, TouchEventType.Down, 0));
            await h.Send(PresenceTests.Touch(1, TouchEventType.Up, 1, 0, 1_000_000));
            await RuntimeTests.Until(() => h.R.CaptureSnapshot().AcceptedPackets >= accepted + 2 && h.R.CaptureSnapshot().ActiveTouchSessionId < 0);
            await Task.Delay(35); Actual(h, mode);
            Check(h.R.CaptureSnapshot().AndroidMotionProfile != TouchModes.Profile(mode), "legacy profile parsed but has no authority");
            foreach (ulong run in new ulong[] { 2, 3 })
            {
                await h.Send(PresenceTests.Heartbeat(run)); await h.Profile(run, 0, MotionProfile.Normal);
                await RuntimeTests.Until(() => h.R.CaptureSnapshot().Presence?.RunId == run);
                await Task.Delay(15); Actual(h, mode);
            }
            Equal(mouseCreates, h.MouseCreates, "mode click never recreates mouse"); Equal(padCreates, h.PadCreates, "mode click never recreates Xbox");
            await h.R.StopAsync();
            Equal(mode, SettingsFileStore.Load(h.Files.PathName).Settings.TouchMode, "persistent restart selection");
            await using var restarted = new Harness(SettingsFileStore.Load(h.Files.PathName).Settings.TouchMode);
            await restarted.Gui.StartAsync(); Actual(restarted, mode);
            await h.R.StartAsync();
        }
    }
    private static async Task Pending()
    {
        foreach (var old in new[] { TouchMode.M_R1, TouchMode.C_Z1 })
        {
            await using var h = new Harness(old); await h.Gui.StartAsync();
            var next = old == TouchMode.C_Z1 ? TouchMode.M_R1 : TouchMode.C_Z1;
            await h.Send(PresenceTests.Touch(1, TouchEventType.Down, 0));
            await RuntimeTests.Until(() => h.R.CaptureSnapshot().ActiveTouchSessionId >= 0);
            Check(await h.Gui.SelectTouchModeAsync(next), "select while touching");
            var s = h.R.CaptureSnapshot();
            Equal(next, s.SelectedTouchMode, "Selected updates after disk Save"); Equal((TouchMode?)next, s.PendingTouchMode, "Pending retains desired mode");
            Equal((TouchMode?)old, s.ActiveTouchMode, "stationary held touch retains old algorithm");
            Check(h.Gui.PendingTouchModeText.Contains(next.ToString().Replace('_', '-')), "GUI pending is visible");
            Check(h.Gui.ActiveTouchModeText.Contains(old.ToString().Replace('_', '-')), "GUI shows old Active truthfully");
            await h.Send(PresenceTests.Touch(1, TouchEventType.Up, 1, 0, 2_000_000));
            await RuntimeTests.Until(() => h.R.CaptureSnapshot().ActiveTouchMode == next);
            Actual(h, next); Equal(1, h.MouseCreates, "safe boundary preserves HID"); Equal(1, h.PadCreates, "safe boundary preserves Xbox");
        }
    }
    private static async Task InputAndXbox()
    {
        foreach(var mode in new[]{TouchMode.M_R1,TouchMode.C_Z1})
        {
            await using var h=new Harness(mode); await h.Gui.StartAsync();
            await h.Send(PresenceTests.Heartbeat(1));
            await RuntimeTests.Until(()=>h.R.CaptureSnapshot().Presence?.Connected==true);
            await h.Send(PresenceTests.Touch(1,TouchEventType.Down,0));
            await h.Send(PresenceTests.Touch(1,TouchEventType.Move,1,40,4_000_000));
            await h.Send(PresenceTests.Touch(1,TouchEventType.Up,2,100,16_000_000));
            await RuntimeTests.Until(()=>h.R.CaptureSnapshot().AcceptedPackets>=3 && h.R.CaptureSnapshot().MotionOutputEvents>0);
            var snapshot=h.R.CaptureSnapshot();
            Equal((TouchMode?)mode,snapshot.ActiveTouchMode,"same raw transport uses actual locally selected engine");
            Check(h.Mouse.Events.Any(x=>x.Kind=="move" && x.X!=0),"nonzero actual native adapter output");
            Equal(0L,snapshot.MouseOutputFailures,"no Mouse failures");
            uint seq=0;
            foreach(var button in new[]{XboxGamepadButtons.X,XboxGamepadButtons.B})
            {
                await h.Send(GamepadPacketTests.Encode(1,seq++,new(button)));
                await RuntimeTests.Until(()=>h.Pad.States.Any(x=>x.Buttons==button));
                await h.Send(GamepadPacketTests.Encode(1,seq++,XboxGamepadState.Neutral,force:true));
                await RuntimeTests.Until(()=>h.Pad.States.LastOrDefault()==XboxGamepadState.Neutral);
            }
            Equal(1,h.MouseCreates,"input channels retain Mouse device"); Equal(1,h.PadCreates,"input channels retain Xbox device");
            Equal(0L,h.R.CaptureSnapshot().GamepadFailures,"no Gamepad failures");
            await Task.Delay(180);
            long finished=h.R.CaptureSnapshot().MotionOutputEvents;
            await Task.Delay(25);
            Equal(finished,h.R.CaptureSnapshot().MotionOutputEvents,"released finite input parks without persistent drift");
        }
    }
    private static Task Boundary(TouchMode old)
    {
        long now = 100_000; const long frequency = 1_000_000;
        var next = old == TouchMode.C_Z1 ? TouchMode.M_R1 : TouchMode.C_Z1;
        List<(long, int, int)> changed = [], reference = [], fresh = [];
        ResampledMotion Engine(List<(long, int, int)> outputs) => new((x,y) => outputs.Add((now,x,y)),
            monotonicNow: () => now, clockFrequency: frequency, finiteCriticalMode: MotionMode.M_R1);
        using var a = Engine(changed); using var b = Engine(reference); using var c = Engine(fresh);
        a.RequestProfile(TouchModes.Profile(old)); b.RequestProfile(TouchModes.Profile(old)); c.RequestProfile(TouchModes.Profile(next));
        TouchPacket Packet(TouchEventType type, int t, float x, uint session) => new(new(2,type,1,session,(uint)t,1), [new((ulong)t*1_000_000,x,x*.37f)]);
        void Input(ResampledMotion m, int t, uint session) => m.Process(Packet(t == 0 || t == 2000 ? TouchEventType.Down : t == 80 || t == 2080 ? TouchEventType.Up : TouchEventType.Move,
            t, t <= 80 ? t*8.125f : (t-2000)*1.1375f, session));
        for (int t=0; t<1900; t++)
        {
            now=100_000+t*1000L;
            if(t<=80 && t%4==0) { Input(a,t,1); Input(b,t,1); }
            if(t==8) a.RequestProfile(TouchModes.Profile(next));
            if(t<=80) Equal(TouchModes.Profile(old),a.ActiveMotionProfile,"touching retains old profile");
            a.Tick(now,a.Schedule.Generation); b.Tick(now,b.Schedule.Generation);
            Check(changed.SequenceEqual(reference), "every old native delta/time exact through reconstruction/glide completion");
        }
        Check(changed.Count>0 && a.Schedule.Deadline is null && b.Schedule.Deadline is null,"nonvacuous finite completion");
        Equal(TouchModes.Profile(next),a.ActiveMotionProfile,"only idle applies request");
        Equal((0.0,0.0),a.CinematicDynamics.Velocity,"no old servo velocity"); Equal((0.0,0.0),a.CinematicDynamics.CarryOver,"no cross-mode carry");
        int count=changed.Count; a.Tick(now+1_000_000,a.Schedule.Generation-1); Equal(count,changed.Count,"stale generation cannot write");
        changed.Clear();
        for(int t=2000;t<3900;t++)
        {
            now=100_000+t*1000L;
            if(t<=2080 && t%4==0) { Input(a,t,2); Input(c,t,2); }
            a.Tick(now,a.Schedule.Generation); c.Tick(now,c.Schedule.Generation);
            Check(changed.SequenceEqual(fresh),"new chain exactly matches fresh target-mode engine and ledger");
        }
        Check(changed.Count>0 && a.Schedule.Deadline is null,"new finite chain parks");
        count=changed.Count; for(int t=4000;t<6000;t++) {now=100_000+t*1000L;a.Tick(now,a.Schedule.Generation);}
        Equal(count,changed.Count,"no persistent drift after completion");
        return Task.CompletedTask;
    }
    private static Task Markup()
    {
        var xml=XDocument.Load(Path.Combine(TestPaths.SourceRoot,"windows","Rightpad.Receiver","Views","MotionView.xaml"));
        string markup=xml.ToString();
        foreach(string binding in new[]{"SelectedTouchModeText","ActiveTouchModeText","PendingTouchModeText","CanSelectTouchMode"})
            Check(markup.Contains("{Binding "+binding+"}"),"actual binding "+binding);
        Check(xml.Descendants().Any(x=>(string?)x.Attribute("Click")=="SelectDirect") && xml.Descendants().Any(x=>(string?)x.Attribute("Click")=="SelectCinematic"),"both direct click handlers");
        return Task.CompletedTask;
    }
}
