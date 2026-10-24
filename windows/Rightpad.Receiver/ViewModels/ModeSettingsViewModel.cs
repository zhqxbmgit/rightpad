namespace Rightpad.Receiver;

internal sealed class ModeSettingsViewModel(Func<ModeBinding> draft,Action<ModeBinding> update) : ObservableModel
{
    public ModeBinding Draft => draft();
    public string Summary => "草稿："+Draft.Summary;
    public string SavedSummary { get; private set; } = "已保存："+draft().Summary;
    public void Select(ModeBindingKind kind,string key) { update(Draft with {Kind=kind,Key=key});Refresh(); }
    public void Behavior(ModePressBehavior behavior) {update(Draft with {Behavior=behavior});Refresh();}
    public void Purpose(ModePurpose purpose) {update(Draft with {Purpose=purpose});Refresh();}
    public void Slot(int slot,ModeBindingKind kind,string key) {update(Draft with {Slots=Draft.Slots.With(slot,new(kind,key))});Refresh();}
    public void Refresh() { Changed(nameof(Draft));Changed(nameof(Summary)); }
    public void Committed(ModeBinding binding) {SavedSummary="已保存："+binding.Summary;Changed(nameof(SavedSummary));Refresh();}
}
