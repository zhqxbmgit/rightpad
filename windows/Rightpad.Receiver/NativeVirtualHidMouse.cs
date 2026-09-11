using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Rightpad.Receiver;

internal sealed class NativeVirtualHidMouse : IVirtualHidMouse
{
    private const string Library = "Rightpad.VirtualHid.dll";
    private const int BufferSize = 2048;
    private readonly MouseHandle handle;
    public string DeviceIdentity { get; }

    public NativeVirtualHidMouse()
    {
        try
        {
            if (AbiVersion() != 1) throw new IOException("libvirtualhid native bridge ABI/version mismatch (expected 1).");
            var error = new StringBuilder(BufferSize);
            var identity = new StringBuilder(BufferSize);
            Check(Create(out handle, identity, BufferSize, error, BufferSize), error);
            DeviceIdentity = identity.ToString();
        }
        catch (Exception e) when (e is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        { throw new IOException("libvirtualhid native bridge load failed; deploy the matching x64 bridge and dependencies. " + e.Message, e); }
    }

    public void Move(int dx, int dy)
    {
        var error = new StringBuilder(BufferSize);
        Check(MoveNative(handle, dx, dy, error, BufferSize), error);
    }
    public void LeftDown() => Button(true);
    public void LeftUp() => Button(false);
    private void Button(bool down)
    {
        var error = new StringBuilder(BufferSize);
        Check(down ? DownNative(handle, error, BufferSize) : UpNative(handle, error, BufferSize), error);
    }
    public void Dispose()
    {
        if (handle.IsClosed) return;
        var error = new StringBuilder(BufferSize);
        // Destroy always consumes the handle, even when release/close returns an error.
        int result = Destroy(handle.DangerousGetHandle(), error, BufferSize);
        handle.SetHandleAsInvalid();
        handle.Dispose();
        Check(result, error);
    }
    private static void Check(int status, StringBuilder error)
    { if (status != 0) throw new IOException($"libvirtualhid status={status}: {error}"); }

    private sealed class MouseHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public MouseHandle() : base(true) { }
        protected override bool ReleaseHandle() => Destroy(handle, null, 0) == 0;
    }
    [DllImport(Library, EntryPoint = "rightpad_vhid_abi_version", CallingConvention = CallingConvention.Cdecl)]
    private static extern int AbiVersion();
    [DllImport(Library, EntryPoint = "rightpad_vhid_create", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int Create(out MouseHandle handle, StringBuilder identity, int identitySize, StringBuilder error, int errorSize);
    [DllImport(Library, EntryPoint = "rightpad_vhid_move", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int MoveNative(MouseHandle handle, int dx, int dy, StringBuilder error, int size);
    [DllImport(Library, EntryPoint = "rightpad_vhid_left_down", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int DownNative(MouseHandle handle, StringBuilder error, int size);
    [DllImport(Library, EntryPoint = "rightpad_vhid_left_up", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int UpNative(MouseHandle handle, StringBuilder error, int size);
    [DllImport(Library, EntryPoint = "rightpad_vhid_destroy", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int Destroy(IntPtr handle, StringBuilder? error, int size);
}
