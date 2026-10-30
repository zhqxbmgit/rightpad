namespace Rightpad.Receiver;

internal sealed record ModeGestureAction(ModeBindingKind Kind = ModeBindingKind.Unbound, string Key = "", bool Menu = false)
{
    public ModeKey? Selected => ModeKeys.All.FirstOrDefault(k => k.Kind == Kind && k.Id == Key);
    public bool IsValid => Enum.IsDefined(Kind) && (Kind == ModeBindingKind.Unbound ? Key == "" : Selected is not null) &&
        (!Menu || Kind == ModeBindingKind.Unbound && Key == "");
    public string Name => Menu ? "打开／关闭电脑快捷菜单" : Selected?.Name ?? "未绑定";
}

internal sealed record ModeGestureSettings
{
    public double FirstWidthDp { get; init; } = 8;
    public double SecondWidthDp { get; init; } = 10;
    public ModeGestureAction Origin { get; init; } = new();
    public ModeGestureAction First { get; init; } = new();
    public ModeGestureAction Second { get; init; } = new();
    public static bool WidthValid(double x) => double.IsFinite(x) && x >= 1 && x <= 50 && x * 2 == Math.Truncate(x * 2);
    public bool IsValid => WidthValid(FirstWidthDp) && WidthValid(SecondWidthDp) && Origin?.IsValid == true &&
        First?.IsValid == true && Second?.IsValid == true && !Origin.Menu && !First.Menu;
    public ModeGestureAction this[int i] => i switch { 0 => Origin, 1 => First, 2 => Second, _ => throw new ArgumentOutOfRangeException(nameof(i)) };
    public ModeGestureSettings With(int i, ModeGestureAction action) => i switch {
        0 => this with { Origin = action }, 1 => this with { First = action }, 2 => this with { Second = action }, _ => throw new ArgumentOutOfRangeException(nameof(i)) };
    public string Summary => $"原地 {Origin.Name} · 一档 {First.Name}（{FirstWidthDp:0.#} dp） · 二档 {Second.Name}（{SecondWidthDp:0.#} dp）";
}

// Actual View coordinates, never display/screenshot dimensions. Insets are the
// display-cutout safe bounds in that same View; transient system bars overlay it.
internal sealed record ModeGeometry(ulong Identity, int Width, int Height, float Density, LayoutRect Mode,
    int Left, int Top, int Right, int Bottom)
{
    public bool IsValid => Identity != 0 && Width is >= 20 and <= 32768 && Height is >= 20 and <= 32768 &&
        float.IsFinite(Density) && Density > 0 && Density <= 16 && Left >= 0 && Top >= 0 && Right <= Width && Bottom <= Height &&
        Right > Left && Bottom > Top && Mode.Valid(Width, Height);
    public string? Error(ModeGestureSettings settings, LayoutRect? rect = null)
    {
        if (!IsValid || !settings.IsValid) return "手机布局／输入坐标空间尚未就绪";
        var r = rect ?? Mode;
        var sides = new List<string>();
        if (r.X < Left) sides.Add("左侧");
        if (r.Y < Top) sides.Add("上侧");
        // Match Android float arithmetic exactly, including fractional dp pixels.
        float end = (float)(r.X + r.Width) + (float)settings.FirstWidthDp * Density + (float)settings.SecondWidthDp * Density;
        if (end > Right) sides.Add("右侧");
        if ((long)r.Y + r.Height > Bottom) sides.Add("下侧");
        return sides.Count == 0 ? null : "Mode 三区域超出手机可用输入范围：" + string.Join("、", sides);
    }
}
