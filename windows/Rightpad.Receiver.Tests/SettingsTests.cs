using System.Text.Json;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class SettingsTests
{
    private sealed class Files : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "rightpad-settings-" + Guid.NewGuid().ToString("N"));
        public string PathName => Path.Combine(DirectoryPath, "settings.json");
        public Files() => Directory.CreateDirectory(DirectoryPath);
        public void Dispose()
        {
            foreach (var file in Directory.GetFiles(DirectoryPath)) File.Delete(file);
            foreach (var directory in Directory.GetDirectories(DirectoryPath)) Directory.Delete(directory);
            Directory.Delete(DirectoryPath);
        }
    }
    private static RuntimeSettings Load(string json)
    {
        using var files = new Files();
        File.WriteAllText(files.PathName, json);
        return SettingsFileStore.Load(files.PathName).Settings;
    }
    public static void NoFile()
    {
        using var files = new Files();
        var loaded = SettingsFileStore.Load(files.PathName);
        Equal(RuntimeSettings.Default, loaded.Settings, "defaults");
        Check(loaded.Warning is null, "first launch is normal");
    }
    public static void Valid() => Equal(new RuntimeSettings(6.95, 7.1, 310, 8.5, 26),
        Load("""{"sensitivityX":6.95,"sensitivityY":7.1,"tapMaxDurationMs":310,"tapMovementThresholdPx":8.5,"clickHoldMs":26}"""), "valid fields");
    public static void Malformed()
    {
        foreach (var json in new[] { "{oops", "null", "[]", "7" }) Equal(RuntimeSettings.Default, Load(json), "malformed/root defaults");
    }
    public static void Missing() => Equal(RuntimeSettings.Default with { SensitivityX = 6.95 }, Load("""{"sensitivityX":6.95}"""), "per-field missing");
    public static void Invalid() => Equal(RuntimeSettings.Default with { SensitivityY = 6 },
        Load("""{"sensitivityX":"7","sensitivityY":6,"tapMaxDurationMs":null,"tapMovementThresholdPx":true,"clickHoldMs":1.5}"""), "per-field invalid");
    public static void Range() => Equal(RuntimeSettings.Default,
        Load("""{"sensitivityX":0.09,"sensitivityY":30.01,"tapMaxDurationMs":1501,"tapMovementThresholdPx":0.49,"clickHoldMs":201}"""), "product limits");
    public static void Unknown() => Equal(RuntimeSettings.Default with { SensitivityX = 5 },
        Load("""{"sensitivityX":5,"future":{"ignored":true}}"""), "unknown ignored");
    public static async Task RoundTrip()
    {
        using var files = new Files();
        var store = new SettingsFileStore(files.PathName);
        var value = new RuntimeSettings(6.95, 7.1, 310, 8.5, 26);
        store.Schedule(value);
        await store.FlushAsync();
        Equal(value, SettingsFileStore.Load(files.PathName).Settings, "round trip");
        using var doc = JsonDocument.Parse(File.ReadAllText(files.PathName));
        Equal(8, doc.RootElement.EnumerateObject().Count(), "only eight product fields");
        Equal(24, doc.RootElement.GetProperty("smoothingTauMs").GetInt32(), "default Tau serialized");
        Equal(120, doc.RootElement.GetProperty("smoothingSupportMs").GetInt32(), "default Support serialized");
    }
    public static async Task SaveNow()
    {
        using var files = new Files();
        var file = new SettingsFileStore(files.PathName);
        var value = RuntimeSettings.Default with { SensitivityX = 6.95 };
        Check(await file.SaveNowAsync(value), "immediate save succeeds");
        Check(File.Exists(files.PathName), "immediate save completes before returning");
        Equal(value, SettingsFileStore.Load(files.PathName).Settings, "immediate save value");
        await file.FlushAsync();
    }
    public static async Task Debounce()
    {
        using var files = new Files();
        var file = new SettingsFileStore(files.PathName);
        file.Schedule(RuntimeSettings.Default with { SensitivityX = 6 });
        await Task.Delay(200);
        Check(!File.Exists(files.PathName), "not synchronously saved");
        file.Schedule(RuntimeSettings.Default with { SensitivityX = 6.95 });
        await Task.Delay(350);
        Check(!File.Exists(files.PathName), "timer restarted");
        await RuntimeTests.Until(() => File.Exists(files.PathName));
        Equal(6.95, SettingsFileStore.Load(files.PathName).Settings.SensitivityX, "debounced latest");
        await file.FlushAsync();
    }
    public static async Task LatestWins()
    {
        using var files = new Files();
        var file = new SettingsFileStore(files.PathName);
        for (int i = 1; i <= 100; i++) file.Schedule(RuntimeSettings.Default with { SensitivityX = i / 10.0 });
        await file.FlushAsync();
        Equal(10.0, SettingsFileStore.Load(files.PathName).Settings.SensitivityX, "latest flush");
        await Task.Delay(550);
        Equal(10.0, SettingsFileStore.Load(files.PathName).Settings.SensitivityX, "old delays cannot overwrite");
    }
    public static async Task SaveFailure()
    {
        using var files = new Files();
        Directory.CreateDirectory(files.PathName);
        var file = new SettingsFileStore(files.PathName);
        var store = new RuntimeSettingsStore();
        var vm = new SettingsViewModel(store, file);
        vm.SensitivityX.Text = "6.5";
        vm.SmoothingTau.Text = "18";
        vm.SmoothingSupport.Text = "90";
        Check(!await vm.SaveAsync(), "save reports failure");
        Equal(RuntimeSettings.Default, store.Current, "failure does not publish runtime");
        Equal(6.5, vm.DraftSettings.SensitivityX, "failure retains draft");
        Equal(18, vm.DraftSettings.SmoothingTauMs, "failure retains Tau draft");
        Equal(90, vm.DraftSettings.SmoothingSupportMs, "failure retains Support draft");
        Check(vm.HasUnsavedChanges && vm.CanSave && !vm.IsSaving, "failure remains retryable");
        Equal("Settings save failed. Changes were not applied.", vm.Notice, "failure notice says not applied");
        Directory.Delete(files.PathName);
        await file.FlushAsync();
        Check(!File.Exists(files.PathName), "exit flush does not retry failed explicit draft");
    }
    public static async Task ExitFlush()
    {
        using var files = new Files();
        var file = new SettingsFileStore(files.PathName);
        file.Schedule(RuntimeSettings.Default with { ClickHoldMs = 27 });
        await file.FlushAsync();
        Equal(27, SettingsFileStore.Load(files.PathName).Settings.ClickHoldMs, "flush before debounce");
    }
    public static async Task AtomicSnapshot()
    {
        var a = new RuntimeSettings(1, 2, 100, 3, 4);
        var b = new RuntimeSettings(5, 6, 200, 7, 8);
        var store = new RuntimeSettingsStore(a);
        var writer = Task.Run(() => { for (int i = 0; i < 100_000; i++) store.Publish((i & 1) == 0 ? a : b); });
        for (int i = 0; i < 100_000; i++)
        {
            var read = store.Current;
            Check(ReferenceEquals(read, a) || ReferenceEquals(read, b), "no mixed snapshot");
        }
        await writer;
    }
    public static async Task NumericDraft()
    {
        using var files = new Files();
        var file = new SettingsFileStore(files.PathName);
        var store = new RuntimeSettingsStore();
        var vm = new SettingsViewModel(store, file);
        vm.SensitivityX.Text = "6.5";
        Equal(RuntimeSettings.Default, store.Current, "valid edit leaves runtime committed value");
        Check(!File.Exists(files.PathName), "valid edit does not write file");
        Equal(6.5, vm.DraftSettings.SensitivityX, "valid edit updates draft");
        Check(vm.HasUnsavedChanges && vm.CanSave, "valid changed draft can save");
        vm.SensitivityX.Text = "7.0";
        Check(!vm.HasUnsavedChanges && !vm.CanSave, "return to committed value clears dirty state");
        foreach (var draft in new[] { "", "-", "6.", "abc", "0", "31", "NaN" })
        {
            vm.SensitivityX.Text = draft;
            Equal(RuntimeSettings.Default, store.Current, "invalid draft leaves runtime unchanged");
            Check(vm.HasUnsavedChanges && !vm.CanSave, "invalid or incomplete draft cannot save");
            Check(!File.Exists(files.PathName), "invalid draft does not write file");
            vm.SensitivityX.Restore();
            Equal("7.0", vm.SensitivityX.Text, "Escape restores committed value");
            Check(!vm.HasUnsavedChanges && !vm.CanSave, "Escape clears field draft");
        }
        vm.SensitivityX.Text = "6.5";
        vm.SensitivityX.Step(1);
        Equal("7.0", vm.SensitivityX.Text, "precision and step");
        vm.TapMaxDuration.Text = "301";
        Equal(301, vm.DraftSettings.TapMaxDurationMs, "step not a quantization restriction");
        Equal(300, store.Current.TapMaxDurationMs, "integer draft does not publish");
        await file.FlushAsync();
        Check(!File.Exists(files.PathName), "exit flush does not persist unsaved UI draft");
    }
    public static async Task SensitivityEditor()
    {
        using var files = new Files();
        var file = new SettingsFileStore(files.PathName);
        var initial = RuntimeSettings.Default with { SensitivityX = 9, SensitivityY = 9 };
        Check(await file.SaveNowAsync(initial), "initial committed settings saved");
        var store = new RuntimeSettingsStore(initial);
        var vm = new SettingsViewModel(store, file);

        Equal("9.0", vm.SensitivityX.Text, "Sensitivity X initial one-decimal format");
        Equal("9.0", vm.SensitivityY.Text, "Sensitivity Y initial one-decimal format");
        vm.SensitivityX.Step(1);
        Equal("9.5", vm.SensitivityX.Text, "9.0 plus one step");
        vm.SensitivityX.Step(1);
        Equal("10.0", vm.SensitivityX.Text, "9.5 plus one step");
        vm.SensitivityX.Step(-1);
        Equal("9.5", vm.SensitivityX.Text, "10.0 minus one step");
        Equal(initial, store.Current, "button/key steps do not publish draft");
        Equal(initial, SettingsFileStore.Load(files.PathName).Settings, "button/key steps do not write settings");

        vm.SensitivityX.Restore();
        Equal("9.0", vm.SensitivityX.Text, "Escape restores committed one-decimal value");
        vm.SensitivityX.Text = "8.5";
        Check(vm.SensitivityX.IsCompleteValid && vm.CanSave, "one decimal direct input valid");
        vm.SensitivityX.Text = "8.25";
        Check(!vm.SensitivityX.IsCompleteValid && !vm.CanSave, "two-decimal direct input invalid");
        Equal(8.5, vm.DraftSettings.SensitivityX, "invalid precision is not rounded into draft");
        Equal(initial, store.Current, "invalid precision leaves runtime unchanged");
        Equal(initial, SettingsFileStore.Load(files.PathName).Settings, "invalid precision leaves settings JSON unchanged");
        Check(vm.SensitivityX.Error.Length > 0, "invalid precision shows inline validation");

        vm.SensitivityX.Restore();
        vm.SensitivityX.Text = "0.1";
        Check(vm.SensitivityX.IsCompleteValid && vm.CanSave, "lower boundary valid");
        vm.SensitivityX.Text = "30.0";
        Check(vm.SensitivityX.IsCompleteValid && vm.CanSave, "upper boundary valid");
        vm.SensitivityX.Text = "30.1";
        Check(!vm.SensitivityX.IsCompleteValid && !vm.CanSave, "above upper boundary invalid");

        vm.SensitivityX.Restore();
        vm.SensitivityX.Text = "8.5";
        Check(await vm.SaveAsync(), "valid one-decimal sensitivity saves");
        Equal(8.5, store.Current.SensitivityX, "Save publishes committed sensitivity snapshot");
        using var json = JsonDocument.Parse(File.ReadAllText(files.PathName));
        Equal(8.5, json.RootElement.GetProperty("sensitivityX").GetDouble(), "saved JSON sensitivity value");
        await file.FlushAsync();
    }
    public static void TauSupportSchema()
    {
        var missing = Load("""{"sensitivityX":9,"sensitivityY":9,"tapMaxDurationMs":300,"tapMovementThresholdPx":8,"clickHoldMs":25,"doubleTapIntervalMs":130}""");
        Equal(24, missing.SmoothingTauMs, "old JSON missing Tau uses baseline");
        Equal(120, missing.SmoothingSupportMs, "old JSON missing Support uses baseline");
        Equal(RuntimeSettings.Default with { SmoothingTauMs = 8, SmoothingSupportMs = 40 },
            Load("""{"smoothingTauMs":8,"smoothingSupportMs":40}"""), "lower boundaries load");
        Equal(RuntimeSettings.Default with { SmoothingTauMs = 60, SmoothingSupportMs = 300 },
            Load("""{"smoothingTauMs":60,"smoothingSupportMs":300}"""), "upper boundaries load");
        foreach (string json in new[]
        {
            """{"smoothingTauMs":7,"smoothingSupportMs":90}""",
            """{"smoothingTauMs":61,"smoothingSupportMs":90}""",
            """{"smoothingTauMs":18,"smoothingSupportMs":39}""",
            """{"smoothingTauMs":18,"smoothingSupportMs":301}""",
            """{"smoothingTauMs":24.5,"smoothingSupportMs":122.5}"""
        })
        {
            var value = Load(json);
            if (json.Contains("\"smoothingTauMs\":18")) Equal(18, value.SmoothingTauMs, "valid Tau survives invalid Support");
            else Equal(24, value.SmoothingTauMs, "invalid Tau per-field fallback");
            if (json.Contains("\"smoothingSupportMs\":90")) Equal(90, value.SmoothingSupportMs, "valid Support survives invalid Tau");
            else Equal(120, value.SmoothingSupportMs, "invalid Support per-field fallback");
        }
    }
    public static async Task TauSupportEditor()
    {
        using var files = new Files();
        var initial = RuntimeSettings.Default;
        var file = new SettingsFileStore(files.PathName);
        Check(await file.SaveNowAsync(initial), "initial JSON");
        var store = new RuntimeSettingsStore(initial);
        var vm = new SettingsViewModel(store, file);

        Equal("24", vm.SmoothingTau.Text, "Tau integer format");
        Equal("120", vm.SmoothingSupport.Text, "Support integer format");
        vm.SmoothingTau.Step(-1); Equal("23", vm.SmoothingTau.Text, "Tau minus step 1");
        vm.SmoothingTau.Step(1); Equal("24", vm.SmoothingTau.Text, "Tau plus step 1");
        vm.SmoothingSupport.Step(-1); Equal("115", vm.SmoothingSupport.Text, "Support minus step 5");
        vm.SmoothingSupport.Step(1); Equal("120", vm.SmoothingSupport.Text, "Support plus step 5");
        vm.SmoothingTau.Text = "24.5";
        Check(!vm.SmoothingTau.IsCompleteValid && !vm.CanSave, "fractional Tau invalid without rounding");
        Equal(24, vm.DraftSettings.SmoothingTauMs, "fractional Tau not copied to draft");
        vm.SmoothingTau.Restore();
        vm.SmoothingSupport.Text = "122.5";
        Check(!vm.SmoothingSupport.IsCompleteValid && !vm.CanSave, "fractional Support invalid without rounding");
        Equal(120, vm.DraftSettings.SmoothingSupportMs, "fractional Support not copied to draft");
        vm.SmoothingSupport.Restore();

        vm.SmoothingTau.Text = "8"; vm.SmoothingTau.Step(-1); Equal("8", vm.SmoothingTau.Text, "Tau lower clamp");
        vm.SmoothingTau.Text = "60"; vm.SmoothingTau.Step(1); Equal("60", vm.SmoothingTau.Text, "Tau upper clamp");
        vm.SmoothingSupport.Text = "40"; vm.SmoothingSupport.Step(-1); Equal("40", vm.SmoothingSupport.Text, "Support lower clamp");
        vm.SmoothingSupport.Text = "300"; vm.SmoothingSupport.Step(1); Equal("300", vm.SmoothingSupport.Text, "Support upper clamp");
        vm.SmoothingTau.Restore(); vm.SmoothingSupport.Restore();

        vm.SmoothingTau.Text = "18"; vm.SmoothingSupport.Text = "90";
        Equal(initial, store.Current, "Tau/Support drafts do not publish");
        Equal(initial, SettingsFileStore.Load(files.PathName).Settings, "Tau/Support drafts do not write JSON");
        Check(vm.HasUnsavedChanges && vm.CanSave, "all eight fields complete and valid");
        vm.ClickHold.Text = ""; Check(!vm.CanSave, "CanSave includes all eight field validities");
        vm.ClickHold.Restore(); Check(vm.CanSave, "restored eighth field permits Save");
        vm.SmoothingTau.Restore(); Equal("24", vm.SmoothingTau.Text, "Tau Escape committed baseline");
        vm.SmoothingSupport.Restore(); Equal("120", vm.SmoothingSupport.Text, "Support Escape committed baseline");
        await file.FlushAsync();
    }
    public static async Task TauSupportSave()
    {
        using var files = new Files();
        var file = new SettingsFileStore(files.PathName);
        var store = new RuntimeSettingsStore();
        var vm = new SettingsViewModel(store, file);
        vm.SmoothingTau.Text = "18"; vm.SmoothingSupport.Text = "90";
        Check(await vm.SaveAsync(), "Tau/Support explicit Save");
        Equal(18, store.Current.SmoothingTauMs, "Save publishes Tau");
        Equal(90, store.Current.SmoothingSupportMs, "Save publishes Support");
        using (var json = JsonDocument.Parse(File.ReadAllText(files.PathName)))
        {
            Equal(18, json.RootElement.GetProperty("smoothingTauMs").GetInt32(), "JSON Tau");
            Equal(90, json.RootElement.GetProperty("smoothingSupportMs").GetInt32(), "JSON Support");
        }
        Check(!vm.HasUnsavedChanges && !vm.CanSave, "Save establishes eight-field baseline");
        vm.SmoothingTau.Text = "15"; vm.SmoothingSupport.Text = "80";
        vm.SmoothingTau.Restore(); vm.SmoothingSupport.Restore();
        Equal("18", vm.SmoothingTau.Text, "Tau Escape uses last successful Save");
        Equal("90", vm.SmoothingSupport.Text, "Support Escape uses last successful Save");
        Check(!vm.HasUnsavedChanges, "page-retained draft can restore committed baseline");
        await file.FlushAsync();
    }
    public static async Task MultiFieldSave()
    {
        using var files = new Files();
        var file = new SettingsFileStore(files.PathName);
        var store = new RuntimeSettingsStore();
        var original = store.Current;
        var vm = new SettingsViewModel(store, file);
        vm.SensitivityX.Text = "6.5";
        vm.TapMaxDuration.Text = "310";
        vm.ClickHold.Text = "26";
        Check(ReferenceEquals(original, store.Current), "all edits preserve complete runtime snapshot");
        Check(!File.Exists(files.PathName), "multiple edits do not write before Save");
        Check(await vm.SaveAsync(), "explicit Save succeeds");
        var expected = RuntimeSettings.Default with { SensitivityX = 6.5, TapMaxDurationMs = 310, ClickHoldMs = 26 };
        Equal(expected, store.Current, "Save publishes one complete snapshot");
        Equal(expected, SettingsFileStore.Load(files.PathName).Settings, "Save writes complete snapshot immediately");
        Check(!vm.HasUnsavedChanges && !vm.CanSave && !vm.IsSaving, "successful Save establishes committed baseline");
        vm.SensitivityX.Text = "7.5";
        Equal(expected, store.Current, "post-save edit does not publish");
        vm.SensitivityX.Restore();
        Equal("6.5", vm.SensitivityX.Text, "Escape uses newly saved committed baseline");
        Check(!vm.HasUnsavedChanges && !vm.CanSave, "restoring new baseline clears draft");
        await file.FlushAsync();
    }
    public static async Task UnsavedExit()
    {
        using var files = new Files();
        var file = new SettingsFileStore(files.PathName);
        var store = new RuntimeSettingsStore();
        var vm = new SettingsViewModel(store, file);
        vm.ClickHold.Text = "27";
        await file.FlushAsync();
        Equal(RuntimeSettings.Default, store.Current, "exit leaves unsaved runtime draft unapplied");
        Check(!File.Exists(files.PathName), "exit does not persist unsaved draft");
    }
    public static void Entry()
    {
        Equal(Rightpad.Receiver.Program.LaunchMode.Gui, Rightpad.Receiver.Program.ParseLaunchArguments([]).Mode, "default GUI");
        Equal(Rightpad.Receiver.Program.LaunchMode.Diagnostics, Rightpad.Receiver.Program.ParseLaunchArguments(["--diagnostics"]).Mode, "explicit diagnostics");
        Equal(Rightpad.Receiver.Program.LaunchMode.RawMouse, Rightpad.Receiver.Program.ParseLaunchArguments(["--raw-mouse"]).Mode, "explicit raw dev");
        var gui = Rightpad.Receiver.Program.ParseLaunchArguments(["--dev-log-dir", "logs", "--dev-settings-path", "test.json"]);
        Equal(Rightpad.Receiver.Program.LaunchMode.Gui, gui.Mode, "dev GUI still GUI");
        Equal("test.json", gui.SettingsPath, "isolated test settings");
    }
    public static void ProductMotion()
    {
        var ordinary = Rightpad.Receiver.Program.ParseLaunchArguments([]);
        Equal(MotionModes.ProductionMode, Rightpad.Receiver.Program.ResolveGuiMotionMode(ordinary),
            "ordinary GUI fixed to production 1000");
        foreach (var item in new[]
        {
            (Name: "RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5_SETTLE", Mode: MotionMode.RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5_SETTLE),
            (Name: "RESAMPLED_500HZ_FINITE_CRITICAL_K24_R5_SETTLE", Mode: MotionMode.RESAMPLED_500HZ_FINITE_CRITICAL_K24_R5_SETTLE),
            (Name: "RESAMPLED_1000HZ_FINITE_CRITICAL_K24_R5_SETTLE", Mode: MotionMode.RESAMPLED_1000HZ_FINITE_CRITICAL_K24_R5_SETTLE)
        })
        {
            var explicitDevelopment = Rightpad.Receiver.Program.ParseLaunchArguments(["--dev-motion-mode", item.Name]);
            Equal(item.Mode, Rightpad.Receiver.Program.ResolveGuiMotionMode(explicitDevelopment),
                $"explicit development mode {item.Name}");
        }
    }
    public static async Task LegacyCadence()
    {
        foreach (int legacyCadence in new[] { 250, 1000 })
        {
            using var files = new Files();
            File.WriteAllText(files.PathName, $$"""
                {"sensitivityX":9,"sensitivityY":9,"tapMaxDurationMs":300,"tapMovementThresholdPx":8,"clickHoldMs":25,"doubleTapIntervalMs":130,"motionCadenceHz":{{legacyCadence}}}
                """);
            var loaded = SettingsFileStore.Load(files.PathName);
            Check(loaded.Warning is null, $"legacy cadence {legacyCadence} ignored without warning");
            Equal(new RuntimeSettings(9, 9, 300, 8, 25, 130), loaded.Settings,
                $"legacy cadence {legacyCadence} absent from runtime settings");
            Equal(MotionModes.ProductionMode,
                Rightpad.Receiver.Program.ResolveGuiMotionMode(Rightpad.Receiver.Program.ParseLaunchArguments([])),
                $"legacy cadence {legacyCadence} cannot affect ordinary launch");

            var file = new SettingsFileStore(files.PathName);
            file.Schedule(loaded.Settings);
            await file.FlushAsync();
            using var saved = JsonDocument.Parse(File.ReadAllText(files.PathName));
            Check(!saved.RootElement.TryGetProperty("motionCadenceHz", out _),
                $"legacy cadence {legacyCadence} removed on normal save");
            Equal(8, saved.RootElement.EnumerateObject().Count(), "saved schema has eight product fields");
        }
    }
}
