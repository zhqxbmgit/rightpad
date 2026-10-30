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
    public void GestureAction(int entry,ModeBindingKind kind,string key,bool menu=false) {
        var action=new ModeGestureAction(kind,key,menu);
        var next=Draft.Gesture.With(entry,action);
        if(!next.IsValid) return;
        update(Draft with {Gesture=next});Refresh();
    }
    public void Width(int entry,int direction) {
        var g=Draft.Gesture;
        double value=Math.Clamp((entry==0?g.FirstWidthDp:g.SecondWidthDp)+direction*.5,1,50);
        update(Draft with {Gesture=entry==0?g with {FirstWidthDp=value}:g with {SecondWidthDp=value}});Refresh();
    }
    public void Refresh() { Changed(nameof(Draft));Changed(nameof(Summary)); }
    public void Committed(ModeBinding binding) {SavedSummary="已保存："+binding.Summary;Changed(nameof(SavedSummary));Refresh();}
}
