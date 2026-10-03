using System.Text.Json;
using System.Xml.Linq;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class MotionUiCleanupTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> Cases =>
    [
        ("Motion UI removes inactive editors and restart advice", () => { NormalUi(); return Task.CompletedTask; }),
        ("Motion UI unrelated Save retains legacy Tau/Support", UnrelatedSave),
        ("Motion UI C-Z1 fixed dynamics ignore saved Tau/Support", () => { FixedC(); return Task.CompletedTask; })
    ];

    private static void NormalUi()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "windows", "Rightpad.Receiver", "Views", "MotionView.xaml")))
            root = root.Parent;
        Check(root is not null, "locate Receiver XAML for UI contract");
        string receiver = Path.Combine(root!.FullName, "windows", "Rightpad.Receiver");
        var motion = XDocument.Load(Path.Combine(receiver, "Views", "MotionView.xaml"));
        var editors = motion.Descendants().Where(e => e.Name.LocalName == "NumericEditor")
            .Select(e => (string?)e.Attribute("DataContext")).ToArray();
        Check(editors.SequenceEqual(new[] { "{Binding Settings.SensitivityX}", "{Binding Settings.SensitivityY}" }),
            "normal Motion editors retain both sensitivities and exclude Tau/Support entirely");
        string markup = motion.ToString();
        foreach (string obsolete in new[] { "Smoothing Tau", "SmoothingTau", "SmoothingSupport", "SavedMotionText", "RestartRequiredText", "require a Receiver restart", "Current baseline:", "12 ms playout" })
            Check(!markup.Contains(obsolete), "no obsolete normal-user control or advice: " + obsolete);
        Check(!XDocument.Load(Path.Combine(receiver, "MainWindow.xaml")).Descendants()
            .Any(e => (string?)e.Attribute("Click") == "RestartReceiver"), "normal UI has no Tau/Support restart action");
    }

    private static async Task UnrelatedSave()
    {
        string directory = Path.Combine(Path.GetTempPath(), "rightpad-ui-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, """{"sensitivityX":6,"sensitivityY":6,"tapMaxDurationMs":300,"tapMovementThresholdPx":8,"clickHoldMs":25,"smoothingTauMs":8,"smoothingSupportMs":85}""");
            var loaded = SettingsFileStore.Load(path);
            Check(loaded.Warning is null, "legacy settings load without warning");
            var store = new RuntimeSettingsStore(loaded.Settings);
            var file = new SettingsFileStore(path);
            var vm = new SettingsViewModel(store, file);
            Check(!vm.HasUnsavedChanges && !vm.CanSave, "hidden legacy values do not dirty a fresh draft");
            vm.ClickHold.Text = "26";
            Check(await vm.SaveAsync(), "unrelated explicit Save succeeds");
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            Equal(8, json.RootElement.GetProperty("smoothingTauMs").GetInt32(), "legacy Tau retained in JSON");
            Equal(85, json.RootElement.GetProperty("smoothingSupportMs").GetInt32(), "legacy Support retained in JSON");
            Equal(loaded.Settings with { ClickHoldMs = 26 }, SettingsFileStore.Load(path).Settings, "only requested setting changes");
            Equal(new SensitivitySnapshot(6, 6), store.Sensitivity.Current, "unrelated Save retains gain pair");
            vm.SensitivityX.Text = "6.5";
            Check(await vm.SaveAsync(), "Sensitivity editor still saves");
            Equal(new SensitivitySnapshot(6.5, 6), store.Sensitivity.Current, "existing live commit pair");
            Equal(8, store.Current.SmoothingTauMs, "Sensitivity Save retains Tau");
            Equal(85, store.Current.SmoothingSupportMs, "Sensitivity Save retains Support");
            await file.FlushAsync();
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void FixedC()
    {
        foreach (MotionMode mode in new[] { MotionMode.M_R1, MotionModes.ProductionMode })
        {
            long now = 100_000;
            var first = new List<(long, int, int)>();
            var second = new List<(long, int, int)>();
            using var a = new ResampledMotion((x, y) => first.Add((now, x, y)), monotonicNow: () => now,
                clockFrequency: 1_000_000, finiteCriticalMode: mode, configuration: new(mode, 8, 85));
            using var b = new ResampledMotion((x, y) => second.Add((now, x, y)), monotonicNow: () => now,
                clockFrequency: 1_000_000, finiteCriticalMode: mode, configuration: new(mode, 60, 300));
            a.RequestProfile(MotionProfile.Cinematic); b.RequestProfile(MotionProfile.Cinematic);
            uint sequence = 0;
            for (int t = 0; t <= 1000; t++)
            {
                now = 100_000 + t * 1000L;
                if (t <= 80 && t % 4 == 0)
                {
                    var packet = new TouchPacket(new(2, t == 0 ? TouchEventType.Down : t == 80 ? TouchEventType.Up : TouchEventType.Move,
                        1, 1, sequence++, 1), [new((ulong)t * 1_000_000, t * 10, t * -3)]);
                    a.Process(packet); b.Process(packet);
                }
                a.Tick(now, a.Schedule.Generation); b.Tick(now, b.Schedule.Generation);
                Equal(a.Position, b.Position, "C fixed position independent of saved filter settings");
                Equal(a.Schedule, b.Schedule, "C fixed release and cadence");
            }
            Check(first.Count > 0 && first.SequenceEqual(second), "every C native delta/time identical");
            Equal(35, a.ActiveAlgorithm.TauMs, "C active fixed tau35");
            var stats = new RuntimeStatsViewModel();
            stats.Refresh(new(1, ReceiverState.Running, ActiveMotionProfile: MotionProfile.Cinematic,
                ActiveMotionAlgorithm: a.ActiveAlgorithm.Algorithm, ActiveMotionTauMs: a.ActiveAlgorithm.TauMs), now);
            Equal("35 ms", stats.ActiveTau, "C diagnostics uses algorithm tau");
            Equal("N/A", stats.ActiveSupport, "C support is inapplicable");
        }
    }
}
