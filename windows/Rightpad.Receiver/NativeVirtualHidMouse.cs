using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Rightpad.Receiver;

internal sealed class NativeVirtualHidMouse : IVirtualHidMouse
{
    private const string Library = "Rightpad.VirtualHid.dll";
    private const int BufferSize = 2048;
    // Additive mouse-button extension. A bridge built before this extension keeps
    // left-button support; right/middle then fail explicitly instead of guessing.
    internal const int RequiredButtonApiVersion = 1;
    private readonly MouseHandle handle;
    private readonly MotionTrace? motionTrace;
    private readonly int buttonApi;
    public string DeviceIdentity { get; }
    public string ButtonSupport => ButtonSupportText(buttonApi);
    internal static string ButtonSupportText(int apiVersion) =>
        apiVersion >= RequiredButtonApiVersion ? "left,right,middle" : "left";
    internal static void EnsureButtonExtension(int apiVersion)
    {
        if (apiVersion < RequiredButtonApiVersion)
            throw new IOException("libvirtualhid mouse button extension unavailable (bridge has no rightpad_vhid_button): deploy the matching bridge. Only the left button can be emitted.");
    }

    public NativeVirtualHidMouse(MotionTrace? motionTrace = null)
    {
        this.motionTrace = motionTrace;
        try
        {
            NativeVirtualHidAbi.Validate(NativeVirtualHidAbi.ReadVersion());
            buttonApi = ProbeButtonApi();
            var error = new StringBuilder(BufferSize);
            var identity = new StringBuilder(BufferSize);
            Check(Create(out handle, identity, BufferSize, error, BufferSize), error);
            DeviceIdentity = identity.ToString();
        }
        catch (Exception e) when (e is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        { throw new IOException("libvirtualhid native bridge load failed; deploy the matching x64 bridge and dependencies. " + e.Message, e); }
    }

    private static int ProbeButtonApi()
    {
        try { return ReadButtonApiVersion(); }
        catch (EntryPointNotFoundException) { return 0; } // Pre-extension bridge: left only.
    }

    public void Move(int dx, int dy)
    {
        var error = new StringBuilder(BufferSize);
        long begin = motionTrace is null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();
        int result;
        try { result = MoveNative(handle, dx, dy, error, BufferSize); }
        finally
        {
            if (motionTrace is not null)
            {
                long end = System.Diagnostics.Stopwatch.GetTimestamp();
                motionTrace.Write(MotionEventKind.NativeBegin, begin, x: dx, y: dy);
                motionTrace.Write(MotionEventKind.NativeEnd, end, x: dx, y: dy);
            }
        }
        Check(result, error);
    }
    public void LeftDown() => SetButton(ModeMouseButton.Left, true);
    public void LeftUp() => SetButton(ModeMouseButton.Left, false);
    public void SetButton(ModeMouseButton button, bool down)
    {
        var error = new StringBuilder(BufferSize);
        if (button == ModeMouseButton.Left)
        {
            // Old entry points stay authoritative for the existing left path.
            Check(down ? DownNative(handle, error, BufferSize) : UpNative(handle, error, BufferSize), error);
            return;
        }
        EnsureButtonExtension(buttonApi);
        Check(ButtonNative(handle, (int)button, down ? 1 : 0, error, BufferSize), error);
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
    [DllImport(Library, EntryPoint = "rightpad_vhid_create", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int Create(out MouseHandle handle, StringBuilder identity, int identitySize, StringBuilder error, int errorSize);
    [DllImport(Library, EntryPoint = "rightpad_vhid_move", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int MoveNative(MouseHandle handle, int dx, int dy, StringBuilder error, int size);
    // Bridge button codes: 0 left, 1 right, 2 middle; mirror ModeMouseButton.
    [DllImport(Library, EntryPoint = "rightpad_vhid_button", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int ButtonNative(MouseHandle handle, int button, int down, StringBuilder error, int size);
    [DllImport(Library, EntryPoint = "rightpad_vhid_left_down", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int DownNative(MouseHandle handle, StringBuilder error, int size);
    [DllImport(Library, EntryPoint = "rightpad_vhid_left_up", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int UpNative(MouseHandle handle, StringBuilder error, int size);
    [DllImport(Library, EntryPoint = "rightpad_vhid_destroy", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int Destroy(IntPtr handle, StringBuilder? error, int size);
    [DllImport(Library, EntryPoint = "rightpad_vhid_mouse_api_version", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ReadButtonApiVersion();
}
