using System.Text.Json.Serialization;

namespace Rightpad.Receiver;

[JsonConverter(typeof(JsonStringEnumConverter<TouchMode>))]
internal enum TouchMode { M_R1, C_Z1 }

internal static class TouchModes
{
    public static bool IsValid(TouchMode mode) => mode is TouchMode.M_R1 or TouchMode.C_Z1;
    public static MotionProfile Profile(TouchMode mode) => mode switch
    {
        TouchMode.M_R1 => MotionProfile.Normal,
        TouchMode.C_Z1 => MotionProfile.Cinematic,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
    public static TouchMode FromProfile(MotionProfile profile) =>
        profile == MotionProfile.Cinematic ? TouchMode.C_Z1 : TouchMode.M_R1;
    public static string Label(TouchMode mode) => mode == TouchMode.C_Z1 ? "C — 重量模式 (C-Z1)" : "M — 直接模式 (M-R1)";
}
