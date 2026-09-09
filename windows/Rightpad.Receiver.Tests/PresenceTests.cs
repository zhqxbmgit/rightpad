using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class PresenceTests
{
    internal static byte[] Heartbeat(ulong run) {
        byte[] b = [2, 4, 0, 0, 0, 0, 0, 0, 0, 0];
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(2), run); return b;
    }
    internal static byte[] Touch(ulong run, TouchEventType type, uint seq, float x = 0, ulong time = 0) {
        var b = PacketDecoderTests.Encode(type, 1, seq, new TouchSample(time, x, 0));
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(2), run); return b;
    }
    private sealed class Harness : IDisposable {
        public readonly List<(int X, int Y)> Moves = new();
        public int Clicks, Clears;
        public long Now = Stopwatch.Frequency * 10;
        public readonly TouchSessionProcessor Motion;
        public readonly UdpReceiver R;
        public Harness() {
            Motion = new((x,y) => Moves.Add((x,y)));
            R = new(new(IPAddress.Loopback, 0), TextWriter.Null, motion: Motion,
                gesture: new GestureProcessor(() => Clicks++), cancelButtons: () => Clears++);
        }
        public void Send(byte[] b) => R.ProcessDatagram(b, new(IPAddress.Loopback, 1234), Now);
        public void Advance(int ms) { Now += (long)(ms * (double)Stopwatch.Frequency / 1000); R.CheckTimeouts(Now); }
        public void Dispose() => R.Dispose();
    }
    public static void HeartbeatBytes() {
        byte[] b = Convert.FromHexString("02040807060504030281");
        Check(PacketDecoder.TryDecode(b, out var p, out _), "heartbeat fixed bytes");
        Equal(0x8102030405060708UL, p!.Header.SenderRunId, "all 64 bits");
        Equal(TouchEventType.Heartbeat, p.Header.EventType, "type"); Equal(0, p.Samples.Length, "no samples");
        Check(b.SequenceEqual(Heartbeat(0x8102030405060708UL)), "independent fixture");
    }
    public static void MalformedHeartbeat() {
        var b = Heartbeat(9);
        for (int i=0;i<10;i++) Check(!PacketDecoder.TryDecode(b[..i],out _,out _), "truncation");
        Check(!PacketDecoder.TryDecode([..b,0],out _,out _), "trailing");
        foreach(byte v in new byte[]{0,1,3,255}) { var c=(byte[])b.Clone(); c[0]=v; Check(!PacketDecoder.TryDecode(c,out _,out _),"version"); }
        foreach(byte v in new byte[]{0,1,2,3,5,255}) { var c=(byte[])b.Clone(); c[1]=v; Check(!PacketDecoder.TryDecode(c,out _,out _),"type/short touch"); }
    }
    public static void FirstHeartbeat() {
        using var h=new Harness(); Check(h.R.Presence.RunId is null,"waiting");
        h.Send(Heartbeat(ulong.MaxValue)); Equal((ulong?)ulong.MaxValue,h.R.Presence.RunId,"first run");
        Check(h.R.Presence.Connected,"heartbeat connects"); Equal((uint?)null,h.R.Statistics.LastSequence,"no baseline yet");
    }
    public static void FirstDown() {
        using var h=new Harness(); h.Send(Touch(3,TouchEventType.Down,0,999));
        Check(h.R.Presence.Connected,"touch connects immediately"); Equal((uint?)0,h.R.Statistics.LastSequence,"zero accepted");
        Equal(0,h.Moves.Count,"DOWN sets origin");
    }
    public static void UnknownMoveUp() {
        using var h=new Harness(); foreach(var type in new[]{TouchEventType.Move,TouchEventType.Up}) h.Send(Touch(7,type,500));
        Check(h.R.Presence.RunId is null,"unknown tail cannot establish run");
        h.Send(Heartbeat(1)); h.Send(Touch(9,TouchEventType.Move,100));
        Equal((ulong?)1,h.R.Presence.RunId,"unknown tail cannot replace run"); Equal(0L,h.R.Statistics.AcceptedPackets,"no input accepted");
    }
    public static void RestartBaseline() {
        using var h=new Harness(); h.Send(Touch(1,TouchEventType.Down,18000,900)); h.Send(Touch(1,TouchEventType.Move,18001,900.75f));
        h.Send(Touch(2,TouchEventType.Down,0,100)); h.Send(Touch(2,TouchEventType.Move,1,100.5f));
        Equal(0,h.Moves.Count,"old position and residual cleared"); h.Send(Touch(2,TouchEventType.Move,2,101));
        Equal((1,0),h.Moves.Single(),"new run movement"); Equal(0L,h.R.Statistics.OldPackets,"zero accepted after restart");
        Equal(5L,h.R.Statistics.AcceptedPackets,"counters survive run change");
    }
    public static void RetiredNeverReturns() {
        using var h=new Harness(); h.Send(Heartbeat(1)); h.Send(Heartbeat(2)); h.Send(Heartbeat(3)); long seen=h.R.Presence.LastSeenAtTicks;
        h.Advance(1500);
        foreach(ulong run in new ulong[]{1,2}) { h.Send(Heartbeat(run)); foreach(var t in new[]{TouchEventType.Down,TouchEventType.Move,TouchEventType.Up}) h.Send(Touch(run,t,999)); }
        Equal((ulong?)3,h.R.Presence.RunId,"retired never switches"); Equal(seen,h.R.Presence.LastSeenAtTicks,"no renewal");
        Equal(8L,h.R.Statistics.OutdatedRunPackets,"retired counter"); Equal(3L,h.R.Statistics.HeartbeatPackets,"retired excluded");
        h.Advance(500); Check(!h.R.Presence.Connected,"retired traffic cannot sustain connection");
        h.Advance(3_600_000);
        h.R.ProcessDatagram(Heartbeat(1), new(IPAddress.Parse("127.0.0.2"), 4321), h.Now);
        Equal((ulong?)3,h.R.Presence.RunId,"retired IDs have no expiry");
        Equal("127.0.0.1",h.R.Presence.RemoteIp,"retired source cannot change IP");
    }
    public static void MalformedCannotSwitch() {
        using var h=new Harness(); h.Send(Heartbeat(1)); h.Send([..Heartbeat(2),0]);
        h.Send(Touch(3,TouchEventType.Down,0,float.NaN));
        Equal((ulong?)1,h.R.Presence.RunId,"full validation before switch"); Equal(2L,h.R.Statistics.InvalidPackets,"malformed count");
    }
    public static void TouchRenews() {
        using var h=new Harness(); h.Send(Touch(1,TouchEventType.Down,0));
        for(uint i=1;i<8;i++) { h.Advance(500); h.Send(Touch(1,TouchEventType.Move,i,i)); Check(h.R.Presence.Connected,"touch renews without heartbeat"); }
        Equal(0L,h.R.PresenceTimeouts,"no false disconnect");
    }
    public static void HeartbeatIdleAndTouchTimeout() {
        using var h=new Harness(); h.Send(Touch(1,TouchEventType.Down,0,100)); h.Send(Touch(1,TouchEventType.Move,1,100.75f));
        for(int i=0;i<8;i++){h.Advance(500);h.Send(Heartbeat(1));}
        Equal(1L,h.R.InputTimeouts,"touch silence counted once"); Equal(0L,h.R.PresenceTimeouts,"heartbeat keeps presence");
        Equal((uint?)1,h.Motion.ActiveSessionId,"stationary hold retained"); h.Send(Touch(1,TouchEventType.Move,2,101.25f));
        Equal((1,0),h.Moves.Single(),"residual retained while heartbeat healthy");
    }
    public static void ExactTimeoutAndRecovery() {
        using var h=new Harness(); h.Send(Heartbeat(1)); h.Advance(1999); Check(h.R.Presence.Connected,"1999ms");
        h.Advance(1); Check(!h.R.Presence.Connected,"2000ms"); h.Advance(10000); Equal(1L,h.R.PresenceTimeouts,"one shot");
        h.Send(Heartbeat(1)); Check(h.R.Presence.Connected,"heartbeat recovery"); h.Advance(2000);
        h.Send(Touch(1,TouchEventType.Down,0)); Check(h.R.Presence.Connected,"touch recovery");
    }
    public static void DisconnectResetsInput() {
        using var h=new Harness(); h.Send(Touch(1,TouchEventType.Down,10,100)); h.Send(Touch(1,TouchEventType.Move,11,100.75f));
        h.Advance(2000); Equal((uint?)11,h.R.Statistics.LastSequence,"timeout retains baseline"); Check(h.Motion.ActiveSessionId is null,"session cleared");
        h.Send(Touch(1,TouchEventType.Move,12,1000)); Check(h.R.Presence.Connected,"accepted orphan still proves presence");
        h.Send(Touch(1,TouchEventType.Up,13,1000,50)); Equal(0,h.Moves.Count,"old tail no jump"); Equal(0,h.Clicks,"old UP no click");
        h.Send(Touch(1,TouchEventType.Down,14,200)); h.Send(Touch(1,TouchEventType.Move,15,200.5f)); Equal(0,h.Moves.Count,"residual cleared");
        h.Send(Touch(1,TouchEventType.Up,16,201,50)); Equal((1,0),h.Moves.Single(),"new DOWN works"); Equal(1,h.Clicks,"new tap works");
    }
    public static void OldDuplicateDoNotRenew() {
        using var h=new Harness(); h.Send(Touch(1,TouchEventType.Down,100)); h.Advance(1500);
        h.Send(Touch(1,TouchEventType.Down,100)); h.Send(Touch(1,TouchEventType.Down,0)); h.Advance(500);
        Check(!h.R.Presence.Connected,"old/duplicate do not renew"); Equal((uint?)100,h.R.Statistics.LastSequence,"same run reset rejected");
        h.Send(Touch(1,TouchEventType.Down,0)); Check(!h.R.Presence.Connected,"old after timeout still rejected");
    }
    public static void Stats() {
        using var h=new Harness(); h.Send(Heartbeat(1)); h.Send(Touch(1,TouchEventType.Down,100));
        for(int i=0;i<3;i++)h.Send(Heartbeat(1)); h.Send(Touch(1,TouchEventType.Up,101,0,1));
        Equal(6L,h.R.Statistics.ReceivedPackets,"all UDP"); Equal(4L,h.R.Statistics.HeartbeatPackets,"heartbeat count");
        Equal(2L,h.R.Statistics.AcceptedPackets,"touch only"); Equal(2L,h.R.Statistics.AcceptedSamples,"samples only");
        Equal(0L,h.R.Statistics.SequenceGapEstimate,"no fake gap"); Equal(0L,h.R.Statistics.DuplicatePackets,"no heartbeat duplicates");
    }
    public static void RunCancelsCandidate() {
        using var h=new Harness(); h.Send(Touch(1,TouchEventType.Down,100)); h.Send(Heartbeat(2));
        h.Send(Touch(2,TouchEventType.Up,0,0,10)); h.Send(Touch(1,TouchEventType.Up,101,0,10));
        Equal(0,h.Clicks,"run switch consumes candidate"); Equal(2,h.Clears,"new runs clear button queue");
    }
    public static void Ui() {
        long now=Stopwatch.Frequency*10; var vm=new RuntimeStatsViewModel();
        var s=new RuntimeStatsSnapshot(1,ReceiverState.Running); vm.Refresh(s,now); Equal("Waiting for Android",vm.Status,"waiting"); Equal("Never",vm.LastSeen,"never");
        s=s with {Presence=new(9,now,true,"192.168.1.2")}; vm.Refresh(s,now+Stopwatch.Frequency/2);
        Equal("Connected",vm.Status,"green"); Equal("0.5 s ago",vm.LastSeen,"age"); Equal("192.168.1.2",vm.RemoteIp,"run IP");
        vm.Refresh(s,now+2*Stopwatch.Frequency); Equal("Disconnected",vm.Status,"red");
        vm.Refresh(s with{RuntimeState=ReceiverState.Stopped},now); Equal("Receiver Stopped",vm.Status,"stopped");
        vm.Refresh(s with{RuntimeState=ReceiverState.Error},now); Equal("Error",vm.Status,"error");
    }
    public static async Task IdleLoopExpiresWithoutUi() {
        using var h=new Harness(); using var sender=new UdpClient(AddressFamily.InterNetwork); using var cts=new CancellationTokenSource();
        var task=h.R.RunAsync(cts.Token);
        try { await sender.SendAsync(Heartbeat(1),h.R.LocalEndpoint); await RuntimeTests.Until(()=>h.R.Presence.Connected);
            await RuntimeTests.Until(()=>h.R.PresenceTimeouts==1); Check(!h.R.Presence.Connected,"idle loop expires without UI/data"); }
        finally { cts.Cancel(); await task; }
    }

    public static async Task ButtonCleanupIntegration()
    {
        int downs = 0, ups = 0;
        using var buttons = new LeftButtonController(() => downs++, () => ups++, _ => { }, () => { }, 1000);
        using var receiver = new UdpReceiver(new(IPAddress.Loopback, 0), TextWriter.Null,
            cancelButtons: buttons.CancelPendingAndRelease);
        long now = Stopwatch.GetTimestamp();
        var endpoint = new IPEndPoint(IPAddress.Loopback, 1234);
        receiver.ProcessDatagram(Heartbeat(1), endpoint, now);
        buttons.Click(); buttons.Click();
        receiver.ProcessDatagram(Heartbeat(2), endpoint, now);
        Equal(1, downs, "run switch cancels queued click"); Equal(1, ups, "run switch releases held button");
        buttons.Click(); buttons.Click();
        receiver.CheckTimeouts(now + 2 * Stopwatch.Frequency);
        Equal(2, downs, "disconnect cancels queued click"); Equal(2, ups, "disconnect releases held button");
        await Task.Delay(1100);
        Equal(2, downs, "no stale queued down"); Equal(2, ups, "no stale timer up");
    }

    public static async Task CleanupFailureIsRuntimeError()
    {
        var settings = new RuntimeSettingsStore(new RuntimeSettings(7, 7, 300, 8, 200));
        var mouse = new WindowsMouseOutput((uint count, ref WindowsMouseOutput.NativeInput input, int size) =>
            input.Data.Mouse.Flags == 4 ? 0u : 1u, () => 5);
        var runtime = new ReceiverRuntime(settings, TextWriter.Null,
            new(IPAddress.Loopback, 0), () => mouse);
        await runtime.StartAsync();
        try
        {
            using var sender = new UdpClient(AddressFamily.InterNetwork);
            await sender.SendAsync(Touch(1, TouchEventType.Down, 0), runtime.LocalEndpoint!);
            await sender.SendAsync(Touch(1, TouchEventType.Up, 1, 0, 1), runtime.LocalEndpoint!);
            await RuntimeTests.Until(() => mouse.LeftDownSuccess == 1);
            await sender.SendAsync(Heartbeat(2), runtime.LocalEndpoint!);
            await RuntimeTests.Until(() => runtime.CaptureSnapshot().RuntimeState == ReceiverState.Error);
            Check(runtime.CaptureSnapshot().LastError is not null, "failed cleanup is actual Runtime Error");
            Check(mouse.LeftButtonFailures > 0, "native failure counted");
        }
        finally { await runtime.StopAsync(); }
    }
}
