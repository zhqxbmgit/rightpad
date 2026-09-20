using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Rightpad.Receiver;

internal sealed class NativeVirtualHidGamepad : IVirtualHidGamepad
{
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    internal readonly struct NativeState(XboxGamepadState state)
    {
        public readonly ushort Buttons = (ushort)state.Buttons;
        public readonly byte LeftTrigger = state.LeftTrigger, RightTrigger = state.RightTrigger;
        public readonly short LeftThumbX = state.LeftThumbX, LeftThumbY = state.LeftThumbY;
        public readonly short RightThumbX = state.RightThumbX, RightThumbY = state.RightThumbY;
    }
    internal interface IApi
    {
        int AbiVersion();
        int Create(out IntPtr handle, StringBuilder identity, int identitySize, StringBuilder error, int errorSize);
        int SetState(IntPtr handle, in NativeState state, StringBuilder error, int size);
        int Destroy(IntPtr handle, StringBuilder? error, int size);
    }
    private const int BufferSize = 2048;
    private readonly object gate = new();
    private readonly IApi api;
    private readonly GamepadHandle handle;
    public string DeviceIdentity { get; }
    public NativeVirtualHidGamepad() : this(new NativeApi()) { }
    internal NativeVirtualHidGamepad(IApi api)
    {
        this.api = api;
        try
        {
            // Check the common symbol before even resolving/calling any ABI-2 gamepad export.
            NativeVirtualHidAbi.Validate(api.AbiVersion());
            var error = new StringBuilder(BufferSize);
            var identity = new StringBuilder(BufferSize);
            Check(api.Create(out var pointer, identity, BufferSize, error, BufferSize), error);
            handle = new GamepadHandle(api, pointer);
            if (handle.IsInvalid) throw new IOException("libvirtualhid gamepad create returned an invalid handle.");
            DeviceIdentity = identity.ToString();
        }
        catch (Exception e) when (e is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        { throw new IOException("libvirtualhid gamepad bridge load failed; deploy the matching ABI-2 x64 bridge. " + e.Message, e); }
    }
    public void SetState(XboxGamepadState state)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(handle.IsClosed, this);
            var error = new StringBuilder(BufferSize);
            var native = new NativeState(state);
            try { Check(api.SetState(handle.DangerousGetHandle(), in native, error, BufferSize), error); }
            finally { GC.KeepAlive(handle); }
        }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (handle.IsClosed) return;
            var error = new StringBuilder(BufferSize);
            int result;
            try { result = api.Destroy(handle.DangerousGetHandle(), error, BufferSize); }
            finally { handle.SetHandleAsInvalid(); handle.Dispose(); }
            Check(result, error);
        }
    }
    private static void Check(int status, StringBuilder error)
    { if (status != 0) throw new IOException($"libvirtualhid gamepad status={status}: {error}"); }
    private sealed class GamepadHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private readonly IApi api;
        public GamepadHandle(IApi api, IntPtr pointer) : base(true) { this.api = api; SetHandle(pointer); }
        // SafeHandle is the last-resort neutral/destroy path if the owning abstraction is abandoned.
        protected override bool ReleaseHandle()
        {
            try { return api.Destroy(handle, null, 0) == 0; }
            catch { return false; }
        }
    }
    private sealed class NativeApi : IApi
    {
        public int AbiVersion() => NativeVirtualHidAbi.ReadVersion();
        public int Create(out IntPtr handle, StringBuilder identity, int identitySize, StringBuilder error, int errorSize)
            => CreateNative(out handle, identity, identitySize, error, errorSize);
        public int SetState(IntPtr handle, in NativeState state, StringBuilder error, int size) => SetNative(handle, in state, error, size);
        public int Destroy(IntPtr handle, StringBuilder? error, int size) => DestroyNative(handle, error, size);
        [DllImport(NativeVirtualHidAbi.Library, EntryPoint = "rightpad_vhid_gamepad_create", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int CreateNative(out IntPtr handle, StringBuilder identity, int identitySize, StringBuilder error, int errorSize);
        [DllImport(NativeVirtualHidAbi.Library, EntryPoint = "rightpad_vhid_gamepad_set_state", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int SetNative(IntPtr handle, in NativeState state, StringBuilder error, int size);
        [DllImport(NativeVirtualHidAbi.Library, EntryPoint = "rightpad_vhid_gamepad_destroy", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int DestroyNative(IntPtr handle, StringBuilder? error, int size);
    }
}
