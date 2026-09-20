using System.Buffers.Binary;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Rightpad.Receiver.Tests.Program;
namespace Rightpad.Receiver.Tests;

internal static class LRConfigTests
{
    public static readonly (string Name, Func<Task> Run)[] Cases = [
        Sync("registry and v2 golden", Registry),
        Sync("legacy and independent field fallback", Loading),
        Sync("boundary settings validation", Boundaries),
        Sync("UI precision integer steps and Escape", Numeric),
        ("LR config Save disk then one global publish", Save),
        ("LR config failed disk preserves draft and revision", Failure),
        Sync("lost X push request recovers whole snapshot", Recovery)
    ];
    static (string, Func<Task>) Sync(string name, Action action) => ("LR config " + name, () => { action(); return Task.CompletedTask; });
    sealed class Files : IDisposable {
        public readonly string Dir = Path.Combine(Path.GetTempPath(), "rightpad-lrconfig-" + Guid.NewGuid().ToString("N"));
        public string PathName => Path.Combine(Dir,"settings.json");
        public Files() => Directory.CreateDirectory(Dir);
        public void Dispose() { foreach(var f in Directory.GetFiles(Dir)) File.Delete(f); Directory.Delete(Dir); }
    }
    static readonly string[] Keys = ["slideLeftThresholdDp","slideRightThresholdDp","slideUpThresholdDp","tapHoldMs","longPressMs"];
    static readonly double[] Defaults = [12,3,2,25,400];
    static readonly double[] Changed = [8,6,4,30,500];
    static double[] Values(SlideControlLRSettings s) => [s.SlideLeftThresholdDp,s.SlideRightThresholdDp,s.SlideUpThresholdDp,s.TapHoldMs,s.LongPressMs];
    static void Registry() {
        Equal(2,ControlDefinitions.All.Count,"two registered controls");
        Equal(2,ControlDefinitions.All.Select(d=>d.ProtocolId).Distinct().Count(),"unique protocol IDs");
        var x=ControlDefinitions.All.Single(d=>d.Id=="xbox.x.slide_lr");
        Equal((ushort)2,x.ProtocolId,"audited next unused ID"); Equal("x",x.JsonKey,"JSON key"); Equal(ControlBehavior.SlideLR,x.Behavior,"kind2");
        var b=ControlConfigProtocol.Encode(0x0807060504030201,0x1817161514131211,0x2827262524232221,VirtualControlsSettings.Default);
        Equal(62,b.Length,"snapshot 36+12+14");
        Equal("5250435402010000010203040506070811121314151617182122232425262728020000000100010C07001E00190090010200020E78001E00140019009001",Convert.ToHexString(b),"exact shared golden");
        Equal((byte)12,b[39],"B size"); Equal((byte)14,b[51],"LR size");
        Equal(26,ControlConfigTests.Request(7).Length,"request unchanged");
    }
    static void Loading() {
        using var f=new Files();
        File.WriteAllText(f.PathName,"""{"controls":{"b":{"slideUpThresholdDp":1.5,"slideDownThresholdDp":4.2,"tapHoldMs":30,"longPressMs":437}}}""");
        var loaded=SettingsFileStore.Load(f.PathName).Settings;
        Equal(new SlideControlLRSettings(),loaded.Controls.X,"old B-only file loads X defaults");
        Equal(new SlideControlSettings(1.5,4.2,30,437),loaded.Controls.B,"B preserved");
        for(int field=0;field<Keys.Length;field++) foreach(var bad in new string?[]{null,"\"wrong\"","true","null","[]","{}",field<3?"0":"-1",field<3?"50.1":field==3?"201":"2001",field<3?"3.14":"25.5"}) {
            var x=new JsonObject(); for(int i=0;i<Keys.Length;i++) x[Keys[i]]=Changed[i];
            if(bad==null) x.Remove(Keys[field]); else x[Keys[field]]=JsonNode.Parse(bad);
            File.WriteAllText(f.PathName,new JsonObject{["controls"]=new JsonObject{["x"]=x}}.ToJsonString());
            var actual=Values(SettingsFileStore.Load(f.PathName).Settings.Controls.X);
            for(int i=0;i<Keys.Length;i++) Equal(i==field?Defaults[i]:Changed[i],actual[i],$"independent fallback {field}/{bad}/{i}");
        }
    }
    static void Boundaries() {
        using var f=new Files();
        foreach(var s in new[]{new SlideControlLRSettings(.1,.1,.1,1,50),new SlideControlLRSettings(50,50,50,200,2000)}) {
            Check(s.IsValid,"inclusive limits");
            File.WriteAllText(f.PathName,JsonSerializer.Serialize(new { controls=new { x=s } },new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
            Equal(s,SettingsFileStore.Load(f.PathName).Settings.Controls.X,"boundary JSON roundtrip");
        }
        foreach(var value in new[]{double.NaN,double.PositiveInfinity,.09,50.1,3.14}) {
            Check(!(new SlideControlLRSettings(SlideLeftThresholdDp:value)).IsValid,"invalid left");
            Check(!(new SlideControlLRSettings(SlideRightThresholdDp:value)).IsValid,"invalid right");
            Check(!(new SlideControlLRSettings(SlideUpThresholdDp:value)).IsValid,"invalid up");
        }
        foreach(var s in new[]{new SlideControlLRSettings(TapHoldMs:0),new SlideControlLRSettings(TapHoldMs:201),new SlideControlLRSettings(LongPressMs:49),new SlideControlLRSettings(LongPressMs:2001)}) Check(!s.IsValid,"timing bounds");
    }
    static void Numeric() {
        using var f=new Files(); var store=new RuntimeSettingsStore(); var vm=new SettingsViewModel(store,new(f.PathName)); var x=vm.Controls[1];
        Equal(5,x.Fields.Length,"LR fields");
        foreach(var field in new[]{x.Left!,x.Right!,x.Up}) {
            field.Text="3.1"; Check(vm.CanSave,"one decimal valid"); field.Text="3.14";
            Check(!vm.CanSave && field.Text=="3.14","no silent rounding"); field.Restore();
        }
        x.Right!.Step(1); Equal("3.1",x.Right.Text,"threshold step"); x.Right.Restore();
        x.TapHold.Text="25.0"; Check(!vm.CanSave,"integer syntax rejects decimal"); x.TapHold.Text="26"; Check(vm.CanSave,"integer valid");
        x.TapHold.Step(-1); Equal("25",x.TapHold.Text,"tap step1");
        x.LongPress.Text="437"; Check(vm.CanSave,"arbitrary integer long"); x.LongPress.Step(1); Equal("447",x.LongPress.Text,"long step10"); x.LongPress.Restore();
        Check(!vm.HasUnsavedChanges,"Escape all restores committed"); Equal(RuntimeSettings.Default,store.Current,"draft only");
    }
    static async Task Save() {
        using var f=new Files(); var store=new RuntimeSettingsStore(); var file=new SettingsFileStore(f.PathName); var vm=new SettingsViewModel(store,file);
        var messages=new List<byte[]>(); bool diskBeforePublish=false;
        using var c=new ControlConfigChannel(store,()=>new(7,0,true,IPAddress.Loopback.ToString()),(_,b)=>{diskBeforePublish=SettingsFileStore.Load(f.PathName).Settings==store.Current;messages.Add(b);},TextWriter.Null);
        var x=vm.Controls[1]; x.Left!.Text="8.0"; x.Right!.Text="6.0"; x.Up.Text="4.0"; x.TapHold.Text="30"; x.LongPress.Text="500";
        vm.Controls[0].Up.Text="1.5"; Equal(0,messages.Count,"no draft push");
        Check(await vm.SaveAsync(),"Save succeeds"); Check(diskBeforePublish,"disk committed before publication");
        Equal(new SlideControlLRSettings(8,6,4,30,500),store.Current.Controls.X,"whole LR roundtrip");
        Equal(store.Current,SettingsFileStore.Load(f.PathName).Settings,"all settings roundtrip");
        Equal(2UL,c.Revision,"B+X one global revision"); Equal(1,messages.Count,"one whole push"); Equal(62,messages[0].Length,"complete snapshot");
        x.Right.Text="10.0"; x.Right.Restore(); Equal("6.0",x.Right.Text,"Escape saved value"); Check(!vm.CanSave,"clean draft");
        await file.FlushAsync();
    }
    static async Task Failure() {
        using var f=new Files(); var store=new RuntimeSettingsStore(); var file=new SettingsFileStore(f.Dir); var vm=new SettingsViewModel(store,file); int sent=0;
        using var c=new ControlConfigChannel(store,()=>new(7,0,true,IPAddress.Loopback.ToString()),(_,_)=>sent++,TextWriter.Null);
        vm.Controls[1].Right!.Text="10.0"; Check(!await vm.SaveAsync(),"real failed disk write");
        Equal(RuntimeSettings.Default,store.Current,"no publication"); Equal(1UL,c.Revision,"no effective revision"); Equal(0,sent,"no push");
        Equal("10.0",vm.Controls[1].Right!.Text,"draft retained"); Check(vm.CanSave && !string.IsNullOrEmpty(vm.Notice),"failure visible and retry possible");
        await file.FlushAsync(); Equal(0,sent,"no hidden flush publication");
    }
    static void Recovery() {
        var store=new RuntimeSettingsStore(); var packets=new List<byte[]>(); bool drop=true;
        using var c=new ControlConfigChannel(store,()=>new(7,0,true,IPAddress.Loopback.ToString()),(_,b)=>{if(!drop) packets.Add(b);},TextWriter.Null);
        store.Publish(RuntimeSettings.Default with {Controls=new(){X=new(12,10,2,25,400)}});
        Equal(2UL,c.Revision,"X-only global revision"); drop=false;
        Check(c.Request(new(7,c.Epoch,1),IPAddress.Loopback,0),"request admitted");
        Equal(62,packets[0].Length,"recovered full snapshot"); Equal((ushort)100,BinaryPrimitives.ReadUInt16LittleEndian(packets[0].AsSpan(54)),"latest right10");
        Equal((ushort)7,BinaryPrimitives.ReadUInt16LittleEndian(packets[0].AsSpan(40)),"B retained");
    }
}
