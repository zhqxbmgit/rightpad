using System.Globalization;

namespace Rightpad.Receiver;

internal sealed class NumericField : ObservableModel
{
    private readonly double min, max, step;
    private readonly bool integer;
    private readonly string format;
    private readonly Action<double> publish;
    private double value;
    private string text;
    private string error = "";
    public string Label { get; }
    public string Unit { get; }
    public string Error { get => error; private set => Set(ref error, value); }
    public string Text
    {
        get => text;
        set
        {
            Set(ref text, value);
            string draft = value.Trim();
            if (draft.Length == 0 || draft is "-" or "." || draft.EndsWith('.'))
            { Error = ""; return; }
            if (!double.TryParse(draft, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out double number) ||
                !RuntimeSettings.InRange(number, min, max) || (integer && number != Math.Truncate(number)))
            {
                Error = $"Enter {min.ToString(CultureInfo.InvariantCulture)}–{max.ToString(CultureInfo.InvariantCulture)}{(integer ? " (whole numbers)" : "")}.";
                return;
            }
            Error = "";
            if (this.value == number) return;
            this.value = number;
            publish(number);
        }
    }

    public NumericField(string label, string unit, double value, double min, double max,
        double step, string format, bool integer, Action<double> publish)
    {
        Label = label; Unit = unit; this.value = value; this.min = min; this.max = max;
        this.step = step; this.format = format; this.integer = integer; this.publish = publish;
        text = value.ToString(format, CultureInfo.InvariantCulture);
    }
    public void Step(int direction) => Text = Math.Clamp(Math.Round(value + step * direction, 8), min, max)
        .ToString(format, CultureInfo.InvariantCulture);
    public void Normalize()
    {
        if (Error.Length == 0) Text = value.ToString(format, CultureInfo.InvariantCulture);
    }
    public void Restore()
    {
        Error = "";
        Set(ref text, value.ToString(format, CultureInfo.InvariantCulture), nameof(Text));
    }
}

internal sealed class SettingsViewModel : ObservableModel
{
    private readonly RuntimeSettingsStore store;
    private readonly SettingsFileStore file;
    private string? loadWarning;
    private string notice = "";
    public string Notice { get => notice; private set => Set(ref notice, value); }
    public NumericField SensitivityX { get; }
    public NumericField SensitivityY { get; }
    public NumericField TapMaxDuration { get; }
    public NumericField MovementThreshold { get; }
    public NumericField ClickHold { get; }

    public SettingsViewModel(RuntimeSettingsStore store, SettingsFileStore file, string? warning = null)
    {
        this.store = store; this.file = file; loadWarning = warning;
        var s = store.Current;
        SensitivityX = new("Sensitivity X", "", s.SensitivityX, .1, 30, .05, "F2", false,
            x => Update(store.Current with { SensitivityX = x }));
        SensitivityY = new("Sensitivity Y", "", s.SensitivityY, .1, 30, .05, "F2", false,
            y => Update(store.Current with { SensitivityY = y }));
        TapMaxDuration = new("Tap Max Duration", "ms", s.TapMaxDurationMs, 50, 1500, 10, "0", true,
            x => Update(store.Current with { TapMaxDurationMs = (int)x }));
        MovementThreshold = new("Movement Threshold", "px", s.TapMovementThresholdPx, .5, 100, .5, "0.##", false,
            x => Update(store.Current with { TapMovementThresholdPx = x }));
        ClickHold = new("Click Hold", "ms", s.ClickHoldMs, 1, 200, 1, "0", true,
            x => Update(store.Current with { ClickHoldMs = (int)x }));
        RefreshNotice();
    }
    private void Update(RuntimeSettings settings)
    {
        if (!settings.IsProductValid) return;
        store.Publish(settings);
        file.Schedule(settings);
        loadWarning = null;
        RefreshNotice();
    }
    public void RefreshNotice() => Notice = file.SaveError ?? loadWarning ?? "";
}
