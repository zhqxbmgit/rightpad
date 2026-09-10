using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Rightpad.Receiver;

// Owned only by the Flight Recorder snapshot task; never called from input or WPF polling.
internal sealed class WindowsInputEnvironmentSnapshot(WindowsInputEnvironmentSnapshot.Reads? reads = null)
{
    private readonly Reads native = reads ?? new();
    private uint? cachedPid;
    private ForegroundProcess cachedProcess = new();
    private int? receiverSessionId;
    private bool receiverSessionRead;

    internal sealed record ForegroundProcess(string Name = "Unavailable", string Path = "Unavailable",
        string Integrity = "Unavailable");

    public Dictionary<string, object?> Capture()
    {
        var v = Unavailable(null);
        if (!receiverSessionRead) { receiverSessionId = ReceiverSession(); receiverSessionRead = true; }
        v["receiverSessionId"] = receiverSessionId;
        Observe(() =>
        {
            var p = native.Cursor();
            v["cursorX"] = p.X; v["cursorY"] = p.Y; v["cursorReadSucceeded"] = true;
        }, "cursorReadError");
        Observe(() =>
        {
            var r = native.Clip();
            v["clipLeft"] = r.Left; v["clipTop"] = r.Top;
            v["clipRight"] = r.Right; v["clipBottom"] = r.Bottom; v["clipReadSucceeded"] = true;
        }, "clipReadError");
        Observe(() =>
        {
            var r = native.VirtualScreen();
            v["virtualScreenLeft"] = r.Left; v["virtualScreenTop"] = r.Top;
            v["virtualScreenWidth"] = r.Width; v["virtualScreenHeight"] = r.Height;
        }, "virtualScreenReadError");
        Observe(() =>
        {
            uint pid = native.ForegroundPid();
            if (cachedPid != pid)
            {
                cachedPid = pid;
                cachedProcess = new();
                // Cache Unavailable too: access failures must not cause repeated token/path queries.
                if (pid != 0) try { cachedProcess = native.ProcessInfo(pid); } catch { }
            }
            v["foregroundPid"] = pid == 0 ? null : pid;
            v["foregroundProcessName"] = cachedProcess.Name;
            v["foregroundProcessPath"] = cachedProcess.Path;
            v["foregroundIntegrity"] = cachedProcess.Integrity;
        }, "foregroundReadError");
        Observe(() =>
        {
            v["inputDesktopName"] = native.InputDesktop(); v["inputDesktopReadSucceeded"] = true;
        }, "inputDesktopReadError");
        Observe(() =>
        {
            uint id = native.ActiveConsoleSession();
            v["activeConsoleSessionId"] = id == uint.MaxValue ? null : id;
            if (id == uint.MaxValue) v["activeConsoleSessionReadError"] = "No active console session";
        }, "activeConsoleSessionReadError");
        return v;

        void Observe(Action read, string errorField)
        {
            try { read(); }
            catch (Exception e) { v[errorField] = Error(e); }
        }
    }

    internal static Dictionary<string, object?> Unavailable(Exception? error) => new()
    {
        ["cursorReadSucceeded"] = false, ["cursorX"] = null, ["cursorY"] = null, ["cursorReadError"] = Error(error),
        ["clipReadSucceeded"] = false, ["clipLeft"] = null, ["clipTop"] = null,
        ["clipRight"] = null, ["clipBottom"] = null, ["clipReadError"] = Error(error),
        ["virtualScreenLeft"] = null, ["virtualScreenTop"] = null,
        ["virtualScreenWidth"] = null, ["virtualScreenHeight"] = null, ["virtualScreenReadError"] = Error(error),
        ["foregroundPid"] = null, ["foregroundProcessName"] = "Unavailable",
        ["foregroundProcessPath"] = "Unavailable", ["foregroundIntegrity"] = "Unavailable", ["foregroundReadError"] = Error(error),
        ["inputDesktopReadSucceeded"] = false, ["inputDesktopName"] = null, ["inputDesktopReadError"] = Error(error),
        ["activeConsoleSessionId"] = null, ["activeConsoleSessionReadError"] = Error(error), ["receiverSessionId"] = null
    };
    private static object? Error(Exception? e) => e is Win32Exception w ? w.NativeErrorCode : e?.Message;
    private static int? ReceiverSession()
    {
        try { using var process = Process.GetCurrentProcess(); return process.SessionId; }
        catch { return null; }
    }

    // One small read boundary for failure/cache tests. These APIs never change input state.
    internal class Reads
    {
        public virtual (int X, int Y) Cursor()
        {
            if (!GetCursorPos(out var p)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            return (p.X, p.Y);
        }
        public virtual (int Left, int Top, int Right, int Bottom) Clip()
        {
            if (!GetClipCursor(out var r)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            return (r.Left, r.Top, r.Right, r.Bottom);
        }
        public virtual (int Left, int Top, int Width, int Height) VirtualScreen() =>
            (GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));
        public virtual uint ForegroundPid()
        {
            nint window = GetForegroundWindow();
            if (window == 0) return 0;
            if (GetWindowThreadProcessId(window, out uint pid) == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            return pid;
        }
        public virtual ForegroundProcess ProcessInfo(uint pid)
        {
            string name = "Unavailable", path = "Unavailable", integrity = "Unavailable";
            using var handle = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
            if (!handle.IsInvalid)
            {
                var text = new StringBuilder(32768); int length = text.Capacity;
                if (QueryFullProcessImageNameW(handle, 0, text, ref length))
                {
                    path = text.ToString(); name = System.IO.Path.GetFileNameWithoutExtension(path);
                }
                try { integrity = Integrity(handle); } catch { }
            }
            if (name == "Unavailable")
                try { using var process = Process.GetProcessById(checked((int)pid)); name = process.ProcessName; } catch { }
            return new(name, path, integrity);
        }
        public virtual string InputDesktop()
        {
            nint desktop = OpenInputDesktop(0, false, 0x0001); // DESKTOP_READOBJECTS only
            if (desktop == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            try
            {
                var name = new StringBuilder(512);
                if (!GetUserObjectInformationW(desktop, 2, name, name.Capacity * sizeof(char), out _))
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                return name.ToString();
            }
            finally { CloseDesktop(desktop); }
        }
        public virtual uint ActiveConsoleSession() => WTSGetActiveConsoleSessionId();

        private static string Integrity(SafeProcessHandle process)
        {
            if (!OpenProcessToken(process, 8, out var token)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            using (token)
            {
                GetTokenInformation(token, 25, 0, 0, out int size); // TokenIntegrityLevel
                if (size <= 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                nint buffer = Marshal.AllocHGlobal(size);
                try
                {
                    if (!GetTokenInformation(token, 25, buffer, size, out _))
                        throw new Win32Exception(Marshal.GetLastPInvokeError());
                    string sid = new SecurityIdentifier(Marshal.ReadIntPtr(buffer)).Value;
                    uint rid = uint.Parse(sid[(sid.LastIndexOf('-') + 1)..]);
                    string level = rid switch { < 0x1000 => "Untrusted", < 0x2000 => "Low", < 0x3000 => "Medium",
                        < 0x4000 => "High", < 0x5000 => "System", _ => "Protected" };
                    return $"{level} ({sid})";
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }

        [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClipCursor(out Rect rect);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
        [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder path, ref int length);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int kind, nint buffer, int size, out int needed);
        [DllImport("user32.dll", SetLastError = true)] private static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern bool GetUserObjectInformationW(nint handle, int index, StringBuilder name, int bytes, out int needed);
        [DllImport("user32.dll")] private static extern bool CloseDesktop(nint desktop);
        [DllImport("kernel32.dll")] private static extern uint WTSGetActiveConsoleSessionId();
    }
}
