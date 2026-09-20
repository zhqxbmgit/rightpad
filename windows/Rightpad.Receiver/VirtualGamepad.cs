namespace Rightpad.Receiver;

// ABI logical flags. These are deliberately independent of XInput's wire button masks.
[Flags]
internal enum XboxGamepadButtons : ushort
{
    None = 0, A = 1 << 0, B = 1 << 1, X = 1 << 2, Y = 1 << 3,
    Back = 1 << 4, Start = 1 << 5, Guide = 1 << 6,
    LeftStick = 1 << 7, RightStick = 1 << 8, LeftShoulder = 1 << 9, RightShoulder = 1 << 10,
    DpadUp = 1 << 11, DpadDown = 1 << 12, DpadLeft = 1 << 13, DpadRight = 1 << 14
}

internal readonly record struct XboxGamepadState(XboxGamepadButtons Buttons = XboxGamepadButtons.None,
    byte LeftTrigger = 0, byte RightTrigger = 0, short LeftThumbX = 0, short LeftThumbY = 0,
    short RightThumbX = 0, short RightThumbY = 0)
{
    public static XboxGamepadState Neutral => default;
}

internal readonly record struct GamepadOutputStats(long Successes = 0, long Failures = 0);

internal interface IVirtualGamepad : IDisposable
{
    string BackendName { get; }
    string DeviceIdentity { get; }
    bool Available { get; }
    string? LastError { get; }
    GamepadOutputStats Stats { get; }
    bool SetState(XboxGamepadState state);
}

internal interface IVirtualHidGamepad : IDisposable
{
    string DeviceIdentity { get; }
    void SetState(XboxGamepadState state);
}
