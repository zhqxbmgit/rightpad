namespace Rightpad.Receiver;

internal readonly record struct GamepadStatePacket(ulong SenderRunId, uint Sequence, XboxGamepadState State,
    ushort MinimumDwellMs = 0, bool ForceNeutral = false);
