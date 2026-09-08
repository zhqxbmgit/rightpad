using Rightpad.Receiver;
using static Rightpad.Receiver.Tests.Program;
using static Rightpad.Receiver.Tests.RawMotionProcessorTests;

namespace Rightpad.Receiver.Tests;

internal static class TouchSessionProcessorTests
{
    internal static TouchPacket Packet(TouchEventType type, uint session, params TouchSample[] samples) =>
        new(new PacketHeader(1, type, (ushort)samples.Length, session, 0), samples);

    public static void DeltasAndUp()
    {
        var moves = new List<(int, int)>();
        var session = new TouchSessionProcessor((x, y) => moves.Add((x, y)));
        session.Process(Packet(TouchEventType.Down, 1, new TouchSample(100, 100, 200)));
        Equal(0, moves.Count, "DOWN must not move");
        session.Process(Packet(TouchEventType.Move, 1, new TouchSample(101, 105, 197)));
        session.Process(Packet(TouchEventType.Up, 1, new TouchSample(102, 107, 196)));
        Check(moves.SequenceEqual(new[] { (5, -3), (2, -1) }), "MOVE and UP final delta");
        Check(session.ActiveSessionId is null, "UP ends session");
        session.Process(Packet(TouchEventType.Move, 1, new TouchSample(103, 1000, 1000)));
        Equal(2, moves.Count, "post-UP MOVE ignored");
    }

    public static void SampleOrder()
    {
        var moves = new List<(int, int)>();
        var session = new TouchSessionProcessor((x, y) => moves.Add((x, y)));
        session.Process(Packet(TouchEventType.Down, 1, new TouchSample(100, 100, 100)));
        session.Process(Packet(TouchEventType.Move, 1,
            new TouchSample(300, 105, 100), new TouchSample(300, 102, 100), new TouchSample(200, 103, 98)));
        session.Process(Packet(TouchEventType.Up, 1, new TouchSample(400, 103, 98)));
        Check(moves.SequenceEqual(new[] { (5, 0), (-3, 0), (1, -2) }), "no coalescing, sorting or timestamp scheduling");
        Equal(4L, session.ProcessedMotionSamples, "includes zero-delta UP");
    }

    public static void SessionIsolation()
    {
        var moves = new List<(int, int)>();
        var session = new TouchSessionProcessor((x, y) => moves.Add((x, y)));
        session.Process(Packet(TouchEventType.Move, 1, new TouchSample(0, 999, 999)));
        session.Process(Packet(TouchEventType.Up, 1, new TouchSample(0, 999, 999)));
        session.Process(Packet(TouchEventType.Down, 1, new TouchSample(0, 0, 0)));
        session.Process(Packet(TouchEventType.Move, 1, new TouchSample(0, .75f, -.75f)));
        session.Process(Packet(TouchEventType.Move, 2, new TouchSample(0, 999, 999)));
        session.Process(Packet(TouchEventType.Up, 2, new TouchSample(0, 999, 999)));
        session.Process(Packet(TouchEventType.Move, 1, new TouchSample(0, 1.25f, -1.25f)));
        Check(moves.SequenceEqual(new[] { (1, -1) }), "wrong session cannot mutate coordinates/residual");
        Equal(4L, session.IgnoredSessionPackets, "ignored sessions");
    }

    public static void SessionReset()
    {
        var moves = new List<(int, int)>();
        var session = new TouchSessionProcessor((x, y) => moves.Add((x, y)));
        foreach (uint id in new uint[] { 1, 2, 2 })
        {
            session.Process(Packet(TouchEventType.Down, id, new TouchSample(0, 100 * id, 100 * id)));
            session.Process(Packet(TouchEventType.Move, id, new TouchSample(0, 100 * id + .75f, 100 * id - .75f)));
        }
        Equal(0, moves.Count, "each DOWN resets coordinates and residual including same id");
        session.Process(Packet(TouchEventType.Up, 2, new TouchSample(0, 200.75f, 199.25f)));
        session.Process(Packet(TouchEventType.Down, 3, new TouchSample(0, 0, 0)));
        session.Process(Packet(TouchEventType.Move, 3, new TouchSample(0, .75f, -.75f)));
        Equal(0, moves.Count, "UP residual does not cross sessions");
        session.Reset();
        session.Process(Packet(TouchEventType.Move, 3, new TouchSample(0, 1.5f, -1.5f)));
        Equal(0, moves.Count, "explicit reset makes MOVE inactive");
    }

    public static void FailureCleanup()
    {
        var session = new TouchSessionProcessor((_, _) => throw new IOException("test output failure"));
        session.Process(Packet(TouchEventType.Down, 1, new TouchSample(0, 0, 0)));
        Throws<IOException>(() => session.Process(Packet(TouchEventType.Move, 1, new TouchSample(0, 5, 0))));
        Check(session.ActiveSessionId is null, "failure clears session");
        Equal(0L, session.OutputEvents, "failed output not counted successful");
        session = new TouchSessionProcessor((_, _) => throw new Exception("must not reach output"));
        session.Process(Packet(TouchEventType.Down, 1, new TouchSample(0, -float.MaxValue, 0)));
        Throws<OverflowException>(() => session.Process(Packet(TouchEventType.Move, 1, new TouchSample(0, float.MaxValue, 0))));
        Check(session.ActiveSessionId is null, "unrepresentable delta clears session");
    }
}
