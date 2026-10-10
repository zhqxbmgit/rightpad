using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

internal static class Cz1ParameterTests
{
    private static Func<Task> S(Action a) => () => { a(); return Task.CompletedTask; };
    public static IEnumerable<(string Name, Func<Task> Run)> Cases =>
    [
        ("C parameters old JSON defaults / no rewrite / current unit conversion", S(Legacy)),
        ("C parameters invalid JSON per-field fallback / diagnostic", S(Fallback)),
        ("C parameters finite conservative bounds / all snapshot entry points", S(Validation)),
        ("C Amax vector acceleration cap", S(Acceleration)),
        ("C Amax 80000 vs 160000 single-variable high-speed reversal", S(Reversal)),
        ("C Vmax vector velocity cap", S(Velocity)),
        ("C Tau ms-to-seconds spring effect", S(Tau)),
        ("C damping term single-variable effect", S(Damping)),
        ("C Glide UP deceleration single-variable effect", S(Glide)),
        ("C position threshold strict snap effect", S(PositionStop)),
        ("C velocity threshold strict snap and glide-stop effect", S(VelocityStop)),
        ("C safety range corners finite / stable / bounded glide", S(Corners)),
        ("C JSON round-trip invariant culture / unknown settings preserved", Persistence),
        ("C edit Apply Save defaults and unrelated drafts isolated", Editor),
        ("C invalid keyboard text and incomplete draft never apply", S(InvalidText)),
        ("C failed Save retains applied and persisted settings", SaveFailure),
        ("C touching request keeps exact old trajectory through natural stop", S(() => Boundary("touching"))),
        ("C UP release-pending request keeps exact old trajectory", S(() => Boundary("pending"))),
        ("C glide request keeps exact old trajectory and carry", S(() => Boundary("glide"))),
        ("C interrupted DOWN holds busy snapshot / stale-generation fence", S(Interrupted)),
        ("C hard CANCEL Reset / Dispose retain lifecycle no-output rules", S(Abort)),
        ("C local Android CANCEL absence never creates an apply boundary", S(LocalCancel)),
        ("C pending mode switch and parameters apply only at old completion", S(ModeBoundary)),
        ("C immutable publication waits for entire Tick native-submit gate", AtomicStep),
        ("C changes leave M-R1 M8 Q0-C sensitivity and output exact", S(MIsolation)),
        ("C fake Runtime settings publication / no device recreation / cleanup", Runtime),
        ("C panel disk Save through busy fake Runtime / new-run lifecycle boundary", RuntimePanelSave),
        ("C actual diagnostics and trace metadata track applied snapshots", S(Diagnostics)),
        ("C panel bindings keyboard and advanced layout contract", S(Markup)),
        .. new[] { "Amax", "Vmax", "Tau", "Damping", "Glide", "Position", "VelocityStop" }
            .Select(name => ("C actual Tick single-variable parameter injection " + name, S(() => TickParameter(name)))),
        ("C panel offline WPF render at narrow page width / advanced error layout", Cz1PanelRenderTests.Run)
    ];
    private sealed class Files : IDisposable
    {
        public readonly string DirectoryPath = Path.Combine(Path.GetTempPath(), "rightpad-c-parameters-" + Guid.NewGuid().ToString("N"));
        public string PathName => Path.Combine(DirectoryPath, "settings.json");
        public Files() => Directory.CreateDirectory(DirectoryPath);
        public void Dispose() => Directory.Delete(DirectoryPath, true);
    }
    private static readonly RuntimeSettings Initial = RuntimeSettings.Default with { SensitivityX = 10, SensitivityY = 10 };
    private static Cz1Parameters Candidate => new(160000, 12000, 45.5, .75, 60000, .25, 1.5);
    private static void Near(double expected, double actual, string message) =>
        Check(Math.Abs(expected - actual) <= Math.Max(1e-10, Math.Abs(expected) * 1e-12), message + $": {expected:R} vs {actual:R}");
    private static void Seed(ZhqTrackpadDynamics d, string property, (double, double) value) =>
        typeof(ZhqTrackpadDynamics).GetField($"<{property}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(d, value);
    private static ZhqTrackpadDynamics Dynamics(Cz1Parameters? p = null)
    { var d = new ZhqTrackpadDynamics(); d.Configure(p ?? Cz1Parameters.Default); d.Down(); return d; }
    private static void Legacy()
    {
        using var files = new Files();
        string old = """{"sensitivityX":10,"sensitivityY":10,"tapMaxDurationMs":300,"tapMovementThresholdPx":8,"clickHoldMs":25,"smoothingTauMs":60,"smoothingSupportMs":300,"touchMode":"C_Z1","unknown":42}""";
        File.WriteAllText(files.PathName, old);
        var (settings, warning) = SettingsFileStore.Load(files.PathName);
        Equal(Cz1Parameters.Default, settings.Cz1, "legacy exact C defaults");
        Equal((80000d,15000d,35d,1d,120000d,.5,2d), (settings.Cz1.MaxAcceleration,settings.Cz1.MaxVelocity,
            settings.Cz1.TauMs,settings.Cz1.DampingRatio,settings.Cz1.GlideDeceleration,settings.Cz1.PositionThreshold,settings.Cz1.VelocityThreshold), "all seven defaults");
        Equal(.035, settings.Cz1.TauSeconds, "same binary64 production tau");
        Equal((10d,10d,60,300,TouchMode.C_Z1),(settings.SensitivityX,settings.SensitivityY,settings.SmoothingTauMs,settings.SmoothingSupportMs,settings.TouchMode),"legacy independent fields retained");
        Check(warning is null, "missing entire C object is an ordinary upgrade");
        Equal(old, File.ReadAllText(files.PathName), "Load does not write");
    }
    private static void Fallback()
    {
        using var files = new Files();
        foreach (string malformed in new[] { "null", "[]", "true", "\"NaN\"", "{\"maxAcceleration\":160000,\"tauMs\":0,\"maxVelocity\":1e999,\"dampingRatio\":\"Infinity\"}" })
        {
            File.WriteAllText(files.PathName, "{\"sensitivityX\":10,\"sensitivityY\":10,\"cz1\":" + malformed + "}");
            var (settings, warning) = SettingsFileStore.Load(files.PathName);
            Check(settings.Cz1.IsValid && warning!.Contains("C-Z1"), "safe fallback diagnosed");
            Equal(malformed.Contains("160000") ? 160000d : 80000d,settings.Cz1.MaxAcceleration,"valid sibling retained");
            Equal(35d,settings.Cz1.TauMs,"invalid tau default");
            Equal(15000d,settings.Cz1.MaxVelocity,"nonfinite default");
            Equal(10d,settings.SensitivityX,"unrelated sensitivity retained");
        }
    }
    private static IEnumerable<Cz1Parameters> InvalidSnapshots()
    {
        foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d, 0d })
        {
            yield return Cz1Parameters.Default with { MaxAcceleration=value }; yield return Cz1Parameters.Default with { MaxVelocity=value };
            yield return Cz1Parameters.Default with { TauMs=value }; yield return Cz1Parameters.Default with { DampingRatio=value };
            yield return Cz1Parameters.Default with { GlideDeceleration=value }; yield return Cz1Parameters.Default with { PositionThreshold=value };
            yield return Cz1Parameters.Default with { VelocityThreshold=value };
        }
        yield return Cz1Parameters.Default with { MaxAcceleration=320001 }; yield return Cz1Parameters.Default with { MaxVelocity=30001 };
        yield return Cz1Parameters.Default with { TauMs=19.99 }; yield return Cz1Parameters.Default with { TauMs=100.01 };
        yield return Cz1Parameters.Default with { DampingRatio=2.01 }; yield return Cz1Parameters.Default with { GlideDeceleration=9999 };
        yield return Cz1Parameters.Default with { PositionThreshold=2.01 }; yield return Cz1Parameters.Default with { VelocityThreshold=10.01 };
    }
    private static void Reject(Action a)
    { bool rejected=false; try {a();} catch(ArgumentOutOfRangeException) {rejected=true;} Check(rejected,"invalid snapshot rejected"); }
    private static void Validation()
    {
        var store = new RuntimeSettingsStore(Initial); using var h = new H();
        foreach(var invalid in InvalidSnapshots())
        {
            Check(!invalid.IsValid && !(Initial with { Cz1=invalid }).IsProductValid,"invalid product state");
            Reject(()=>store.Publish(Initial with {Cz1=invalid})); Reject(()=>store.ApplyCz1(invalid));
            Reject(()=>h.M.RequestCz1(invalid)); Reject(()=>Dynamics(invalid));
            Equal(Cz1Parameters.Default,store.RequestedCz1,"failed validation cannot publish");
        }
    }
    private static void Acceleration()
    {
        foreach(double cap in new[]{80000d,160000d})
        {var d=Dynamics(Cz1Parameters.Default with{MaxAcceleration=cap});d.SetTarget(1e6,1e6);d.Step();
            Near(cap*.004,Math.Sqrt(d.Velocity.X*d.Velocity.X+d.Velocity.Y*d.Velocity.Y),"vector A not per-axis A");}
    }
    private static void Reversal()
    {
        (int First,double OldDistance) Run(double cap)
        {
            var d=Dynamics(Cz1Parameters.Default with{MaxAcceleration=cap});d.SetTarget(1e6,0);
            for(int i=0;i<100;i++)d.Step();Equal(15000d,d.Velocity.X,"equal reversal initial velocity");
            d.SetTarget(-1e6,0);double distance=0;int first=0;
            for(int i=1;i<=60;i++){double old=d.Position.X;d.Step();if(d.Velocity.X<0){first=i;break;}distance+=d.Position.X-old;}
            return(first,distance);
        }
        var baseline=Run(80000);var candidate=Run(160000);
        Equal(47,baseline.First,"80000 first reversed fixed step");Equal(24,candidate.First,"160000 first reversed fixed step");
        Check(candidate.OldDistance<baseline.OldDistance,"higher A reduces old-direction continuation");
        Console.WriteLine(FormattableString.Invariant($"C A-only synthetic reversal: A80000 first={baseline.First*4}ms old={baseline.OldDistance:G}counts; A160000 first={candidate.First*4}ms old={candidate.OldDistance:G}counts"));
    }
    private static void Velocity()
    {
        foreach(double cap in new[]{1000d,15000d})
        {var d=Dynamics(Cz1Parameters.Default with{MaxVelocity=cap});d.SetTarget(1e8,1e8);for(int i=0;i<100;i++)d.Step();
            Near(cap,Math.Sqrt(d.Velocity.X*d.Velocity.X+d.Velocity.Y*d.Velocity.Y),"vector V cap");}
    }
    private static void Tau()
    {
        foreach(double tau in new[]{35d,70d})
        {var d=Dynamics(Cz1Parameters.Default with{TauMs=tau});d.SetTarget(10,0);d.Step();Near(10/Math.Pow(tau/1000,2)*.004,d.Velocity.X,"spring uses seconds");}
    }
    private static void Damping()
    {
        var a=Dynamics();var b=Dynamics(Cz1Parameters.Default with{DampingRatio=.5});
        Seed(a,"Velocity",(100,0));Seed(b,"Velocity",(100,0));a.SetTarget(10,0);b.SetTarget(10,0);a.Step();b.Step();
        Near(100*2*.5/.035*.004,b.Velocity.X-a.Velocity.X,"only damping term changes");
    }
    private static void Glide()
    {
        foreach(double decel in new[]{120000d,60000d})
        {var d=Dynamics(Cz1Parameters.Default with{GlideDeceleration=decel});Seed(d,"Velocity",(3000,4000));d.Release();d.Step();
            Near(5000-decel*.004,Math.Sqrt(d.Velocity.X*d.Velocity.X+d.Velocity.Y*d.Velocity.Y),"UP glide deceleration vector");
            Near((5000-decel*.004)*.004,Math.Sqrt(d.Position.X*d.Position.X+d.Position.Y*d.Position.Y),"glide uses new velocity");}
    }
    private static void PositionStop()
    {
        var a=Dynamics();var b=Dynamics(Cz1Parameters.Default with{PositionThreshold=.25});
        a.SetTarget(.4,0);b.SetTarget(.4,0);a.Step();b.Step();Equal(.4,a.Position.X,"baseline snap");Check(b.Position.X<.4 && b.Velocity.X>0,"lower threshold prevents snap");
        var edge=Dynamics();edge.SetTarget(.5,0);edge.Step();Check(edge.Position.X!=.5,"strict less-than unchanged");
    }
    private static void VelocityStop()
    {
        var a=Dynamics();var b=Dynamics(Cz1Parameters.Default with{VelocityThreshold=1});
        Seed(a,"Velocity",(1.5,0));Seed(b,"Velocity",(1.5,0));a.SetTarget(.4,0);b.SetTarget(.4,0);a.Step();b.Step();
        Equal(.4,a.Position.X,"baseline velocity threshold snaps");Check(b.Position.X!=.4,"lower speed threshold prevents snap");
        a=Dynamics();b=Dynamics(Cz1Parameters.Default with{VelocityThreshold=1});Seed(a,"Velocity",(481.5,0));Seed(b,"Velocity",(481.5,0));a.Release();b.Release();
        Check(a.Step() && !b.Step(),"same threshold also used at glide stop");
    }
    private static void Corners()
    {
        foreach(double tau in new[]{20d,100d})foreach(double damping in new[]{.25,2d})
        foreach(double a in new[]{20000d,320000d})foreach(double v in new[]{100d,30000d})
        {
            var d=Dynamics(new(a,v,tau,damping,10000,.01,.1));d.SetTarget(1000,-500);
            for(int i=0;i<10000;i++){d.Step();var q=d.IntegerDelta();d.Commit(q.X,q.Y);
                Check(double.IsFinite(d.Position.X)&&double.IsFinite(d.Position.Y),"finite range corner");
                Check(Math.Abs(q.X)<=122&&Math.Abs(q.Y)<=122,"bounded int32/short step");}
            Near(1000,d.Position.X,$"stable corner reaches fixed target tau={tau} damping={damping} A={a} V={v}");
            Seed(d,"Velocity",(v,0));d.Release();int ticks=0;while(!d.Step()&&ticks++<751){}Check(ticks<=750,"glide<=3 seconds at minimum deceleration");
        }
    }
    private static async Task Persistence()
    {
        using var files=new Files();var culture=CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");
            File.WriteAllText(files.PathName,"""{"unknown":{"keep":true},"controls":{"b":{"future":42}},"cz1":{"future":"retained"}}""");
            var file=new SettingsFileStore(files.PathName);Check(await file.SaveNowAsync(Initial with{Cz1=Candidate}),"save");
            var loaded=SettingsFileStore.Load(files.PathName);Equal(Initial with{Cz1=Candidate},loaded.Settings,"seven-value invariant round trip");Check(loaded.Warning is null,"valid saved file");
            using var doc=JsonDocument.Parse(File.ReadAllText(files.PathName));
            Check(doc.RootElement.GetProperty("unknown").GetProperty("keep").GetBoolean(),"unknown root retained");
            Equal(42,doc.RootElement.GetProperty("controls").GetProperty("b").GetProperty("future").GetInt32(),"unknown nested retained");
            Equal("retained",doc.RootElement.GetProperty("cz1").GetProperty("future").GetString(),"unknown C field retained");
            Equal(45.5,doc.RootElement.GetProperty("cz1").GetProperty("tauMs").GetDouble(),"JSON decimal point");await file.FlushAsync();
        }
        finally{CultureInfo.CurrentCulture=culture;}
    }
    private static async Task Editor()
    {
        using var files=new Files();var store=new RuntimeSettingsStore(Initial);var file=new SettingsFileStore(files.PathName);
        Check(await file.SaveNowAsync(Initial),"initial file");var vm=new SettingsViewModel(store,file);var c=vm.Cz1;
        vm.SensitivityX.Text="11.0";c.Amax.Text="160000";
        Equal(Cz1Parameters.Default,store.RequestedCz1,"editing does not apply");Equal(Cz1Parameters.Default,store.Current.Cz1,"editing does not save");
        Check(c.CanApply&&c.CanSave,"separate draft buttons");c.Apply();
        Equal(160000d,store.RequestedCz1.MaxAcceleration,"memory apply");Equal(Initial,SettingsFileStore.Load(files.PathName).Settings,"Apply never writes");
        Equal(new SensitivitySnapshot(10,10),store.Sensitivity.Current,"C apply leaves gain10");
        // General Save and mode Save preserve the applied C snapshot, and never save its draft.
        Check(await vm.SaveAsync(),"unrelated sensitivity save");Equal(160000d,store.RequestedCz1.MaxAcceleration,"unrelated Save does not undo C Apply");
        Check(await vm.SelectTouchModeAsync(TouchMode.C_Z1),"independent mode save");Equal(80000d,store.Current.Cz1.MaxAcceleration,"mode Save excludes C draft");
        vm.SensitivityX.Text="12.0";Check(await c.SaveAsync(),"C-only save");
        Equal(11d,store.Current.SensitivityX,"C save excludes unrelated draft");Equal(12d,vm.DraftSettings.SensitivityX,"unrelated draft retained");
        Equal(160000d,SettingsFileStore.Load(files.PathName).Settings.Cz1.MaxAcceleration,"C actual disk save");
        Check(!c.CanSave&&!c.CanApply,"saved and requested match");
        c.RestoreDefaults();Equal(80000d,c.Draft.MaxAcceleration,"C-only reset draft");Equal(160000d,store.RequestedCz1.MaxAcceleration,"reset needs apply");
        Equal(12d,vm.DraftSettings.SensitivityX,"reset leaves other settings");
        Check(await vm.SaveAsync(),"later other Save");Equal(160000d,store.Current.Cz1.MaxAcceleration,"stale general draft cannot revert C Save");await file.FlushAsync();
    }
    private static void InvalidText()
    {
        using var files=new Files();var store=new RuntimeSettingsStore(Initial);var c=new SettingsViewModel(store,new(files.PathName)).Cz1;
        foreach(var field in c.Fields)foreach(string text in new[]{"NaN","Infinity","-1","1,5","1e999","junk","","-",".","35.","999999999999"})
        {field.Text=text;Check(!field.IsCompleteValid&&!c.CanApply&&!c.CanSave,"invalid/incomplete editor blocked");c.Apply();Equal(Cz1Parameters.Default,store.RequestedCz1,"invalid draft cannot leak");field.Restore();}
    }
    private static async Task SaveFailure()
    {
        using var files=new Files();string blocked=Path.Combine(files.DirectoryPath,"blocked");File.WriteAllText(blocked,"file not directory");
        var store=new RuntimeSettingsStore(Initial);var file=new SettingsFileStore(Path.Combine(blocked,"settings.json"));var c=new SettingsViewModel(store,file).Cz1;
        c.Amax.Text="160000";Check(!await c.SaveAsync(),"Save failure");Equal(Cz1Parameters.Default,store.RequestedCz1,"failed disk Save never applies");Equal(Initial,store.Current,"failed disk Save never publishes");
        Check(c.CanSave&&c.Notice.Contains("failed"),"failure retained and diagnosed");await file.FlushAsync();
    }
    private sealed class H:IDisposable
    {
        public long Now=100000;public readonly List<(long At,int X,int Y)> Output=[];public readonly ResampledMotion M;private uint sequence;
        public H(bool c=true)
        {M=new((x,y)=>Output.Add((Now,x,y)),monotonicNow:()=>Now,clockFrequency:1000000,finiteCriticalMode:MotionMode.M_R1);if(c)M.RequestProfile(MotionProfile.Cinematic);}
        public void Touch(TouchEventType e,int t,float x,uint session=1)
        {Now=100000+t*1000L;M.Process(new(new(2,e,1,session,sequence++,1),[new((ulong)t*1000000,x,0)]));}
        public void Tick(int t,long? generation=null){Now=100000+t*1000L;M.Tick(Now,generation??M.Schedule.Generation);}
        public void Dispose()=>M.Dispose();
    }
    private static void Setup(H h)
    {h.Touch(TouchEventType.Down,0,0);h.Touch(TouchEventType.Move,4,100000);for(int t=12;t<=100;t+=4)h.Tick(t);}
    private static void Boundary(string stage)
    {
        using var a=new H();using var b=new H();Setup(a);Setup(b);
        if(stage!="touching"){a.Touch(TouchEventType.Up,101,100000);b.Touch(TouchEventType.Up,101,100000);}
        if(stage=="glide"){for(int t=104;t<=116;t+=4){a.Tick(t);b.Tick(t);}Check(a.M.CinematicDynamics.Gliding,"nonvacuous glide");}
        var schedule=a.M.Schedule;a.M.RequestCz1(Candidate);
        Equal(schedule,a.M.Schedule,"Apply does not fence or retime old work");Check(a.M.CaptureCz1State().Pending,"busy pending");
        if(stage=="touching"){a.Touch(TouchEventType.Up,101,100000);b.Touch(TouchEventType.Up,101,100000);}
        for(int t=stage=="glide"?120:104;t<2000;t+=4)
        {
            a.Tick(t);b.Tick(t);Equal(b.M.CinematicDynamics.Position,a.M.CinematicDynamics.Position,"busy trajectory unchanged");Equal(b.M.CinematicDynamics.Velocity,a.M.CinematicDynamics.Velocity,"busy velocity unchanged");
            Check(a.Output.SequenceEqual(b.Output),"every old-chain output and submit time exact");
            if(a.M.Schedule.Deadline is null)break;
            Equal(Cz1Parameters.Default,a.M.CaptureCz1State().Active,"old snapshot retained until last submit");
        }
        Check(a.M.Schedule.Deadline is null,"natural stop");Equal(Candidate,a.M.CaptureCz1State().Active,"complete snapshot applied after stop");Check(!a.M.CaptureCz1State().Pending,"pending clears");
        Equal(b.M.CinematicDynamics.CarryOver,a.M.CinematicDynamics.CarryOver,"parameter apply preserves natural carry");
        a.Touch(TouchEventType.Down,2200,0,2);a.Touch(TouchEventType.Move,2204,10000,2);a.Tick(2216);
        Near(160000*.004,a.M.CinematicDynamics.Velocity.X,"new C uses requested acceleration");Equal(12,a.M.ReconstructionDelayMs,"C12 unchanged");Equal(4,a.M.PeriodMs,"fixed step unchanged");
    }
    private static void Interrupted()
    {
        using var h=new H();Setup(h);h.Touch(TouchEventType.Up,101,100000);h.Tick(116);h.M.RequestCz1(Candidate);
        long old=h.M.Schedule.Generation;h.Touch(TouchEventType.Down,117,500,2);int count=h.Output.Count;
        Equal(Cz1Parameters.Default,h.M.CaptureCz1State().Active,"interrupted chain retains old params");h.Tick(200,old);Equal(count,h.Output.Count,"stale wake fenced");
        h.Touch(TouchEventType.Up,201,500,2);for(int t=202;t<400;t++)h.Tick(t);
        Equal(Candidate,h.M.CaptureCz1State().Active,"idle after interrupted new contact applies");
    }
    private static void Abort()
    {
        foreach(bool dispose in new[]{false,true}){using var h=new H();Setup(h);h.M.RequestCz1(Candidate);long old=h.M.Schedule.Generation;int n=h.Output.Count;
            if(dispose)h.M.Dispose();else h.M.Reset();h.Tick(500,old);Equal(n,h.Output.Count,"hard abort no artificial output");
            Equal((0d,0d),h.M.CinematicDynamics.CarryOver,"existing lifecycle carry clear");Check(h.M.Schedule.Deadline is null,"parked");
            if(!dispose)Equal(Candidate,h.M.CaptureCz1State().Active,"reset creates safe idle boundary");}
    }
    private static void LocalCancel()
    {
        using var h=new H();Setup(h);h.M.RequestCz1(Candidate);for(int t=104;t<=1000;t+=4)h.Tick(t);
        Check(h.M.CaptureCz1State().Pending&&!h.M.CinematicDynamics.Gliding,"no transport CANCEL: absent moves remain touching and pending");
    }
    private static void ModeBoundary()
    {
        using var h=new H();Setup(h);h.Touch(TouchEventType.Up,101,100000);h.M.RequestProfile(MotionProfile.Normal);h.M.RequestCz1(Candidate);
        Equal(MotionProfile.Cinematic,h.M.ActiveMotionProfile,"old C profile retained");Equal(Cz1Parameters.Default,h.M.CaptureCz1State().Active,"old C parameters retained");
        for(int t=104;t<2000;t+=4)h.Tick(t);Equal(MotionProfile.Normal,h.M.ActiveMotionProfile,"idle profile switch");Equal(Candidate,h.M.CaptureCz1State().Active,"same idle parameter boundary");
        Equal(8,h.M.ReconstructionDelayMs,"M8");Equal(1,h.M.PeriodMs,"M 1ms");
    }
    private static async Task AtomicStep()
    {
        long now=100000;using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        using var m=new ResampledMotion((x,y)=>{entered.Set();Check(release.Wait(3000),"release test gate");},monotonicNow:()=>now,clockFrequency:1000000,finiteCriticalMode:MotionMode.M_R1);
        m.RequestProfile(MotionProfile.Cinematic);m.Process(new(new(2,TouchEventType.Down,1,1,0,1),[new(0,0,0)]));now+=4000;
        m.Process(new(new(2,TouchEventType.Move,1,1,1,1),[new(4000000,10000,0)]));now=116000;
        var tick=Task.Run(()=>m.Tick(now,m.Schedule.Generation));Check(entered.Wait(3000),"Tick inside native output");
        var request=Task.Run(()=>m.RequestCz1(Candidate));try{await Task.Delay(40);Check(!request.IsCompleted,"publication cannot interleave with submit");}finally{release.Set();}
        await Task.WhenAll(tick,request);Equal(Cz1Parameters.Default,m.CaptureCz1State().Active,"old whole snapshot");Equal(Candidate,m.CaptureCz1State().Selected,"new whole selected snapshot");
    }
    private static void MIsolation()
    {
        {
            using var a=new H(false);using var b=new H(false);
            for(int t=0;t<=250;t++)
            {
                if(t<=80&&t%4==0){float x=t<40?t*10:(80-t)*10;var e=t==0?TouchEventType.Down:t==80?TouchEventType.Up:TouchEventType.Move;a.Touch(e,t,x);b.Touch(e,t,x);}
                if(t is 10 or 50 or 120)a.M.RequestCz1(Candidate with{MaxAcceleration=t==50?80000:160000});
                a.Tick(t);b.Tick(t);Equal(b.M.Position,a.M.Position,"M position exact");Equal(b.M.Schedule,a.M.Schedule,"M phase/generation exact");
                Check(a.Output.SequenceEqual(b.Output),"M Q0-C output unchanged");
            }
            Equal(8,a.M.ReconstructionDelayMs,"M8 remains");Equal(0d,a.M.ActiveAlgorithm.TauMs,"no C tau read by M");
        }
    }
    private static async Task Runtime()
    {
        var store=new RuntimeSettingsStore(Initial with{TouchMode=TouchMode.C_Z1});var fake=new VirtualHidTests.FakeNative();int created=0;
        var runtime=new ReceiverRuntime(store,TextWriter.Null,MouseBackend.VirtualHid,new(IPAddress.Loopback,0),()=>{created++;return new LibVirtualHidMouseOutput(fake);},motionMode:MotionMode.M_R1,useProductMotionSettings:true);
        try
        {
            store.ApplyCz1(Candidate);await runtime.StartAsync();await RuntimeTests.Until(()=>runtime.CaptureCz1State()?.Active==Candidate);
            store.ApplyCz1(Cz1Parameters.Default);Equal(Cz1Parameters.Default,runtime.CaptureCz1State()!.Value.Active,"live idle publication connected");
            Equal(new SensitivitySnapshot(10,10),runtime.ActiveSensitivity,"gain10 unchanged");Equal(TouchMode.C_Z1,runtime.CaptureSnapshot().ActiveTouchMode,"no mode switch");Equal(1,created,"no mouse device recreation");
        }
        finally{await runtime.StopAsync();}
        store.ApplyCz1(Candidate);Equal(1,created,"stopped handler detached");Check(fake.Events.Count(e=>e.Kind=="move")==0,"no physical or fake move needed");
    }
    private static void Diagnostics()
    {
        using var files=new Files();using(var trace=new MotionTrace(files.DirectoryPath,MotionMode.M_R1,64))
        {
            using var m=new ResampledMotion((x,y)=>{},trace:trace,clockFrequency:1000000,finiteCriticalMode:MotionMode.M_R1);
            m.RequestProfile(MotionProfile.Cinematic);m.RequestCz1(Candidate);
            Equal(45.5,m.ActiveAlgorithm.TauMs,"fractional Tau diagnostics exact");Check(m.ActiveAlgorithm.Algorithm.Contains("Amax 160000"),"real active cap");
            var vm=new RuntimeStatsViewModel();vm.Refresh(new(1,ReceiverState.Running,ActiveMotionProfile:MotionProfile.Cinematic,ActiveMotionTauMs:45.5,ActiveMotionAlgorithm:m.ActiveAlgorithm.Algorithm),0);
            Equal("45.5 ms",vm.ActiveTau,"real active tau text");Check(vm.ActiveMotionKernel.Contains("160000"),"no stale constants");trace.Freeze();
        }
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(files.DirectoryPath,"metadata.json")));
        Equal(45.5,doc.RootElement.GetProperty("TauMs").GetDouble(),"trace actual tau");Equal(160000d,doc.RootElement.GetProperty("Cz1").GetProperty("MaxAcceleration").GetDouble(),"trace full parameter identity");
        Equal(3,doc.RootElement.GetProperty("ProfileSegments").GetArrayLength(),"parameter change is a separate trace segment");
    }
    private static async Task RuntimePanelSave()
    {
        using var files = new Files();
        var store = new RuntimeSettingsStore(Initial with { TouchMode = TouchMode.C_Z1 });
        var file = new SettingsFileStore(files.PathName);
        var panel = new SettingsViewModel(store, file).Cz1;
        var fake = new VirtualHidTests.FakeNative(); int creates = 0;
        var runtime = new ReceiverRuntime(store, TextWriter.Null, MouseBackend.VirtualHid,
            new(IPAddress.Loopback,0), () => { creates++; return new LibVirtualHidMouseOutput(fake); },
            motionMode: MotionMode.M_R1, useProductMotionSettings: true);
        using var sender = new UdpClient();
        try
        {
            await runtime.StartAsync();
            var endpoint = runtime.LocalEndpoint!;
            await sender.SendAsync(PresenceTests.Heartbeat(1), endpoint);
            await sender.SendAsync(PacketDecoderTests.Encode(TouchEventType.Down, 1, 0, new TouchSample(0,0,0)), endpoint);
            await sender.SendAsync(PacketDecoderTests.Encode(TouchEventType.Move, 1, 1, new TouchSample(4_000_000,100000,0)), endpoint);
            await RuntimeTests.Until(() => runtime.CaptureSnapshot().MotionOutputEvents >= 10);
            panel.Amax.Text = "160000";
            Check(await panel.SaveAsync(), "C-only panel Save in an active Runtime");
            panel.Refresh(runtime.CaptureCz1State());
            Check(panel.StatusText.Contains("待生效"), "UI truthful pending after disk Save");
            Equal(80000d, runtime.CaptureCz1State()!.Value.Active.MaxAcceleration, "old busy snapshot");
            Equal(160000d, SettingsFileStore.Load(files.PathName).Settings.Cz1.MaxAcceleration, "new disk snapshot");
            Equal(TouchMode.C_Z1, runtime.CaptureSnapshot().ActiveTouchMode, "Save does not switch mode");
            Equal(new SensitivitySnapshot(10,10), runtime.ActiveSensitivity, "Save preserves sensitivity10");
            // The existing sender-run invalidation aborts old input without output.
            await sender.SendAsync(PresenceTests.Heartbeat(2), endpoint);
            await RuntimeTests.Until(() => runtime.CaptureCz1State()?.Active.MaxAcceleration == 160000);
            panel.Refresh(runtime.CaptureCz1State());
            Check(panel.StatusText.Contains("已生效") && !runtime.CaptureCz1State()!.Value.Pending, "lifecycle idle resolves pending");
            long moves = runtime.CaptureSnapshot().MotionOutputEvents;
            await Task.Delay(25); Equal(moves, runtime.CaptureSnapshot().MotionOutputEvents, "aborted old chain stays parked");
            Equal(1, creates, "Save never recreates native device");
        }
        finally { await runtime.StopAsync(); await file.FlushAsync(); }
    }
    private static void Markup()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!Directory.Exists(Path.Combine(root.FullName,"windows","Rightpad.Receiver")))root=root.Parent;
        Check(root is not null,"repo root");string source=Path.Combine(root!.FullName,"windows","Rightpad.Receiver");
        var xml=XDocument.Load(Path.Combine(source,"Views","MotionView.xaml"));string text=xml.ToString();
        foreach(string binding in new[]{"Amax","Vmax","Tau","Damping","Glide","PositionStop","VelocityStop","ActiveText","SavedText","StatusText"})Check(text.Contains("{Binding "+binding+"}"),"C field bound: "+binding);
        Check(xml.Descendants().Any(e=>e.Name.LocalName=="Expander"&&e.Descendants().Count(x=>x.Name.LocalName=="Cz1ParameterEditor")==2),"stop thresholds in expandable advanced region");
        Equal(2,xml.Descendants().Count(e=>e.Name.LocalName=="NumericEditor"),"original two sensitivity editors only");
        var editor=XDocument.Load(Path.Combine(source,"Controls","Cz1ParameterEditor.xaml"));Check(editor.Descendants().Any(e=>e.Name.LocalName=="TextBox"),"keyboard numeric input");
        Check(!text.Contains("Restart"),"no restart action");
    }
    private static void TickParameter(string name)
    {
        var defaults = Cz1Parameters.Default;
        var parameters = name switch
        {
            "Amax" => defaults with { MaxAcceleration = 160000 },
            "Vmax" => defaults with { MaxVelocity = 1000 },
            "Tau" => defaults with { TauMs = 70 },
            "Damping" => defaults with { DampingRatio = .5 },
            "Glide" => defaults with { GlideDeceleration = 60000 },
            "Position" => defaults with { PositionThreshold = .25 },
            _ => defaults with { VelocityThreshold = 1 }
        };
        using var a = new H(); using var b = new H(); b.M.RequestCz1(parameters);
        float target = name == "Position" ? .4f : name == "VelocityStop" ? .5f : name is "Tau" or "Damping" ? 10 : 100000;
        a.Touch(TouchEventType.Down,0,0); b.Touch(TouchEventType.Down,0,0);
        a.Touch(TouchEventType.Move,4,target); b.Touch(TouchEventType.Move,4,target);
        bool changed = false;
        for(int t=12;t<=400;t+=4)
        {
            if(name=="Glide" && t==104) {a.Touch(TouchEventType.Up,101,target);b.Touch(TouchEventType.Up,101,target);}
            a.Tick(t);b.Tick(t);
            if(a.M.CinematicDynamics.Position!=b.M.CinematicDynamics.Position || a.M.CinematicDynamics.Velocity!=b.M.CinematicDynamics.Velocity) changed=true;
        }
        Check(changed,"parameter reaches real Tick math: "+name);
        Equal(12,b.M.ReconstructionDelayMs,"C12 preserved"); Equal(4,b.M.PeriodMs,"4ms preserved");
    }
}
