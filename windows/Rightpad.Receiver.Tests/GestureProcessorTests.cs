using Rightpad.Receiver;
using static Rightpad.Receiver.Tests.Program;
using static Rightpad.Receiver.Tests.TouchSessionProcessorTests;
using static Rightpad.Receiver.Tests.RawMotionProcessorTests;

namespace Rightpad.Receiver.Tests;

internal static class GestureProcessorTests
{
    private static TouchSample S(ulong ms, float x = 0, float y = 0) => new(ms * 1_000_000, x, y);

    private static void Trace(bool expected, TouchSample[] moves, TouchSample up)
    {
        int clicks = 0;
        var gesture = new GestureProcessor(() => clicks++);
        gesture.Process(Packet(TouchEventType.Down, 1, S(100)));
        if (moves.Length > 0) gesture.Process(Packet(TouchEventType.Move, 1, moves));
        gesture.Process(Packet(TouchEventType.Up, 1, up));
        gesture.Process(Packet(TouchEventType.Up, 1, up));
        Equal(expected ? 1 : 0, clicks, "click count (including repeated consumed UP)");
        Equal(1L, gesture.TapCandidates, "candidate count");
        Equal((long)clicks, gesture.ClicksTriggered, "trigger count");
    }

    public static void Stationary() => Trace(true, [], S(200));
    public static void SmallMovement() => Trace(true, [S(150, -4, 5)], S(200, 8, -8));
    public static void XExceeded() => Trace(false, [S(150, 8.01f, 0)], S(200));
    public static void YExceeded() => Trace(false, [S(150, 0, -8.01f)], S(200));
    public static void ReturnToOrigin() => Trace(false, [S(150, 10), S(160)], S(200));
    public static void Duration()
    {
        Trace(true, [], S(400));
        Trace(false, [], new TouchSample(400_000_001, 0, 0));
        Trace(false, [], S(99));
        int clicks = 0;
        var gesture = new GestureProcessor(() => clicks++);
        gesture.Process(Packet(TouchEventType.Down, 1, new TouchSample(ulong.MaxValue - 100, 0, 0)));
        gesture.Process(Packet(TouchEventType.Up, 1, new TouchSample(ulong.MaxValue, 0, 0)));
        Equal(1, clicks, "uint64 timestamps must not overflow");
    }
    public static void UpExceeded() => Trace(false, [], S(200, 0, 9));
    public static void WrongMove()
    {
        int clicks = 0;
        var g = new GestureProcessor(() => clicks++);
        g.Process(Packet(TouchEventType.Down, 1, S(100)));
        g.Process(Packet(TouchEventType.Move, 2, S(150, 999, 999)));
        g.Process(Packet(TouchEventType.Up, 1, S(200)));
        Equal(1, clicks, "foreign MOVE does not poison candidate");
        Equal(0L, g.ConfirmedMoves, "foreign movement ignored");
    }
    public static void WrongUp()
    {
        int clicks = 0;
        var g = new GestureProcessor(() => clicks++);
        g.Process(Packet(TouchEventType.Up, 1, S(99)));
        g.Process(Packet(TouchEventType.Down, 1, S(100)));
        g.Process(Packet(TouchEventType.Up, 2, S(150)));
        Equal(0, clicks, "foreign UP does not click");
        g.Process(Packet(TouchEventType.Up, 1, S(200)));
        Equal(1, clicks, "foreign UP does not end candidate");
    }
    public static void NewDown()
    {
        int clicks = 0;
        var g = new GestureProcessor(() => clicks++);
        g.Process(Packet(TouchEventType.Down, 1, S(100)));
        g.Process(Packet(TouchEventType.Move, 1, S(150, 20)));
        g.Process(Packet(TouchEventType.Down, 1, S(1000, 100, 100)));
        g.Process(Packet(TouchEventType.Up, 1, S(1100, 100, 100)));
        Equal(1, clicks, "new DOWN resets same-id candidate");
        Equal(2L, g.TapCandidates, "new DOWN count");
        Equal(1L, g.ConfirmedMoves, "latched move counted once");
        g.Process(Packet(TouchEventType.Down, 2, S(1200)));
        g.Reset();
        g.Process(Packet(TouchEventType.Up, 2, S(1250)));
        Equal(1, clicks, "cleanup ends candidate");
    }
    public static void MiddleSample() => Trace(false, [S(110, 1), S(120, 9), S(130, 1)], S(140));

    public static void Arguments()
    {
        var o = Rightpad.Receiver.Program.ParseArguments(["--raw-mouse", "--tap-max-duration-ms", "200",
            "--tap-movement-threshold-px", "4.5", "--click-hold-ms", "40"]);
        Equal(new Rightpad.Receiver.Program.Options(true, 7, 7, 200, 4.5, 40), o, "tap overrides");
        foreach (string name in new[] { "--tap-max-duration-ms", "--tap-movement-threshold-px", "--click-hold-ms" })
        {
            foreach (string value in new[] { "0", "-1", "NaN", "Infinity", "abc" })
                Throws<ArgumentException>(() => Rightpad.Receiver.Program.ParseArguments(["--raw-mouse", name, value]));
            Throws<ArgumentException>(() => Rightpad.Receiver.Program.ParseArguments([name, "1"]));
            Throws<ArgumentException>(() => Rightpad.Receiver.Program.ParseArguments(["--raw-mouse", name]));
            Throws<ArgumentException>(() => Rightpad.Receiver.Program.ParseArguments(["--raw-mouse", name, "1", name, "2"]));
        }
        foreach (string name in new[] { "--tap-max-duration-ms", "--click-hold-ms" })
            foreach (string value in new[] { "0.5", "1.5", "2147483648" })
                Throws<ArgumentException>(() => Rightpad.Receiver.Program.ParseArguments(["--raw-mouse", name, value]));
        Throws<ArgumentException>(() => Rightpad.Receiver.Program.ParseArguments(["--raw-mouse", "--double-tap-interval", "130"]));
        int clicks = 0;
        var g = new GestureProcessor(() => clicks++, 50, 2);
        g.Process(Packet(TouchEventType.Down, 1, S(0)));
        g.Process(Packet(TouchEventType.Up, 1, S(50, 2, -2)));
        Equal(1, clicks, "custom duration/threshold applied");
        g.Process(Packet(TouchEventType.Down, 2, S(100)));
        g.Process(Packet(TouchEventType.Up, 2, S(151)));
        Equal(1, clicks, "custom duration rejects");
    }
}
