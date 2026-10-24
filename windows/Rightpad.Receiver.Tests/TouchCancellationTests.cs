using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class TouchCancellationTests
{
    public static readonly (string Name,Func<Task> Run)[] Cases = [
        ("Touch cancel held drag, queues, no click or confirmation and recovery",()=>Run(Drag)),
        ("Touch cancel before DOWN, late tails and repeated barrier",()=>Run(Reorder)),
        ("Touch cancel old run, source, contact and delayed cancel isolation",()=>Run(Isolation)),
        ("Touch stationary heartbeat retains drag; cancel loss expires or run-resync clears",()=>Run(Loss)),
        ("Touch cancellation strict codec and ACK independent golden",()=>Run(Codec))
    ];
    private static Task Run(Action action) {action();return Task.CompletedTask;}
    internal static byte[] Cancel(ulong run,uint session,uint down,uint sequence) {
        byte[] b=new byte[22];b[0]=2;b[1]=9;
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(2),run);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(10),session);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(14),down);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(18),sequence);return b;
    }
    private sealed class Harness:IDisposable {
        public int Presses,Releases,Clicks,Confirmations,Replies;
        public bool Held;
        public long Now=Stopwatch.Frequency*10;
        public readonly LeftButtonController Buttons;
        public readonly GestureProcessor Gesture;
        public readonly TouchSessionProcessor Motion;
        public readonly UdpReceiver Receiver;
        public Harness() {
            Buttons=new(()=>{Presses++;Held=true;},()=>{Releases++;Held=false;},_=>{},()=>{});
            Gesture=new((hold,packet)=>{Clicks++;Buttons.Click(hold,()=>Confirmations++);},RuntimeSettings.Default,Buttons.BeginDrag,Buttons.EndDrag);
            Motion=new((x,y)=>{});
            Receiver=new(new(IPAddress.Loopback,0),TextWriter.Null,motion:Motion,gesture:Gesture,
                cancelButtons:Buttons.CancelPendingAndRelease,controlSend:(ip,b)=>{if(b.AsSpan(0,4).SequenceEqual("RPTC"u8))Replies++;});
        }
        public void Send(byte[] b,string ip="127.0.0.1")=>Receiver.ProcessDatagram(b,new(IPAddress.Parse(ip),1234),Now);
        public void Touch(TouchEventType type,uint seq,uint session=1,ulong time=0) {
            byte[] b=PresenceTests.Touch(1,type,seq,time:time);
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12),session);Send(b);
        }
        public void Drag() {Touch(TouchEventType.Down,0,time:1);Touch(TouchEventType.Up,1,time:2);Touch(TouchEventType.Down,2,2,3);Check(Held && Gesture.IsDragging,"drag held");}
        public void Dispose(){Receiver.Dispose();Buttons.Dispose();}
    }
    private static void Drag() {
        using var h=new Harness();h.Drag();int presses=h.Presses,clicks=h.Clicks,confirm=h.Confirmations;
        h.Buttons.Click(1000,()=>h.Confirmations++); // A queued click must never emerge after safety release.
        h.Send(Cancel(1,2,2,3));Check(!h.Held && !h.Gesture.IsDragging && !h.Gesture.DoubleTapArmed,"released and disarmed");
        Equal((uint?)null,h.Motion.ActiveSessionId,"motion reset");
        h.Touch(TouchEventType.Up,4,2,4);h.Touch(TouchEventType.Move,5,2,5);
        Equal(presses,h.Presses,"no repress");Equal(clicks,h.Clicks,"no extra click");Equal(confirm,h.Confirmations,"no feedback");
        h.Touch(TouchEventType.Down,6,3,100);h.Touch(TouchEventType.Up,7,3,101);Equal(clicks+1,h.Clicks,"next tap");
        h.Touch(TouchEventType.Down,8,4,102);Check(h.Gesture.IsDragging,"next drag");h.Touch(TouchEventType.Up,9,4,103);Check(!h.Held,"next release");
    }
    private static void Reorder() {
        using var h=new Harness();h.Send(PresenceTests.Heartbeat(1));h.Send(Cancel(1,1,0,2));
        h.Touch(TouchEventType.Down,0);h.Touch(TouchEventType.Move,1);h.Touch(TouchEventType.Up,3);
        Equal(0,h.Clicks,"cancel before DOWN fences it");Equal((uint?)null,h.Motion.ActiveSessionId,"no resurrection");
        h.Send(Cancel(1,1,0,2));Equal(2,h.Replies,"lost ACK can retry idempotently");
        h.Touch(TouchEventType.Down,4,2,100);h.Touch(TouchEventType.Up,5,2,101);Equal(1,h.Clicks,"normal next tap");
    }
    private static void Isolation() {
        using var h=new Harness();h.Drag();
        h.Send(Cancel(2,2,2,3));h.Send(Cancel(1,2,2,3),"127.0.0.2");h.Send(Cancel(1,9,2,3));Check(h.Held,"wrong run/source/session ignored");
        h.Touch(TouchEventType.Down,4,3,100);Check(!h.Held,"replacement closes previous drag");
        h.Touch(TouchEventType.Up,5,3,101);h.Touch(TouchEventType.Down,6,4,102);Check(h.Held,"new drag held");
        h.Send(Cancel(1,2,2,3));h.Send(Cancel(1,2,2,7));Check(h.Held,"delayed or old contact cannot release new contact");
        h.Send(Cancel(1,4,6,7));Check(!h.Held,"matching new cancellation");
    }
    private static void Loss() {
        using var h=new Harness();h.Drag();
        for(int i=0;i<8;i++){h.Now+=Stopwatch.Frequency/2;h.Send(PresenceTests.Heartbeat(1));}
        Check(h.Held && h.Receiver.InputTimeouts>0,"stationary drag legal beyond diagnostic timeout");
        // Simulate all cancel/ACK packets lost. Sender stops old-run presence.
        h.Now+=Stopwatch.Frequency*3;h.Receiver.CheckTimeouts(h.Now);Check(!h.Held,"presence expiry releases");
        h.Touch(TouchEventType.Down,7,3,100);h.Touch(TouchEventType.Up,8,3,101);h.Touch(TouchEventType.Down,9,4,102);Check(h.Held,"recovered drag");
        h.Send(PresenceTests.Heartbeat(2));Check(!h.Held,"new run resync releases even with heartbeat alive");
        h.Send(Cancel(1,4,9,10));Equal((ulong?)2,h.Receiver.Presence.RunId,"old cancel cannot cross run");
    }
    private static void Codec() {
        var b=Convert.FromHexString("02090807060504030281785634122A0000002B000000");
        Check(TouchCancellationProtocol.TryDecode(b,out var c),"golden cancel");
        Check(TouchCancellationProtocol.Acknowledge(c).SequenceEqual(Convert.FromHexString("52505443010100000807060504030281785634122A0000002B000000")),"ACK golden");
        for(int i=0;i<b.Length;i++)Check(!TouchCancellationProtocol.TryDecode(b[..i],out _),"truncated");
        Check(!TouchCancellationProtocol.TryDecode([..b,0],out _),"trailing");Check(!TouchCancellationProtocol.TryDecode(Cancel(1,1,3,3),out _),"order invalid");
        Check(!PacketDecoder.TryDecode(b,out _,out _),"old decoder never treats cancel as UP");
    }
}
