using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using Rightpad.Receiver;
namespace Rightpad.Receiver.Tests;

internal static class ModeBindingTests
{
    public static IEnumerable<(string Name,Func<Task> Run)> Cases => new (string,Action)[] {
        ("Mode unbound and strict codec",Codec), ("Mode momentary dwell hold move and release",Momentary),
        ("Mode toggle DOWN dedupe UP and second DOWN",Toggle), ("Mode ordering loss and late DOWN fencing",Ordering),
        ("Mode stationary refresh and lease recovery",Lease), ("Mode lifecycle and config fences",Lifecycle),
        ("Mode output error isolated and truthful",Failure), ("Mode catalogue complete and independent merge",Mappings),
        ("Mode atomic save draft failure and unknown preservation",Save)
    }.Select(c=>(c.Item1,(Func<Task>)(()=> {c.Item2();return Task.CompletedTask;})));
    private static void Check(bool condition,string message) {if(!condition) throw new Exception(message);}
    private sealed class Keyboard : IModeKeyboard {
        public readonly List<(string,bool)> Calls=new();public bool Fail;public bool Closed;
        public void Set(ModeKey key,bool down) {if(Fail) throw new IOException("injected output failure");Calls.Add((key.Id,down));}
        public void Dispose()=>Closed=true;
    }
    private sealed class Harness : IDisposable {
        public readonly RuntimeSettingsStore Store;
        public readonly Keyboard Keyboard=new();public readonly ModeSession Session;
        public readonly List<XboxGamepadState> States=new();public readonly List<byte[]> Replies=new();
        public long Now=Stopwatch.GetTimestamp();public uint Seq,Contact;public ulong Gen;public bool GamepadReady=true;
        public Harness(ModeBinding? binding=null) {
            Store=new(RuntimeSettings.Default with {ModeControl=binding??new(ModeBindingKind.Keyboard,"A")});
            Session=new(Store,s=> {States.Add(s);return GamepadReady;},(_,b)=>Replies.Add(b),()=>Keyboard,clock:()=>Now,gamepadReady:()=>GamepadReady);
            Session.UpdatePresence(new(7,Now,true,"127.0.0.1"));Arm();
        }
        public void Arm() {Gen=Session.Generation;Seq=Contact=0;Packet(new(7,Gen,0,0,ModeContactPhase.Idle));}
        public void Packet(ModeContact p)=>Session.Process(p,IPAddress.Loopback,Now);
        public ModeContact Down() {var p=new ModeContact(7,Gen,++Seq,++Contact,ModeContactPhase.Down);Packet(p);return p;}
        public void Up()=>Packet(new(7,Gen,++Seq,Contact,ModeContactPhase.Up));
        public void Advance(int ms) {Now+=Stopwatch.Frequency*ms/1000;Session.Tick(Now);}
        public void Dispose()=>Session.Dispose();
    }
    private static void Codec() {
        using var h=new Harness(new());h.Down();h.Up();h.Advance(50);Check(h.Keyboard.Calls.Count==0 && !h.Session.Held,"unbound output");
        var b=new byte[32];b[0]=2;b[1]=8;BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(10),3);
        Check(ModeProtocol.TryDecode(b,out _),"idle codec");
        for(int n=0;n<40;n++) {if(n==32) continue;Check(!ModeProtocol.TryDecode(new byte[n],out _),"length");}
        for(int i=27;i<32;i++) {b[i]=1;Check(!ModeProtocol.TryDecode(b,out _),"reserved");b[i]=0;}
        b[26]=4;Check(!ModeProtocol.TryDecode(b,out _),"phase");
    }
    private static void Momentary() {
        using var h=new Harness();var down=h.Down();Check(h.Session.Held,"down");h.Up();Check(h.Session.Held,"dwell");
        h.Advance(30);Check(!h.Session.Held && h.Keyboard.Calls.SequenceEqual(new[]{("A",true),("A",false)}),"short release");
        h.Down();for(int i=0;i<20;i++){h.Advance(100);h.Packet(new(7,h.Gen,h.Seq,h.Contact,ModeContactPhase.Down));}
        Check(h.Session.Held && h.Keyboard.Calls.Count==3,"stationary hold");h.Up();Check(!h.Session.Held,"normal UP");
    }
    private static void Toggle() {
        using var h=new Harness(new(ModeBindingKind.Keyboard,"RightCtrl",ModePressBehavior.Toggle));
        var p=h.Down();for(int i=0;i<8;i++) h.Packet(p);Check(h.Session.Held && h.Keyboard.Calls.Count==1,"duplicate toggle");
        h.Up();h.Advance(100);Check(h.Session.Held,"UP keeps hold");
        h.Packet(new(7,h.Gen,h.Seq,h.Contact,ModeContactPhase.Up));h.Down();Check(!h.Session.Held,"second down off");h.Up();Check(h.Keyboard.Calls.Count==2,"second up");
    }
    private static void Ordering() {
        using var h=new Harness(new(ModeBindingKind.Keyboard,"A",ModePressBehavior.Toggle));
        var down=new ModeContact(7,h.Gen,1,1,ModeContactPhase.Down);var up=down with {Sequence=2,Phase=ModeContactPhase.Up};
        h.Packet(up);Check(!h.Session.Held && h.Session.Acknowledged==0,"UP before DOWN must retry");
        h.Packet(down);h.Packet(up);h.Packet(down);Check(h.Session.Held && h.Session.Acknowledged==2,"late down no toggle");
        h.Packet(down with {Sequence=3});Check(h.Session.Acknowledged==2,"same contact new sequence");
        h.Packet(up with {Run=8,Sequence=3});Check(h.Session.Acknowledged==2,"run rejection");
        h.Packet(up with {Sequence=99,Phase=ModeContactPhase.Cancel});Check(!h.Session.Held && h.Session.Generation!=h.Gen,"priority cancel");
        h.Packet(down);Check(!h.Session.Held,"old DOWN fenced");
    }
    private static void Lease() {
        using var h=new Harness(new(ModeBindingKind.Keyboard,"A",ModePressBehavior.Toggle));h.Down();h.Up();
        for(int i=0;i<20;i++){h.Advance(100);h.Packet(new(7,h.Gen,h.Seq,h.Contact,ModeContactPhase.Up));}
        Check(h.Session.Held,"toggle refresh");h.Advance(601);Check(!h.Session.Held,"disconnect release");
        h.Packet(new(7,h.Gen,++h.Seq,++h.Contact,ModeContactPhase.Down));Check(!h.Session.Held,"expired token");
        h.Arm();Check(!h.Session.Held,"reconnect released");h.Down();Check(h.Session.Held,"fresh contact");
    }
    private static void Lifecycle() {
        using var h=new Harness(new(ModeBindingKind.Keyboard,"A",ModePressBehavior.Toggle));h.Down();h.Up();
        var token=h.Gen;h.Store.Publish(h.Store.Current);Check(h.Session.Generation==token && h.Session.Held,"identical config");
        h.Store.Publish(h.Store.Current with {SensitivityX=8});Check(h.Session.Held,"unrelated save");
        h.Store.Publish(h.Store.Current with {ModeControl=new(ModeBindingKind.Keyboard,"B")});Check(!h.Session.Held && h.Keyboard.Calls.Last()==("A",false),"old release before rebind");
        h.Packet(new(7,token,3,2,ModeContactPhase.Down));Check(!h.Session.Held,"contact not reinterpreted");
        h.Arm();h.Down();Check(h.Keyboard.Calls.Last()==("B",true),"next contact binding");
        h.Session.UpdatePresence(new(7,h.Now,false,"127.0.0.1"));Check(!h.Session.Held,"presence safety");
        h.Session.UpdatePresence(new(8,h.Now,true,"127.0.0.1"));h.Arm();h.Down();h.Session.Dispose();Check(!h.Session.Held && h.Keyboard.Closed,"dispose neutral");
    }
    private static void Failure() {
        using var h=new Harness();h.Keyboard.Fail=true;h.Down();Check(!h.Session.Held && h.Session.Status.Contains("输出失败") && h.Keyboard.Closed,"failure status");
        using var g=new Harness(new(ModeBindingKind.Gamepad,"LT",ModePressBehavior.Toggle));g.Down();g.Up();g.GamepadReady=false;g.Advance(10);
        Check(!g.Session.Held && g.Session.Status.Contains("输出失败"),"runtime gamepad loss");
    }
    private static void Mappings() {
        var keys=ModeKeys.All;Check(keys.Select(k=>(k.Kind,k.Id)).Distinct().Count()==keys.Count,"IDs unique");
        Check(keys.Count(k=>k.Kind==ModeBindingKind.Gamepad)==16,"all gamepad mappings");
        foreach(var name in new[]{"A","Z","0","9","F1","F24","LeftCtrl","RightCtrl","LeftAlt","RightAlt","LeftShift","RightShift","LeftWin","RightWin","Numpad0","NumpadEnter","Print Screen","Pause"}) Check(keys.Any(k=>k.Id==name),"missing "+name);
        Check(keys.Single(k=>k.Id=="NumpadEnter").ScanCode==0xE01C && keys.Single(k=>k.Id=="Enter").ScanCode==0,"keypad enter distinct");
        foreach(var k in keys.Where(k=>k.Kind==ModeBindingKind.Gamepad)) {
            using var h=new Harness(new(k.Kind,k.Id));h.Down();Check(h.States.Last()==k.Gamepad,"mapping "+k.Id);h.Up();h.Advance(30);Check(h.States.Last()==default,"release "+k.Id);
        }
        var b=new XboxGamepadState(XboxGamepadButtons.B,100,200);var mode=new XboxGamepadState(XboxGamepadButtons.B|XboxGamepadButtons.X,255,0);
        Check(ModeKeys.Merge(b,mode)==new XboxGamepadState(XboxGamepadButtons.B|XboxGamepadButtons.X,255,200),"OR and trigger max");
        Check(ModeKeys.Merge(b,default)==b && ModeKeys.Merge(default,mode)==mode,"independent release");
    }
    private static void Save() {
        string root=Path.Combine(Path.GetTempPath(),"rightpad-mode-"+Guid.NewGuid());Directory.CreateDirectory(root);
        string path=Path.Combine(root,"settings.json");File.WriteAllText(path,"{\"unknown\":42,\"modeControl\":{\"vendorNote\":\"keep\"}}");
        var loaded=SettingsFileStore.Load(path).Settings;Check(loaded.ModeControl==new ModeBinding(),"old default");
        var store=new RuntimeSettingsStore(loaded);var file=new SettingsFileStore(path);var vm=new SettingsViewModel(store,file);
        vm.Mode.Select(ModeBindingKind.Keyboard,"RightAlt");vm.Mode.Behavior(ModePressBehavior.Toggle);
        Check(store.Current.ModeControl==new ModeBinding() && vm.CanSave,"draft only");
        Check(vm.SaveAsync().GetAwaiter().GetResult(),"save");Check(store.Current.ModeControl==new ModeBinding(ModeBindingKind.Keyboard,"RightAlt",ModePressBehavior.Toggle),"atomic binding and behavior");
        Check(SettingsFileStore.Load(path).Settings.ModeControl==store.Current.ModeControl && File.ReadAllText(path).Contains("vendorNote"),"roundtrip unknown preservation");
        string blocked=Path.Combine(root,"blocked");Directory.CreateDirectory(blocked);
        var failing=new SettingsViewModel(store,new SettingsFileStore(blocked));failing.Mode.Select(ModeBindingKind.Gamepad,"RT");
        Check(!failing.SaveAsync().GetAwaiter().GetResult() && store.Current.ModeControl.Kind==ModeBindingKind.Keyboard && failing.HasUnsavedChanges,"failed save no publish");
    }
}
