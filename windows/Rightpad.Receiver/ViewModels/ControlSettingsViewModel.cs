namespace Rightpad.Receiver;

internal sealed class ControlSettingsViewModel
{
    public ControlDefinition Definition { get; }
    public string Name => Definition.Name;
    public NumericField Up { get; }
    public NumericField? Down { get; }
    public NumericField? Left { get; }
    public NumericField? Right { get; }
    public NumericField TapHold { get; }
    public NumericField LongPress { get; }
    public NumericField[] Fields { get; }
    public ControlSettingsViewModel(ControlDefinition definition, Func<IControlSettings> draft,
        Action<IControlSettings> update, Action changed)
    {
        Definition = definition;
        NumericField Threshold(string label, double value, Action<double> setter) =>
            new(label, "dp", value, .1, 50, .1, "F1", false, setter, changed, 1);
        NumericField Timing(string label, int value, bool tap, Action<double> setter) =>
            new(label, "ms", value, tap ? 1 : 50, tap ? 200 : 2000, tap ? 1 : 10, "0", true, setter, changed, 0);
        if (draft() is SlideControlSettings b) {
            SlideControlSettings Current() => (SlideControlSettings)draft();
            Up = Threshold("Slide Up Threshold", b.SlideUpThresholdDp, v => update(Current() with { SlideUpThresholdDp = v }));
            Down = Threshold("Slide Down Threshold", b.SlideDownThresholdDp, v => update(Current() with { SlideDownThresholdDp = v }));
            TapHold = Timing("Tap Hold", b.TapHoldMs, true, v => update(Current() with { TapHoldMs = (int)v }));
            LongPress = Timing("Long Press", b.LongPressMs, false, v => update(Current() with { LongPressMs = (int)v }));
            Fields = [Up, Down, TapHold, LongPress];
        } else if (draft() is SlideControlLRSettings x) {
            SlideControlLRSettings Current() => (SlideControlLRSettings)draft();
            Left = Threshold("X Left Threshold", x.SlideLeftThresholdDp, v => update(Current() with { SlideLeftThresholdDp = v }));
            Right = Threshold("X Right Threshold", x.SlideRightThresholdDp, v => update(Current() with { SlideRightThresholdDp = v }));
            Up = Threshold("X Up Threshold", x.SlideUpThresholdDp, v => update(Current() with { SlideUpThresholdDp = v }));
            TapHold = Timing("X Tap Hold", x.TapHoldMs, true, v => update(Current() with { TapHoldMs = (int)v }));
            LongPress = Timing("X Long Press", x.LongPressMs, false, v => update(Current() with { LongPressMs = (int)v }));
            Fields = [Left, Right, Up, TapHold, LongPress];
        } else throw new ArgumentException("Unknown control settings", nameof(draft));
    }
    public void SetCommitted(IControlSettings settings)
    {
        switch (settings) {
            case SlideControlSettings s:
                Up.SetCommitted(s.SlideUpThresholdDp); Down!.SetCommitted(s.SlideDownThresholdDp);
                TapHold.SetCommitted(s.TapHoldMs); LongPress.SetCommitted(s.LongPressMs);
                break;
            case SlideControlLRSettings s:
                Left!.SetCommitted(s.SlideLeftThresholdDp); Right!.SetCommitted(s.SlideRightThresholdDp);
                Up.SetCommitted(s.SlideUpThresholdDp); TapHold.SetCommitted(s.TapHoldMs); LongPress.SetCommitted(s.LongPressMs);
                break;
        }
    }
}
