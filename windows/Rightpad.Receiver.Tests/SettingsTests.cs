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
        Equal(6, doc.RootElement.EnumerateObject().Count(), "only six fields");
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
        vm.SensitivityX.Text = "6.95";
        await file.FlushAsync();
        vm.RefreshNotice();
        Equal(6.95, store.Current.SensitivityX, "failure keeps runtime value");
        Equal("Settings active, save failed.", vm.Notice, "nonblocking failure");
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
        foreach (var draft in new[] { "", "-", "6.", "abc", "0", "31", "NaN" })
        {
            vm.SensitivityX.Text = draft;
            Equal(7.0, store.Current.SensitivityX, "draft rejected");
        }
        vm.SensitivityX.Text = "6.95";
        Equal(6.95, store.Current.SensitivityX, "immediate publish");
        vm.SensitivityX.Step(1);
        Equal("7.00", vm.SensitivityX.Text, "precision and step");
        vm.TapMaxDuration.Text = "301";
        Equal(301, store.Current.TapMaxDurationMs, "step not a quantization restriction");
        await file.FlushAsync();
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
            Equal(6, saved.RootElement.EnumerateObject().Count(), "saved schema remains six fields");
        }
    }
}
