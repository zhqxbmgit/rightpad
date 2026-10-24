using System.Windows;
using System.Diagnostics;
using Point = System.Windows.Point;

namespace Rightpad.Receiver;

internal readonly record struct MenuArea(double X, double Y, double Width, double Height, double Scale = 1);
internal sealed record MenuPlacement(MenuArea Area, double CenterX, double CenterY, double Diameter)
{
    public const double NeutralRadiusDip = 12;
    public double NeutralRadius => NeutralRadiusDip * Area.Scale;
    public static MenuPlacement Create(MenuArea area, double x, double y)
    {
        if (!double.IsFinite(area.Scale) || area.Width <= 0 || area.Height <= 0 || area.Scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(area));
        // True opening cursor = visual center = fixed anchor. Clip decoration at edges.
        return new(area, x, y, Math.Min(480 * area.Scale, Math.Min(area.Width, area.Height)));
    }
    public Point SlotCenter(int slot)
    {
        double angle = (slot * 60 - 90) * Math.PI / 180;
        return new(CenterX + Diameter * .34 * Math.Cos(angle), CenterY + Diameter * .34 * Math.Sin(angle));
    }
    public int Select(double x, double y)
    {
        double dx = x - CenterX, dy = y - CenterY;
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) throw new IOException("菜单系统光标位置无效");
        if (dx * dx + dy * dy <= NeutralRadius * NeutralRadius) return -1;
        double clockwise = (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
        // Exactly halfway belongs to the clockwise slot; stabilize trig boundaries.
        clockwise = Math.Round(clockwise, 10, MidpointRounding.AwayFromZero);
        return (int)Math.Floor((clockwise + 30) / 60) % 6;
    }
}

// Coordinates are observations only, never an accumulated navigation target.
internal sealed record ModeMenuSnapshot(MenuPlacement Placement, ModeBinding Configuration, double CursorX, double CursorY,
    int Hovered, int Pressed, long Revision);

// Diagnostics of the existing read path, never an additional position source.
internal readonly record struct ModeMenuObservation(ModeMenuSnapshot Snapshot,long Reads,long Changes,long LastReadTicks);

internal interface IModeMenuCursor
{
    MenuPlacement Capture();
    Point Read(MenuPlacement placement);
}

// Owned by ModeSession's gate. Accepted menu movement reaches the Windows cursor
// only through the original Motion/HID path; this class never writes a position.
// Highlight and tap selection keep observing the actual system cursor so the
// asynchronous Motion output is visible without predicting it.
internal sealed class ModeMenu
{
    private readonly IModeMenuCursor cursor;
    private readonly Action<ModeKey,ClickFeedback> execute;
    private readonly Action<ClickFeedback>? unbound;
    private readonly Action<string>? log;
    private readonly GestureProcessor gestures;
    private ModeMenuSnapshot? snapshot;
    private RuntimeSettings contactSettings = RuntimeSettings.Default;
    private long revision;
    private long reads,changes,lastReadTicks;
    public bool Open => snapshot is not null;
    public ModeMenuSnapshot? Snapshot => snapshot;
    public ModeMenuObservation? Observation=>snapshot is { } s?new(s,reads,changes,lastReadTicks):null;
    internal string Operation { get; private set; } = "capture";
    public ModeMenu(IModeMenuCursor cursor, Action<ModeKey,ClickFeedback> execute, Action<ClickFeedback>? unbound = null, Action<string>? log = null)
    {
        this.cursor = cursor; this.execute = execute; this.unbound = unbound; this.log = log;
        // Ordinary click/drag gestures are deliberately not attached here: a menu
        // tap executes only one slot pulse and can never produce a mouse button.
        gestures = new GestureProcessor((int _,TouchPacket packet) => Click(packet), RuntimeSettings.Default, () => { }, () => { });
    }
    public void Show(ModeBinding binding)
    {
        ClearContact(); var p = cursor.Capture();
        reads=1;changes=0;lastReadTicks=Stopwatch.GetTimestamp();
        snapshot = new(p, binding, p.CenterX, p.CenterY, -1, -1, ++revision);
        log?.Invoke($"mode_menu_anchor: x={p.CenterX} y={p.CenterY} dpi={p.Area.Scale * 96} neutralRadius={p.NeutralRadius}");
    }
    public void Close() { snapshot = null; ClearContact(); }
    private void ClearContact() => gestures.Reset();
    internal void CancelTouch() => ClearContact();
    public void Refresh()
    {
        if (snapshot is not { } s) return;
        Operation = "refresh";
        var p = cursor.Read(s.Placement);
        reads++;lastReadTicks=Stopwatch.GetTimestamp();
        int selected = s.Placement.Select(p.X, p.Y);
        if (selected != s.Hovered) log?.Invoke($"mode_menu_selection: slot={selected} cursor={p.X},{p.Y} anchor={s.Placement.CenterX},{s.Placement.CenterY}");
        if (p.X != s.CursorX || p.Y != s.CursorY || selected != s.Hovered) {
            changes++;
            snapshot = s with { CursorX = p.X, CursorY = p.Y, Hovered = selected, Pressed = -1, Revision = ++revision };
        }
    }
    public void Process(TouchPacket packet, RuntimeSettings settings)
    {
        if (snapshot is null) return;
        if (packet.Header.EventType == TouchEventType.Down) contactSettings = settings;
        // Movement for this accepted packet has already entered the original
        // Motion/HID path exactly once. Only tap qualification is decided here.
        Operation = "tap_detection";
        gestures.Process(packet, contactSettings.TapMaxDurationMs, contactSettings.TapMovementThresholdPx, 25, 50);
        if (packet.Header.EventType == TouchEventType.Up) gestures.Reset();
        Refresh();
    }
    private void Click(TouchPacket packet)
    {
        Operation = "tap_read";
        Refresh();
        if (snapshot is not { } s) return;
        snapshot = s with { Pressed = s.Hovered, Revision = ++revision };
        Operation = "tap_output";
        if (s.Hovered < 0) return; // Center/neutral: no action and no close.
        // The accepted UP packet is the event identity the phone already
        // registered for click feedback, so the existing RPHF correlation and
        // dedupe apply without a new protocol field.
        var h = packet.Header;
        var tap = new ClickFeedback(h.SenderRunId, h.SessionId, h.Sequence);
        if (s.Configuration.Slots[s.Hovered].Selected is { } key) execute(key, tap);
        else unbound?.Invoke(tap);
    }
}
