using System.IO;
using System.Runtime.InteropServices;

namespace Rightpad.Receiver;

internal static class NativeVirtualHidAbi
{
    internal const string Library = "Rightpad.VirtualHid.dll";
    internal const int Version = 2;
    internal static void Validate(int version)
    {
        if (version != Version)
            throw new IOException($"libvirtualhid native bridge ABI/version mismatch (expected {Version}, actual {version}).");
    }
    [DllImport(Library, EntryPoint = "rightpad_vhid_abi_version", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ReadVersion();
}
