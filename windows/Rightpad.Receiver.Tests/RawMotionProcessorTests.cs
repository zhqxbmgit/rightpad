using Rightpad.Receiver;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class RawMotionProcessorTests
{
    internal static void Near(double expected, double actual, double tolerance = 1e-10) =>
        Check(Math.Abs(expected - actual) <= tolerance, $"expected={expected:R} actual={actual:R}");

    internal static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}");
    }

    public static void PositiveNegative()
    {
        foreach (int sign in new[] { 1, -1 })
        {
            var motion = new RawMotionProcessor();
            Equal((0, 0), motion.Process(sign * .30, 0), "first fraction");
            Equal((0, 0), motion.Process(sign * .35, 0), "second fraction");
            Equal((sign, 0), motion.Process(sign * .40, 0), "whole count");
            Near(sign * .05, motion.ResidualX);
            Near(0, motion.ResidualY);
        }
    }

    public static void Alternating()
    {
        var motion = new RawMotionProcessor();
        long x = 0, y = 0;
        double sum = 0;
        double[] cycle = [.75, .75, -.75, -.75];
        for (int i = 0; i < 100_000; i++)
        {
            double delta = cycle[i % 4];
            var result = motion.Process(delta, -delta);
            x += result.X; y += result.Y; sum += delta;
            Near(sum, x + motion.ResidualX);
            Near(-sum, y + motion.ResidualY);
            Check(Math.Abs(motion.ResidualX) < 1 && Math.Abs(motion.ResidualY) < 1, "residual range");
        }
        Equal(0L, x, "alternating X drift");
        Equal(0L, y, "alternating Y drift");
    }

    public static void ScalingAndReset()
    {
        var motion = new RawMotionProcessor(2, .5);
        Equal((0, 0), motion.Process(.375, -.5), "independent fractions");
        Equal((1, -1), motion.Process(.25, -1.5), "independent sensitivity");
        Near(.25, motion.ResidualX);
        Near(0, motion.ResidualY);
        motion.Reset();
        Equal((0, 0), motion.Process(.375, .5), "reset drops fraction");
        Near(.75, motion.ResidualX);
        Near(.25, motion.ResidualY);
    }

    public static void LongDistance()
    {
        foreach (double delta in new[] { .125, .1, -.125, -.1 })
        {
            var motion = new RawMotionProcessor();
            long output = 0;
            const int count = 200_000;
            for (int i = 0; i < count; i++) output += motion.Process(delta, 0).X;
            double expected = count * delta;
            Near(expected, output + motion.ResidualX, 1e-7);
            Check(Math.Abs(motion.ResidualX) < 1, "bounded residual");
            Check(Math.Abs(expected - output) <= 1 + 1e-7, "distance quantization bound");
        }
    }

    public static void InvalidNumbers()
    {
        foreach (double value in new[] { 0, -1, double.NaN, double.PositiveInfinity })
        {
            Throws<ArgumentOutOfRangeException>(() => new RawMotionProcessor(value, 1));
            Throws<ArgumentOutOfRangeException>(() => new RawMotionProcessor(1, value));
        }
        var motion = new RawMotionProcessor();
        motion.Process(.5, .25);
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, (double)int.MaxValue + 1, (double)int.MinValue - 2 })
        {
            Throws<OverflowException>(() => motion.Process(1, bad));
            Near(.5, motion.ResidualX);
            Near(.25, motion.ResidualY);
        }
    }
}
