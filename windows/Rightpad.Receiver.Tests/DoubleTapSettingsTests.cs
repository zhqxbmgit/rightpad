using System.Text.Json;
using static Rightpad.Receiver.Tests.Program;
using static Rightpad.Receiver.Tests.RawMotionProcessorTests;

namespace Rightpad.Receiver.Tests;

internal static class DoubleTapSettingsTests
{
    public static async Task SchemaUpgrade()
    {
        string path = Path.Combine(Path.GetTempPath(), "rightpad-drag-" + Guid.NewGuid().ToString("N") + ".json");
        const string old = """{"sensitivityX":6.95,"sensitivityY":7.1,"tapMaxDurationMs":310,"tapMovementThresholdPx":8.5,"clickHoldMs":26}""";
        try
        {
            File.WriteAllText(path, old);
            var (value, warning) = SettingsFileStore.Load(path);
            Equal(new RuntimeSettings(6.95, 7.1, 310, 8.5, 26, 130), value, "old tuning retained");
            Check(warning is null, "missing new field is a silent schema upgrade");
            var store = new SettingsFileStore(path); store.Schedule(value); await store.FlushAsync();
            using (var saved = JsonDocument.Parse(File.ReadAllText(path)))
                Equal(130, saved.RootElement.GetProperty("doubleTapIntervalMs").GetInt32(), "next save writes new field");
            Equal(value, SettingsFileStore.Load(path).Settings, "all prior tuning survives save");
            foreach (string field in new[] { "\"130\"", "null", "true", "130.5", "49", "1001", "1e999" })
            {
                File.WriteAllText(path, old[..^1] + ",\"doubleTapIntervalMs\":" + field + "}");
                var result = SettingsFileStore.Load(path);
                Equal(value, result.Settings, "invalid new field falls back independently: " + field);
                Check(result.Warning is not null, "present invalid field warns");
            }
            foreach (int valid in new[] { 50, 130, 1000 })
            {
                File.WriteAllText(path, old[..^1] + ",\"doubleTapIntervalMs\":" + valid + "}");
                var result = SettingsFileStore.Load(path);
                Equal(valid, result.Settings.DoubleTapIntervalMs, "inclusive setting range");
                Check(result.Warning is null, "valid field silent");
            }
        }
        finally { File.Delete(path); }
    }
    public static async Task ValidationAndUi()
    {
        Equal(130, RuntimeSettings.Default.DoubleTapIntervalMs, "product default");
        foreach (int value in new[] { int.MinValue, 49, 1001, int.MaxValue })
        {
            var invalid = RuntimeSettings.Default with { DoubleTapIntervalMs = value };
            Check(!invalid.IsProductValid, "product validation");
            Throws<ArgumentOutOfRangeException>(invalid.ValidateCore);
            Throws<ArgumentOutOfRangeException>(() => new RuntimeSettingsStore().Publish(invalid));
        }
        string path = Path.Combine(Path.GetTempPath(), "rightpad-drag-ui-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var file = new SettingsFileStore(path); var store = new RuntimeSettingsStore(); var vm = new SettingsViewModel(store, file);
            Equal("Double Tap Interval", vm.DoubleTapInterval.Label, "existing editor label");
            Equal("ms", vm.DoubleTapInterval.Unit, "unit");
            foreach (string bad in new[] { "49", "1001", "50.5", "abc" })
            {
                vm.DoubleTapInterval.Text = bad; Equal(130, store.Current.DoubleTapIntervalMs, "invalid draft not published");
                Check(vm.DoubleTapInterval.Error.Length > 0, "inline validation");
            }
            vm.DoubleTapInterval.Text = "130"; vm.DoubleTapInterval.Step(1);
            Equal(140, vm.DraftSettings.DoubleTapIntervalMs, "ten ms draft step");
            Equal(130, store.Current.DoubleTapIntervalMs, "draft step does not publish");
            vm.DoubleTapInterval.Text = "151"; Equal(151, vm.DraftSettings.DoubleTapIntervalMs, "integer not quantized to step");
            Check(await vm.SaveAsync(), "explicit Save succeeds");
            Equal(151, store.Current.DoubleTapIntervalMs, "explicit Save publishes");
            Equal(151, SettingsFileStore.Load(path).Settings.DoubleTapIntervalMs, "explicit Save writes immediately");
        }
        finally { File.Delete(path); }
    }
    public static void Arguments()
    {
        Equal(130, Rightpad.Receiver.Program.ParseArguments(["--raw-mouse"]).DoubleTapIntervalMs, "CLI default");
        foreach (string value in new[] { "50", "130", "1000" })
            Equal(int.Parse(value), Rightpad.Receiver.Program.ParseArguments(["--raw-mouse", "--double-tap-interval-ms", value]).DoubleTapIntervalMs, "CLI accepted");
        foreach (string value in new[] { "49", "1001", "130.5", "NaN", "Infinity", "abc", "-1", "2147483648" })
            Throws<ArgumentException>(() => Rightpad.Receiver.Program.ParseArguments(["--raw-mouse", "--double-tap-interval-ms", value]));
        foreach (string[] args in new[] {
            new[] { "--double-tap-interval-ms", "130" }, new[] { "--raw-mouse", "--double-tap-interval-ms" },
            new[] { "--raw-mouse", "--double-tap-interval", "130" },
            new[] { "--raw-mouse", "--double-tap-interval-ms", "130", "--double-tap-interval-ms", "140" } })
            Throws<ArgumentException>(() => Rightpad.Receiver.Program.ParseArguments(args));
    }
}
