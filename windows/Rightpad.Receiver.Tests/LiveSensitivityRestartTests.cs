using System.Net;
using System.Net.Sockets;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class LiveSensitivityRestartTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> Cases
    {
        get
        {
            yield return ("live gain: draft/save/mid-contact/pending/settle", LiveSave);
            yield return ("live gain: failed save preserves committed gain and disk", FailedSave);
            yield return ("live gain: atomic XY snapshots under concurrent publication", AtomicPair);
            yield return ("safe restart: normal and UI lifecycle guards", () => Restart(0));
            yield return ("safe restart: target retry succeeds", () => Restart(1));
            yield return ("safe restart: old active restored without changing saved settings", () => Restart(2));
            yield return ("safe restart: recovery failure keeps actionable errors", () => Restart(3));
            yield return ("live gain: runtime UDP MOVE applies save without restart", RuntimeMove);
            yield return ("live gain: Save preserves realized history and partially played pending", HistoryPreserved);
        }
    }

    private sealed class Files : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "rightpad-live-" + Guid.NewGuid().ToString("N"));
        public string PathName => Path.Combine(DirectoryPath, "settings.json");
        public Files() => Directory.CreateDirectory(DirectoryPath);
        public void Dispose() => Directory.Delete(DirectoryPath, true);
    }

    private static RuntimeSettings Initial => RuntimeSettings.Default with { SensitivityX = 9, SensitivityY = 9 };
    private static async Task LiveSave()
    {
        using var files = new Files();
        var store = new RuntimeSettingsStore(Initial);
        var vm = new SettingsViewModel(store, new(files.PathName));
        long now = 100_000;
        uint sequence = 0;
        var moves = new List<(int X, int Y)>();
        using var motion = new ResampledMotion((x, y) => moves.Add((x, y)), 9, 9, () => now, 1_000_000,
            finiteCriticalMode: MotionModes.ProductionMode, liveSensitivity: store.Sensitivity);
        void Send(TouchEventType type, int ms, float x)
        {
            now = 100_000 + ms * 1000;
            motion.Process(new(new(2, type, 1, 1, sequence++, 1), [new((ulong)ms * 1_000_000, x, x)]));
        }
        Send(TouchEventType.Down, 0, 0);
        Send(TouchEventType.Move, 1, 10);
        Equal((90d, 90d), motion.Pending, "first MOVE times nine");
        vm.SensitivityX.Text = "3.0"; vm.SensitivityY.Text = "3.0";
        vm.SmoothingTau.Text = "18"; vm.SmoothingSupport.Text = "90";
        Send(TouchEventType.Move, 2, 20);
        Equal((180d, 180d), motion.Pending, "draft does not change live input");
        var schedule = motion.Schedule;
        Check(await vm.SaveAsync(), "disk save then publish succeeds");
        Equal((180d, 180d), motion.Pending, "Save does not rescale already-earned target");
        Equal(0, moves.Count, "Save creates no mouse movement");
        Equal(schedule, motion.Schedule, "Save preserves phase and generation");
        Send(TouchEventType.Move, 3, 30);
        Equal((210d, 210d), motion.Pending, "next MOVE in same contact times three");
        Send(TouchEventType.Up, 4, 32);
        Equal((216d, 216d), motion.Pending, "real UP endpoint uses current gain");
        Equal(24, motion.KernelTauMs, "Tau remains frozen");
        Equal(120, motion.KernelSupportMs, "Support remains frozen");
        while (motion.Schedule.Deadline is long tick)
        {
            Check(tick < 400_000, "finite settle deadline");
            now = tick; motion.Tick(tick, motion.Schedule.Generation);
        }
        Equal((216, 216), (moves.Sum(m => m.X), moves.Sum(m => m.Y)), "old and new earned displacement both conserved");
        Equal(0L, motion.UpFlushCount, "Earned-Settle unchanged");
        Equal(1, motion.PeriodMs, "1000Hz unchanged");
        Equal("Q0C", MotionModes.QuantizerName(MotionModes.ProductionMode), "Q0-C unchanged");
        Equal(12, ResampledMotion.PlayoutDelayMs, "12ms unchanged");
    }

    private static async Task FailedSave()
    {
        using var files = new Files();
        var file = new SettingsFileStore(files.PathName);
        Check(await file.SaveNowAsync(Initial), "seed disk");
        string before = File.ReadAllText(files.PathName);
        var store = new RuntimeSettingsStore(Initial);
        var vm = new SettingsViewModel(store, file);
        vm.SensitivityX.Text = "3.0"; vm.SensitivityY.Text = "3.0";
        Directory.CreateDirectory(files.PathName + ".tmp");
        Check(!await vm.SaveAsync(), "blocked temporary file fails Save");
        Equal(before, File.ReadAllText(files.PathName), "disk unchanged");
        Equal(Initial, store.Current, "committed unchanged");
        Equal(new SensitivitySnapshot(9, 9), store.Sensitivity.Current, "live gain unchanged");
        Check(vm.CanSave && vm.HasUnsavedChanges, "draft retained for retry");
    }

    private static Task HistoryPreserved()
    {
        using var files = new Files();
        long now = 100_000;
        var live = new LiveSensitivity(9, 9);
        var actual = new List<(int, int)>();
        var control = new List<(int, int)>();
        using (var trace = new MotionTrace(files.DirectoryPath, MotionModes.ProductionMode))
        using (var a = new ResampledMotion((x, y) => actual.Add((x, y)), 9, 9, () => now, 1_000_000,
            trace: trace, finiteCriticalMode: MotionModes.ProductionMode, liveSensitivity: live))
        using (var b = new ResampledMotion((x, y) => control.Add((x, y)), 9, 9, () => now, 1_000_000,
            finiteCriticalMode: MotionModes.ProductionMode))
        {
            TouchPacket Packet(TouchEventType type, int ms, float x) =>
                new(new(2, type, 1, 1, (uint)ms, 1), [new((ulong)ms * 1_000_000, x, x)]);
            var down = Packet(TouchEventType.Down, 0, 0);
            var move = Packet(TouchEventType.Move, 4, 10);
            a.Process(down); b.Process(down);
            now += 4000; a.Process(move); b.Process(move);
            for (int t = 5; t <= 200; t++)
            {
                now = 100_000 + t * 1000;
                a.Tick(now, a.Schedule.Generation); b.Tick(now, b.Schedule.Generation);
                if (t == 30)
                {
                    Check(a.Position.X > 0 && a.Pending.X > 0, "history partly played and pending remains");
                    var pending = a.Pending;
                    live.Publish(3, 3);
                    Equal(pending, a.Pending, "publication does not rescale partially played pending");
                }
                Equal(b.Position, a.Position, "Save does not reinterpret realized filter history");
                Check(actual.SequenceEqual(control), "all subsequent ticks match fixed gain control without new input");
            }
            a.Process(Packet(TouchEventType.Move, 201, 20));
        }
        var changes = File.ReadAllLines(Path.Combine(files.DirectoryPath, "motion.csv"))
            .Where(line => line.StartsWith("SensitivityChanged,")).ToArray();
        Equal(2, changes.Length, "initial and changed sensitivity recorded only on real input");
        Check(changes[1].Contains(",3,3,9,9,"), "trace records actual old/new gain");
        return Task.CompletedTask;
    }

    private static async Task AtomicPair()
    {
        var store = new RuntimeSettingsStore(Initial);
        var writer = Task.Run(() =>
        {
            for (int i = 0; i < 100_000; i++)
                store.Publish(Initial with { SensitivityX = (i % 2 == 0 ? 3 : 9), SensitivityY = (i % 2 == 0 ? 6 : 9) });
        });
        for (int i = 0; i < 200_000; i++)
        {
            var pair = store.Sensitivity.Current;
            Check(pair is { X: 9, Y: 9 } or { X: 3, Y: 6 }, "never observes mixed X/Y");
        }
        await writer;
    }

    private sealed class StartupMemory : IStartupValueStore
    {
        public string? Read(string name) => null;
        public void Write(string name, string command) { }
        public void Delete(string name) { }
    }

    private static async Task Restart(int failures)
    {
        using var files = new Files();
        var store = new RuntimeSettingsStore(Initial);
        var file = new SettingsFileStore(files.PathName);
        var settings = new SettingsViewModel(store, file);
        int attempts = 0;
        var devices = new List<VirtualHidTests.FakeNative>();
        using var entered = new ManualResetEventSlim();
        using var proceed = new ManualResetEventSlim();
        using var reserve = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var endpoint = (IPEndPoint)reserve.Client.LocalEndPoint!;
        reserve.Dispose();
        var runtime = new ReceiverRuntime(store, TextWriter.Null, MouseBackend.VirtualHid, endpoint, () =>
        {
            int attempt = ++attempts;
            if (attempt > 1)
            {
                Check(devices.All(d => d.Events.Any(e => e.Kind == "dispose")), "old HID disposed before next start");
                using var probe = new UdpClient(endpoint); // Old socket must already be released.
            }
            if (attempt == 2) { entered.Set(); Check(proceed.Wait(5000), "restart gate released"); }
            if (attempt > 1 && attempt <= 1 + failures) throw new IOException($"injected start failure {attempt - 1}");
            var native = new VirtualHidTests.FakeNative(); devices.Add(native);
            return new LibVirtualHidMouseOutput(native);
        }, motionMode: MotionModes.ProductionMode, useProductMotionSettings: true);
        var vm = new MainViewModel(runtime, settings, new(new(new StartupMemory(), @"C:\rightpad\Receiver.exe")));
        try
        {
            await vm.StartAsync();
            Check(!vm.RestartRequired && !vm.CanRestart, "matching config disables restart");
            settings.SensitivityX.Text = "3.0"; settings.SensitivityY.Text = "3.0";
            settings.SmoothingTau.Text = "18"; settings.SmoothingSupport.Text = "90";
            Check(await settings.SaveAsync(), "commit new gain and smoothing");
            vm.Refresh();
            Check(vm.RestartRequired && vm.CanRestart, "saved differs from active");
            Equal(24, vm.ActiveTauMs!.Value, "active comes from run");
            Equal(new SensitivitySnapshot(3, 3), runtime.ActiveSensitivity, "live gain already updated");
            Equal(3d, runtime.CaptureActiveRuntimeSnapshot()!.Settings.SensitivityX, "recovery uses live gain, not startup nine");
            string saved = File.ReadAllText(files.PathName);
            long oldRun = runtime.CaptureSnapshot().RunId;
            var restart = vm.RestartAsync();
            await Task.Run(() => Check(entered.Wait(5000), "target entered"));
            Check(vm.IsRestarting && !vm.CanRestart && !settings.CanSave && !vm.CanToggle, "lifecycle controls gated");
            Check(!settings.SensitivityX.IsEnabled && !settings.SmoothingTau.IsEnabled && !settings.SmoothingSupport.IsEnabled, "editors gated");
            Equal("Restarting...", vm.RestartText, "busy label");
            Check(!await settings.SaveAsync(), "save rejected during restart");
            await vm.RestartAsync();
            var duplicate = await runtime.RestartAsync();
            Check(!duplicate.Succeeded && duplicate.Error!.Contains("already in progress"), "runtime duplicate rejected");
            proceed.Set(); await restart;
            Equal(2 + Math.Min(failures, 2), attempts, "bounded target retry and recovery");
            Equal(saved, File.ReadAllText(files.PathName), "recovery never writes disk");
            Equal(18, store.Current.SmoothingTauMs, "recovery never republishes old committed Tau");
            Equal(3d, runtime.ActiveSensitivity.X, "recovery retains latest committed gain");
            Check(!vm.IsRestarting && settings.SmoothingTau.IsEnabled, "controls restored");
            if (failures < 3)
            {
                Equal(ReceiverState.Running, runtime.CaptureSnapshot().RuntimeState, "input runtime restored");
                Check(runtime.CaptureSnapshot().RunId > oldRun, "new Runtime run");
                Equal(failures == 2 ? 24 : 18, vm.ActiveTauMs!.Value, "actual active config");
                Equal(failures == 2, vm.RestartRequired, "mismatch retained on fallback");
                Check(runtime.CaptureSnapshot().LastError is null, "recovered run has no error");
                if (failures == 2) Check(vm.RestartError.Contains("Previous receiver configuration was restored") &&
                    vm.RestartError.Contains("failure 1") && vm.RestartError.Contains("failure 2"), "recovery message includes both target errors");
                else Equal("", vm.RestartError, "normal/retry success");
            }
            else
            {
                Equal(ReceiverState.Error, runtime.CaptureSnapshot().RuntimeState, "recovery failure visible");
                Check(vm.RestartError.Contains("automatic recovery failed") && vm.RestartError.Contains("failure 3"), "critical error retains recovery failure");
                Check(vm.CanToggle && !vm.CanRestart && !vm.RestartRequired, "manual Start remains available");
            }
        }
        finally { proceed.Set(); await runtime.StopAsync(); }
    }

    private static async Task RuntimeMove()
    {
        using var files = new Files();
        var store = new RuntimeSettingsStore(Initial);
        var settings = new SettingsViewModel(store, new(files.PathName));
        var runtime = new ReceiverRuntime(store, TextWriter.Null, MouseBackend.VirtualHid, new(IPAddress.Loopback, 0),
            () => new LibVirtualHidMouseOutput(new VirtualHidTests.FakeNative()),
            motionMode: MotionModes.ProductionMode, useProductMotionSettings: true);
        using var sender = new UdpClient();
        try
        {
            await runtime.StartAsync();
            long id = runtime.CaptureSnapshot().RunId;
            var endpoint = runtime.LocalEndpoint!;
            await sender.SendAsync(PresenceTests.Touch(77, TouchEventType.Down, 0, 0, 0), endpoint);
            await sender.SendAsync(PresenceTests.Touch(77, TouchEventType.Move, 1, 10, 1_000_000), endpoint);
            await RuntimeTests.Until(() => runtime.CaptureSnapshot().IntendedRelativeDxTotal == 90);
            settings.SensitivityX.Text = "3.0"; settings.SensitivityY.Text = "3.0";
            Check(await settings.SaveAsync(), "live runtime Save");
            await sender.SendAsync(PresenceTests.Touch(77, TouchEventType.Move, 2, 20, 200_000_000), endpoint);
            await RuntimeTests.Until(() => runtime.CaptureSnapshot().IntendedRelativeDxTotal == 120);
            Equal(id, runtime.CaptureSnapshot().RunId, "same run after Save");
            Equal(endpoint, runtime.LocalEndpoint!, "same socket after Save");
            Equal(0L, runtime.CaptureSnapshot().MouseOutputFailures, "no output failures");
        }
        finally { await runtime.StopAsync(); }
    }
}
