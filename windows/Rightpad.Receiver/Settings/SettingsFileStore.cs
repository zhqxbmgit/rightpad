using System.IO;
using System.Text.Json;

namespace Rightpad.Receiver;

internal sealed class SettingsFileStore(string path)
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "rightpad", "settings.json");
    public static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);
    private readonly object gate = new();
    private readonly SemaphoreSlim writer = new(1, 1);
    private CancellationTokenSource? delayCancellation;
    private RuntimeSettings? latest;
    private long revision, savedRevision;
    private bool closing;
    private string? saveError;
    public string? SaveError => Volatile.Read(ref saveError);

    public static (RuntimeSettings Settings, string? Warning) Load(string path)
    {
        if (!File.Exists(path)) return (RuntimeSettings.Default, null);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return (RuntimeSettings.Default, "Settings could not be read. Defaults are active.");
            var root = document.RootElement;
            bool fallback = false;
            double Read(string name, double defaultValue, double min, double max, bool integer = false)
            {
                if (root.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.Number &&
                    item.TryGetDouble(out double value) && RuntimeSettings.InRange(value, min, max) &&
                    (!integer || value == Math.Truncate(value))) return value;
                fallback = true;
                return defaultValue;
            }
            double ReadOptional(string name, double defaultValue, double min, double max, bool integer = false) =>
                root.TryGetProperty(name, out _) ? Read(name, defaultValue, min, max, integer) : defaultValue;
            var settings = new RuntimeSettings(Read("sensitivityX", 7, .1, 30), Read("sensitivityY", 7, .1, 30),
                (int)Read("tapMaxDurationMs", 300, 50, 1500, true), Read("tapMovementThresholdPx", 8, .5, 100),
                (int)Read("clickHoldMs", 25, 1, 200, true),
                (int)ReadOptional("doubleTapIntervalMs", RuntimeSettings.Default.DoubleTapIntervalMs, 50, 1000, true),
                (int)ReadOptional("smoothingTauMs", RuntimeSettings.DefaultSmoothingTauMs, 8, 60, true),
                (int)ReadOptional("smoothingSupportMs", RuntimeSettings.DefaultSmoothingSupportMs, 40, 300, true));
            var controls = VirtualControlsSettings.Default;
            if (root.TryGetProperty("controls", out var controlsJson))
            {
                foreach (var definition in ControlDefinitions.All)
                {
                    var defaults = definition.Get(VirtualControlsSettings.Default);
                    JsonElement record = default;
                    if (controlsJson.ValueKind != JsonValueKind.Object || !controlsJson.TryGetProperty(definition.JsonKey, out record)
                        || record.ValueKind != JsonValueKind.Object) { fallback = true; continue; }
                    double Field(string key, double defaultValue, double min, double max, bool threshold = false)
                    {
                        if (record.TryGetProperty(key, out var element) && element.ValueKind == JsonValueKind.Number
                            && element.TryGetDouble(out var number) && RuntimeSettings.InRange(number, min, max)
                            && (threshold ? SlideControlSettings.ValidThreshold(number) : number == Math.Truncate(number))) return number;
                        fallback = true; return defaultValue;
                    }
                    IControlSettings loaded = defaults switch {
                        SlideControlSettings s => new SlideControlSettings(
                            Field("slideUpThresholdDp", s.SlideUpThresholdDp, .1, 50, true),
                            Field("slideDownThresholdDp", s.SlideDownThresholdDp, .1, 50, true),
                            (int)Field("tapHoldMs", s.TapHoldMs, 1, 200),
                            (int)Field("longPressMs", s.LongPressMs, 50, 2000)),
                        SlideControlLRSettings s => new SlideControlLRSettings(
                            Field("slideLeftThresholdDp", s.SlideLeftThresholdDp, .1, 50, true),
                            Field("slideRightThresholdDp", s.SlideRightThresholdDp, .1, 50, true),
                            Field("slideUpThresholdDp", s.SlideUpThresholdDp, .1, 50, true),
                            (int)Field("tapHoldMs", s.TapHoldMs, 1, 200),
                            (int)Field("longPressMs", s.LongPressMs, 50, 2000)),
                        _ => throw new InvalidOperationException("Unknown control settings")
                    };
                    controls = definition.Set(controls, loaded);
                }
            }
            settings = settings with { Controls = controls };
            return (settings, fallback ? "Some settings were invalid or missing. Defaults were used for those fields." : null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return (RuntimeSettings.Default, "Settings could not be read. Defaults are active.");
        }
    }

    public void Schedule(RuntimeSettings settings)
    {
        if (!settings.IsProductValid) throw new ArgumentOutOfRangeException(nameof(settings));
        lock (gate)
        {
            if (closing) return;
            latest = settings;
            revision++;
            delayCancellation?.Cancel();
            delayCancellation?.Dispose();
            delayCancellation = new();
            _ = SaveAfterDelayAsync(delayCancellation.Token);
        }
    }

    private async Task SaveAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(SaveDelay, token).ConfigureAwait(false);
            await WriteLatestAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    public async Task<bool> SaveNowAsync(RuntimeSettings settings)
    {
        if (!settings.IsProductValid) throw new ArgumentOutOfRangeException(nameof(settings));
        long immediateRevision;
        lock (gate)
        {
            if (closing)
            {
                Volatile.Write(ref saveError, "Settings save failed. Changes were not applied.");
                return false;
            }
            latest = settings;
            revision++;
            immediateRevision = revision;
            delayCancellation?.Cancel();
            delayCancellation?.Dispose();
            delayCancellation = null;
        }
        bool saved = await WriteLatestAsync().ConfigureAwait(false);
        if (!saved)
        {
            lock (gate)
            {
                // A failed explicit Save remains a UI draft. Exit Flush must not retry it.
                if (revision == immediateRevision)
                {
                    latest = null;
                    savedRevision = immediateRevision;
                }
            }
        }
        return saved;
    }

    private async Task<bool> WriteLatestAsync()
    {
        await writer.WaitAsync().ConfigureAwait(false);
        try
        {
            RuntimeSettings? value;
            long writingRevision;
            lock (gate)
            {
                value = latest;
                writingRevision = revision;
                if (value is null || writingRevision == savedRevision) return true;
            }
            // File operations run off the UI thread, including directory creation and replacement.
            await Task.Run(async () =>
            {
                string target = Path.GetFullPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string temporary = target + ".tmp";
                try
                {
                    string json = JsonSerializer.Serialize(value, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                        // Serialize positional values, not computed validation properties.
                        IgnoreReadOnlyProperties = true
                    });
                    await File.WriteAllTextAsync(temporary, json).ConfigureAwait(false);
                    File.Move(temporary, target, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }).ConfigureAwait(false);
            lock (gate) savedRevision = writingRevision;
            Volatile.Write(ref saveError, null);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Volatile.Write(ref saveError, "Settings save failed. Changes were not applied.");
            return false;
        }
        finally { writer.Release(); }
    }

    public async Task FlushAsync()
    {
        lock (gate)
        {
            closing = true;
            delayCancellation?.Cancel();
            delayCancellation?.Dispose();
            delayCancellation = null;
        }
        await WriteLatestAsync().ConfigureAwait(false);
    }
}
