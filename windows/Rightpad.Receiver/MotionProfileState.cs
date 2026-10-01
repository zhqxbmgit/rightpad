namespace Rightpad.Receiver;

internal enum MotionProfile : byte { Normal = 0, Cinematic = 1 }

internal readonly record struct MotionProfilePacket(ulong SenderRunId, uint Sequence, MotionProfile Profile);
