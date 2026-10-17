using System.Diagnostics;
using System.Net;
using static Rightpad.Receiver.Tests.Program;
namespace Rightpad.Receiver.Tests;
internal static class RemoteLayoutJavaTests
{
    private static string javaExecutable = "", classPath = "";
    private static long T(long ms)=>ms*Stopwatch.Frequency/1000;
    private sealed class Peer : IDisposable {
        private readonly Process process;
        public int Writes, Publications, X;
        public Peer() {
            process=Process.Start(new ProcessStartInfo(javaExecutable) {
                UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,
                ArgumentList={"-cp",classPath,"com.rightpad.capture.LayoutPeer"}
            })!;
        }
        public LayoutMessage Handle(LayoutMessage message,long ms) {
            process.StandardInput.WriteLine(ms+" "+Convert.ToBase64String(RemoteLayoutProtocol.Encode(message))); process.StandardInput.Flush();
            var read=process.StandardOutput.ReadLineAsync(); Check(read.Wait(5000),"Java peer response bounded");
            var parts=read.Result!.Split('|'); Writes=int.Parse(parts[1]);Publications=int.Parse(parts[2]);X=int.Parse(parts[3]);
            return RemoteLayoutProtocol.Decode(Convert.FromBase64String(parts[0]))!;
        }
        public void Fail() { process.StandardInput.WriteLine("fail");process.StandardInput.Flush();Equal("ok",process.StandardOutput.ReadLine(),"save fault armed"); }
        public void Dispose() {process.StandardInput.Close();if(!process.WaitForExit(5000))process.Kill();process.Dispose();}
    }
    private sealed class F : IDisposable {
        public readonly Peer Phone=new(); public readonly List<LayoutMessage> Sends=[];
        public readonly RemoteLayoutClient Client; public LayoutMessage Basis;
        public F() {
            Client=new(()=>new(99,0,true,"127.0.0.1"),(_,b)=>Sends.Add(RemoteLayoutProtocol.Decode(b)!));
            Client.Poll(0); Basis=Phone.Handle(Sends.Last(),0);Reply(Basis,0);
        }
        public void Reply(LayoutMessage m,long ms)=>Client.Accept(RemoteLayoutProtocol.Encode(m),IPAddress.Loopback,T(ms));
        public LayoutMessage Try(long ms=0) {
            var items=Basis.Items.Where(i=>i.Flags==0).Select(i=>i.Id=="xbox.b.slide"?i with {Rect=i.Rect with {X=650}}:i).ToArray();
            Check(Client.Begin(2,Basis,items,T(ms)),"explicit user Try");return Sends.Last();
        }
        public LayoutMessage Query(long ms) { Client.Poll(T(ms),true);var q=Sends.Last();Equal((byte)1,q.Kind,"reconciliation only");return q; }
        public void Hide(long ms) { Client.Suspend(T(ms));Client.Poll(T(ms),true); }
        public void Dispose()=>Phone.Dispose();
    }
    public static void Run(string java, string classes) {
        javaExecutable=java; classPath=classes;
        using(var f=new F()) {
            var first=f.Try();f.Hide(0); // both first Try and hide Query are lost
            var q=f.Query(120000);Equal(first.Transaction,q.Transaction,"original token at reopen120s");
            var sealedState=f.Phone.Handle(q,120000);f.Reply(sealedState,120000);
            Equal((byte)4,sealedState.State,"never applied Query seals token");Equal(700,f.Phone.X,"original layout");
            var late=f.Phone.Handle(first,120001);Equal((byte)4,late.State,"late first Try rejected");Equal(700,f.Phone.X,"late first Try cannot publish");
            var duplicate=f.Phone.Handle(q,120002);Equal((byte)4,duplicate.State,"duplicate seal idempotent");Equal(0,f.Phone.Writes,"seal never saves");
            f.Reply(duplicate,120002);f.Reply(sealedState,120003); // duplicate/out of order correlations
            Check(!f.Client.Capture(T(120003)).Uncertain,"confirmed seal remains certain");Equal(1,f.Sends.Count(m=>m.Kind==2),"no old Try retransmission");
            f.Basis=sealedState;var fresh=f.Try(120003);Equal(first.Transaction+1,fresh.Transaction,"new click new token");
            Equal((byte)2,f.Phone.Handle(fresh,120003).State,"fresh explicit trial works");
            Console.WriteLine("JAVA A PASS lost first Try + hidden120s + Query before late Try: original X700, writes0, old token sealed, fresh Try works");
        }
        using(var f=new F()) {
            var first=f.Try();var ignored=f.Phone.Handle(first,0);Equal((byte)2,ignored.State,"Try actually applied; ACK lost");
            int publications=f.Phone.Publications; f.Hide(100);var q=f.Query(2000);
            var trial=f.Phone.Handle(q,2000);f.Reply(trial,2000);
            Equal(58000,trial.Remaining,"original deadline at60000");Equal(58000,f.Client.Capture(T(2000)).Remaining,"client reads actual remaining");
            var dup=f.Phone.Handle(first,3000);Equal(57000,dup.Remaining,"duplicate old Try cannot renew");
            Equal(publications,f.Phone.Publications,"no repeated publication");
            f.Hide(4000);q=f.Query(60000);var restored=f.Phone.Handle(q,60000);f.Reply(restored,60000);
            Equal((byte)4,restored.State,"phone expires independently");Equal(700,f.Phone.X,"original geometry restored");
            Equal((byte)4,f.Client.Capture(T(60000)).Snapshot!.State,"PC syncs expiry");Equal(1,f.Sends.Count(m=>m.Kind==2),"no trial replay");
            Console.WriteLine("JAVA C PASS Try-before-Query ACK lost: original60000ms deadline, duplicate no renewal, original layout restored at60000");
        }
        foreach(string save in new[]{"saved","lost","failed"}) using(var f=new F()) {
            var first=f.Try();var applied=f.Phone.Handle(first,0);f.Reply(applied,0);
            Check(f.Client.Begin(3,applied,null,T(1000)),"explicit Keep");var keep=f.Sends.Last();
            if(save=="failed")f.Phone.Fail();
            if(save!="lost") {var result=f.Phone.Handle(keep,1000);Equal(save=="saved"?(byte)3:(byte)5,result.State,"save processed; result lost");}
            f.Hide(1100);var q=f.Query(120000);var actual=f.Phone.Handle(q,120000);f.Reply(actual,120000);
            Equal(save=="saved"?(byte)3:save=="failed"?(byte)5:(byte)4,actual.State,"actual authoritative save result");
            Equal(save=="saved"?650:700,f.Phone.X,"correct effective layout");
            Equal((byte)actual.State,f.Client.Capture(T(120000)).Snapshot!.State,"PC reports actual persistence");
            Check(!f.Client.Capture(T(120000)).Uncertain,"result confirmed by Query");
            var late=f.Phone.Handle(keep,120001);Equal(actual.State,late.State,"late Keep cannot revive or rollback");
            Equal(save=="lost"?0:1,f.Phone.Writes,"no duplicate Save");
            var lateTry=f.Phone.Handle(first,120002);Equal(actual.State,lateTry.State,"late Try terminal idempotent");
            Equal(save=="saved"?650:700,f.Phone.X,"saved layout never reverted");
            Equal(1,f.Sends.Count(m=>m.Kind==3),"reopen only queries Keep result");
            Console.WriteLine($"JAVA D PASS Keep {save}: real state={actual.State}, writes={f.Phone.Writes}, X={f.Phone.X}, late Keep no resurrection/rollback");
        }
    }
}
