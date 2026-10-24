using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Rightpad.Receiver.Tests;

internal static class ModeMenuTests
{
    public static IEnumerable<(string Name,Func<Task> Run)> Cases => new (string,Action)[] {
        ("Mode menu ordered toggle UP refresh lease and retired generation",Toggle),
        ("Mode menu movement and tap use one original motion path without gestures",Routing),
        ("Mode menu and normal fixed-state paths produce identical HID displacement",Equivalence),
        ("Mode menu keyboard slot completes press release then closes with one feedback",KeyboardSlot),
        ("Mode menu gamepad slot completes press release then closes with one feedback",GamepadSlot),
        ("Mode menu neutral center boundary navigation and recontact emit nothing",NoActionTaps),
        ("Mode menu six unbound directions each confirm once close and emit nothing",UnboundDirections),
        ("Mode menu unbound feedback failure still closes and never replays",UnboundFeedbackFailure),
        ("Mode menu unbound selection shares one accepted action per opening",UnboundOncePerCycle),
        ("Mode menu failed slot press never confirms and safely closes",FailedPress),
        ("Mode menu repeated duplicate and late packets execute and confirm once",RepeatPackets),
        ("Mode menu feedback send failure never replays or blocks recovery",FeedbackFailure),
        ("Mode menu manual close disconnect config and output failure boundaries",PulseLifecycle),
        ("Mode menu config equality draft unknown fields failed save old G compatibility",Settings),
        ("Mode menu gamepad modal neutral barrier preserves ordinary mapping",Gamepad),
        ("Mode menu mouse ownership cancels drag click and orphan exit events",Ownership),
        ("Mode menu safety release is logged separately from tap output",SafetyLog),
        ("Mode menu output and presentation failure return fresh normal input",Failures),
        ("Mode menu edge multi-monitor DPI geometry and shared slot mapping",Geometry)
        ,("Mode directional selection six axes clockwise ties and unlimited radius",Directions)
        ,("Mode cursor observed position authoritative and fixed anchor across lift",CursorAuthority)
        ,("Mode cursor capture read failures release fence and fresh recovery",CursorFailures)
        ,("Mode menu production UDP route motion once tap no click and recovery",UdpRoute)
        ,("Mode menu stalled presentation auto-closes without blocking normal mouse",PresentationTimeout)
        ,("Mode menu committed sensitivity fractional contact and Save isolation",NavigationGain)
        ,("Mode menu sixty seconds idle with healthy presence lease and presentation",IdleMinute)
        ,("Mode cursor read-only adapter actual authority environment failures",CursorHealth)
        ,("Mode menu observation counters reuse existing reads and expire on close",ObservationCounters)
        ,("Mode menu diagnostic metrics distinguish sampling from callback stalls without position output",PresentationMetrics)
    }.Select(t=>(t.Item1,(Func<Task>)(()=> {t.Item2();return Task.CompletedTask;})))
        .Append(("Mode menu WPF render ring at small and scaled viewports",()=>Sta(Render)))
        .Append(("Mode menu observed arrow hotspot and DIP size across origins DPI and small overlays",()=>Sta(CursorRenderCoordinates)))
        .Append(("Mode menu arrow paints above ring and caption and clips without clamping",()=>Sta(CursorRenderLayers)))
        .Append(("Mode menu preview ignores cursor position and absent snapshot draws nothing",()=>Sta(CursorRenderPreview)))
        .Append(("Mode menu retained arrow updates latest snapshot without rebuilding unchanged labels",()=>Sta(CursorRetainedUpdates)))
        .Append(("Mode menu hidden HWND uses software rendering only for overlay and preserves no-activation",()=>Sta(OverlaySoftwareRendering)))
        .Append(("Mode menu settings fixed ring preview no Islands and six-slot Save",()=>Sta(SettingsWindow)))
        .Append(("Mode menu legacy Islands appearance normalizes ring preserves all and load is read-only",()=> {LegacyAppearance();return Task.CompletedTask;}));
    private static void Check(bool condition,string message) {if(!condition)throw new Exception(message);}
    private static ModeBinding Config() => new(ModeBindingKind.Keyboard,"G") {
        Purpose=ModePurpose.ShortcutMenu,
        Slots=new() {Top=new(ModeBindingKind.Keyboard,"F13"),UpperRight=new(ModeBindingKind.Gamepad,"A"),
            LowerRight=new(ModeBindingKind.Keyboard,"F14"),Bottom=new(ModeBindingKind.Gamepad,"LT"),
            LowerLeft=new(ModeBindingKind.Keyboard,"RightCtrl"),UpperLeft=new(ModeBindingKind.Gamepad,"RB")}
    };
    private sealed class Keyboard : IModeKeyboard {
        public List<(string,bool)> Calls=new();public bool Fail;
        public void Set(ModeKey key,bool down) {if(Fail)throw new IOException("injected HID failure");Calls.Add((key.Id,down));}
        public void Dispose() { }
    }
    private sealed class Cursor : IModeMenuCursor {
        public Point Position=new(500,400);
        public bool FailRead;public double Scale=1;
        public MenuPlacement Capture() {if(FailRead)throw new IOException("cursor unavailable");return MenuPlacement.Create(new(0,0,1000,800,Scale),Position.X,Position.Y);}
        public Point Read(MenuPlacement placement) {if(FailRead)throw new IOException("cursor unavailable");return Position;}
    }
    private sealed class Mouse : IMouseOutput {
        public readonly List<string> Events=new();
        public string BackendName=>"fake";public MouseOutputStats Stats=>default;
        public string ButtonSupport=>"left,right,middle";
        public void Move(int dx,int dy)=>Events.Add($"move {dx} {dy}");
        public void LeftDown()=>Events.Add("down");
        public void LeftUp()=>Events.Add("up");
        public void SetButton(ModeMouseButton button,bool down)=>Events.Add(down?$"{button}-down":$"{button}-up");
        public void Dispose()=>Events.Add("dispose");
        public int Count(string evt)=>Events.Count(e=>e==evt);
    }
    private sealed class Harness : IDisposable {
        public readonly Cursor Cursor=new();
        public readonly RuntimeSettingsStore Store;public readonly ModeSession Mode;
        public readonly Keyboard Keyboard=new();public readonly List<XboxGamepadState> States=new();
        public readonly List<string> Logs=new();
        public readonly List<(string Target,ClickFeedback Click)> Feedbacks=new();
        public readonly TouchSessionProcessor Motion;
        public readonly List<(int X,int Y)> Moves=new();
        public int MovementCalls,Gestures,Boundaries;
        public long Now=Stopwatch.GetTimestamp();public uint Seq,Contact,TouchSeq,TouchId;public ulong Gen;
        public TouchPacket? LastUp;
        public bool FailFeedback,FailGamepad;
        public Action? OnFeedback;
        private ulong ms=1000;
        public Harness(ModeBinding? binding=null) {
            Store=new(RuntimeSettings.Default with {ModeControl=binding??Config()});
            Motion=new((x,y)=> {Moves.Add((x,y));Cursor.Position=new(Cursor.Position.X+x,Cursor.Position.Y+y);});
            Mode=new(Store,s=> {if(FailGamepad)return false;States.Add(s);return true;},(_,_)=>{},()=>Keyboard,log:Logs.Add,clock:()=>Now,menuCursor:Cursor,
                gamepadReady:()=>!FailGamepad,
                haptic:(address,click)=> {if(FailFeedback)throw new IOException("injected feedback failure");Feedbacks.Add((address.ToString(),click));OnFeedback?.Invoke();});
            Mode.MenuBoundary=open=> {Motion.Reset();Boundaries++;};
            Mode.UpdatePresence(new(7,Now,true,"127.0.0.1"));Arm();
        }
        public void Arm() {Gen=Mode.Generation;Seq=Contact=0;Packet(new(7,Gen,0,0,ModeContactPhase.Idle));}
        public void Packet(ModeContact p)=>Mode.Process(p,IPAddress.Loopback,Now);
        public ModeContact Down() {var p=new ModeContact(7,Gen,++Seq,++Contact,ModeContactPhase.Down);Packet(p);return p;}
        public void Up()=>Packet(new(7,Gen,++Seq,Contact,ModeContactPhase.Up));
        public void Advance(int value) {Now+=Stopwatch.Frequency*value/1000;Mode.Tick(Now);}
        // Advances only the phone sample clock so a contact cannot qualify as a tap.
        public void Lapse(int value)=>ms+=(ulong)value;
        public void Observe()=>Mode.Tick(Now);
        public TouchPacket Touch(TouchEventType type,float x=0,float y=0) {
            if(type==TouchEventType.Down)TouchId++;
            ms+=30;return new(new(2,type,1,TouchId,++TouchSeq,7),[new(ms*1_000_000,x,y)]);
        }
        public void Route(TouchPacket p,Action? movement=null,Action? gestures=null)=>Mode.RouteTouch(p,Store.Current,
            movement??(()=> {MovementCalls++;Motion.Process(p,Store.Current.SensitivityX,Store.Current.SensitivityY);}),
            gestures??(()=>Gestures++));
        public TouchPacket Tap() {Route(Touch(TouchEventType.Down));var up=Touch(TouchEventType.Up);LastUp=up;Route(up);return up;}
        public void SetCursor(double x,double y) {Cursor.Position=new(x,y);Observe();}
        public void MoveTo(double x,double y) {
            double dx=(x-Cursor.Position.X)/Store.Current.SensitivityX,dy=(y-Cursor.Position.Y)/Store.Current.SensitivityY;
            Route(Touch(TouchEventType.Down));ms+=400;Route(Touch(TouchEventType.Move,(float)dx,(float)dy));Route(Touch(TouchEventType.Up,(float)dx,(float)dy));
        }
        public void Dispose()=>Mode.Dispose();
    }
    private static void Toggle() {
        using var h=new Harness();var p=h.Down();Check(h.Mode.MenuOpen,"DOWN opens");
        for(int i=0;i<20;i++)h.Packet(p);Check(h.Mode.MenuOpen,"duplicate held DOWN no retoggle");
        h.Up();for(int i=0;i<10;i++){h.Advance(100);h.Packet(new(7,h.Gen,h.Seq,h.Contact,ModeContactPhase.Up));}
        Check(h.Mode.MenuOpen,"normal UP refresh keeps open");
        h.Packet(p);Check(h.Mode.MenuOpen,"late old DOWN ignored");
        h.Packet(new(7,h.Gen,h.Seq+2,h.Contact+1,ModeContactPhase.Down));Check(h.Mode.MenuOpen,"gap not interpreted");
        h.Down();Check(!h.Mode.MenuOpen && h.Keyboard.Calls.Count==0,"second valid DOWN closes without G or Esc");h.Up();
        h.Down();h.Up();h.Advance(601);Check(!h.Mode.MenuOpen && h.Mode.Generation!=h.Gen,"Mode lease closes");
        h.Packet(p);Check(!h.Mode.MenuOpen,"retired generation fenced");h.Arm();h.Down();h.Up();Check(h.Mode.MenuOpen,"fresh sync");
    }
    // The menu must not own a second movement implementation: every accepted packet
    // takes the normal Motion callback once and never reaches ordinary gestures.
    private static void Routing() {
        using var h=new Harness();h.Down();
        var packets=new[]{h.Touch(TouchEventType.Down),h.Touch(TouchEventType.Move,10),h.Touch(TouchEventType.Move,20),h.Touch(TouchEventType.Up,20)};
        int before=h.Moves.Count;
        foreach(var packet in packets)h.Route(packet);
        Check(h.MovementCalls==packets.Length,"one movement pass per accepted menu packet");
        Check(h.Gestures==0,"ordinary gesture path withheld for the whole menu contact");
        Check(h.Moves.Count==before+2,"stateful motion produced the real displacements once");
        Check(h.Cursor.Position.X==500+20*h.Store.Current.SensitivityX,"actual cursor reflects the original motion path");
        Check(h.Mode.MenuOpen,"navigation keeps menu open");
    }
    // Same fixed configuration and initial Motion state: the menu path and the normal
    // path must submit identical HID relative displacement for equivalent input.
    private static void Equivalence() {
        long now=100_000;const long frequency=1_000_000;
        RuntimeSettings settings=RuntimeSettings.Default with {SensitivityX=12,SensitivityY=12};
        var menuOutput=new List<(long At,int X,int Y)>();var normalOutput=new List<(long At,int X,int Y)>();
        using var menuMotion=new ResampledMotion((x,y)=>menuOutput.Add((now,x,y)),12,12,()=>now,frequency,
            finiteCriticalMode:MotionMode.M_R1,configuration:new(MotionMode.M_R1,24,120));
        using var normalMotion=new ResampledMotion((x,y)=>normalOutput.Add((now,x,y)),12,12,()=>now,frequency,
            finiteCriticalMode:MotionMode.M_R1,configuration:new(MotionMode.M_R1,24,120));
        var store=new RuntimeSettingsStore(settings with {ModeControl=Config()});
        var cursor=new Cursor();
        using var mode=new ModeSession(store,_=>true,(_,_)=>{},log:_=>{},clock:()=>now,menuCursor:cursor);
        mode.UpdatePresence(new(7,now,true,"127.0.0.1"));
        ulong generation=mode.Generation;uint sequence=0;
        void Packet(ModeContact p)=>mode.Process(p,IPAddress.Loopback,now);
        Packet(new(7,generation,0,0,ModeContactPhase.Idle));
        Packet(new(7,generation,++sequence,1,ModeContactPhase.Down));
        Check(mode.MenuOpen,"menu open for comparison");
        // The opening boundary reset both chains to the same idle state.
        normalMotion.Reset();
        var packets=new List<TouchPacket> {
            new(new(2,TouchEventType.Down,1,1,1,7),[new(0,100,50)]),
            new(new(2,TouchEventType.Move,1,1,2,7),[new(4_000_000,140,30)]),
            new(new(2,TouchEventType.Move,1,1,3,7),[new(8_000_000,150,45)]),
            new(new(2,TouchEventType.Up,1,1,4,7),[new(12_000_000,150,45)])
        };
        foreach(var packet in packets) {
            now+=4_000;
            mode.RouteTouch(packet,settings,()=>menuMotion.ProcessAt(packet,now,settings),()=>throw new Exception("menu gesture leak"));
            normalMotion.ProcessAt(packet,now,settings);
        }
        for(int t=0;t<40;t++) {
            now+=1_000;
            menuMotion.Tick(now,menuMotion.Schedule.Generation);
            normalMotion.Tick(now,normalMotion.Schedule.Generation);
        }
        Check(menuOutput.Count>0,"comparison produced HID displacement");
        Check(menuOutput.SequenceEqual(normalOutput),"menu and normal HID relative displacement identical");
        Check(menuMotion.TotalDx==normalMotion.TotalDx && menuMotion.TotalDy==normalMotion.TotalDy,"identical totals");
        Check(menuMotion.TickCount==normalMotion.TickCount && menuMotion.MissedTicks==normalMotion.MissedTicks,"identical fixed cadence state");
        Check(mode.MenuOpen && mode.MenuSnapshot!.CursorX==500,"menu stayed open without a private pointer");
    }
    // Requirement A: one press and one release, then exactly one confirmation for
    // the qualifying UP identity. Requirement B: the close happens after the release
    // so the pulse is never truncated.
    private static void KeyboardSlot() {
        using var h=new Harness();h.Down();h.Up();
        var slot=h.Mode.MenuSnapshot!.Placement.SlotCenter(0);
        h.SetCursor(slot.X,slot.Y);
        var up=h.Tap();
        Check(h.Mode.Held && h.Mode.MenuOpen,"press starts inside the open menu");
        Check(h.Keyboard.Calls.Count==1 && h.Keyboard.Calls[0]==("F13",true),"exactly one keyboard press");
        Check(h.Feedbacks.Count==0,"feedback waits for the completed release");
        h.Advance(24);
        Check(h.Mode.Held && h.Mode.MenuOpen,"auto-close cannot truncate the minimum visible pulse");
        h.Advance(1);
        Check(!h.Mode.Held,"exactly one release");
        Check(h.Keyboard.Calls.Count==2 && h.Keyboard.Calls[1]==("F13",false),"release follows the press");
        var expected=new ClickFeedback(7,up.Header.SessionId,up.Header.Sequence);
        Check(h.Feedbacks.Count==1 && h.Feedbacks[0].Click==expected,"one confirmation for the qualifying raw UP");
        Check(h.Feedbacks[0].Target=="127.0.0.1","confirmation targets the accepted presence source");
        Check(!h.Mode.MenuOpen,"menu closes only after press and release completed");
        int feedback=h.Logs.FindIndex(l=>l.StartsWith("mode_menu_feedback:"));
        int complete=h.Logs.FindIndex(l=>l.StartsWith("mode_menu_action_complete"));
        int close=h.Logs.FindIndex(l=>l.StartsWith("mode_menu_close"));
        Check(feedback>0 && feedback<complete && complete<close,"feedback then completion then close order");
        Check(h.Gestures==0 && h.MovementCalls==2,"both tap packets entered only the original motion path");
    }
    private static void GamepadSlot() {
        using var h=new Harness();h.Down();h.Up();
        var slot=h.Mode.MenuSnapshot!.Placement.SlotCenter(1);
        h.SetCursor(slot.X,slot.Y);
        var up=h.Tap();
        Check(h.Mode.Held && h.Mode.MenuOpen && h.Keyboard.Calls.Count==0,"gamepad press without keyboard output");
        Check(h.States.Count==1 && h.States[0]==new XboxGamepadState(XboxGamepadButtons.A),"exactly one gamepad press");
        h.Advance(25);
        Check(!h.Mode.Held && !h.Mode.MenuOpen,"gamepad pulse released then closed");
        Check(h.States.Count==2 && h.States[1]==default,"exactly one neutral release");
        Check(h.Feedbacks.Count==1 && h.Feedbacks[0].Click==new ClickFeedback(7,up.Header.SessionId,up.Header.Sequence),"one confirmation");
    }
    // Center (including the inclusive 12 DIP boundary), navigation, finger-down and
    // lift-and-reland must never emit a binding, a confirmation or an automatic close.
    private static void NoActionTaps() {
        using var h=new Harness();h.Down();h.Up();var anchor=h.Mode.MenuSnapshot!.Placement;
        foreach(var point in new[]{new Point(anchor.CenterX,anchor.CenterY),
            new Point(anchor.CenterX+anchor.NeutralRadius,anchor.CenterY),
            new Point(anchor.CenterX,anchor.CenterY-anchor.NeutralRadius)}) {
            h.SetCursor(point.X,point.Y);
            Check(h.Mode.MenuSnapshot!.Hovered==-1,"inclusive neutral radius selects nothing");
            for(int i=0;i<2;i++){h.Tap();h.Advance(30);}
        }
        Check(h.Keyboard.Calls.Count==0 && h.States.Count==0 && h.Feedbacks.Count==0 && h.Mode.MenuOpen,"center and 12 DIP boundary emit nothing and keep the menu");
        int moves=h.Moves.Count;
        h.Route(h.Touch(TouchEventType.Down));h.Route(h.Touch(TouchEventType.Move,30));h.Route(h.Touch(TouchEventType.Up,30));
        h.Advance(30);
        Check(h.Moves.Count>moves && h.Feedbacks.Count==0 && h.Mode.MenuOpen && h.Keyboard.Calls.Count==0,"navigation and lift emit nothing");
    }
    // Requirement A: every unbound direction accepts one valid tap, submits the
    // ordinary-click confirmation exactly once, emits no key/gamepad/mouse button
    // and closes. Duplicate or late packets cannot confirm or click again.
    private static void UnboundDirections() {
        using var h=new Harness(Config() with {Slots=new ModeMenuSlots()});
        var mouse=new Mouse();var logs=new List<string>();
        using var buttons=new LeftButtonController(mouse.LeftDown,mouse.LeftUp,logs.Add,()=>{},25);
        var gesture=new GestureProcessor((hold,_)=>buttons.Click(hold),h.Store.Current,
            ()=>buttons.BeginDrag(),()=>buttons.EndDrag());
        h.Mode.MenuBoundary=open=> {h.Motion.Reset();gesture.Reset();buttons.CancelPendingAndRelease(open?"menu_enter":"menu_exit");h.Boundaries++;};
        void Send(TouchPacket p)=>h.Route(p,()=>h.Motion.Process(p,h.Store.Current.SensitivityX,h.Store.Current.SensitivityY),
            ()=>gesture.Process(p,h.Store.Current.TapMaxDurationMs,h.Store.Current.TapMovementThresholdPx,h.Store.Current.ClickHoldMs,h.Store.Current.DoubleTapIntervalMs));
        for(int slot=0;slot<6;slot++) {
            h.Down();h.Up();
            Check(h.Mode.MenuOpen,"menu open for unbound direction "+slot);
            var placement=h.Mode.MenuSnapshot!.Placement;
            var at=placement.SlotCenter(slot);
            h.SetCursor(at.X,at.Y);
            Check(h.Mode.MenuSnapshot!.Hovered==slot,"hover selects unbound direction "+slot);
            int feedback=h.Feedbacks.Count;
            Send(h.Touch(TouchEventType.Down));
            var up=h.Touch(TouchEventType.Up);
            Send(up);
            Check(h.Feedbacks.Count==feedback+1,"exactly one confirmation for unbound "+slot);
            Check(h.Feedbacks[^1].Click==new ClickFeedback(7,up.Header.SessionId,up.Header.Sequence),"confirmation identity is the qualifying UP");
            Check(h.Feedbacks[^1].Target=="127.0.0.1","confirmation targets the accepted presence source");
            Check(h.Keyboard.Calls.Count==0 && h.States.Count==0,"unbound direction emits no key or gamepad button");
            Check(mouse.Count("down")==0 && mouse.Count("up")==0,"unbound direction emits no ordinary mouse button");
            Check(!h.Mode.MenuOpen && !h.Mode.Held,"unbound tap confirms then closes without holding output");
            Send(up);h.Advance(30);
            Check(h.Feedbacks.Count==feedback+1 && h.Keyboard.Calls.Count==0,"late or duplicate UP cannot confirm or act twice");
        }
        Thread.Sleep(60);
        Send(h.Touch(TouchEventType.Down));Send(h.Touch(TouchEventType.Up));
        Thread.Sleep(60);
        Check(mouse.Count("down")==1 && mouse.Count("up")==1,"next fresh DOWN restores ordinary click after an unbound close");
    }
    // The confirmation is best effort on the unbound path too: the close happens,
    // the failure is logged, nothing replays and normal control resumes.
    private static void UnboundFeedbackFailure() {
        using var h=new Harness(Config() with {Slots=new ModeMenuSlots()});
        h.FailFeedback=true;h.Down();h.Up();
        var at=h.Mode.MenuSnapshot!.Placement.SlotCenter(3);h.SetCursor(at.X,at.Y);
        h.Tap();
        Check(!h.Mode.MenuOpen && !h.Mode.Held && h.Feedbacks.Count==0,"feedback failure still closes the unbound menu");
        Check(h.Logs.Any(l=>l.StartsWith("mode_menu_feedback_error")),"unbound feedback failure is logged");
        Check(h.Logs.Any(l=>l.StartsWith("mode_menu_unbound_complete")),"unbound completion is still recorded");
        Check(h.Keyboard.Calls.Count==0 && h.States.Count==0,"failed unbound feedback emits no output");
        int moves=0,gestures=0;
        h.Route(h.Touch(TouchEventType.Up),()=>moves++,()=>gestures++);
        h.Route(h.Touch(TouchEventType.Down),()=>moves++,()=>gestures++);
        Check(moves==1 && gestures==1,"next new DOWN restores ordinary input");
        Check(h.Keyboard.Calls.Count==0,"failed unbound feedback never replays");
    }
    // One accepted selection per opening is shared by bound and unbound slots. A
    // second unbound completion attempted before the automatic close is rejected.
    private static void UnboundOncePerCycle() {
        using var h=new Harness(Config() with {Slots=new ModeMenuSlots()});
        h.Down();h.Up();
        var at=h.Mode.MenuSnapshot!.Placement.SlotCenter(4);h.SetCursor(at.X,at.Y);
        bool attempted=false;
        h.OnFeedback=()=> {
            if(attempted)return;attempted=true;
            var again=typeof(ModeSession).GetMethod("UnboundTap",BindingFlags.NonPublic|BindingFlags.Instance)!;
            again.Invoke(h.Mode,new object[]{new ClickFeedback(7,424242,99)});
        };
        h.Tap();
        Check(h.Feedbacks.Count==1,"only the first unbound selection is accepted");
        Check(h.Logs.Count(l=>l.StartsWith("mode_menu_unbound:"))==1,"second unbound completion is rejected before logging");
        Check(!h.Mode.MenuOpen,"the accepted unbound selection closes the menu once");
        h.Advance(30);
        Check(h.Feedbacks.Count==1,"late work cannot confirm again");
    }
    // A failed press never produces a confirmation and must close safely.
    private static void FailedPress() {
        using var h=new Harness();h.Down();h.Up();
        var at=h.Mode.MenuSnapshot!.Placement.SlotCenter(0);h.SetCursor(at.X,at.Y);
        h.Keyboard.Fail=true;h.Tap();
        Check(!h.Mode.MenuOpen && !h.Mode.Held,"failed slot press closes safely");
        Check(h.Feedbacks.Count==0,"failed slot press sends no confirmation");
        Check(h.Logs.Any(l=>l.StartsWith("mode_output_error")),"failed slot press is logged");
        h.Advance(100);Check(!h.Mode.Held,"failed press leaves no held output");
        int moves=0,gestures=0;
        h.Route(h.Touch(TouchEventType.Up),()=>moves++,()=>gestures++);
        h.Route(h.Touch(TouchEventType.Down),()=>moves++,()=>gestures++);
        Check(moves==1 && gestures==1,"new DOWN resumes ordinary input after failed press");
    }
    // Retransmitted, repeated and late packets cannot execute or confirm twice.
    private static void RepeatPackets() {
        using var h=new Harness();h.Down();h.Up();
        var slot=h.Mode.MenuSnapshot!.Placement.SlotCenter(0);h.SetCursor(slot.X,slot.Y);
        var up=h.Tap();
        h.Route(up);            // exact retransmission of the qualifying UP
        h.Tap();h.Tap();        // further taps while the pulse is still in flight
        Check(h.Keyboard.Calls.Count==1,"only the first press is admitted");
        h.Advance(25);
        Check(h.Keyboard.Calls.Count==2 && !h.Mode.Held,"exactly one press and one release");
        Check(h.Feedbacks.Count==1 && !h.Mode.MenuOpen,"one confirmation and one close");
        int keys=h.Keyboard.Calls.Count;
        h.Route(up);h.Advance(100);
        Check(h.Keyboard.Calls.Count==keys && h.Feedbacks.Count==1,"late or retransmitted UP cannot replay");
        h.Down();h.Up();
        Check(h.Mode.MenuOpen,"explicit new Mode contact reopens");
        var reopened=h.Mode.MenuSnapshot!.Placement.SlotCenter(0);
        h.SetCursor(reopened.X,reopened.Y);h.Tap();h.Advance(25);
        Check(h.Keyboard.Calls.Count==keys+2 && h.Feedbacks.Count==2 && !h.Mode.MenuOpen,"a new opening executes once");
    }
    // The confirmation is best effort: a failing send never replays the action,
    // never leaves the menu open and never blocks normal control.
    private static void FeedbackFailure() {
        using var h=new Harness();h.FailFeedback=true;h.Down();h.Up();
        var slot=h.Mode.MenuSnapshot!.Placement.SlotCenter(0);h.SetCursor(slot.X,slot.Y);
        h.Tap();h.Advance(25);
        Check(h.Keyboard.Calls.Count==2 && !h.Mode.Held && !h.Mode.MenuOpen,"completed action still closes the menu");
        Check(h.Logs.Any(l=>l.StartsWith("mode_menu_feedback_error")),"feedback failure is logged");
        int keys=h.Keyboard.Calls.Count,moved=0,gestures=0;
        h.Route(h.Touch(TouchEventType.Up),()=>moved++,()=>gestures++);
        h.Route(h.Touch(TouchEventType.Down),()=>moved++,()=>gestures++);
        Check(moved==1 && gestures==1,"next new DOWN restores ordinary input");
        Check(h.Keyboard.Calls.Count==keys && h.Feedbacks.Count==0,"feedback failure never replays the action");
    }
    // Manual close, disconnect, configuration change and output failure must all
    // release the pulse, send no confirmation and preserve later openings.
    private static void PulseLifecycle() {
        using(var h=new Harness()) {
            h.Down();h.Up();var slot=h.Mode.MenuSnapshot!.Placement.SlotCenter(0);h.SetCursor(slot.X,slot.Y);h.Tap();
            Check(h.Mode.Held,"pulse held before the manual close");
            h.Down();
            Check(!h.Mode.MenuOpen && !h.Mode.Held && h.Keyboard.Calls.Last()==("F13",false),"manual close releases the pulse");
            Check(h.Feedbacks.Count==0,"manual close is not a completed slot action");
            h.Up();h.Down();Check(h.Mode.MenuOpen,"reopen after manual close");
            var again=h.Mode.MenuSnapshot!.Placement.SlotCenter(0);h.SetCursor(again.X,again.Y);h.Tap();h.Advance(25);
            Check(!h.Mode.MenuOpen && h.Feedbacks.Count==1,"reopened menu executes and confirms once");
            h.Up();h.Advance(200);
            Check(h.Feedbacks.Count==1 && !h.Mode.MenuOpen,"stale work cannot close or confirm after reopen");
        }
        foreach(string cause in new[]{"presence","run","config","gamepad","output"}) {
            using var h=new Harness();h.Down();h.Up();
            var slot=h.Mode.MenuSnapshot!.Placement.SlotCenter(cause=="gamepad"?1:0);h.SetCursor(slot.X,slot.Y);
            h.Tap();
            Check(h.Mode.Held,"pulse started "+cause);
            switch(cause) {
                case "presence":h.Mode.UpdatePresence(new(7,h.Now,false,"127.0.0.1"));break;
                case "run":h.Mode.UpdatePresence(new(8,h.Now,true,"127.0.0.1"));break;
                case "config":h.Store.Publish(h.Store.Current with {ModeControl=h.Store.Current.ModeControl with {
                    Slots=h.Store.Current.ModeControl.Slots.With(0,new(ModeBindingKind.Keyboard,"F15"))}});break;
                case "gamepad":h.FailGamepad=true;h.Advance(1);break;
                default:h.Keyboard.Fail=true;h.Advance(25);break;
            }
            Check(!h.Mode.MenuOpen && !h.Mode.Held,"fault releases and closes "+cause);
            Check(h.Feedbacks.Count==0,"no confirmation for an aborted action "+cause);
            h.Advance(100);Check(!h.Mode.Held,"old release cannot affect later state "+cause);
        }
    }
    private static void Settings() {
        string root=Path.Combine(Path.GetTempPath(),"rightpad-menu-"+Guid.NewGuid());Directory.CreateDirectory(root);
        string path=Path.Combine(root,"settings.json");
        var original=System.Text.Json.JsonSerializer.SerializeToNode(RuntimeSettings.Default,new System.Text.Json.JsonSerializerOptions {PropertyNamingPolicy=System.Text.Json.JsonNamingPolicy.CamelCase,IgnoreReadOnlyProperties=true})!.AsObject();
        original["modeControl"]=JsonNode.Parse("{\"kind\":1,\"key\":\"G\",\"behavior\":1,\"extension\":42}");File.WriteAllText(path,original.ToJsonString());
        var loaded=SettingsFileStore.Load(path).Settings;Check(loaded.ModeControl==new ModeBinding(ModeBindingKind.Keyboard,"G",ModePressBehavior.Toggle),"missing menu fields preserves old G toggle");
        var store=new RuntimeSettingsStore(loaded);var vm=new SettingsViewModel(store,new SettingsFileStore(path));vm.Mode.Purpose(ModePurpose.ShortcutMenu);vm.Mode.Slot(0,ModeBindingKind.Keyboard,"F13");
        Check(store.Current==loaded && vm.HasUnsavedChanges,"draft isolated");Check(vm.SaveAsync().GetAwaiter().GetResult(),"disk first Save");
        Check(SettingsFileStore.Load(path).Settings==store.Current && File.ReadAllText(path).Contains("extension"),"equality roundtrip unknown retained");
        using var h=new Harness(store.Current.ModeControl);h.Down();h.Up();var generation=h.Mode.Generation;
        h.Store.Publish(h.Store.Current with {SensitivityY=9});Check(h.Mode.MenuOpen && h.Mode.Generation==generation,"unrelated Save keeps menu");
        h.Store.Publish(h.Store.Current with {ModeControl=SettingsFileStore.Load(path).Settings.ModeControl});Check(h.Mode.MenuOpen,"equal deserialized snapshot keeps menu");
        string blocked=Path.Combine(root,"blocked");Directory.CreateDirectory(blocked);var failing=new SettingsViewModel(store,new SettingsFileStore(blocked));
        failing.Mode.Slot(1,ModeBindingKind.Gamepad,"RT");Check(!failing.SaveAsync().GetAwaiter().GetResult() && failing.HasUnsavedChanges && store.Current.ModeControl.Slots[1].Kind==ModeBindingKind.Unbound,"failed Save no publish and retains draft");
        Check(store.Current.ModeControl.Key=="G" && store.Current.ModeControl.Behavior==ModePressBehavior.Toggle,"single mode original retained");
    }
    private static void Gamepad() {
        var states=new List<XboxGamepadState>();var g=new GamepadSessionProcessor(s=> {states.Add(s);return true;});g.UpdatePresence(new(7,0,true,"127.0.0.1"));long now=Stopwatch.GetTimestamp();
        void Send(uint seq,XboxGamepadState state)=>g.Process(new(7,seq,state),IPAddress.Loopback,now++);
        var b=new XboxGamepadState(XboxGamepadButtons.B);Send(1,b);g.SetInputModal(true);Check(states.Last()==default,"held B released entering");
        Send(2,b);g.SetInputModal(false);Send(3,b);Check(states.Last()==default,"old refresh cannot revive");
        Send(4,default);Send(5,b);Check(states.Last()==b,"new press after neutral");
        g.SetInputModal(true);Send(6,default);g.SetInputModal(false);Send(7,new(XboxGamepadButtons.X));Check(states.Last().Buttons==XboxGamepadButtons.X,"neutral during modal admits next new X");
        g.SetInputModal(true);g.SetInputModal(false);g.UpdatePresence(new(8,now,true,"127.0.0.1"));
        g.Process(new(8,0,b),IPAddress.Loopback,now++);Check(states.Last()==b,"new Sender cannot inherit old run modal barrier");g.Stop();
    }
    private static void Ownership() {
        using var h=new Harness();
        var mouse=new Mouse();var logs=new List<string>();
        using var buttons=new LeftButtonController(mouse.LeftDown,mouse.LeftUp,logs.Add,()=>{},25);
        var gesture=new GestureProcessor((hold,_)=>buttons.Click(hold),h.Store.Current,
            ()=>buttons.BeginDrag(),()=>buttons.EndDrag());
        h.Mode.MenuBoundary=open=> {h.Motion.Reset();gesture.Reset();buttons.CancelPendingAndRelease(open?"menu_enter":"menu_exit");h.Boundaries++;};
        void Send(TouchPacket p)=>h.Route(p,()=>h.Motion.Process(p,h.Store.Current.SensitivityX,h.Store.Current.SensitivityY),
            ()=>gesture.Process(p,h.Store.Current.TapMaxDurationMs,h.Store.Current.TapMovementThresholdPx,h.Store.Current.ClickHoldMs,h.Store.Current.DoubleTapIntervalMs));
        Send(h.Touch(TouchEventType.Down));Send(h.Touch(TouchEventType.Up));Thread.Sleep(60);Check(mouse.Count("down")==1 && mouse.Count("up")==1,"fresh single click before menu");
        Send(h.Touch(TouchEventType.Down));Check(mouse.Count("down")==2 && mouse.Count("up")==1,"double tap drag holds left button");
        h.Down();h.Up();
        Check(h.Boundaries==1 && mouse.Count("down")==mouse.Count("up"),"menu entry safely releases held mouse button");
        Check(logs.Any(l=>l.StartsWith("left_button_safety_release: reason=menu_enter")),"safety release recorded separately");
        int moves=h.Moves.Count;long clicks=gesture.ClicksTriggered;
        Send(h.Touch(TouchEventType.Move,50));Send(h.Touch(TouchEventType.Up,50));
        Check(h.Moves.Count==moves && gesture.ClicksTriggered==clicks,"orphan old move and UP cannot move or click");
        Send(h.Touch(TouchEventType.Down));Send(h.Touch(TouchEventType.Move,30));Send(h.Touch(TouchEventType.Up,30));
        Check(h.Moves.Count>moves && mouse.Count("down")==mouse.Count("up"),"menu contact moves without any mouse button");
        h.Down();h.Up();int afterClose=h.Moves.Count;
        Send(h.Touch(TouchEventType.Move,100));Send(h.Touch(TouchEventType.Up,100));
        Check(h.Moves.Count==afterClose && mouse.Count("down")==mouse.Count("up"),"exit orphans cannot drag or click");
        Send(h.Touch(TouchEventType.Down));Send(h.Touch(TouchEventType.Up));Check(gesture.ClicksTriggered==clicks+1,"fresh click restored");
        Send(h.Touch(TouchEventType.Down));Send(h.Touch(TouchEventType.Move,10));Send(h.Touch(TouchEventType.Up,10));
        Check(gesture.IsDragging==false && gesture.DragStarts==2 && gesture.DragEnds==1 && mouse.Count("down")==mouse.Count("up"),"fresh double tap drag restored and released");
    }
    private static void SafetyLog() {
        var mouse=new Mouse();var logs=new List<string>();
        using var buttons=new LeftButtonController(mouse.LeftDown,mouse.LeftUp,logs.Add,()=>{},25);
        buttons.Click();buttons.Click(); // second click necessarily waits for the first hold
        buttons.CancelPendingAndRelease("menu_enter");
        var line=logs.SingleOrDefault(l=>l.StartsWith("left_button_safety_release:"));
        Check(line is not null && line.Contains("reason=menu_enter") && line.Contains("held=True") && line.Contains("pendingClicks=1"),"separate safety release record includes held and queued work");
        Check(mouse.Events.Count(e=>e=="up")==1 && mouse.Count("down")==1,"exactly one safety UP, queued click discarded");
        using var tapHarness=new Harness();tapHarness.Down();tapHarness.Up();
        var slot=tapHarness.Mode.MenuSnapshot!.Placement.SlotCenter(0);
        tapHarness.SetCursor(slot.X,slot.Y);tapHarness.Tap();
        Check(tapHarness.Logs.Any(l=>l.StartsWith("mode_output:")),"tap pulse has its own output record");
    }
    private static void Failures() {
        foreach(string cause in new[]{"presence","run","cancel","display","stop","output"}) {
            using var h=new Harness();h.Down();h.Up();var c=h.Mode.MenuSnapshot!.Placement.SlotCenter(0);h.SetCursor(c.X,c.Y);h.Tap();
            switch(cause) {
                case "presence":h.Mode.UpdatePresence(new(7,h.Now,false,"127.0.0.1"));break;
                case "run":h.Mode.UpdatePresence(new(8,h.Now,true,"127.0.0.1"));break;
                case "cancel":h.Packet(new(7,h.Gen,99,99,ModeContactPhase.Cancel));break;
                case "display":h.Mode.MenuFault("injected display error");break;
                case "stop":h.Mode.Dispose();break;
                default:h.Keyboard.Fail=true;h.Advance(25);break;
            }
            Check(!h.Mode.MenuOpen && !h.Mode.Held,"safety closes and releases "+cause);
            Check(h.Feedbacks.Count==0,"no confirmation for the interrupted action "+cause);
            h.Advance(100);Check(!h.Mode.Held,"old work fenced "+cause);
            if(cause!="stop") {
                int moved=0,gestures=0;
                h.Route(h.Touch(TouchEventType.Up),()=>moved++,()=>gestures++);
                h.Route(h.Touch(TouchEventType.Down),()=>moved++,()=>gestures++);
                Check(moved==1 && gestures==1,"new DOWN boundary resumes normal input "+cause);
            }
        }
    }
    private static void Geometry() {
        foreach(var a in new[]{new MenuArea(0,0,1920,1080),new(-1920,-1080,1920,1080,1.5),new(1920,0,800,600,2),new(0,0,280,220)})
        foreach(double x in new[]{a.X,a.X+a.Width/2,a.X+a.Width})
        foreach(double y in new[]{a.Y,a.Y+a.Height/2,a.Y+a.Height}) {
            var p=MenuPlacement.Create(a,x,y);double r=p.Diameter/2;
            Check(p.CenterX==x && p.CenterY==y,"edge anchor never clamped or moved");
            for(int i=0;i<6;i++) {var c=p.SlotCenter(i);Check(p.Select(c.X,c.Y)==i,"same orientation slot "+i);}
            Check(p.Select(x,y)==-1 && p.Select(x+p.NeutralRadius,y)==-1,"inclusive neutral radius scales with DPI");
        }
    }
    private static void Directions() {
        foreach(double scale in new[]{1d,1.25,1.5,2,3}) {
            var p=MenuPlacement.Create(new(-2200,-1000,2200,1600,scale),-1800,-800);
            Check(p.Select(p.CenterX,p.CenterY)==-1,"opening neutral");
            foreach(double radius in new[]{12*scale+.001,40*scale,1000*scale,100000d})
            for(int i=0;i<6;i++) {
                int At(double angle) {double r=angle*Math.PI/180;return p.Select(p.CenterX+radius*Math.Sin(r),p.CenterY-radius*Math.Cos(r));}
                Check(At(i*60)==i,"six axes at any distance");
                Check(At(i*60+30)==(i+1)%6,"exact clockwise boundary");
                Check(At(i*60+30-.0001)==i && At(i*60+30+.0001)==(i+1)%6,"both sides of boundary");
            }
            Check(p.Select(p.CenterX+12*scale,p.CenterY)==-1,"inclusive dead zone");
            Check(p.Select(p.CenterX+12*scale+.01,p.CenterY)==2,"outside dead zone horizontal tie clockwise");
        }
        using var ring=new Harness(Config());ring.Down();ring.Up();
        var placement=ring.Mode.MenuSnapshot!.Placement;
        foreach(var point in new[]{new Point(500,380),new Point(500,-4000),new Point(1900,400)}) {
            ring.SetCursor(point.X,point.Y);
            Check(ring.Mode.MenuSnapshot!.Hovered==placement.Select(point.X,point.Y),"ring selection includes gaps and exterior");
        }
    }
    private static void CursorAuthority() {
        using var h=new Harness();h.Down();h.Up();var anchor=h.Mode.MenuSnapshot!.Placement;
        h.SetCursor(500,380);
        Check(h.Mode.MenuSnapshot!.Hovered==0 && h.Mode.MenuSnapshot.Placement==anchor,"short direction movement, fixed anchor");
        h.SetCursor(500,420);h.Tap();
        Check(h.States.SequenceEqual(new[]{new XboxGamepadState(LeftTrigger:255)}) && h.Keyboard.Calls.Count==0,"tap reads final system position rather than old highlight");
        h.Advance(25);
        Check(!h.Mode.Held && !h.Mode.MenuOpen && h.Feedbacks.Count==1,"one pulse, one confirmation, then closed");
        // A later opening uses the actual current position as its own fixed anchor.
        h.Down();h.Up();
        var reopened=h.Mode.MenuSnapshot!.Placement;
        Check(reopened.CenterX==500 && reopened.CenterY==420,"new opening anchors at the actual position without warping");
        h.SetCursor(500,420);h.Tap();h.Advance(25);
        Check(!h.Mode.Held && h.Mode.MenuOpen && h.Feedbacks.Count==1 && h.Keyboard.Calls.Count==0,"neutral recontact emits nothing and keeps the new opening");
    }
    private static void CursorFailures() {
        foreach(string failure in new[]{"capture","maintenance","tap"}) {
            using var h=new Harness();if(failure=="capture")h.Cursor.FailRead=true;
            h.Down();h.Up();
            if(failure!="capture") {
                h.SetCursor(500,380);h.Tap();Check(h.Mode.Held,"active output before fault");
                h.Cursor.FailRead=true;
                if(failure=="maintenance")h.Advance(1);
                else {h.Tap();}
            }
            Check(!h.Mode.MenuOpen && !h.Mode.Held && h.Mode.Status.Contains("失败"),"cursor fault exits and releases "+failure);
            Check(h.Feedbacks.Count==0,"no confirmation for the faulted action "+failure);
            int normal=0;h.Route(h.Touch(TouchEventType.Move),()=>normal++,()=>normal++);h.Route(h.Touch(TouchEventType.Up),()=>normal++,()=>normal++);
            h.Route(h.Touch(TouchEventType.Down),()=>normal++,()=>normal++);Check(normal==2,"no stale contact after cursor fault");
            h.Cursor.FailRead=false;h.Arm();h.Down();h.Up();
            Check(h.Mode.MenuOpen,"new synchronized Mode contact retries recovered cursor environment");
        }
        using var release=new Harness();release.Down();release.Up();release.SetCursor(500,380);release.Tap();
        release.Keyboard.Fail=true;release.Cursor.FailRead=true;release.Advance(1);
        release.Keyboard.Fail=false;release.Cursor.FailRead=false;release.Arm();release.Down();
        Check(!release.Mode.MenuOpen,"device release error remains latched across environment recovery");
        Check(release.Feedbacks.Count==0,"latched release error sends no confirmation");
    }
    private sealed class CountingMotion(TouchSessionProcessor inner) : ITouchMotion {
        public int Calls;
        public uint? ActiveSessionId=>inner.ActiveSessionId;
        public long ProcessedMotionSamples=>inner.ProcessedMotionSamples;
        public long IgnoredSessionPackets=>inner.IgnoredSessionPackets;
        public long OutputEvents=>inner.OutputEvents;
        public long LastOutputAtTicks=>inner.LastOutputAtTicks;
        public long TotalDx=>inner.TotalDx;
        public long TotalDy=>inner.TotalDy;
        public void Process(TouchPacket packet)=>inner.Process(packet);
        public void Process(TouchPacket packet,double sx,double sy)=>inner.Process(packet,sx,sy);
        public void ProcessAt(TouchPacket packet,long at,RuntimeSettings? settings){Calls++;inner.Process(packet,settings?.SensitivityX??1,settings?.SensitivityY??1);}
        public void Reset()=>inner.Reset();
    }
    // End-to-end production routing: UdpReceiver -> Mode ownership -> original
    // Motion for every accepted packet, ordinary Gesture only outside the menu.
    private static void UdpRoute() {
        using var h=new Harness();
        var mouse=new Mouse();var logs=new List<string>();
        using var buttons=new LeftButtonController(mouse.LeftDown,mouse.LeftUp,logs.Add,()=>{},25);
        var gesture=new GestureProcessor((hold,_)=>buttons.Click(hold),h.Store.Current,
            ()=>buttons.BeginDrag(),()=>buttons.EndDrag());
        var counting=new CountingMotion(h.Motion);
        using var udp=new UdpReceiver(new(IPAddress.Loopback,0),TextWriter.Null,motion:counting,gesture:gesture,
            settings:h.Store,mode:h.Mode,cancelButtons:reason=>buttons.CancelPendingAndRelease(reason));
        uint seq=0,id=1;ulong ms=1000;
        var remote=new IPEndPoint(IPAddress.Loopback,43000);
        void Send(TouchEventType type,float x,float y=0) {
            var bytes=PacketDecoderTests.Encode(type,id,seq++,new TouchSample((ms+=30)*1_000_000,x,y));
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(2),7);
            h.Now+=Stopwatch.Frequency/1000;udp.ProcessDatagram(bytes,remote,h.Now);
        }
        void SendDuplicate(TouchEventType type,float x,float y=0) {
            var bytes=PacketDecoderTests.Encode(type,id,seq-1,new TouchSample((ms+=30)*1_000_000,x,y));
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(2),7);
            h.Now+=Stopwatch.Frequency/1000;udp.ProcessDatagram(bytes,remote,h.Now);
        }
        void ModeSend(ulong generation,uint sequence,uint contact,ModeContactPhase phase) {
            var bytes=ModeBytes(7,generation,sequence,contact,(byte)phase);
            h.Now+=Stopwatch.Frequency/1000;udp.ProcessDatagram(bytes,remote,h.Now);
        }
        // Normal fresh click, then a held double-tap drag that menu entry must release.
        Send(TouchEventType.Down,0);Send(TouchEventType.Up,0);Check(gesture.ClicksTriggered==1,"normal click before menu");
        Thread.Sleep(60); // let the real click hold complete outside the menu
        Send(TouchEventType.Down,0);Send(TouchEventType.Move,10);
        Check(gesture.IsDragging && mouse.Count("down")==2 && mouse.Count("up")==1,"drag held before menu entry");
        ulong generation=h.Mode.Generation;
        ModeSend(generation,0,0,ModeContactPhase.Idle);
        ModeSend(generation,1,1,ModeContactPhase.Down);
        Check(h.Mode.MenuOpen,"production menu open");
        Check(mouse.Count("down")==mouse.Count("up") && logs.Any(l=>l.Contains("reason=menu_enter")),"entry safety release is logged separately");
        long clicks=gesture.ClicksTriggered;int moves=h.Moves.Count;
        Send(TouchEventType.Move,60);Send(TouchEventType.Up,60);
        Check(gesture.ClicksTriggered==clicks && mouse.Count("down")==mouse.Count("up"),"old drag move/UP cannot click inside menu");
        int calls=counting.Calls;
        Send(TouchEventType.Down,0);Send(TouchEventType.Move,40);Send(TouchEventType.Up,40);
        Check(counting.Calls==calls+3,"each accepted menu packet entered original motion exactly once");
        Check(h.Moves.Count>moves && h.Mode.MenuOpen,"menu movement is real HID movement and stays open");
        Check(gesture.ClicksTriggered==clicks && mouse.Count("down")==mouse.Count("up"),"menu movement and tap never produce ordinary buttons");
        calls=counting.Calls;
        SendDuplicate(TouchEventType.Move,41); // rejected before movement
        Check(counting.Calls==calls,"rejected duplicate does not enter motion");
        // A real Touch cancellation clears menu tap qualification without a slot action,
        // ordinary mouse output, Mode close or menu confirmation.
        uint cancelledDown=seq;Send(TouchEventType.Down,0);
        udp.ProcessDatagram(TouchCancellationTests.Cancel(7,id,cancelledDown,seq++),remote,++h.Now);
        Send(TouchEventType.Up,0);Send(TouchEventType.Move,0);
        Check(h.Mode.MenuOpen && h.Keyboard.Calls.Count==0 && h.Feedbacks.Count==0
            && mouse.Count("down")==mouse.Count("up"),"cancelled menu contact emits nothing and stays open");
        // Tap the configured top slot: one keyboard pulse, one confirmation, then close.
        var anchor=h.Mode.MenuSnapshot!.Placement;h.SetCursor(anchor.CenterX,anchor.SlotCenter(0).Y-1);
        int keys=h.Keyboard.Calls.Count,downs=mouse.Count("down");
        Send(TouchEventType.Down,0);
        uint menuUpSequence=seq;
        Send(TouchEventType.Up,0);
        Check(h.Keyboard.Calls.Count==keys+1 && h.Keyboard.Calls.Last()==("F13",true),"menu tap executes only the slot binding");
        Check(h.Feedbacks.Count==0 && h.Mode.MenuOpen,"confirmation waits for the completed release");
        h.Advance(30);
        Check(h.Keyboard.Calls.Count==keys+2 && h.Keyboard.Calls.Last()==("F13",false),"menu tap releases once");
        Check(!h.Mode.MenuOpen,"completed slot action closes the menu");
        Check(h.Feedbacks.Count==1 && h.Feedbacks[0].Click==new ClickFeedback(7,id,menuUpSequence),"one confirmation for the qualifying raw UP");
        Check(mouse.Count("down")==downs && mouse.Count("up")==downs,"menu tap emits no ordinary mouse buttons");
        int afterClose=h.Moves.Count;
        Send(TouchEventType.Move,90);Send(TouchEventType.Up,90);
        Check(h.Moves.Count==afterClose && gesture.ClicksTriggered==clicks,"orphan post-close MOVE/UP cannot move, click or drag");
        ModeSend(generation,2,1,ModeContactPhase.Up);
        ModeSend(generation,3,2,ModeContactPhase.Down);
        Check(h.Mode.MenuOpen,"explicit Mode DOWN reopens after the automatic close");
        ModeSend(generation,4,2,ModeContactPhase.Up);
        ModeSend(generation,5,3,ModeContactPhase.Down);
        Check(!h.Mode.MenuOpen && h.Keyboard.Calls.Count==keys+2,"explicit Mode DOWN still closes manually");
        clicks=gesture.ClicksTriggered;
        Send(TouchEventType.Down,0);Send(TouchEventType.Up,0);
        Check(gesture.ClicksTriggered==clicks+1 && mouse.Count("down")==downs+1,"fresh normal click restored");
        Thread.Sleep(60);
        Send(TouchEventType.Down,0);Send(TouchEventType.Move,10);Send(TouchEventType.Up,10);
        Check(gesture.IsDragging==false && gesture.DragStarts==2 && gesture.DragEnds==1,"fresh double tap drag restored and released");
        Check(mouse.Count("down")==mouse.Count("up"),"all ordinary mouse buttons released");
    }
    private static byte[] ModeBytes(ulong run,ulong generation,uint sequence,uint contact,byte phase) {
        var b=new byte[32];b[0]=2;b[1]=8;
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(2),run);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(10),generation);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(18),sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(22),contact);
        b[26]=phase;return b;
    }
    private static void PresentationTimeout() {
        using var h=new Harness();h.Down();h.Up();
        for(int i=0;i<21;i++) {h.Advance(100);h.Packet(new(7,h.Gen,h.Seq,h.Contact,ModeContactPhase.Up));}
        Check(!h.Mode.MenuOpen && h.Mode.Status.Contains("显示未响应"),"display stalls while Mode remains alive");
        int normal=0;h.Route(h.Touch(TouchEventType.Up),()=>normal++,()=>normal++);h.Route(h.Touch(TouchEventType.Down),()=>normal++,()=>normal++);
        Check(normal==2,"next clear DOWN resumes normal input");
    }
    private static void IdleMinute() {
        using var h=new Harness();h.Down();h.Up();var anchor=h.Mode.MenuSnapshot!.Placement;
        for(int i=0;i<650;i++) {
            h.Mode.MenuPresented();h.Advance(100);h.Packet(new(7,h.Gen,h.Seq,h.Contact,ModeContactPhase.Up));
            Check(h.Mode.MenuOpen && !h.Mode.Held && h.Mode.MenuSnapshot!.Placement==anchor,"healthy idle must stay open "+i);
        }
        for(int i=0;i<60;i++) {h.Mode.MenuPresented();h.MoveTo(500+i%2*30,370+i%3*30);h.Advance(100);h.Packet(new(7,h.Gen,h.Seq,h.Contact,ModeContactPhase.Up));}
        Check(h.Mode.MenuOpen && h.Keyboard.Calls.Count==0,"continuous navigation recontact no tap no output");
    }
    private static void NavigationGain() {
        foreach(double scale in new[]{1d,1.25,1.5,2,3}) {
            using var h=new Harness();h.Cursor.Scale=scale;h.Store.Publish(h.Store.Current with {SensitivityX=4,SensitivityY=7});h.Down();h.Up();
            h.Route(h.Touch(TouchEventType.Down));h.Route(h.Touch(TouchEventType.Move,3,2));
            Check(h.Cursor.Position==new Point(512,414),"X/Y committed gain independent of DPI");
            h.Store.Publish(h.Store.Current with {SensitivityX=2,SensitivityY=3});h.Route(h.Touch(TouchEventType.Move,4,3));
            Check(h.Cursor.Position==new Point(514,417),"published hot Save affects only new real delta");
            h.Route(h.Touch(TouchEventType.Up,4,3));
            h.Route(h.Touch(TouchEventType.Down));
            for(int i=1;i<=4;i++)h.Route(h.Touch(TouchEventType.Move,i*.125f,i*.125f));
            Check(h.Cursor.Position==new Point(515,418),"fractional displacement conserved within contact");
            h.Lapse(400); // keep this contact out of the tap duration so it cannot close the menu
            h.Route(h.Touch(TouchEventType.Up,.5f,.5f));
            h.Route(h.Touch(TouchEventType.Down));h.Route(h.Touch(TouchEventType.Move,.125f,.125f));h.Lapse(400);h.Route(h.Touch(TouchEventType.Up,.125f,.125f));
            Check(h.Cursor.Position==new Point(515,418),"fractional remainder cannot cross contacts");
            h.Down();h.Up();int normal=0;h.Route(h.Touch(TouchEventType.Move,10,10),()=>normal++,()=>normal++);h.Route(h.Touch(TouchEventType.Up,10,10),()=>normal++,()=>normal++);
            Check(normal==0,"exit has no leftover menu movement or residual");
        }
        string root=Path.Combine(Path.GetTempPath(),"rightpad-menu-gain-"+Guid.NewGuid());Directory.CreateDirectory(root);
        using var saved=new Harness();string path=Path.Combine(root,"settings.json");
        var vm=new SettingsViewModel(saved.Store,new SettingsFileStore(path));saved.Down();saved.Up();
        vm.SensitivityX.Text="9";vm.SensitivityY.Text="5";
        var before=saved.Cursor.Position;saved.Route(saved.Touch(TouchEventType.Down));saved.Route(saved.Touch(TouchEventType.Move,2,2));saved.Route(saved.Touch(TouchEventType.Up,2,2));
        Check(saved.Cursor.Position==new Point(before.X+2*saved.Store.Current.SensitivityX,before.Y+2*saved.Store.Current.SensitivityY),"unsaved draft no gain change");
        Check(vm.SaveAsync().GetAwaiter().GetResult(),"gain common disk-first Save");
        before=saved.Cursor.Position;saved.Route(saved.Touch(TouchEventType.Down));saved.Route(saved.Touch(TouchEventType.Move,2,2));saved.Route(saved.Touch(TouchEventType.Up,2,2));
        Check(saved.Cursor.Position==new Point(before.X+18,before.Y+10),"successful Save publishes menu movement gain");
    }
    private sealed class CursorApi : IModeMenuCursorApi {
        public MenuCursorPosition Value=new(500,400);public MenuArea Display=new(0,0,1000,800);
        public string? Fail;
        public MenuCursorPosition Position(){if(Fail=="read")throw new System.ComponentModel.Win32Exception(5,"injected read");return Value;}
        public MenuArea Area(int x,int y){if(Fail=="display")throw new System.ComponentModel.Win32Exception(87,"injected display");return Display;}
    }
    private static void CursorHealth() {
        var api=new CursorApi();var c=new WindowsModeMenuCursor(api);var p=c.Capture();
        bool Fault(Action action,string expected) {try{action();return false;}catch(IOException e){return e.Message.Contains(expected) && e.Message.Contains("actual=") && e.Message.Contains("display=") && !e.Message.Contains("flags=") && !e.Message.Contains("clip=");}}
        api.Value=new(509,400);Check(c.Read(p)==new Point(509,400),"actual system position is authoritative");
        foreach(string fail in new[]{"read","display"}) {
            api.Value=new(500,400);api.Fail=null;c.Capture();api.Fail=fail;
            Check(Fault(()=>c.Read(p),"win32="),"real API failure contextual diagnostics "+fail);
        }
        api.Fail=null;api.Value=new(2000,700);Check(c.Read(p)==new Point(2000,700),"position beyond the opening monitor is readable without a clip veto");
        api.Value=new(500,400);api.Display=api.Display with {Scale=2};Check(Fault(()=>c.Read(p),"DPI"),"display change fails");
        var names=typeof(WindowsModeMenuCursor).GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static)
            .Select(m=>m.Name).ToHashSet();
        Check(!names.Contains("SetCursorPos") && !names.Contains("ShowCursor") && !names.Contains("ClipCursor") && !names.Contains("SetCapture"),"adapter has no cursor write or constraint submission");
        Check(!names.Contains("GetCursorInfo") && !names.Contains("GetClipCursor"),"adapter no longer reads visibility or clip state");
        Check(typeof(IModeMenuCursor).GetMethods().Select(m=>m.Name).OrderBy(n=>n).SequenceEqual(new[]{"Capture","Read"}),"cursor surface is capture and read only");
    }
    private static void LegacyAppearance() {
        string root=Path.Combine(Path.GetTempPath(),"rightpad-menu-legacy-"+Guid.NewGuid());Directory.CreateDirectory(root);
        string path=Path.Combine(root,"settings.json");
        try {
            var slots=new ModeMenuSlots {Top=new(ModeBindingKind.Keyboard,"F13"),UpperRight=new(ModeBindingKind.Gamepad,"A"),
                LowerRight=new(ModeBindingKind.Keyboard,"F14"),Bottom=new(ModeBindingKind.Gamepad,"LT"),
                LowerLeft=new(ModeBindingKind.Keyboard,"RightCtrl"),UpperLeft=new(ModeBindingKind.Gamepad,"RB")};
            var fixture=RuntimeSettings.Default with {
                SensitivityX=12,SensitivityY=9.5,TapMaxDurationMs=280,TapMovementThresholdPx=6.5,ClickHoldMs=18,
                DoubleTapIntervalMs=140,SmoothingTauMs=20,SmoothingSupportMs=100,TouchMode=TouchMode.C_Z1,
                Cz1=new(160000,12000,30,1,90000,.75,1.5),
                Controls=VirtualControlsSettings.Default with {B=new(.8,3.2,30,450),X=new(13,2.5,1.8,28,380)},
                ModeControl=new ModeBinding(ModeBindingKind.Keyboard,"G",ModePressBehavior.Toggle) {
                    Purpose=ModePurpose.ShortcutMenu,Appearance=ModeMenuAppearance.Ring,Slots=slots }
            };
            var json=JsonSerializer.SerializeToNode(fixture,new JsonSerializerOptions {
                PropertyNamingPolicy=JsonNamingPolicy.CamelCase,IgnoreReadOnlyProperties=true})!.AsObject();
            var mode=json["modeControl"]!.AsObject();
            Check(mode["appearance"]!.GetValue<int>()==0,"fixture starts as current ring");
            mode["appearance"]=1;mode["legacyNote"]="keep";json["unknownRoot"]="keep";
            File.WriteAllText(path,json.ToJsonString());
            var stamp=new DateTime(2026,1,2,3,4,5,DateTimeKind.Utc);File.SetLastWriteTimeUtc(path,stamp);
            byte[] before=File.ReadAllBytes(path);
            var (loaded,warning)=SettingsFileStore.Load(path);
            Check(warning is null,"legacy appearance is accepted without defaults fallback");
            Check(File.ReadAllBytes(path).SequenceEqual(before) && File.GetLastWriteTimeUtc(path)==stamp,"load never rewrites settings");
            var normalized=fixture with {ModeControl=fixture.ModeControl with {Appearance=ModeMenuAppearance.Ring}};
            Check(loaded==normalized,"only appearance normalizes; all other settings retain");
            Check(loaded.ModeControl.Appearance==ModeMenuAppearance.Ring && loaded.ModeControl.Purpose==ModePurpose.ShortcutMenu &&
                loaded.ModeControl.Kind==ModeBindingKind.Keyboard && loaded.ModeControl.Key=="G" &&
                loaded.ModeControl.Behavior==ModePressBehavior.Toggle && loaded.ModeControl.Slots==slots,"mode fields item by item");
            var file=new SettingsFileStore(path);
            Check(file.SaveNowAsync(loaded).GetAwaiter().GetResult(),"normalized settings explicit Save");
            var saved=JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            Check(saved["modeControl"]!["appearance"]!.GetValue<int>()==0,"Save writes ring appearance 0");
            Check(saved["modeControl"]!["legacyNote"]!.GetValue<string>()=="keep" && saved["unknownRoot"]!.GetValue<string>()=="keep","unknown fields retained");
            Check(SettingsFileStore.Load(path).Settings==loaded,"saved other settings unchanged");
        } finally { Directory.Delete(root,true); }
    }
    private static void SettingsWindow() {
        string root=Path.Combine(Path.GetTempPath(),"rightpad-menu-ui-"+Guid.NewGuid());Directory.CreateDirectory(root);
        string path=Path.Combine(root,"settings.json");
        try {
            var model=new SettingsViewModel(new RuntimeSettingsStore(RuntimeSettings.Default),new SettingsFileStore(path));
            var window=new ModeMenuSettingsWindow(model.Mode);
            try {
                window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-10000;window.Top=-10000;
                window.ShowActivated=false;window.ShowInTaskbar=false;window.Show();window.UpdateLayout();
                var nodes=Logical(window).ToList();
                var ids=nodes.OfType<DependencyObject>().Select(AutomationProperties.GetAutomationId).Where(id=>!string.IsNullOrEmpty(id)).ToList();
                Check(ids.Contains("MenuAppearancePreview"),"fixed ring preview remains");
                Check(!ids.Contains("MenuAppearance_Islands") && !ids.Contains("MenuAppearance_Ring"),"no appearance choice buttons");
                Check(nodes.OfType<ModeMenuVisual>().Count()==1 && nodes.OfType<ModeMenuVisual>().Single().Preview,"preview is non-interactive");
                Check(nodes.OfType<Button>().Count(b=>AutomationProperties.GetAutomationId(b).StartsWith("MenuSlot_"))==6,"six editable slots remain");
                var text=nodes.SelectMany(n=>n switch {TextBlock t=>new[]{t.Text},Button b when b.Content is string s=>new[]{s},_=>Array.Empty<string>()});
                Check(!text.Any(s=>s.Contains("Islands",StringComparison.OrdinalIgnoreCase) || s.Contains("六向独立大按钮")),"no retired appearance text");
                string[] keys=["F13","A","F14","LT","RightCtrl","RB"];
                model.Mode.Purpose(ModePurpose.ShortcutMenu);
                for(int i=0;i<6;i++) model.Mode.Slot(i,i%2==0?ModeBindingKind.Keyboard:ModeBindingKind.Gamepad,keys[i]);
                Check(model.HasUnsavedChanges && model.SaveAsync().GetAwaiter().GetResult(),"six-slot draft uses common Save");
                var loaded=SettingsFileStore.Load(path).Settings;
                Check(loaded.ModeControl.Appearance==ModeMenuAppearance.Ring && loaded.ModeControl.Purpose==ModePurpose.ShortcutMenu,"Save keeps fixed ring menu");
                for(int i=0;i<6;i++) Check(loaded.ModeControl.Slots[i].Key==keys[i],"slot "+i+" persisted");
            } finally { window.Close(); }
        } finally { Directory.Delete(root,true); }
    }
    private static IEnumerable<object> Logical(DependencyObject obj) {
        yield return obj;
        foreach(var child in LogicalTreeHelper.GetChildren(obj)) if(child is DependencyObject childObject)
            foreach(var descendant in Logical(childObject)) yield return descendant;
    }
    private static Task Sta(Action action) {
        var done=new TaskCompletionSource();var t=new Thread(()=>{try{action();done.SetResult();}catch(Exception e){done.SetException(e);}finally{Dispatcher.CurrentDispatcher.InvokeShutdown();}});
        t.SetApartmentState(ApartmentState.STA);t.Start();return done.Task;
    }
    private static void Render() {
        string root=Path.Combine(TestPaths.EvidenceDirectory,"render");Directory.CreateDirectory(root);
        foreach(int size in new[]{280,480,720}) {
            var v=new ModeMenuVisual {Width=size,Height=size,State=new(MenuPlacement.Create(new(0,0,size,size,size/480d),size/2d,size/2d),Config(),size/2d,size/2d,-1,-1,0)};
            v.Measure(new(size,size));v.Arrange(new(0,0,size,size));v.UpdateLayout();
            var bitmap=new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32);bitmap.Render(v);
            using var file=File.Create(Path.Combine(root,$"Ring-{size}.png"));var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));encoder.Save(file);
        }
    }
    private static byte[] RenderPixels(ModeMenuSnapshot? state,int width,int height,bool preview=false,string? name=null) {
        var v=new ModeMenuVisual {Width=width,Height=height,State=state,Preview=preview};
        v.Measure(new(width,height));v.Arrange(new(0,0,width,height));v.UpdateLayout();
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(v);
        if(name is not null) {
            string root=Path.Combine(TestPaths.EvidenceDirectory,"render");Directory.CreateDirectory(root);
            using var file=File.Create(Path.Combine(root,name+".png"));
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));encoder.Save(file);
        }
        var pixels=new byte[width*height*4];bitmap.CopyPixels(pixels,width*4,0);return pixels;
    }
    private static void Pixel(byte[] pixels,int width,int x,int y,byte value,string message) {
        int index=(y*width+x)*4;
        Check(pixels[index]==value && pixels[index+1]==value && pixels[index+2]==value && pixels[index+3]==255,message);
    }
    private static void CursorRenderCoordinates() {
        foreach(var (x,y,scale,size) in new[]{(0d,0d,1d,640),(-2400d,-1200d,1.25,640),(1920d,300d,1.5,640),(-3840d,0d,2d,640),(0d,0d,3d,280)}) {
            // Screen pixels -> local DIP: (position - monitor origin) / DPI.
            var area=new MenuArea(x,y,size*scale,480*scale,scale);
            var placement=MenuPlacement.Create(area,x+size*scale/2,y+240*scale);
            var s=new ModeMenuSnapshot(placement,Config(),x+40*scale,y+50*scale,0,-1,1);
            var pixels=RenderPixels(s,size,480,name:$"Cursor-origin-{x}-dpi-{scale}-size-{size}");
            Pixel(pixels,size,40,52,0,"black silhouette starts at observed hotspot after monitor/DPI transform");
            Pixel(pixels,size,43,60,255,"white body at the same DIP offset for every DPI and decoration size");
            Check(pixels[(49*size+40)*4+3]==0 && pixels[(52*size+39)*4+3]==0,"nothing above or left of hotspot");
            Check(pixels[(78*size+51)*4+3]==0 && pixels[(65*size+61)*4+3]==0,"small 20 by 27 DIP silhouette, independent of ring diameter");
        }
    }
    private static void CursorRenderLayers() {
        var p=MenuPlacement.Create(new(0,0,640,640),320,270);
        foreach(var (x,y,label) in new[]{(320d,100d,"tile"),(320d,543d,"caption"),(320d,270d,"neutral")}) {
            var s=new ModeMenuSnapshot(p,Config(),x,y,p.Select(x,y),-1,1);
            var pixels=RenderPixels(s,640,640,name:"Cursor-topmost-"+label);
            Pixel(pixels,640,(int)x,(int)y+2,0,"black border covers "+label);
            Pixel(pixels,640,(int)x+3,(int)y+10,255,"white arrow covers "+label);
        }
        var baseline=new ModeMenuSnapshot(p,Config(),1000,1000,-1,-1,1);
        var outside=RenderPixels(baseline,640,640);
        Check(outside.SequenceEqual(RenderPixels(baseline with {CursorX=641,CursorY=50},640,640)),"outside arrow is naturally clipped, never clamped onto ring or viewport");
        var crossing=RenderPixels(baseline with {CursorX=-5,CursorY=50},640,640,name:"Cursor-edge-clipped");
        Pixel(crossing,640,0,60,255,"visible part of an arrow whose hotspot is outside remains at true position");
    }
    private static void CursorRenderPreview() {
        var p=MenuPlacement.Create(new(-1920,0,960,960,2),-1440,480);
        var s=new ModeMenuSnapshot(p,Config(),-1500,200,-1,-1,1);
        var preview=RenderPixels(s,480,480,true,name:"Cursor-static-preview");
        Check(preview.SequenceEqual(RenderPixels(s with {CursorX=20000,CursorY=-20000},480,480,true)),"preview pixels do not depend on observed cursor");
        Check(RenderPixels(null,480,480).All(b=>b==0),"no menu snapshot means no marker or decoration");
    }
    private static void CursorRetainedUpdates() {
        var p=MenuPlacement.Create(new(-1920,-1080,1280,960,2),-1280,-600);
        var s=new ModeMenuSnapshot(p,Config(),-1840,-980,0,-1,1);
        var v=new ModeMenuVisual {Width=640,Height=480,State=s};
        v.Measure(new(640,480));v.Arrange(new(0,0,640,480));v.UpdateLayout();
        static IEnumerable<GlyphRun> Glyphs(Drawing? drawing) {
            if(drawing is GlyphRunDrawing g) yield return g.GlyphRun;
            if(drawing is DrawingGroup group) foreach(var child in group.Children)
                foreach(var glyph in Glyphs(child))yield return glyph;
        }
        var before=Glyphs(VisualTreeHelper.GetDrawing(v)).ToArray();
        Check(before.Length>0,"initial Ring labels were shaped");
        var arrow=(DrawingVisual)VisualTreeHelper.GetChild(v,0);
        for(int i=0;i<120;i++) {
            s=s with {CursorX=-1840+i*2,CursorY=-980+i,Revision=i+2};v.State=s;v.UpdateLayout();
            var tip=arrow.Transform.Transform(new Point(0,0));
            Check(tip==new Point(40+i,50+i*.5),"every observed snapshot immediately updates hotspot, with no smoothing or queued steps");
        }
        var after=Glyphs(VisualTreeHelper.GetDrawing(v)).ToArray();
        Check(before.SequenceEqual(after),"cursor-only changes retain the already shaped Ring labels");
        v.State=s with {Hovered=1};v.UpdateLayout();
        Check(!before.SequenceEqual(Glyphs(VisualTreeHelper.GetDrawing(v))),"selection change still updates Ring and caption");
        v.Preview=true;Check(arrow.Opacity==0,"preview transition hides the retained marker");
        v.Preview=false;Check(arrow.Opacity==1,"live view restores the current observed marker");
        v.State=null;v.UpdateLayout();
        Check(arrow.Opacity==0 && !Glyphs(VisualTreeHelper.GetDrawing(v)).Any(),"closed snapshot removes marker and labels");
    }
    private static void ObservationCounters() {
        using var h=new Harness();h.Down();h.Up();
        var before=h.Mode.MenuObservation!.Value;
        for(int i=0;i<100;i++)Check(h.Mode.MenuObservation!.Value==before,"reading diagnostics does not read the cursor again");
        h.SetCursor(500,380);
        var after=h.Mode.MenuObservation!.Value;
        Check(after.Reads==before.Reads+1 && after.Changes==before.Changes+1,"one existing refresh recorded once");
        Check(after.LastReadTicks>=before.LastReadTicks && ReferenceEquals(after.Snapshot,h.Mode.MenuSnapshot),"same actual snapshot and monotonic read time");
        h.Observe();var idle=h.Mode.MenuObservation!.Value;
        Check(idle.Reads==after.Reads+1 && idle.Changes==after.Changes,"stationary read distinguished from changed position");
        h.Down();Check(h.Mode.MenuObservation is null,"closed cycle exposes no observation");
    }
    private static void PresentationMetrics() {
        var p=MenuPlacement.Create(new(0,0,640,480),320,240);
        var s=new ModeMenuSnapshot(p,Config(),320,220,0,-1,1);
        var m=new ModeMenuFrameMetrics(1000);
        Check(m.Record(new(s,100,20,1000),1005,1,400) is null,"no per-frame logging");
        Check(m.Record(new(s with {Revision=2},102,22,1020),1021,2) is null,"one second aggregate");
        var a=m.Record(new(s with {Revision=3},200,80,2000),2005,3)!.Value;
        Check(a.Callbacks==3 && a.Reads==100 && a.PositionChanges==60 && a.SnapshotChanges==2,"sampling and callback counts remain distinct");
        Check(a.MaxGapMs==984 && a.MaxReadAgeMs==5 && a.MaxPresentCallMs==3,"callback gap, observed age and preparation cost use their own clocks");
        Check(a.MaxSnapshotFetchMs==400,"snapshot lock/fetch wait is measured separately from update work");
        m.Record(new(s with {Revision=3},250,80,2500),2505,1);
        var b=m.Finish()!.Value;
        Check(b.MaxGapMs==500 && b.Reads==50 && b.PositionChanges==0,"stall across interval boundary is retained and stationary position is not invented");
        Check(m.Finish() is null,"close flush happens at most once");
        m.Record(new(s,1,0,3000),3005,1);var c=m.Finish()!.Value;
        Check(c.MaxGapMs==0 && c.Reads==0 && c.PositionChanges==0,"new opening fences old counters and gaps");
    }
    private static void OverlaySoftwareRendering() {
        var processMode=System.Windows.Media.RenderOptions.ProcessRenderMode;
        var ordinary=new Window {Width=20,Height=20,ShowActivated=false};
        var overlay=new ModeMenuWindow();
        try {
            var ordinaryHandle=new System.Windows.Interop.WindowInteropHelper(ordinary).EnsureHandle();
            var ordinaryTarget=System.Windows.Interop.HwndSource.FromHwnd(ordinaryHandle)!.CompositionTarget;
            var prior=ordinaryTarget.RenderMode;
            var handle=new System.Windows.Interop.WindowInteropHelper(overlay).EnsureHandle();
            Check(overlay.OverlayRenderMode==System.Windows.Interop.RenderMode.SoftwareOnly,"real hidden overlay HWND target is software only");
            Check(ordinaryTarget.RenderMode==prior && System.Windows.Media.RenderOptions.ProcessRenderMode==processMode,"ordinary window and process preference unchanged");
            Check(!overlay.IsVisible && !overlay.ShowActivated && !overlay.Focusable && !overlay.IsHitTestVisible && overlay.Topmost,"test never shows overlay; no-activation and input transparency retained");
        } finally {overlay.Close();ordinary.Close();}
    }
}
