using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
namespace Rightpad.Receiver;

internal enum ModeContactPhase : byte { Idle, Down, Up, Cancel }
internal readonly record struct ModeContact(ulong Run, ulong Generation, uint Sequence, uint Contact, ModeContactPhase Phase);
internal static class ModeProtocol
{
    public const int ContactSize=32, ReplySize=32;
    public static bool TryDecode(ReadOnlySpan<byte> b,out ModeContact contact)
    {
        contact=default;
        if(b.Length!=ContactSize || b[0]!=2 || b[1]!=8 || b[26]>3 || b[27..].IndexOfAnyExcept((byte)0)>=0) return false;
        contact=new(BinaryPrimitives.ReadUInt64LittleEndian(b[2..]),BinaryPrimitives.ReadUInt64LittleEndian(b[10..]),
            BinaryPrimitives.ReadUInt32LittleEndian(b[18..]),BinaryPrimitives.ReadUInt32LittleEndian(b[22..]),(ModeContactPhase)b[26]);
        return contact.Generation!=0 && (contact.Phase==ModeContactPhase.Idle ? contact.Sequence==0 && contact.Contact==0 : contact.Sequence!=0);
    }
    public static byte[] Reply(ulong run,ulong generation,uint ack,bool ready=false)
    {
        byte[] b=new byte[ReplySize]; "RPMD"u8.CopyTo(b); b[4]=1;b[5]=1;
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(8),run);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(16),generation);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(24),ack);b[28]=ready?(byte)1:(byte)0;return b;
    }
}

// Generations fence safety breaks. Ordered acknowledgements make retransmitted
// DOWN/UP idempotent and keep the contact history bounded on both peers.
internal sealed class ModeSession : IDisposable
{
    private readonly object gate=new();
    private readonly RuntimeSettingsStore settings;
    private readonly Func<XboxGamepadState,bool> gamepad;
    private readonly Action<ModeMouseButton,bool>? mouse;
    private readonly Func<IModeKeyboard> keyboardFactory;
    private readonly Action<string>? log;
    private readonly Func<long> clock;
    private readonly Action<IPAddress,byte[]> send;
    private readonly Action<IPAddress,ClickFeedback>? haptic;
    private readonly Func<bool> gamepadReady;
    private IModeKeyboard? keyboard;
    private ModeBinding binding;
    private SenderPresence presence=new();
    private static long generationSource=DateTime.UtcNow.Ticks;
    private ulong generation=(ulong)Interlocked.Increment(ref generationSource);
    private ModeContact last;
    private uint ack,contact;
    private bool touching,held,stopped,armed;
    private long seen,replyAt,releaseAt;
    private string? error;
    private bool menuEnvironmentFault;
    private readonly ModeMenu menu;
    private ModeKey? outputKey;
    // One open menu executes at most one slot action; the pulse completes (press
    // then release) before the single best-effort confirmation and the auto-close.
    private bool menuActionAccepted;
    private MenuFeedback? menuFeedback;
    private bool freshTouch;
    private long presentationUntil;
    private readonly record struct MenuFeedback(IPAddress Address,ClickFeedback Click);
    internal Action<bool>? MenuBoundary { get; set; }
    internal ModeMenuSnapshot? MenuSnapshot { get { lock(gate) return menu.Snapshot; } }
    internal ModeMenuObservation? MenuObservation { get { lock(gate) return menu.Observation; } }
    internal bool MenuOpen { get { lock(gate) return menu.Open; } }
    public static readonly TimeSpan Lease=TimeSpan.FromMilliseconds(600);
    public ModeSession(RuntimeSettingsStore settings,Func<XboxGamepadState,bool> gamepad,Action<IPAddress,byte[]> send,
        Func<IModeKeyboard>? keyboardFactory=null,Action<string>? log=null,Func<long>? clock=null,Func<bool>? gamepadReady=null,
        IModeMenuCursor? menuCursor=null,Action<IPAddress,ClickFeedback>? haptic=null,Action<ModeMouseButton,bool>? mouse=null)
    {
        this.settings=settings;this.gamepad=gamepad;this.send=send;this.keyboardFactory=keyboardFactory??(()=>new NativeModeKeyboard());
        this.log=log;this.clock=clock??Stopwatch.GetTimestamp;binding=settings.Current.ModeControl;settings.Published+=Configure;
        this.gamepadReady=gamepadReady??(()=>true);this.haptic=haptic;this.mouse=mouse;
        menu=new(menuCursor??new WindowsModeMenuCursor(),Pulse,UnboundTap,log);
    }
    public string Status { get { lock(gate) return binding.Summary+" · "+(error is not null?"输出失败："+error:stopped?"已停止":!presence.Connected?"未连接":!armed?"等待同步 / 新接触":menu.Open?"菜单已打开 · 再点 Mode 关闭":held?"保持中（已提交）":"已释放"); } }
    internal bool Held { get { lock(gate) return held; } }
    internal ulong Generation { get { lock(gate) return generation; } }
    internal uint Acknowledged { get { lock(gate) return ack; } }
    private void Configure()
    {
        lock(gate) {
            var next=settings.Current.ModeControl;
            if(next==binding || stopped) return;
            Reset("binding_changed");binding=next;error=null;menuEnvironmentFault=false;
        }
    }
    public void UpdatePresence(SenderPresence next)
    {
        lock(gate) {
            if(next.RunId!=presence.RunId || next.RemoteIp!=presence.RemoteIp || next.Connected!=presence.Connected) Reset("presence_change");
            presence=next;
        }
    }
    public void Process(ModeContact packet,IPAddress source,long now)
    {
        lock(gate) {
            TickLocked(now);
            if(stopped || !presence.Connected || packet.Run!=presence.RunId || source.ToString()!=presence.RemoteIp || packet.Generation!=generation) return;
            if(packet.Phase==ModeContactPhase.Idle && ack==0) { armed=true;seen=now;Reply(now,true);return; }
            if(packet.Phase==ModeContactPhase.Cancel) { Reset("cancel");Reply(now,true);return; }
            if(!armed || (error is not null && !menuEnvironmentFault)) return;
            if(packet==last && packet.Sequence==ack) { seen=now;Reply(now,false);return; }
            if(packet.Sequence!=ack+1 || packet.Sequence==0) { Reply(now,true);return; }
            if(packet.Phase==ModeContactPhase.Down) {
                if(touching || packet.Contact!=contact+1 || packet.Contact==0) return;
                if(binding.Purpose==ModePurpose.SingleBinding && held && binding.Behavior==ModePressBehavior.Momentary) return; // Finish the previous short press first.
                contact=packet.Contact;touching=true;
                if(binding.Purpose==ModePurpose.ShortcutMenu) ToggleMenu();
                else if(binding.Behavior==ModePressBehavior.Toggle) SetHeld(!held);
                else SetHeld(true);
            } else if(packet.Phase==ModeContactPhase.Up) {
                if(!touching || packet.Contact!=contact) return;
                touching=false;
                if(binding.Purpose==ModePurpose.SingleBinding && binding.Behavior==ModePressBehavior.Momentary && now>=releaseAt) SetHeld(false);
            } else return;
            last=packet;ack=packet.Sequence;seen=now;
            if(error is not null) Reset("output_error");
            Reply(now,true);
        }
    }
    private void SetHeld(bool next,ModeKey? selected=null)
    {
        if(next==held) return;
        var key=next ? selected??binding.Selected : outputKey;
        if(key is null) return;
        try {
            if(key.Kind==ModeBindingKind.Keyboard) { keyboard??=keyboardFactory();keyboard.Set(key,next); }
            else if(key.Kind==ModeBindingKind.Mouse) {
                // Only this session's contribution changes; the shared mouse device
                // keeps every other source's still-required buttons.
                if(mouse is null) throw new IOException("鼠标按键输出不可用");
                mouse(key.Mouse,next);
            }
            else if(!gamepad(next?key.Gamepad:default)) throw new IOException("Xbox360 backend unavailable / submission failed");
            held=next;outputKey=next?key:null;
            if(next) releaseAt=clock()+Stopwatch.Frequency/40;
            log?.Invoke($"mode_output: binding={key.Id} held={held} behavior={binding.Behavior}");
        } catch(Exception e) {
            menuEnvironmentFault=false; // A device release failure must keep its output latch.
            error=e.Message;
            try { keyboard?.Dispose(); } catch(Exception cleanup) { error+=" | "+cleanup.Message; }
            keyboard=null;held=false;outputKey=null;
            try { gamepad(default); } catch { }
            // Revoke only this session's mouse contribution. A failed attempt may
            // still have reached the device, and the output layer keeps that
            // ambiguous press for its own cleanup release.
            if(key.Kind==ModeBindingKind.Mouse) { try { mouse?.Invoke(key.Mouse,false); } catch { } }
            log?.Invoke("mode_output_error: "+error);
        }
    }
    public void Tick(long now) { lock(gate) TickLocked(now); }
    private void TickLocked(long now)
    {
        if(stopped) return;
        if(menu.Open) {
            try {menu.Refresh();}
            catch(Exception e) {MenuException(e,"maintenance","menu_cursor_error");}
        }
        if(menu.Open && now>=presentationUntil) {error="菜单显示未响应，已恢复正常输入";menuEnvironmentFault=true;Reset("menu_presentation_timeout");}
        if(held && outputKey?.Kind==ModeBindingKind.Gamepad && !gamepadReady()) { Reset("gamepad_unavailable");error="Xbox360 backend unavailable"; }
        if(armed && Stopwatch.GetElapsedTime(seen,now)>=Lease) Reset("lease_expired");
        if(held && now>=releaseAt && (binding.Purpose==ModePurpose.ShortcutMenu || (!touching && binding.Behavior==ModePressBehavior.Momentary))) {
            bool pulse=menu.Open && binding.Purpose==ModePurpose.ShortcutMenu;
            SetHeld(false);
            // A completed menu pulse is the only new feedback source; it never
            // changes the already-completed action and never blocks the close.
            if(pulse && error is null) CompleteMenuAction();
            else if(pulse) Reset("menu_release_error");
        }
        if(error is not null && menu.Open) Reset("menu_output_error");
        Reply(now,false);
    }
    private void Reply(long now,bool immediate)
    {
        if(stopped || !presence.Connected || presence.RunId is not ulong run || presence.RemoteIp is null || (!immediate && now<replyAt)) return;
        replyAt=now+Stopwatch.Frequency/10;
        send(IPAddress.Parse(presence.RemoteIp),ModeProtocol.Reply(run,generation,ack,armed));
    }
    private void Reset(string reason)
    {
        CloseMenu();
        SetHeld(false);
        touching=held=armed=false;ack=contact=0;last=default;releaseAt=replyAt=0;
        generation=(ulong)Interlocked.Increment(ref generationSource);
        log?.Invoke($"mode_reset: reason={reason} generation={generation}");
    }
    private void ToggleMenu()
    {
        if(menu.Open) {CloseMenu();SetHeld(false);return;}
        try {
            // Do not invalidate RPMD here: opening is an ordinary accepted DOWN.
            // The corresponding normal UP keeps the menu open and is acknowledged.
            freshTouch=true;ClearMenuAction();MenuBoundary?.Invoke(true);menu.Show(binding);presentationUntil=clock()+2*Stopwatch.Frequency;
            error=null;menuEnvironmentFault=false;
            log?.Invoke("mode_menu_open");
        } catch(Exception e) {
            error=e.Message;menuEnvironmentFault=true;menu.Close();MenuBoundary?.Invoke(false);
            log?.Invoke($"mode_menu_fault: phase=open closeReason=menu_open_error exception={e.GetType().FullName} message={error}");
        }
    }
    private void CloseMenu()
    {
        ClearMenuAction();
        if(!menu.Open) return;
        menu.Close();freshTouch=true;MenuBoundary?.Invoke(false);log?.Invoke("mode_menu_close");
    }
    private void ClearMenuAction() {menuActionAccepted=false;menuFeedback=null;}
    // Exactly one slot action per opening. The tap identity is captured now so a
    // later presence or configuration change cannot retarget the feedback.
    private void Pulse(ModeKey key,ClickFeedback tap)
    {
        // Ring slots are validated to keyboard/gamepad/unbound only; this second
        // check keeps a future catalog mistake from pulsing a mouse button.
        if(stopped || error is not null || !menu.Open || menuActionAccepted || key.Kind==ModeBindingKind.Mouse) return;
        menuActionAccepted=true;
        menuFeedback=presence.RemoteIp is null ? null : new(IPAddress.Parse(presence.RemoteIp),tap);
        SetHeld(true,key);
        if(error is not null) Reset("menu_output_error");
    }
    private void CompleteMenuAction()
    {
        var feedback=menuFeedback;menuFeedback=null;
        // Feedback is a best-effort side effect of a fully completed pulse: a send
        // or execution failure is logged and can never replay keys or block close.
        if(feedback is { } f) {
            try {haptic?.Invoke(f.Address,f.Click);log?.Invoke($"mode_menu_feedback: senderRunId={f.Click.SenderRunId:X16} sessionId={f.Click.SessionId} upSequence={f.Click.UpSequence} target={f.Address}");}
            catch(Exception e) {log?.Invoke($"mode_menu_feedback_error: {e.Message}");}
        }
        log?.Invoke("mode_menu_action_complete: press+release=completed");
        CloseMenu();
    }
    // A valid tap on an unbound slot emits nothing except the same ordinary-click
    // confirmation, then closes. No key/gamepad/mouse output, no 25 ms hold, no
    // waiting and no output latch; a feedback failure is logged and still closes.
    private void UnboundTap(ClickFeedback tap)
    {
        if(stopped || error is not null || !menu.Open || menuActionAccepted) return;
        menuActionAccepted=true;
        log?.Invoke($"mode_menu_unbound: sessionId={tap.SessionId} upSequence={tap.UpSequence}");
        if(presence.RemoteIp is { } ip && haptic is { } confirm) {
            var address=IPAddress.Parse(ip);
            try {confirm(address,tap);log?.Invoke($"mode_menu_feedback: senderRunId={tap.SenderRunId:X16} sessionId={tap.SessionId} upSequence={tap.UpSequence} target={address}");}
            catch(Exception e) {log?.Invoke($"mode_menu_feedback_error: {e.Message}");}
        }
        log?.Invoke("mode_menu_unbound_complete: unbound selection confirmed");
        CloseMenu();
    }
    internal void CancelTouch() { lock(gate) { menu.CancelTouch(); freshTouch=true; } }
    internal void RouteTouch(TouchPacket packet,RuntimeSettings snapshot,Action movement,Action gestures)
    {
        lock(gate) {
            if(stopped) return;
            if(menu.Open) {
                // Every accepted packet enters the original Motion/HID movement path
                // exactly once. Only ordinary Gesture/button output is withheld, so a
                // menu tap can never leak a left click or double-tap drag.
                movement();
                try {menu.Process(packet,snapshot);}
                catch(Exception e) {MenuException(e,menu.Operation,"menu_navigation_error");}
                return;
            }
            if(freshTouch) {
                if(packet.Header.EventType!=TouchEventType.Down) return;
                freshTouch=false;
            }
            movement();gestures();
        }
    }
    private void MenuException(Exception e,string phase,string reason)
    {
        log?.Invoke($"mode_menu_fault: phase={phase} closeReason={reason} exception={e.GetType().FullName} message={e.Message}");
        error=e.Message;menuEnvironmentFault=true;Reset(reason);
    }
    internal void MenuFault(string message) { lock(gate) {error=message;menuEnvironmentFault=true;Reset("menu_display_error");} }
    internal void MenuPresented() { lock(gate) {if(menu.Open) presentationUntil=clock()+2*Stopwatch.Frequency;} }
    internal void MenuPresentationDiagnostic(string line) {lock(gate) {try {log?.Invoke(line);} catch { /* Diagnostics must not control input. */ }}}
    public void Dispose()
    {
        lock(gate) {
            if(stopped) return;
            settings.Published-=Configure;Reset("stop");stopped=true;
            try {keyboard?.Dispose();} catch(Exception e) {error=e.Message;}
            keyboard=null;
        }
    }
}
