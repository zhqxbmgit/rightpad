using System.ComponentModel;
using System.Runtime.InteropServices;
using Point = System.Windows.Point;

namespace Rightpad.Receiver;

internal readonly record struct MenuCursorPosition(int X, int Y);
internal interface IModeMenuCursorApi
{
    MenuCursorPosition Position();
    MenuArea Area(int x, int y);
}

// Read-only system-cursor adapter. Menu movement itself is submitted by the
// original Motion/HID mouse path; this adapter only captures the opening anchor
// and observes the real system position plus display validity.
internal sealed class WindowsModeMenuCursor : IModeMenuCursor
{
    private readonly IModeMenuCursorApi? api;
    public WindowsModeMenuCursor(IModeMenuCursorApi? api=null) { this.api=api; }
    private string stage="none", actual="unknown", display="unknown";
    private Exception Diagnose(Exception e) => new IOException($"stage={stage} actual={actual} display={display} exception={e.GetType().FullName} win32={(e is Win32Exception w?w.NativeErrorCode:0)} message={e.Message}", e);
    public MenuPlacement Capture()
    {
        stage="capture";
        try {
        var p = Position();
        var placement = MenuPlacement.Create(Area(p), p.X, p.Y);
        Validate(placement, p);
        return placement;
        } catch(Exception e) {throw Diagnose(e);}
    }
    public Point Read(MenuPlacement placement)
    {
        stage="read";
        try {var p = Position(); Validate(placement, p); return new(p.X, p.Y);}
        catch(Exception e) {throw Diagnose(e);}
    }
    private NativePoint Position()
    {
        actual="unavailable";
        NativePoint point;
        if(api is not null) {
            var injected=api.Position();point=new NativePoint {X=injected.X,Y=injected.Y};
        } else if (!GetCursorPos(out point)) throw new Win32Exception(Marshal.GetLastWin32Error(), "菜单系统光标位置读取失败");
        actual=$"{point.X},{point.Y}";
        // Visibility and clip are not availability: hidden/suppressed cursors still
        // have a real position, and a ClipCursor constraint does not invalidate the
        // coordinate. Only an actual read or display change closes the menu.
        return point;
    }
    private MenuArea Area(NativePoint point)
    {
        if(api is not null) return api.Area(point.X,point.Y);
        var monitor = MonitorFromPoint(point, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) throw new Win32Exception(Marshal.GetLastWin32Error(), "菜单显示区域读取失败");
        int result = GetDpiForMonitor(monitor, 0, out uint dpi, out _);
        if (result != 0 || dpi == 0) throw new IOException($"菜单显示 DPI 读取失败 HRESULT=0x{result:X8} dpi={dpi}");
        var r = info.Monitor;
        return new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, dpi / 96d);
    }
    private void Validate(MenuPlacement placement, NativePoint point)
    {
        // Monitor and DPI stay attached to the opening anchor across navigation.
        var anchor = new NativePoint { X = (int)placement.CenterX, Y = (int)placement.CenterY };
        var current=Area(anchor);display=$"opening={placement.Area} current={current}";
        if (current != placement.Area) throw new IOException("菜单显示区域或 DPI 已变化；已退出菜单");
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
}
