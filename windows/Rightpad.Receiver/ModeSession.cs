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
    private readonly Func<IModeKeyboard> keyboardFactory;
    private readonly Action<string>? log;
    private readonly Func<long> clock;
    private readonly Action<IPAddress,byte[]> send;
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
    public static readonly TimeSpan Lease=TimeSpan.FromMilliseconds(600);
    public ModeSession(RuntimeSettingsStore settings,Func<XboxGamepadState,bool> gamepad,Action<IPAddress,byte[]> send,
        Func<IModeKeyboard>? keyboardFactory=null,Action<string>? log=null,Func<long>? clock=null,Func<bool>? gamepadReady=null)
    {
        this.settings=settings;this.gamepad=gamepad;this.send=send;this.keyboardFactory=keyboardFactory??(()=>new NativeModeKeyboard());
        this.log=log;this.clock=clock??Stopwatch.GetTimestamp;binding=settings.Current.ModeControl;settings.Published+=Configure;
        this.gamepadReady=gamepadReady??(()=>true);
    }
    public string Status { get { lock(gate) return binding.Summary+" · "+(error is not null?"输出失败："+error:stopped?"已停止":!presence.Connected?"未连接":!armed?"等待同步 / 新接触":held?"保持中（已提交）":"已释放"); } }
    internal bool Held { get { lock(gate) return held; } }
    internal ulong Generation { get { lock(gate) return generation; } }
    internal uint Acknowledged { get { lock(gate) return ack; } }
    private void Configure()
    {
        lock(gate) {
            var next=settings.Current.ModeControl;
            if(next==binding || stopped) return;
            Reset("binding_changed");binding=next;error=null;
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
            if(!armed || error is not null) return;
            if(packet==last && packet.Sequence==ack) { seen=now;Reply(now,false);return; }
            if(packet.Sequence!=ack+1 || packet.Sequence==0) { Reply(now,true);return; }
            if(packet.Phase==ModeContactPhase.Down) {
                if(touching || packet.Contact!=contact+1 || packet.Contact==0) return;
                if(held && binding.Behavior==ModePressBehavior.Momentary) return; // Finish the previous short press first.
                contact=packet.Contact;touching=true;
                if(binding.Behavior==ModePressBehavior.Toggle) SetHeld(!held);
                else SetHeld(true);
            } else if(packet.Phase==ModeContactPhase.Up) {
                if(!touching || packet.Contact!=contact) return;
                touching=false;
                if(binding.Behavior==ModePressBehavior.Momentary && now>=releaseAt) SetHeld(false);
            } else return;
            last=packet;ack=packet.Sequence;seen=now;
            if(error is not null) Reset("output_error");
            Reply(now,true);
        }
    }
    private void SetHeld(bool next)
    {
        if(next==held || binding.Kind==ModeBindingKind.Unbound) return;
        var key=binding.Selected!;
        try {
            if(key.Kind==ModeBindingKind.Keyboard) { keyboard??=keyboardFactory();keyboard.Set(key,next); }
            else if(!gamepad(next?key.Gamepad:default)) throw new IOException("Xbox360 backend unavailable / submission failed");
            held=next;
            if(next) releaseAt=clock()+Stopwatch.Frequency/40;
            log?.Invoke($"mode_output: binding={key.Id} held={held} behavior={binding.Behavior}");
        } catch(Exception e) {
            error=e.Message;
            try { keyboard?.Dispose(); } catch(Exception cleanup) { error+=" | "+cleanup.Message; }
            keyboard=null;held=false;
            log?.Invoke("mode_output_error: "+error);
        }
    }
    public void Tick(long now) { lock(gate) TickLocked(now); }
    private void TickLocked(long now)
    {
        if(stopped) return;
        if(held && binding.Kind==ModeBindingKind.Gamepad && !gamepadReady()) { Reset("gamepad_unavailable");error="Xbox360 backend unavailable"; }
        if(armed && Stopwatch.GetElapsedTime(seen,now)>=Lease) Reset("lease_expired");
        if(!touching && held && binding.Behavior==ModePressBehavior.Momentary && now>=releaseAt) SetHeld(false);
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
        SetHeld(false);
        touching=held=armed=false;ack=contact=0;last=default;releaseAt=replyAt=0;
        generation=(ulong)Interlocked.Increment(ref generationSource);
        log?.Invoke($"mode_reset: reason={reason} generation={generation}");
    }
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
