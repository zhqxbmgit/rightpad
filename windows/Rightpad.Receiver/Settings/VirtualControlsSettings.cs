namespace Rightpad.Receiver;

internal interface IControlSettings { bool IsValid { get; } }

internal sealed record SlideControlSettings(double SlideUpThresholdDp = .7, double SlideDownThresholdDp = 3,
    int TapHoldMs = 25, int LongPressMs = 400) : IControlSettings
{
    public static bool ValidThreshold(double value) => RuntimeSettings.InRange(value, .1, 50)
        && (decimal)value == decimal.Round((decimal)value, 1);
    public bool IsValid => ValidThreshold(SlideUpThresholdDp) && ValidThreshold(SlideDownThresholdDp)
        && TapHoldMs is >= 1 and <= 200 && LongPressMs is >= 50 and <= 2000;
}

internal sealed record SlideControlLRSettings(double SlideLeftThresholdDp = 12, double SlideRightThresholdDp = 3,
    double SlideUpThresholdDp = 2, int TapHoldMs = 25, int LongPressMs = 400) : IControlSettings
{
    public bool IsValid => SlideControlSettings.ValidThreshold(SlideLeftThresholdDp)
        && SlideControlSettings.ValidThreshold(SlideRightThresholdDp) && SlideControlSettings.ValidThreshold(SlideUpThresholdDp)
        && TapHoldMs is >= 1 and <= 200 && LongPressMs is >= 50 and <= 2000;
}

internal sealed record VirtualControlsSettings
{
    public SlideControlSettings B { get; init; } = new();
    public SlideControlLRSettings X { get; init; } = new();
    public static VirtualControlsSettings Default { get; } = new();
    public bool IsValid => ControlDefinitions.All.All(d => d.Get(this)?.IsValid == true);
}

internal enum ControlBehavior : byte { Slide = 1, SlideLR = 2 }
internal sealed record ControlDefinition(ushort ProtocolId, string Id, string JsonKey, string Name, ControlBehavior Behavior,
    Func<VirtualControlsSettings, IControlSettings> Get,
    Func<VirtualControlsSettings, IControlSettings, VirtualControlsSettings> Set);

internal static class ControlDefinitions
{
    // Registration is the only place that associates a particular control with settings/UI/wire ID.
    public static readonly IReadOnlyList<ControlDefinition> All = Array.AsReadOnly(new[] {
        new ControlDefinition(1, "xbox.b.slide", "b", "B Slide Control", ControlBehavior.Slide,
            s => s.B, (s, v) => s with { B = (SlideControlSettings)v }),
        new ControlDefinition(2, "xbox.x.slide_lr", "x", "X / Slide LR", ControlBehavior.SlideLR,
            s => s.X, (s, v) => s with { X = (SlideControlLRSettings)v })
    });
}
