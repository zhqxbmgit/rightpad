using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class HistoricalM12GoldenTests
{
    public static IEnumerable<(string Name, Action Run)> Cases =>
    [
        ("M12 historical ordinary golden fingerprint", () => Equivalence(false)),
        ("M12 historical missed-tick golden fingerprint", () => Equivalence(true))
    ];
    private static byte[] Packet(ulong run, uint seq, MotionProfile p)
    {
        byte[] b = new byte[16]; b[0] = 2; b[1] = 7;
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(2), run);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(10), seq); b[14] = (byte)p; return b;
    }
    private sealed record Result(List<(long At,int X,int Y)> Moves, List<string> States, long X, long Y, long Missed);
    private static Result Replay(MotionProfile profile, bool missed)
    {
        const long frequency = 1_000_000; long origin = frequency * 10, now = origin;
        long T(int ms) => origin + frequency * ms / 1000;
        List<(long,int,int)> moves = []; List<string> states = [];
        using var m = new M12HistoricalMotion((x,y)=> { if(x!=0 || y!=0) moves.Add((now,x,y)); }, 9, 7, () => now,
            clockFrequency: frequency, finiteCriticalMode: MotionMode.RESAMPLED_1000HZ_FINITE_CRITICAL_K24_R5_SETTLE);
        using var r = new UdpReceiver(new(IPAddress.Loopback,0),TextWriter.Null,motion:m,detailedLogging:false);
        void Send(byte[] b)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(2),1);
            r.ProcessDatagram(b,new(IPAddress.Loopback,1234),now);
        }
        Send(PresenceTests.Heartbeat(1)); Send(Packet(1,0,profile));
        Equal(profile,r.CurrentMotionProfile,"profile accepted through production receiver");
        Equal(1,m.PeriodMs,"1000Hz"); Equal(12,ResampledMotion.PlayoutDelayMs,"12ms");
        uint seq=0;
        for(int ms=0;ms<=400;ms++)
        {
            now=T(ms);
            if(ms==0 || ms==27) Send(PacketDecoderTests.Encode(TouchEventType.Down,(uint)(ms+1),seq++,new TouchSample((ulong)ms*1000000,0,0)));
            if(ms is 4 or 9 or 16 or 31 or 39) Send(PacketDecoderTests.Encode(TouchEventType.Move,ms<27?1U:28U,seq++,new TouchSample((ulong)ms*1000000,ms<27?ms*.6f:-ms*.3f,ms*.125f)));
            if(ms is 20 or 42) Send(PacketDecoderTests.Encode(TouchEventType.Up,ms<27?1U:28U,seq++,new TouchSample((ulong)ms*1000000,ms<27?12:-14,ms*.125f)));
            if(m.Schedule.Deadline is long deadline && deadline<=now && !(missed && ms is >= 12 and <= 18)) m.Tick(now,m.Schedule.Generation);
            states.Add($"{m.Pending}|{m.BasePending}|{m.KernelPending}|{m.Position}|{m.Schedule}|{m.TickCount}|{m.MissedTicks}");
            Equal(profile,r.CurrentMotionProfile,"selected profile retained throughout trace");
        }
        Check(moves.Count>0,"nonvacuous motion"); Check(m.Schedule.Deadline is null,"settle completes and parks");
        Check(Math.Abs(m.Pending.X)<1 && Math.Abs(m.Pending.Y)<1,"settled pending");
        if(missed) Check(m.MissedTicks>0,"missed tick exercised");
        return new(moves,states,m.TotalDx,m.TotalDy,m.MissedTicks);
    }
    private static void Equivalence(bool missed)
    {
        var m=Replay(MotionProfile.Normal,missed);
        Equal(missed ? "A3719B0D077F924ECEE47B4857B1EEA844EAB01CD3141A7D600374A37B759D08" :
            "72FFC866FE2B59C9FB90B9FBA4DE229028EEA6895E6AADC3A815FF0D056C0F49", Fingerprint(m),
            "production M byte fingerprint: every output/time and continuous/pending/schedule state");
        Console.WriteLine($"M12 historical golden missed={missed} fingerprint={Fingerprint(m)} EXACT");
    }
    private static string Fingerprint(Result r) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join(";", r.Moves) + "|" + string.Join(";", r.States))));
}
