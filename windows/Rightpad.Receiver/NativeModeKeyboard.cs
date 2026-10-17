using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
namespace Rightpad.Receiver;

internal interface IModeKeyboard : IDisposable { void Set(ModeKey key, bool down); }
internal sealed class NativeModeKeyboard : IModeKeyboard
{
    private sealed class KeyboardHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public KeyboardHandle(IntPtr value) : base(true) => SetHandle(value);
        protected override bool ReleaseHandle() { try { return Destroy(handle,null,0)==0; } catch { return false; } }
    }
    private readonly KeyboardHandle handle;
    public NativeModeKeyboard()
    {
        NativeVirtualHidAbi.Validate(NativeVirtualHidAbi.ReadVersion());
        if (Version()!=1) throw new IOException("Mode keyboard extension mismatch (expected 1).");
        var identity=new StringBuilder(2048);var error=new StringBuilder(2048);
        Check(Create(out var pointer,identity,2048,error,2048),error);
        handle=new(pointer);
        if(handle.IsInvalid) throw new IOException("Mode keyboard create returned an invalid handle.");
    }
    public void Set(ModeKey key,bool down)
    {
        ObjectDisposedException.ThrowIf(handle.IsClosed,this);
        var error=new StringBuilder(2048);
        try { Check(SetNative(handle.DangerousGetHandle(),key.VirtualKey,key.ScanCode,down?1:0,error,2048),error); }
        finally { GC.KeepAlive(handle); }
    }
    public void Dispose()
    {
        if(handle.IsClosed) return;
        var error=new StringBuilder(2048);int status;
        try { status=Destroy(handle.DangerousGetHandle(),error,2048); }
        finally { handle.SetHandleAsInvalid();handle.Dispose(); }
        Check(status,error);
    }
    private static void Check(int status,StringBuilder error) { if(status!=0) throw new IOException($"Mode HID keyboard status={status}: {error}"); }
    [DllImport(NativeVirtualHidAbi.Library,EntryPoint="rightpad_vhid_keyboard_api_version",CallingConvention=CallingConvention.Cdecl)] private static extern int Version();
    [DllImport(NativeVirtualHidAbi.Library,EntryPoint="rightpad_vhid_keyboard_create",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Ansi)] private static extern int Create(out IntPtr handle,StringBuilder identity,int identitySize,StringBuilder error,int size);
    [DllImport(NativeVirtualHidAbi.Library,EntryPoint="rightpad_vhid_keyboard_set",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Ansi)] private static extern int SetNative(IntPtr handle,ushort key,ushort scan,int down,StringBuilder error,int size);
    [DllImport(NativeVirtualHidAbi.Library,EntryPoint="rightpad_vhid_keyboard_destroy",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Ansi)] private static extern int Destroy(IntPtr handle,StringBuilder? error,int size);
}
