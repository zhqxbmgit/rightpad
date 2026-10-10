namespace Rightpad.Receiver;

internal sealed class Cz1SettingsViewModel : ObservableModel
{
    private readonly RuntimeSettingsStore store;
    private readonly SettingsFileStore file;
    private readonly Func<bool> isBusy;
    private readonly Action<bool> setSaving;
    private readonly Action<Cz1Parameters> saved;
    private Cz1Parameters draft;
    private (Cz1Parameters Selected, Cz1Parameters Active, bool Pending)? runtime;
    private string notice = "";
    public NumericField Amax { get; }
    public NumericField Vmax { get; }
    public NumericField Tau { get; }
    public NumericField Damping { get; }
    public NumericField Glide { get; }
    public NumericField PositionStop { get; }
    public NumericField VelocityStop { get; }
    public IReadOnlyList<NumericField> Fields { get; }
    internal Cz1Parameters Draft => draft;
    public bool CanApply => !isBusy() && ValidDraft && draft != store.RequestedCz1;
    public bool CanSave => !isBusy() && ValidDraft &&
        (draft != store.Current.Cz1 || draft != store.RequestedCz1);
    public bool CanEdit => !isBusy();
    private bool ValidDraft => draft.IsValid && Fields.All(f => f.IsCompleteValid);
    public string Notice { get => notice; private set => Set(ref notice, value); }
    public string SavedText => "已保存（磁盘）：" + store.Current.Cz1.Description;
    public string ActiveText => runtime is { } state ? "已应用（C 快照）：" + state.Active.Description :
        "Receiver 未运行；下次启动 C 快照：" + store.RequestedCz1.Description;
    public string StatusText
    {
        get
        {
            bool dirty = Fields.Any(f => f.IsDirty);
            string edit = dirty ? "候选值未保存" : "候选值已保存";
            if (!ValidDraft) return edit + " · 输入无效或未完成，不能应用或保存";
            string application = runtime is { Pending: true } ? "待生效：等待当前运动链完全空闲" :
                runtime is null ? "等待 Receiver 下次启动" : "已生效（下次 C 触摸使用该快照）";
            if (draft != store.RequestedCz1) application += " · 候选值尚未应用";
            if (store.RequestedCz1 != store.Current.Cz1) application += " · 内存应用值未保存";
            return edit + " · " + application;
        }
    }
    public Cz1SettingsViewModel(RuntimeSettingsStore store, SettingsFileStore file,
        Func<bool> isBusy, Action<bool> setSaving, Action<Cz1Parameters> saved)
    {
        this.store = store; this.file = file; this.isBusy = isBusy;
        this.setSaving = setSaving; this.saved = saved;
        draft = store.Current.Cz1;
        NumericField Field(string label, string unit, double value, (double Min, double Max) range,
            double step, Func<Cz1Parameters, double, Cz1Parameters> update) =>
            new(label, unit, value, range.Min, range.Max, step, "G", false,
                v => { draft = update(draft, v); Refresh(); }, Refresh);
        Amax = Field("C 最大加速度 Amax", "counts/s²", draft.MaxAcceleration, Cz1Parameters.AccelerationRange,
            1000, (p, v) => p with { MaxAcceleration = v });
        Vmax = Field("C 最大速度 Vmax", "counts/s", draft.MaxVelocity, Cz1Parameters.VelocityRange,
            100, (p, v) => p with { MaxVelocity = v });
        Tau = Field("C 时间常数 Tau", "ms", draft.TauMs, Cz1Parameters.TauRange,
            1, (p, v) => p with { TauMs = v });
        Damping = Field("C 阻尼比", "无量纲", draft.DampingRatio, Cz1Parameters.DampingRange,
            .05, (p, v) => p with { DampingRatio = v });
        Glide = Field("C 滑行减速度 Glide", "counts/s²", draft.GlideDeceleration, Cz1Parameters.GlideRange,
            1000, (p, v) => p with { GlideDeceleration = v });
        PositionStop = Field("C 位置停止阈值", "counts", draft.PositionThreshold, Cz1Parameters.PositionRange,
            .01, (p, v) => p with { PositionThreshold = v });
        VelocityStop = Field("C 速度停止阈值", "counts/s", draft.VelocityThreshold, Cz1Parameters.StopVelocityRange,
            .1, (p, v) => p with { VelocityThreshold = v });
        Fields = [Amax, Vmax, Tau, Damping, Glide, PositionStop, VelocityStop];
    }
    public void Refresh((Cz1Parameters Selected, Cz1Parameters Active, bool Pending)? state)
    { runtime = state; Refresh(); }
    public void Refresh()
    {
        if (Fields is null) return;
        foreach (var field in Fields) field.SetEnabled(CanEdit);
        foreach (var name in new[] { nameof(CanApply), nameof(CanSave), nameof(CanEdit),
            nameof(StatusText), nameof(SavedText), nameof(ActiveText) }) Changed(name);
    }
    public void Apply()
    {
        if (!CanApply) return;
        store.ApplyCz1(draft);
        Notice = "已提交完整 C 参数快照；忙时等待自然空闲，未写入磁盘。";
        Refresh();
    }
    public async Task<bool> SaveAsync()
    {
        if (!CanSave) return false;
        var parameters = draft;
        var snapshot = store.Current with { Cz1 = parameters };
        setSaving(true);
        try
        {
            if (!await file.SaveNowAsync(snapshot))
            { Notice = file.SaveError ?? "C 参数保存失败，应用值保持不变。"; return false; }
            store.Publish(snapshot);
            // Also request when saving the disk baseline after a memory-only Apply.
            store.ApplyCz1(parameters);
            saved(parameters);
            foreach (var (field, value) in Fields.Zip(new[] { parameters.MaxAcceleration, parameters.MaxVelocity,
                parameters.TauMs, parameters.DampingRatio, parameters.GlideDeceleration,
                parameters.PositionThreshold, parameters.VelocityThreshold })) field.SetCommitted(value);
            Notice = "C 参数已保存并提交；运行中等待安全边界，无需重新编译或重启。";
            return true;
        }
        finally { setSaving(false); Refresh(); }
    }
    public void RestoreDefaults()
    {
        if (!CanEdit) return;
        foreach (var (field, value) in Fields.Zip(new[] { 80000d, 15000, 35, 1, 120000, .5, 2 }))
            field.Text = value.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        Notice = "仅恢复 C 候选值；请应用或保存。";
        Refresh();
    }
}
