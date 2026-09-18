using System.Globalization;

namespace Rightpad.Receiver;

internal sealed class NumericField : ObservableModel
{
    private readonly double min, max, step;
    private readonly bool integer;
    private readonly int? maximumDecimalPlaces;
    private readonly string format;
    private readonly Action<double> updateDraft;
    private readonly Action stateChanged;
    private double value, committedValue;
    private string text;
    private string error = "";
    private bool isCompleteValid = true;
    private bool isEnabled = true;
    public string Label { get; }
    public string Unit { get; }
    public string Error { get => error; private set => Set(ref error, value); }
    public bool IsCompleteValid { get => isCompleteValid; private set => Set(ref isCompleteValid, value); }
    public bool IsEnabled { get => isEnabled; private set => Set(ref isEnabled, value); }
    public bool IsDirty => IsCompleteValid ? value != committedValue :
        text.Trim() != committedValue.ToString(format, CultureInfo.InvariantCulture);
    public string Text
    {
        get => text;
        set
        {
            if (!IsEnabled) return;
            Set(ref text, value);
            string draft = value.Trim();
            if (draft.Length == 0 || draft is "-" or "." || draft.EndsWith('.'))
            {
                Error = "";
                IsCompleteValid = false;
                stateChanged();
                return;
            }
            if (!double.TryParse(draft, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out double number) ||
                !RuntimeSettings.InRange(number, min, max) || (integer && number != Math.Truncate(number)) ||
                !HasValidDecimalPlaces(draft))
            {
                string precision = integer ? " (whole numbers)" : maximumDecimalPlaces is int places
                    ? $" (up to {places} decimal place{(places == 1 ? "" : "s")})" : "";
                Error = $"Enter {min.ToString(CultureInfo.InvariantCulture)}–{max.ToString(CultureInfo.InvariantCulture)}{precision}.";
                IsCompleteValid = false;
                stateChanged();
                return;
            }
            Error = "";
            IsCompleteValid = true;
            if (this.value != number)
            {
                this.value = number;
                updateDraft(number);
            }
            else stateChanged();
        }
    }

    public NumericField(string label, string unit, double value, double min, double max,
        double step, string format, bool integer, Action<double> updateDraft, Action stateChanged,
        int? maximumDecimalPlaces = null)
    {
        Label = label; Unit = unit; this.value = committedValue = value; this.min = min; this.max = max;
        this.step = step; this.format = format; this.integer = integer;
        this.maximumDecimalPlaces = maximumDecimalPlaces;
        this.updateDraft = updateDraft; this.stateChanged = stateChanged;
        text = value.ToString(format, CultureInfo.InvariantCulture);
    }
    private bool HasValidDecimalPlaces(string draft)
    {
        if (maximumDecimalPlaces is not int places) return true;
        int decimalPoint = draft.IndexOf('.');
        return decimalPoint < 0 || draft.Length - decimalPoint - 1 <= places;
    }
    public void Step(int direction)
    {
        if (IsEnabled) Text = Math.Clamp(Math.Round(value + step * direction, 8), min, max)
            .ToString(format, CultureInfo.InvariantCulture);
    }
    public void Normalize()
    {
        if (IsEnabled && IsCompleteValid) Text = value.ToString(format, CultureInfo.InvariantCulture);
    }
    public void Restore()
    {
        if (!IsEnabled) return;
        Error = "";
        IsCompleteValid = true;
        value = committedValue;
        Set(ref text, value.ToString(format, CultureInfo.InvariantCulture), nameof(Text));
        updateDraft(value);
    }
    public void SetEnabled(bool enabled) => IsEnabled = enabled;
    public void SetCommitted(double committed) => committedValue = committed;
}

internal sealed class SettingsViewModel : ObservableModel
{
    private readonly RuntimeSettingsStore store;
    private readonly SettingsFileStore file;
    private readonly NumericField[] fields;
    private RuntimeSettings committedSettings;
    private RuntimeSettings draftSettings;
    private string? loadWarning;
    private string notice = "";
    private bool hasUnsavedChanges;
    private bool canSave;
    private bool isSaving;
    private bool isRestarting;
    public RuntimeSettings CommittedSettings => committedSettings;
    public string Notice { get => notice; private set => Set(ref notice, value); }
    public bool HasUnsavedChanges { get => hasUnsavedChanges; private set => Set(ref hasUnsavedChanges, value); }
    public bool CanSave { get => canSave; private set => Set(ref canSave, value); }
    public bool IsSaving { get => isSaving; private set => Set(ref isSaving, value); }
    internal RuntimeSettings DraftSettings => draftSettings;
    public NumericField SensitivityX { get; }
    public NumericField SensitivityY { get; }
    public NumericField SmoothingTau { get; }
    public NumericField SmoothingSupport { get; }
    public NumericField TapMaxDuration { get; }
    public NumericField MovementThreshold { get; }
    public NumericField ClickHold { get; }
    public NumericField DoubleTapInterval { get; }

    public SettingsViewModel(RuntimeSettingsStore store, SettingsFileStore file, string? warning = null)
    {
        this.store = store; this.file = file; loadWarning = warning;
        committedSettings = draftSettings = store.Current;
        SensitivityX = new("Sensitivity X", "", draftSettings.SensitivityX, .1, 30, .5, "F1", false,
            x => UpdateDraft(draftSettings with { SensitivityX = x }), Recalculate, 1);
        SensitivityY = new("Sensitivity Y", "", draftSettings.SensitivityY, .1, 30, .5, "F1", false,
            y => UpdateDraft(draftSettings with { SensitivityY = y }), Recalculate, 1);
        SmoothingTau = new("Smoothing Tau", "ms", draftSettings.SmoothingTauMs, 8, 60, 1, "0", true,
            x => UpdateDraft(draftSettings with { SmoothingTauMs = (int)x }), Recalculate, 0);
        SmoothingSupport = new("Support", "ms", draftSettings.SmoothingSupportMs, 40, 300, 5, "0", true,
            x => UpdateDraft(draftSettings with { SmoothingSupportMs = (int)x }), Recalculate, 0);
        TapMaxDuration = new("Tap Max Duration", "ms", draftSettings.TapMaxDurationMs, 50, 1500, 10, "0", true,
            x => UpdateDraft(draftSettings with { TapMaxDurationMs = (int)x }), Recalculate);
        MovementThreshold = new("Movement Threshold", "px", draftSettings.TapMovementThresholdPx, .5, 100, .5, "0.##", false,
            x => UpdateDraft(draftSettings with { TapMovementThresholdPx = x }), Recalculate);
        ClickHold = new("Click Hold", "ms", draftSettings.ClickHoldMs, 1, 200, 1, "0", true,
            x => UpdateDraft(draftSettings with { ClickHoldMs = (int)x }), Recalculate);
        DoubleTapInterval = new("Double Tap Interval", "ms", draftSettings.DoubleTapIntervalMs, 50, 1000, 10, "0", true,
            x => UpdateDraft(draftSettings with { DoubleTapIntervalMs = (int)x }), Recalculate);
        fields = [SensitivityX, SensitivityY, SmoothingTau, SmoothingSupport,
            TapMaxDuration, MovementThreshold, ClickHold, DoubleTapInterval];
        Recalculate();
        RefreshNotice();
    }
    private void UpdateDraft(RuntimeSettings settings)
    {
        draftSettings = settings;
        Recalculate();
    }
    private void Recalculate()
    {
        if (fields is null) return;
        HasUnsavedChanges = draftSettings != committedSettings ||
            fields.Any(field => !field.IsCompleteValid && field.IsDirty);
        CanSave = HasUnsavedChanges && fields.All(field => field.IsCompleteValid) && !IsSaving && !isRestarting;
    }
    private void SetSaving(bool saving)
    {
        IsSaving = saving;
        foreach (var field in fields) field.SetEnabled(!saving && !isRestarting);
        Recalculate();
    }
    public void SetRestarting(bool restarting)
    {
        isRestarting = restarting;
        foreach (var field in fields) field.SetEnabled(!IsSaving && !restarting);
        Recalculate();
    }
    public async Task<bool> SaveAsync()
    {
        if (!CanSave) return false;
        RuntimeSettings snapshot = draftSettings;
        if (!snapshot.IsProductValid) return false;
        SetSaving(true);
        try
        {
            if (!await file.SaveNowAsync(snapshot))
            {
                Notice = file.SaveError ?? "Settings save failed. Changes were not applied.";
                return false;
            }
            store.Publish(snapshot);
            committedSettings = snapshot;
            SensitivityX.SetCommitted(snapshot.SensitivityX);
            SensitivityY.SetCommitted(snapshot.SensitivityY);
            SmoothingTau.SetCommitted(snapshot.SmoothingTauMs);
            SmoothingSupport.SetCommitted(snapshot.SmoothingSupportMs);
            TapMaxDuration.SetCommitted(snapshot.TapMaxDurationMs);
            MovementThreshold.SetCommitted(snapshot.TapMovementThresholdPx);
            ClickHold.SetCommitted(snapshot.ClickHoldMs);
            DoubleTapInterval.SetCommitted(snapshot.DoubleTapIntervalMs);
            loadWarning = null;
            Notice = "";
            return true;
        }
        finally { SetSaving(false); }
    }
    public void RefreshNotice() => Notice = file.SaveError ?? loadWarning ?? "";
}
