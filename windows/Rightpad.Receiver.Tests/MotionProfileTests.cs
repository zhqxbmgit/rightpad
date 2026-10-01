using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class MotionProfileTests
{
    public static IEnumerable<(string Name, Action Run)> Cases =>
    [
        ("C2A exact golden and strict malformed profile codec", Codec),
        ("C2A current-run/source authority and no admission", Authority),
        ("C2A independent uint32 duplicate/stale/wrap ordering", Ordering),
        ("C2A presence timeout retention and new run reset M", Lifecycle),
        ("C2A profile/gamepad/Touch/config sequence isolation", Isolation),
        ("C2A Diagnostics read-only M/C projection", Diagnostics),
        ("Profile M/C production replay fingerprint and exact equivalence", () => Equivalence(false)),
        ("Profile M/C production missed-tick fingerprint and exact equivalence", () => Equivalence(true))
    ];
    private static byte[] Packet(ulong run, uint seq, MotionProfile p)
    {
        byte[] b = new byte[16]; b[0] = 2; b[1] = 7;
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(2), run);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(10), seq); b[14] = (byte)p; return b;
    }
    private static void Codec()
    {
        var b = Convert.FromHexString("0207080706050403028198BADCFE0100");
        Check(b.SequenceEqual(Packet(0x8102030405060708UL, 0xfedcba98, MotionProfile.Cinematic)), "shared golden");
        Check(PacketDecoder.TryDecodeMotionProfile(b, out var p), "C decode");
        Equal(new MotionProfilePacket(0x8102030405060708UL, 0xfedcba98, MotionProfile.Cinematic), p, "all fields");
        b[14] = 0; Check(PacketDecoder.TryDecodeMotionProfile(b, out p) && p.Profile == MotionProfile.Normal, "M");
        for (int n = 0; n < 40; n++)
            if (n != 16) Check(!PacketDecoder.TryDecodeMotionProfile(b.Take(n).Concat(new byte[Math.Max(0,n-16)]).ToArray(), out _), "exact length");
        foreach (int offset in new[] { 0, 1, 14, 15 })
            for (int v = 0; v < 256; v++)
            {
                if (offset == 0 && v == 2 || offset == 1 && v == 7 || offset == 14 && v <= 1 || offset == 15 && v == 0) continue;
                byte[] bad = (byte[])b.Clone(); bad[offset] = (byte)v;
                Check(!PacketDecoder.TryDecodeMotionProfile(bad, out _), "invalid field");
            }
    }
    private sealed class H : IDisposable
    {
        public long Now = Stopwatch.Frequency * 10;
        public readonly GamepadSessionProcessor Gamepad = new(_ => true);
        public readonly UdpReceiver R;
        public H() => R = new(new(IPAddress.Loopback, 0), TextWriter.Null, detailedLogging: false, gamepad: Gamepad);
        public void Send(byte[] b, string ip = "127.0.0.1") => R.ProcessDatagram(b, new(IPAddress.Parse(ip), 1234), Now);
        public void Profile(uint seq, MotionProfile p, ulong run = 1, string ip = "127.0.0.1") => Send(Packet(run,seq,p),ip);
        public void Dispose() => R.Dispose();
    }
    private static void Authority()
    {
        using var h = new H(); h.Profile(0, MotionProfile.Cinematic);
        Check(h.R.Presence.RunId is null, "profile cannot admit");
        Equal(MotionProfile.Normal, h.R.CurrentMotionProfile, "fresh M");
        h.Send(PresenceTests.Heartbeat(1));
        h.Profile(0, MotionProfile.Cinematic, 2); h.Profile(0, MotionProfile.Cinematic, 1, "127.0.0.2");
        Equal(MotionProfile.Normal,h.R.CurrentMotionProfile,"wrong run/source rejected");
        h.Profile(0,MotionProfile.Cinematic); Equal(MotionProfile.Cinematic,h.R.CurrentMotionProfile,"rejections do not consume watermark");
        h.Profile(1,MotionProfile.Normal); Equal(MotionProfile.Normal,h.R.CurrentMotionProfile,"C to M");
        h.Send(PresenceTests.Heartbeat(1), "127.0.0.2");
        h.Profile(2, MotionProfile.Cinematic); Equal(MotionProfile.Normal,h.R.CurrentMotionProfile,"follows existing authority source change");
        h.Profile(2, MotionProfile.Cinematic, 1, "127.0.0.2"); Equal(MotionProfile.Cinematic,h.R.CurrentMotionProfile,"current source accepted");
    }
    private static void Ordering()
    {
        using var h = new H(); h.Send(PresenceTests.Heartbeat(1));
        h.Profile(uint.MaxValue, MotionProfile.Cinematic);
        h.Profile(uint.MaxValue, MotionProfile.Normal); h.Profile(uint.MaxValue-1, MotionProfile.Normal);
        Equal(MotionProfile.Cinematic,h.R.CurrentMotionProfile,"duplicate/stale ignored");
        h.Profile(0, MotionProfile.Normal); Equal(MotionProfile.Normal,h.R.CurrentMotionProfile,"wrap newer");
        h.Profile(0x80000000, MotionProfile.Cinematic); Equal(MotionProfile.Normal,h.R.CurrentMotionProfile,"half range ambiguous");
        h.Profile(1, MotionProfile.Cinematic); Equal(MotionProfile.Cinematic,h.R.CurrentMotionProfile,"next newer");
    }
    private static void Lifecycle()
    {
        using var h = new H(); h.Send(PresenceTests.Heartbeat(1)); var p = h.R.Presence;
        h.Now += Stopwatch.Frequency; h.Profile(8,MotionProfile.Cinematic);
        Equal(p,h.R.Presence,"type7 never renews presence");
        h.Now += Stopwatch.Frequency; h.Profile(9,MotionProfile.Normal);
        Check(!h.R.Presence.Connected,"profile traffic cannot prevent expiry");
        Equal(MotionProfile.Cinematic,h.R.CurrentMotionProfile,"timeout keeps run state");
        h.Send(PresenceTests.Heartbeat(1)); h.Profile(8,MotionProfile.Normal);
        Equal(MotionProfile.Cinematic,h.R.CurrentMotionProfile,"same-run watermark preserved");
        h.Profile(9,MotionProfile.Normal); Equal(MotionProfile.Normal,h.R.CurrentMotionProfile,"unaccepted disconnected packet did not advance watermark");
        h.Profile(10,MotionProfile.Cinematic); h.Send(PresenceTests.Heartbeat(2));
        Equal(MotionProfile.Normal,h.R.CurrentMotionProfile,"new run resets M immediately");
        h.Profile(0,MotionProfile.Cinematic,2); Equal(MotionProfile.Cinematic,h.R.CurrentMotionProfile,"new run accepts sequence zero");
        h.Send(PresenceTests.Touch(3,TouchEventType.Down,0)); Equal(MotionProfile.Normal,h.R.CurrentMotionProfile,"DOWN admission resets M too");
        h.Profile(99,MotionProfile.Cinematic,2); Equal(MotionProfile.Normal,h.R.CurrentMotionProfile,"retired profile ignored");
    }
    private static void Isolation()
    {
        using var h = new H(); h.Send(PresenceTests.Touch(1,TouchEventType.Down,123));
        var p = h.R.Presence; var config = h.R.ControlConfigVersion; long accepted = h.R.Statistics.AcceptedPackets;
        Check(h.Gamepad.Process(new(1, 900, new(XboxGamepadButtons.B)),IPAddress.Loopback,h.Now),"gamepad 900");
        h.Profile(0,MotionProfile.Cinematic);
        Equal(MotionProfile.Cinematic,h.R.CurrentMotionProfile,"profile independent of gamepad watermark");
        Check(h.Gamepad.Process(new(1,901,XboxGamepadState.Neutral),IPAddress.Loopback,h.Now),"gamepad independent of profile");
        Equal((uint?)123,h.R.Statistics.LastSequence,"Touch unchanged"); Equal(accepted,h.R.Statistics.AcceptedPackets,"no Touch acceptance");
        Equal(p,h.R.Presence,"presence unchanged"); Equal(config,h.R.ControlConfigVersion,"config unchanged");
        Check(h.Gamepad.Process(new(1,902,new(XboxGamepadButtons.B)),IPAddress.Loopback,h.Now),"press");
        h.Now += Stopwatch.Frequency / 5; h.Profile(1,MotionProfile.Normal);
        h.Now += Stopwatch.Frequency / 10; h.Gamepad.CheckLease(h.Now);
        Equal(XboxGamepadState.Neutral,h.Gamepad.State,"profile does not renew gamepad lease");
    }
    private static void Diagnostics()
    {
        var vm = new RuntimeStatsViewModel(); vm.Refresh(new(1,ReceiverState.Running),0); Equal("M",vm.MotionProfile,"default M");
        vm.Refresh(new(1,ReceiverState.Running,MotionProfile:MotionProfile.Cinematic),0); Equal("C",vm.MotionProfile,"C projection");
        Equal("M",vm.ActiveMotionProfile,"pending C / active M separately visible");
        vm.Refresh(new(1,ReceiverState.Running,MotionProfile:MotionProfile.Cinematic,ActiveMotionProfile:MotionProfile.Cinematic),0);
        Equal("C",vm.ActiveMotionProfile,"active C projection");
        vm.Refresh(new(2,ReceiverState.Running),0); Equal("M",vm.MotionProfile,"fresh runtime M");
    }
    private sealed record Result(List<(long At,int X,int Y)> Moves, List<string> States, long X, long Y, long Missed);
    private static Result Replay(MotionProfile profile, bool missed)
    {
        const long frequency = 1_000_000; long origin = frequency * 10, now = origin;
        long T(int ms) => origin + frequency * ms / 1000;
        List<(long,int,int)> moves = []; List<string> states = [];
        using var m = new ResampledMotion((x,y)=> { if(x!=0 || y!=0) moves.Add((now,x,y)); }, 9, 7, () => now,
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
        var m=Replay(MotionProfile.Normal,missed); var c=Replay(MotionProfile.Cinematic,missed);
        Equal(missed ? "A3719B0D077F924ECEE47B4857B1EEA844EAB01CD3141A7D600374A37B759D08" :
            "72FFC866FE2B59C9FB90B9FBA4DE229028EEA6895E6AADC3A815FF0D056C0F49", Fingerprint(m),
            "production M byte fingerprint: every output/time and continuous/pending/schedule state");
        Equal((m.X,m.Y,m.Missed),(c.X,c.Y,c.Missed),"exact final totals");
        Check(m.Moves.SequenceEqual(c.Moves),"every native dx/dy and actual submit time exact");
        Check(m.States.SequenceEqual(c.States),"every continuous/logical/settle/missed/deadline state exact");
    }
    private static string Fingerprint(Result r) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join(";", r.Moves) + "|" + string.Join(";", r.States))));
}
