using System.Diagnostics;
using System.Net;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class RemoteLayoutTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> Cases => new (string, Action)[] {
        ("RPLY strict codec and Java golden", Codec),
        ("RPLY Windows draft isolated and stale basis fenced", Draft),
        ("RPLY bounded retry, lost save result reconciliation and no replay", Loss),
        ("RPLY deadline never extends, stale snapshot and run fences", Ordering),
        ("RPLY operation lifetime survives sparse/paused/forced polling", Lifetime),
        ("RPLY suspended Try retains token until authoritative reconciliation", Suspended),
        ("RPLY reconciliation bounded across disconnect, route/run/epoch changes", ReconciliationIdentity),
        ("RPLY layout packets cannot establish or renew input presence", Presence),
    }.Select(c => (c.Item1, (Func<Task>)(() => { c.Item2(); return Task.CompletedTask; })))
        .Append(("RPLY real UDP multiplex on existing sender/receiver workers", Loopback))
        .Append(("RPLY Runtime Stop immediately invalidates editor capability", RuntimeLifecycle));
    private static readonly LayoutItem[] Items = [new("xbox.b.slide", "B", 0, new(700, 500, 150, 900)),
        new("xbox.x.slide_lr", "X", 0, new(0,400,60,1200)), new("rightpad.input.mode", "Mode", 0, new(400,500,200,900)),
        new("settings", "设置", 1, new(800,20,40,40))];
    private static LayoutMessage Snapshot(ulong request = 1, ulong serial = 1) => new(128, 0, 0, 99, 123, 2, 1, 0, request, 1, serial, 0, 1000, 2000, Items);
    private static long Seconds(double value) => (long)(value * Stopwatch.Frequency);
    public static async Task RuntimeLifecycle()
    {
        var runtime = new ReceiverRuntime(new RuntimeSettingsStore(RuntimeSettings.Default),
            TextWriter.Null, MouseBackendDefaults.Production, endpoint: new(IPAddress.Loopback,0), rawMouse:false);
        Check(runtime.Layout is null,"stopped editor unavailable");
        try {
            await runtime.StartAsync();
            Check(runtime.Layout is not null,"running transport exposes capability channel");
        } finally { await runtime.StopAsync(); }
        Check(runtime.Layout is null,"Stop fences stale connected presence immediately");
    }
    public static async Task Loopback()
    {
        using var phone = new System.Net.Sockets.UdpClient(new IPEndPoint(IPAddress.Loopback,0));
        await using var sender = new HapticFeedbackSender(TextWriter.Null, ((IPEndPoint)phone.Client.LocalEndPoint!).Port);
        using var receiver = new UdpReceiver(new(IPAddress.Loopback,0),TextWriter.Null,controlSend:sender.TryEnqueueConfig);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var task=receiver.RunAsync(stop.Token);
        try
        {
            byte[] heartbeat=new byte[10]; heartbeat[0]=2; heartbeat[1]=4;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(heartbeat.AsSpan(2),99);
            await phone.SendAsync(heartbeat,receiver.LocalEndpoint,stop.Token);
            while(!receiver.Presence.Connected) await Task.Delay(5,stop.Token);
            receiver.Layout!.Poll(Stopwatch.GetTimestamp());
            var query=RemoteLayoutProtocol.Decode((await phone.ReceiveAsync(stop.Token)).Buffer)!;
            Equal((byte)1,query.Kind,"actual worker query");
            var snapshot=Snapshot(query.Request);
            await phone.SendAsync(RemoteLayoutProtocol.Encode(snapshot),receiver.LocalEndpoint,stop.Token);
            while(receiver.Layout.Capture(Stopwatch.GetTimestamp()).Snapshot is null) await Task.Delay(5,stop.Token);
            var current=receiver.Layout.Capture(Stopwatch.GetTimestamp()).Snapshot!;
            Check(receiver.Layout.Begin(2,current,Items.Where(i=>i.Flags==0).ToArray(),Stopwatch.GetTimestamp()),"real route Try");
            var trial=RemoteLayoutProtocol.Decode((await phone.ReceiveAsync(stop.Token)).Buffer)!;
            Equal((byte)2,trial.Kind,"complete Try through existing worker");
            Equal(3,trial.Items.Length,"all registered editable controls");
            var applied=current with { State=2,Transaction=1,Next=2,Remaining=60000,Serial=2,Request=trial.Request };
            await phone.SendAsync(RemoteLayoutProtocol.Encode(applied),receiver.LocalEndpoint,stop.Token);
            while(receiver.Layout.Capture(Stopwatch.GetTimestamp()).Snapshot?.State!=2) await Task.Delay(5,stop.Token);
            Equal((byte)2,receiver.Layout.Capture(Stopwatch.GetTimestamp()).Snapshot!.State,"only applied acknowledgement enters Trial");
            Equal(0L,receiver.Statistics.AcceptedPackets,"layout emits no Touch input");
        }
        finally { stop.Cancel(); await task; }
    }
    public static void Codec()
    {
        var query = new LayoutMessage(1,0,0,0x0807060504030201,0,0,0,0,9,0,0,0,0,0,[]);
        string golden = "52504C59010100000102030405060708" + new string('0',64) + "0900000000000000" + new string('0',64);
        Equal(88, Convert.FromHexString(golden).Length, "shared 88-byte header");
        Check(RemoteLayoutProtocol.Encode(query).SequenceEqual(Convert.FromHexString(golden)), "Java query golden");
        foreach(ulong run in new[] {0UL,ulong.MaxValue}) {
            var unsigned=query with { Run=run };
            Equal(run,RemoteLayoutProtocol.Decode(RemoteLayoutProtocol.Encode(unsigned))!.Run,"existing uint64 run domain");
        }
        byte[] bytes = RemoteLayoutProtocol.Encode(Snapshot());
        Equal("设置", RemoteLayoutProtocol.Decode(bytes)!.Items[3].Label, "UTF8");
        for (int n=0; n<bytes.Length; n++) Check(RemoteLayoutProtocol.Decode(bytes[..n]) is null, "all truncations");
        Check(RemoteLayoutProtocol.Decode([..bytes, 0]) is null, "trailing");
        foreach (int offset in new[] { 4,5,6,7,84,85,86,87 })
        { var bad = (byte[])bytes.Clone(); bad[offset] = 255; Check(RemoteLayoutProtocol.Decode(bad) is null, "bad framing"); }
        var duplicate = Snapshot() with { Items = [Items[0], Items[0]] };
        Check(RemoteLayoutProtocol.Decode(RemoteLayoutProtocol.Encode(duplicate)) is null, "duplicate");
        var overflow = Snapshot() with { Items = [Items[0] with { Rect = new(int.MaxValue,0,200,200) }] };
        Check(RemoteLayoutProtocol.Decode(RemoteLayoutProtocol.Encode(overflow)) is null, "overflow geometry");
    }
    public static void Draft()
    {
        var d = new RemoteLayoutDraft(); var phone = Snapshot(); d.Load(phone);
        Check(d.Adjust(0, -50), "click adjust"); Equal(650, d.Selection!.Rect.X, "View pixels");
        Equal(700, phone.Items[0].Rect.X, "phone immutable");
        Check(d.Dirty && !d.Conflict, "draft only");
        d.Observe(phone); Equal(650,d.Selection.Rect.X,"query retains unsent draft");
        d.Observe(phone with { Revision = 3 }); Check(d.Conflict,"local Save conflicts with draft");
        Check(!d.Adjust(0,1),"conflict freezes edit");
        d.Load(phone); Check(!d.Dirty && !d.Conflict,"explicit refresh discards");
        Check(!d.Adjust(2,int.MaxValue),"overflow blocked");
        d.Adjust(0,-50);
        d.Observe(phone with { State=2,Transaction=1,Next=2,Items=d.Items,Remaining=60000 });
        d.Observe(phone with { State=4,Transaction=1,Next=2 });
        Equal(700,d.Selection.Rect.X,"expiry restores values and preview even same transaction");
        Check(!d.Dirty,"expiry discards draft");
    }
    private sealed class Fixture
    {
        public SenderPresence P = new(99,0,true,"127.0.0.1");
        public List<LayoutMessage> Sends = [];
        public RemoteLayoutClient Client;
        public Fixture() { Client = new(() => P, (_, bytes) => Sends.Add(RemoteLayoutProtocol.Decode(bytes)!)); }
        public void Reply(LayoutMessage m, long now) => Client.Accept(RemoteLayoutProtocol.Encode(m),IPAddress.Loopback,now);
        public void Connect(long now=0) { Client.Poll(now); Reply(Snapshot(Sends.Last().Request),now); }
    }
    public static void Lifetime()
    {
        foreach (var schedule in new[] { new[] { 0.2, 1, 2, 3 }, new[] { 2.9, 3 }, new[] { 120.0 } }) {
            var f=new Fixture(); f.Connect(); var s=f.Client.Capture(0).Snapshot!;
            Check(f.Client.Begin(2,s,Items.Where(i=>i.Flags==0).ToArray(),0),"explicit Try");
            var first=f.Sends.Last();
            foreach(double t in schedule) f.Client.Poll(Seconds(t),true);
            Equal((byte)1,f.Sends.Last().Kind,"deadline checked before force/next-send/count");
            Equal(first.Transaction,f.Sends.Last().Transaction,"expiry query seals original token");
            Check(f.Sends.Count(m=>m.Kind==2)<=3,"at most three attempts");
            Check(f.Client.Capture(Seconds(schedule.Last())).Uncertain,"expired is still unknown");
        }
        var b=new Fixture(); b.Connect(); var basis=b.Client.Capture(0).Snapshot!;
        b.Client.Begin(2,basis,Items.Where(i=>i.Flags==0).ToArray(),0);
        b.Client.Poll(Seconds(1),true);
        b.Client.Poll(Seconds(3)-1,true); Equal((byte)2,b.Sends.Last().Kind,"last instant before deadline eligible");
        b.Client.Poll(Seconds(3),true); Equal((byte)1,b.Sends.Last().Kind,"deadline inclusive even before retry cadence");
        Equal(3,b.Sends.Count(m=>m.Kind==2),"bounded attempts at 0,1,3-minus-one-tick");
        b=new Fixture(); b.Connect(); b.Client.Begin(2,basis,Items.Where(i=>i.Flags==0).ToArray(),0);
        var old=b.Sends.Last();
        b.Client.Accept(RemoteLayoutProtocol.Encode(basis with {State=2,Transaction=1,Next=2,Serial=2,Remaining=60000,Request=old.Request}),IPAddress.Loopback,Seconds(3));
        Check(b.Client.Capture(Seconds(3)).Uncertain,"late operation result cannot re-enable mutation retry");
        Check(!b.Client.Begin(2,basis,Items.Where(i=>i.Flags==0).ToArray(),Seconds(3)),"new Try blocked until reconciliation");
        b.Client.Poll(Seconds(3),true); Equal((byte)1,b.Sends.Last().Kind,"Accept/Begin also expire");
    }
    public static void Suspended()
    {
        var f=new Fixture(); f.Connect(); var s=f.Client.Capture(0).Snapshot!;
        f.Client.Poll(0,true); var before=f.Sends.Last();
        f.Client.Begin(2,s,Items.Where(i=>i.Flags==0).ToArray(),0); var first=f.Sends.Last();
        for(int i=0;i<12;i++) f.Client.Suspend(0);
        f.Reply(s with {Request=before.Request,Serial=2},0);
        Check(f.Client.Capture(0).Uncertain,"pre-operation Query cannot reconcile");
        f.Client.Poll(Seconds(120),true); var q=f.Sends.Last();
        Equal((byte)1,q.Kind,"reopen after 120 seconds is Query");
        Equal((first.Run,first.Epoch,first.Revision,first.Geometry,first.Transaction),
            (q.Run,q.Epoch,q.Revision,q.Geometry,q.Transaction),"full original identity retained");
        Equal(1,f.Sends.Count(m=>m.Kind==2),"lost first Try never replayed");
        f.Reply(s with {Request=q.Request,Serial=3},Seconds(120));
        Check(f.Client.Capture(Seconds(120)).Uncertain,"unconsumed Ready is not proof of token sealing");
        f.Client.Poll(Seconds(121)); q=f.Sends.Last();
        var sealedState=s with {State=4,Transaction=1,Next=2,Request=q.Request,Serial=4};
        f.Reply(sealedState,Seconds(121)); f.Reply(sealedState,Seconds(121));
        Check(!f.Client.Capture(Seconds(121)).Uncertain,"confirmed terminal state reconciles idempotently");
        Check(f.Client.Begin(2,sealedState,Items.Where(i=>i.Flags==0).ToArray(),Seconds(121)),"fresh explicit Try permitted");
        Equal(2UL,f.Sends.Last().Transaction,"uses new token");
    }
    public static void ReconciliationIdentity()
    {
        foreach (string identity in new[] { "disconnect", "route", "run", "epoch" }) {
            var f=new Fixture(); f.Connect(); var s=f.Client.Capture(0).Snapshot!;
            f.Client.Begin(2,s,Items.Where(i=>i.Flags==0).ToArray(),0); var old=f.Sends.Last();
            if(identity=="disconnect") {
                f.P=f.P with {Connected=false}; f.Client.Poll(Seconds(1));
                f.P=f.P with {Connected=true};
            } else if(identity=="route") f.P=f.P with {RemoteIp="127.0.0.2"};
            else if(identity=="run") f.P=f.P with {RunId=100};
            else f.Client.Suspend(Seconds(1));
            f.Client.Poll(Seconds(2),true); var q=f.Sends.Last();
            Equal((byte)1,q.Kind,"identity recovery is Query");
            Equal(identity is "disconnect" or "epoch" ? old.Transaction : 0UL,q.Transaction,"token only retained on original route/run");
            f.Reply(s with {Request=old.Request,State=2,Transaction=1,Serial=2,Remaining=60000},Seconds(2));
            Check(f.Client.Capture(Seconds(2)).Snapshot?.State!=2,"old mutation response fenced");
            var current=s with {Run=f.P.RunId!.Value,Epoch=identity=="epoch"?124UL:123UL,
                State=4,Transaction=1,Next=2,Request=q.Request,Serial=3};
            f.Client.Accept(RemoteLayoutProtocol.Encode(current),IPAddress.Parse(f.P.RemoteIp!),Seconds(2));
            Check(!f.Client.Capture(Seconds(2)).Uncertain,"current identity reply reconciles");
        }
        var b=new Fixture(); b.Connect(); var initial=b.Client.Capture(0).Snapshot!;
        b.Client.Begin(2,initial,Items.Where(i=>i.Flags==0).ToArray(),0); b.Client.Suspend(0);
        for(int i=0;i<100;i++) { b.Client.Suspend(Seconds(i)); b.Client.Poll(Seconds(i),true); }
        var last=b.Sends.Last(); var evicted=b.Sends.First(m=>m.Kind==1&&m.Transaction==1);
        b.Reply(initial with {State=4,Transaction=1,Next=2,Serial=99,Request=evicted.Request},Seconds(100));
        Check(b.Client.Capture(Seconds(100)).Uncertain,"old evicted query ignored");
        var queries=(System.Collections.IDictionary)typeof(RemoteLayoutClient).GetField("queries",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(b.Client)!;
        Equal(8,queries.Count,"bounded eight correlations after repeated open/loss");
        b.Reply(initial with {State=4,Transaction=1,Next=2,Serial=100,Request=last.Request},Seconds(100));
        Check(!b.Client.Capture(Seconds(100)).Uncertain,"latest repeated reconciliation completes");
        Equal(1,b.Sends.Count(m=>m.Kind==2),"no mutation resurrection or transaction accumulation");
        b=new Fixture(); b.Connect(); b.Client.Begin(2,initial,Items.Where(i=>i.Flags==0).ToArray(),0); b.Client.Suspend(0);
        b.Client.Poll(Seconds(1),true); var oldEpochQuery=b.Sends.Last();
        b.Client.Poll(Seconds(2),true); var newEpochQuery=b.Sends.Last();
        b.Reply(initial with {Epoch=124,Serial=1,Request=newEpochQuery.Request},Seconds(2));
        b.Reply(initial with {Serial=999,Request=oldEpochQuery.Request},Seconds(2));
        Equal(124UL,b.Client.Capture(Seconds(2)).Snapshot!.Epoch,"new epoch fences other outstanding old-epoch responses");
    }
    public static void Loss()
    {
        var f=new Fixture(); f.Connect(); var s=f.Client.Capture(0).Snapshot!;
        Check(f.Client.Begin(2,s,Items.Where(i=>i.Flags==0).ToArray(),0),"explicit trial");
        for(int i=1;i<=5;i++) f.Client.Poll(Seconds(i));
        Equal(3,f.Sends.Count(m=>m.Kind==2),"three bounded attempts");
        Check(f.Sends.Last().Kind==1 && f.Client.Capture(Seconds(5)).Uncertain,"query after lost ACK");
        Equal(1UL,f.Sends.Last().Transaction,"uncertain first Try query fences unused token against late delivery");
        var trial=s with { State=2,Transaction=1,Next=2,Remaining=55000,Serial=2,Request=f.Sends.Last().Request };
        f.Reply(trial,Seconds(5)); Check(!f.Client.Capture(Seconds(5)).Uncertain,"actual state reconciled");
        Check(f.Client.Begin(3,trial,null,Seconds(5)),"keep current trial");
        for(int i=6;i<=9;i++) f.Client.Poll(Seconds(i));
        var saved=trial with { State=3,Revision=3,Remaining=0,Serial=3,Request=f.Sends.Last().Request };
        f.Reply(saved,Seconds(9)); Equal((byte)3,f.Client.Capture(Seconds(9)).Snapshot!.State,"lost save result queried as committed");
        f.P=f.P with { Connected=false }; f.Client.Poll(Seconds(10));
        f.P=f.P with { Connected=true }; f.Client.Poll(Seconds(11));
        Equal((byte)1,f.Sends.Last().Kind,"reconnect read only, no draft replay");
    }
    public static void Ordering()
    {
        var f=new Fixture(); f.Connect(); var initial=f.Client.Capture(0).Snapshot!;
        f.Client.Begin(2,initial,Items.Where(i=>i.Flags==0).ToArray(),0);
        var trial=initial with { State=2,Transaction=1,Next=2,Remaining=60000,Serial=2,Request=f.Sends.Last().Request };
        f.Reply(trial,0); Equal(60000,f.Client.Capture(0).Remaining,"starts at phone deadline");
        f.Reply(trial with { Serial=3,Request=0,Remaining=59900 },Seconds(10));
        Equal(50000,f.Client.Capture(Seconds(10)).Remaining,"network delay cannot extend observed deadline");
        f.Reply(trial with { Serial=2,Request=0 },Seconds(11));
        Equal(49000,f.Client.Capture(Seconds(11)).Remaining,"out of order ignored");
        Check(!f.Client.Begin(3,trial,null,Seconds(61)),"late keep disabled");
        f.P=f.P with { RunId=100 }; f.Client.Poll(Seconds(62));
        f.Reply(trial with { Serial=4,Request=0 },Seconds(62));
        Check(f.Client.Capture(Seconds(62)).Snapshot is null,"old sender ignored");
        f=new Fixture(); f.Client.Poll(0); var oldQuery=f.Sends.Last().Request;
        f.Reply(Snapshot(oldQuery),0); f.Client.Poll(Seconds(1),true);
        oldQuery=f.Sends.Last().Request;
        Check(f.Client.Begin(2,Snapshot(),Items.Where(i=>i.Flags==0).ToArray(),Seconds(1)),"begin after query");
        f.Reply(Snapshot(oldQuery,2),Seconds(1));
        f.Client.Poll(Seconds(2)); Equal((byte)2,f.Sends.Last().Kind,"earlier query cannot cancel pending operation");
    }
    public static void Presence()
    {
        using var r=new UdpReceiver(new(IPAddress.Loopback,0),TextWriter.Null,controlSend:(_,_)=>{});
        long now=Stopwatch.GetTimestamp(); var m=Snapshot();
        r.ProcessDatagram(RemoteLayoutProtocol.Encode(m),new(IPAddress.Loopback,1234),now);
        Check(r.Presence.RunId is null && !r.Presence.Connected,"cannot admit run");
        // Existing heartbeat golden for run 99, followed by layout-only traffic after presence expiry.
        var hb=new byte[10]; hb[0]=2; hb[1]=4; System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(hb.AsSpan(2),99);
        r.ProcessDatagram(hb,new(IPAddress.Loopback,1234),now);
        Check(r.Presence.Connected,"heartbeat admits");
        r.ProcessDatagram(RemoteLayoutProtocol.Encode(m),new(IPAddress.Loopback,1234),now+Seconds(3));
        Check(!r.Presence.Connected,"layout cannot renew lease");
    }
}
