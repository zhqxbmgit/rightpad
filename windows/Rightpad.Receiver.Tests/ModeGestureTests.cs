using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Point=System.Windows.Point;
namespace Rightpad.Receiver.Tests;

internal static class ModeGestureTests
{
    public static IEnumerable<(string Name,Func<Task> Run)> Cases => new (string,Action)[] {
        ("Mode gesture exact v2 codec and Java golden",Codec),
        ("Mode gesture UP-only pulse three bindings and unbound",Actions),
        ("Mode gesture ordered duplicate late run contact generation and lease",Ordering),
        ("Mode gesture Save old contact snapshot next contact and B X version isolation",Snapshots),
        ("Mode gesture layout purpose lifecycle and incompatible client fences",Boundaries),
        ("Mode gesture pulse release pending cannot be overwritten and failure never replays",Pulses),
        ("Mode gesture mouse and gamepad source contributions preserved",Merge),
        ("Mode gesture Ring UP entry manual close contact and original six slots",Ring),
        ("Mode gesture geometry validation independent widths overflow and old settings",Geometry),
        ("Mode gesture common disk first Save failure and geometry unavailable",Save)
        ,("Mode gesture production UDP dispatch keeps Touch and config versions independent",UdpRoute)
    }.Select(c=>(c.Item1,(Func<Task>)(()=>{c.Item2();return Task.CompletedTask;})))
        .Append(("Mode gesture WPF directory and proportional fixed preview",()=>Sta(Ui)));
    static void Check(bool x,string message) {if(!x)throw new Exception(message);}
    static ModeGeometry G(ulong id=1)=>new(id,1200,2670,3,new(600,772,264,1881),0,90,1200,2670);
    static ModeBinding Config()=>new(ModeBindingKind.Mouse,"left",ModePressBehavior.Toggle) {Purpose=ModePurpose.GestureBinding,
        Gesture=new() {Origin=new(ModeBindingKind.Keyboard,"A"),First=new(ModeBindingKind.Gamepad,"LB"),Second=new(ModeBindingKind.Mouse,"right")}};
    sealed class Keyboard : IModeKeyboard {
        public readonly List<(string,bool)> Calls=[];public bool FailDown,FailUp;
        public void Set(ModeKey k,bool down) {Calls.Add((k.Id,down));if(down?FailDown:FailUp)throw new IOException("fake keyboard failure");}
        public void Dispose() { }
    }
    sealed class Cursor : IModeMenuCursor {
        public Point Position=new(500,400);
        public MenuPlacement Capture()=>MenuPlacement.Create(new(0,0,1000,800),500,400);
        public Point Read(MenuPlacement p)=>Position;
    }
    sealed class Mouse : IMouseOutput {
        public readonly List<(ModeMouseButton,bool)> Calls=[];
        public string BackendName=>"fake";public string ButtonSupport=>"left,right,middle";public MouseOutputStats Stats=>default;
        public void Move(int x,int y) { } public void LeftDown()=>SetButton(ModeMouseButton.Left,true);public void LeftUp()=>SetButton(ModeMouseButton.Left,false);
        public void SetButton(ModeMouseButton b,bool d)=>Calls.Add((b,d));public void Dispose() { }
    }
    sealed class Harness : IDisposable {
        public readonly RuntimeSettingsStore Store;public readonly ModeSession Session;public readonly Keyboard Keyboard=new();
        public readonly List<XboxGamepadState> Pad=[];public readonly Mouse Mouse=new();public readonly MouseButtonMerge MouseMerge;
        public readonly List<byte[]> Replies=[];public readonly Cursor Cursor=new();public int Confirmations;
        public long Now=Stopwatch.GetTimestamp();public ulong Gen;public uint Seq,Contact;public ulong Revision=1;public ModeGeometry Geometry=G();
        public Harness(ModeBinding? config=null) {
            Store=new(RuntimeSettings.Default with {ModeControl=config??Config()});MouseMerge=new(Mouse);
            Session=new(Store,p=>{Pad.Add(p);return true;},(_,b)=>Replies.Add(b),()=>Keyboard,clock:()=>Now,
                menuCursor:Cursor,haptic:(_,_)=>Confirmations++,mouse:(b,d)=>MouseMerge.Set(MouseButtonSource.Mode,b,d));
            Session.UpdatePresence(new(7,Now,true,"127.0.0.1"));Arm();
        }
        public void Arm() {Gen=Session.Generation;Seq=Contact=0;Packet(new(7,Gen,0,0,ModeContactPhase.Idle,Revision,0,Geometry));}
        public void Packet(ModeContact p)=>Session.Process(p,IPAddress.Loopback,Now);
        public ModeContact Down() {var p=new ModeContact(7,Gen,++Seq,++Contact,ModeContactPhase.Down,Revision,0,Geometry);Packet(p);return p;}
        public ModeContact Up(byte selected) {var p=new ModeContact(7,Gen,++Seq,Contact,ModeContactPhase.Up,Revision,selected,Geometry);Packet(p);return p;}
        public void Advance(int ms) {Now+=Stopwatch.Frequency*ms/1000;Session.Tick(Now);}
        public void Save(ModeBinding b) {Store.Publish(Store.Current with {ModeControl=b});Revision++;}
        public void Dispose()=>Session.Dispose();
    }
    static byte[] Encode(ModeContact p) {
        var b=new byte[96];b[0]=2;b[1]=10;b[26]=(byte)p.Phase;b[27]=2;b[28]=p.Selection;
        void U(int at,ulong x)=>BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(at),x);
        void I(int at,int x)=>BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(at),x);
        U(2,p.Run);U(10,p.Generation);I(18,(int)p.Sequence);I(22,(int)p.Contact);U(32,p.Revision);
        var g=p.Geometry!;U(40,g.Identity);I(48,g.Width);I(52,g.Height);I(56,BitConverter.SingleToInt32Bits(g.Density));
        I(60,g.Mode.X);I(64,g.Mode.Y);I(68,g.Mode.Width);I(72,g.Mode.Height);I(80,g.Left);I(84,g.Top);I(88,g.Right);I(92,g.Bottom);return b;
    }
    static void Codec() {
        var p=new ModeContact(7,100,1,1,ModeContactPhase.Down,1,0,G());var b=Encode(p);
        Check(ModeGestureProtocol.TryDecode(b,out var decoded) && decoded==p,"full roundtrip");
        const string golden="020a07000000000000006400000000000000010000000100000001020000000001000000000000000100000000000000b00400006e0a0000000040405802000004030000080100005907000000000000000000005a000000b00400006e0a0000";
        Check(Convert.ToHexString(b).Equals(golden,StringComparison.OrdinalIgnoreCase),"Java shared golden");
        for(int i=0;i<110;i++) if(i!=96) Check(!ModeGestureProtocol.TryDecode(new byte[i],out _),"exact length");
        foreach(int at in new[]{29,30,31,76,77,78,79}) {var bad=(byte[])b.Clone();bad[at]=1;Check(!ModeGestureProtocol.TryDecode(bad,out _),"reserved");}
        foreach(int at in new[]{0,1,26,27,28}) {var bad=(byte[])b.Clone();bad[at]=255;Check(!ModeGestureProtocol.TryDecode(bad,out _),"field");}
        var reply=ModeGestureProtocol.Reply(7,100,0,true,Config(),1,1);Check(reply.Length==56 && reply[4]==2 && reply[29]==2,"RPMD v2");
        Check(!ModeProtocol.TryDecode(b,out _),"legacy cannot interpret extension");
    }
    static void Actions() {
        for(byte selected=0;selected<=3;selected++) {
            using var h=new Harness();h.Down();h.Advance(400);Check(!h.Session.Held && h.Keyboard.Calls.Count==0 && h.Pad.Count==0 && h.Mouse.Calls.Count==0,"DOWN hold 400ms never emits");
            h.Up(selected);Check(h.Session.Held==(selected!=0),"UP final action");h.Advance(24);Check(h.Session.Held==(selected!=0),"fixed dwell");h.Advance(2);Check(!h.Session.Held,"pulse released");
            Check(selected!=1 || h.Keyboard.Calls.SequenceEqual(new[]{("A",true),("A",false)}),"origin binding");
            Check(selected!=2 || h.Pad.First().Buttons==XboxGamepadButtons.LeftShoulder && h.Pad.Last()==default,"first binding");
            Check(selected!=3 || h.Mouse.Calls.SequenceEqual(new[]{(ModeMouseButton.Right,true),(ModeMouseButton.Right,false)}),"second mouse binding");
        }
        using var u=new Harness(Config() with {Gesture=new()});u.Down();u.Up(1);Check(!u.Session.Held && u.Keyboard.Calls.Count==0,"unbound never falls back to old single");
    }
    static void Ordering() {
        using var h=new Harness();var down=new ModeContact(7,h.Gen,1,1,ModeContactPhase.Down,1,0,G());var up=down with {Sequence=2,Phase=ModeContactPhase.Up,Selection=1};
        h.Packet(up);Check(h.Session.Acknowledged==0,"UP before DOWN");h.Packet(down);h.Packet(down);h.Packet(up);h.Packet(up);h.Packet(down);
        Check(h.Keyboard.Calls.Count==1 && h.Session.Acknowledged==2,"exactly once");h.Advance(30);h.Packet(up);Check(h.Keyboard.Calls.Count==2,"refresh never replays");
        h.Packet(down with {Sequence=3,Contact=1});h.Packet(down with {Sequence=3,Contact=2,Run=8});Check(h.Session.Acknowledged==2,"wrong contact/run");
        h.Advance(601);var generation=h.Session.Generation;h.Packet(down);h.Packet(up);Check(h.Session.Generation==generation && !h.Session.Held,"lease fences late");
    }
    static void Snapshots() {
        using var h=new Harness();using var channel=new ControlConfigChannel(h.Store,()=>new(7,h.Now,true,"127.0.0.1"),(_,_)=>{},TextWriter.Null);
        var version=channel.CaptureVersion();h.Down();ulong gen=h.Gen;
        h.Save(h.Store.Current.ModeControl with {Gesture=h.Store.Current.ModeControl.Gesture with {Origin=new(ModeBindingKind.Keyboard,"B"),FirstWidthDp=10}});
        Check(h.Session.Generation==gen && channel.CaptureVersion()==version,"retains current contact; no B X version");
        h.Packet(new(7,h.Gen,++h.Seq,h.Contact,ModeContactPhase.Up,1,1,G()));Check(h.Keyboard.Calls.Last()==("A",true),"old revision old binding");h.Advance(30);
        h.Down();h.Up(1);Check(h.Keyboard.Calls.Last()==("B",true),"next DOWN latest");h.Advance(30);
        h.Down();h.Packet(new(7,h.Gen,++h.Seq,h.Contact,ModeContactPhase.Up,1,1,G()));Check(!h.Session.Held,"mismatched revision cannot reinterpret");
        using var menu=new Harness(Config() with {Gesture=new() {Second=new(Menu:true)},Slots=new() {Top=new(ModeBindingKind.Keyboard,"Esc")}});
        menu.Down();menu.Save(menu.Store.Current.ModeControl with {Slots=new() {Top=new(ModeBindingKind.Keyboard,"B")}});
        menu.Packet(new(7,menu.Gen,2,1,ModeContactPhase.Up,1,3,G()));
        Check(menu.Session.MenuSnapshot?.Configuration.Slots.Top.Key=="Esc","gesture menu action retains complete old Ring binding snapshot");
    }
    static void Boundaries() {
        foreach(string boundary in new[]{"cancel","layout","purpose","presence","run","stop"}) {
            using var h=new Harness();var down=h.Down();
            switch(boundary) {
                case "cancel":h.Packet(down with {Sequence=8,Phase=ModeContactPhase.Cancel});break;
                case "layout":h.Packet(down with {Geometry=G(2),Sequence=2,Phase=ModeContactPhase.Up,Selection=1});break;
                case "purpose":h.Save(h.Store.Current.ModeControl with {Purpose=ModePurpose.SingleBinding});break;
                case "presence":h.Session.UpdatePresence(new(7,h.Now,false,"127.0.0.1"));break;
                case "run":h.Session.UpdatePresence(new(8,h.Now,true,"127.0.0.1"));break;
                case "stop":h.Session.Dispose();break;
            }
            h.Packet(down with {Sequence=2,Phase=ModeContactPhase.Up,Selection=1});Check(!h.Session.Held && h.Keyboard.Calls.Count==0,"boundary "+boundary);
        }
        using var legacy=new Harness();legacy.Session.Process(new(7,legacy.Gen,1,1,ModeContactPhase.Down),IPAddress.Loopback,legacy.Now);Check(!legacy.Session.Held,"old type8 never gesture/single fallback");
        using var stale=new Harness();stale.Packet(new(7,stale.Gen,1,1,ModeContactPhase.Down,99,0,G()));Check(!stale.Session.Held && stale.Session.Generation!=stale.Gen,"wrong config resync without replay");
    }
    static void Pulses() {
        using var h=new Harness();h.Down();h.Up(1);var next=new ModeContact(7,h.Gen,3,2,ModeContactPhase.Down,1,0,G());h.Packet(next);
        Check(h.Session.Acknowledged==2,"pending release refuses following contact");h.Advance(30);h.Packet(next);h.Packet(next with {Sequence=4,Phase=ModeContactPhase.Up,Selection=1});Check(h.Keyboard.Calls.Count==3,"subsequent contact only after old release");
        foreach(bool press in new[]{true,false}) {
            using var f=new Harness();f.Keyboard.FailDown=press;f.Keyboard.FailUp=!press;var d=f.Down();var up=f.Up(1);f.Advance(30);var n=f.Keyboard.Calls.Count;
            f.Packet(up);f.Packet(d);f.Advance(100);Check(f.Keyboard.Calls.Count==n && !f.Session.Held && f.Session.Status.Contains("输出失败"),"failed output never replay");
        }
    }
    static void Merge() {
        using var h=new Harness(Config() with {Gesture=new() {Origin=new(ModeBindingKind.Mouse,"left")}});
        h.MouseMerge.Set(MouseButtonSource.Gesture,ModeMouseButton.Left,true);h.Down();h.Up(1);h.Advance(30);
        Check(h.Mouse.Calls.Count==1 && h.MouseMerge.GestureMask==1 && h.MouseMerge.ModeMask==0,"Mode only releases own mouse contribution");
        h.MouseMerge.Set(MouseButtonSource.Gesture,ModeMouseButton.Left,false);Check(h.Mouse.Calls.Last()==(ModeMouseButton.Left,false),"final source releases");
        var screen=new XboxGamepadState(XboxGamepadButtons.B);Check(ModeKeys.Merge(screen,new(XboxGamepadButtons.B|XboxGamepadButtons.LeftShoulder)).Buttons==(XboxGamepadButtons.B|XboxGamepadButtons.LeftShoulder),"gamepad union");Check(ModeKeys.Merge(screen,default)==screen,"screen contribution survives");
    }
    static void Ring() {
        using var h=new Harness(Config() with {Gesture=new() {Second=new(Menu:true)},Slots=new() {Top=new(ModeBindingKind.Keyboard,"Esc")}});
        var d=h.Down();Check(!h.Session.MenuOpen,"DOWN no menu");var up=h.Up(3);Check(h.Session.MenuOpen,"UP opens existing Ring");
        h.Packet(up);h.Packet(d);Check(h.Session.MenuOpen && h.Keyboard.Calls.Count==0,"Mode late packets not menu taps");
        h.Down();Check(!h.Session.MenuOpen,"new Mode DOWN manually closes");h.Up(1);h.Advance(30);Check(!h.Session.Held && h.Keyboard.Calls.Count==0,"close contact fully consumed, no origin fallback");
        h.Down();h.Up(3);h.Cursor.Position=new(500,300);
        TouchPacket Touch(TouchEventType type,uint seq)=>new(new(2,type,1,99,seq,7),[new(1000000UL*seq,1,1)]);
        // Route the original production Gesture recognizer inside the Ring.
        h.Session.RouteTouch(Touch(TouchEventType.Down,1),h.Store.Current,()=>{},()=>throw new Exception("ordinary gesture leak"));
        h.Session.RouteTouch(Touch(TouchEventType.Up,2),h.Store.Current,()=>{},()=>throw new Exception("ordinary gesture leak"));
        Check(h.Keyboard.Calls.Last()==("Esc",true),"existing slot binding");h.Advance(30);
        Check(h.Keyboard.Calls.Last()==("Esc",false) && h.Confirmations==1 && !h.Session.MenuOpen,"slot release confirm auto close in gesture purpose");
    }
    static void Geometry() {
        var g=G();Check(g.Error(new()) is null,"initial widths fit");
        Check(g.Error(new(),new(1150,100,20,20))?.Contains("右侧")==true,"right overflow named");
        Check(g.Error(new(),new(600,80,20,20))?.Contains("上侧")==true,"cutout top");
        Check((g with {Left=620}).Error(new())?.Contains("左侧")==true,"left named");
        Check((g with {Bottom=2600}).Error(new())?.Contains("下侧")==true,"bottom named");
        Check(new ModeGestureSettings {FirstWidthDp=10,SecondWidthDp=1}.IsValid,"independent widths");
        foreach(double bad in new[]{double.NaN,double.PositiveInfinity,0,.5,1.1,50.5}) Check(!(new ModeGestureSettings {FirstWidthDp=bad}).IsValid,"range finite step");
        Check(!new ModeGestureSettings {Origin=new(Menu:true)}.IsValid && !new ModeGestureSettings {First=new(Menu:true)}.IsValid,"menu second only");
        Check(new ModeBinding(ModeBindingKind.Mouse,"left",ModePressBehavior.Toggle).IsValid,"legacy toggle intact");
    }
    static void Save() {
        string root=Path.Combine(Path.GetTempPath(),"rightpad-fixed-regions-"+Guid.NewGuid());Directory.CreateDirectory(root);
        string path=Path.Combine(root,"settings.json");File.WriteAllText(path,"{\"unknown\":17,\"modeControl\":{\"kind\":3,\"key\":\"left\",\"behavior\":1}}");
        var old=SettingsFileStore.Load(path).Settings;Check(old.ModeControl.Purpose==ModePurpose.SingleBinding && old.ModeControl.Behavior==ModePressBehavior.Toggle && old.ModeControl.Gesture==new ModeGestureSettings(),"old settings no activation");
        using var h=new Harness(old.ModeControl);var vm=new SettingsViewModel(h.Store,new(path));vm.Mode.Purpose(ModePurpose.GestureBinding);
        Check(!vm.SaveAsync().GetAwaiter().GetResult() && h.Store.Current==old,"unknown geometry blocks Save");
        vm.ModeGeometry=()=>G();vm.Mode.GestureAction(1,ModeBindingKind.Mouse,"middle");vm.Mode.Width(0,1);Check(vm.SaveAsync().GetAwaiter().GetResult(),"disk first Save");
        Check(SettingsFileStore.Load(path).Settings.ModeControl==h.Store.Current.ModeControl && File.ReadAllText(path).Contains("unknown"),"full roundtrip unknown preserved");
        string blocked=Path.Combine(root,"blocked");Directory.CreateDirectory(blocked);var failing=new SettingsViewModel(h.Store,new(blocked)) {ModeGeometry=()=>G()};
        failing.Mode.GestureAction(0,ModeBindingKind.Keyboard,"B");ulong gen=h.Session.Generation;int replies=h.Replies.Count;
        Check(!failing.SaveAsync().GetAwaiter().GetResult() && h.Session.Generation==gen && h.Replies.Count==replies && failing.HasUnsavedChanges,"failed write no publish generation or config reply");
        var oldVm=new SettingsViewModel(new(old),new(Path.Combine(root,"old.json"))) {ModeGeometry=()=>G() with {Mode=new(1150,100,20,20)}};
        oldVm.Mode.Select(ModeBindingKind.Keyboard,"C");Check(oldVm.SaveAsync().GetAwaiter().GetResult(),"inactive regions cannot veto old uses");
        string racePath=Path.Combine(root,"geometry-race.json");var raceFile=new SettingsFileStore(racePath);
        Check(raceFile.SaveNowAsync(old).GetAwaiter().GetResult(),"race baseline");var raceStore=new RuntimeSettingsStore(old);
        int reads=0;var race=new SettingsViewModel(raceStore,raceFile) {ModeGeometry=()=>reads++==0?G():G() with {Mode=new(1150,100,20,20)}};
        race.Mode.Purpose(ModePurpose.GestureBinding);
        Check(!race.SaveAsync().GetAwaiter().GetResult() && raceStore.Current==old && SettingsFileStore.Load(racePath).Settings==old,"geometry changed during disk save: restore old disk, no publish");
    }
    static void UdpRoute() {
        using var h=new Harness();int clicks=0,moves=0;
        using var receiver=new UdpReceiver(new(IPAddress.Loopback,0),TextWriter.Null,
            motion:new TouchSessionProcessor((_,_)=>moves++),gesture:new GestureProcessor(()=>clicks++,()=>{},()=>{}),
            settings:h.Store,mode:h.Session,controlSend:(_,_)=>{});
        var remote=new IPEndPoint(IPAddress.Loopback,1234);
        receiver.ProcessDatagram(PresenceTests.Heartbeat(7),remote,h.Now);
        var d=new ModeContact(7,h.Session.Generation,0,0,ModeContactPhase.Idle,1,0,G());
        receiver.ProcessDatagram(Encode(d),remote,h.Now);
        receiver.ProcessDatagram(Encode(d with {Sequence=1,Contact=1,Phase=ModeContactPhase.Down}),remote,h.Now);
        receiver.ProcessDatagram(Encode(d with {Sequence=2,Contact=1,Phase=ModeContactPhase.Up,Selection=1}),remote,h.Now);
        Check(h.Keyboard.Calls.Last()==("A",true) && clicks==0 && moves==0 && receiver.Statistics.AcceptedPackets==0,"type10 invokes only Mode production stream");
        h.Advance(30);receiver.ProcessDatagram(PresenceTests.Touch(7,TouchEventType.Down,0,0,0),remote,h.Now);
        receiver.ProcessDatagram(PresenceTests.Touch(7,TouchEventType.Move,1,1,1000000),remote,h.Now);
        receiver.ProcessDatagram(PresenceTests.Touch(7,TouchEventType.Up,2,1,2000000),remote,h.Now);
        Check(moves>0 && clicks==1,"ordinary touchpad resumes unchanged");
        var layout=receiver.Layout!;Check(layout.ValidateModeLayout(new(1150,100,20,20))?.Contains("右侧")==true,"real editor path names overflow side");
    }
    static Task Sta(Action action) {
        var done=new TaskCompletionSource();var t=new Thread(()=>{try {action();done.SetResult();}catch(Exception e){done.SetException(e);}finally{Dispatcher.CurrentDispatcher.InvokeShutdown();}});
        t.SetApartmentState(ApartmentState.STA);t.Start();return done.Task;
    }
    static void Ui() {
        var binding=Config();var model=new ModeSettingsViewModel(()=>binding,b=>binding=b);
        var w=new ModeGestureSettingsWindow(model,()=>G());var content=(FrameworkElement)w.Content;content.Measure(new(1000,880));content.Arrange(new(0,0,1000,880));content.UpdateLayout();
        var bitmap=new RenderTargetBitmap(1000,880,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(content);
        string folder=Path.Combine(TestPaths.EvidenceDirectory,"render");Directory.CreateDirectory(folder);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var f=File.Create(Path.Combine(folder,"mode-fixed-regions.png")))encoder.Save(f);
        var picker=new ModeKeyPickerWindow(model,ModeBindingKind.Mouse,gesture:2);Check(picker.Title.Contains("鼠标"),"gesture mouse uses existing picker");picker.Close();w.Close();
    }
}
