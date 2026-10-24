using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rightpad.Receiver;

// Append-only: existing numeric values are persisted, so a new kind must never
// be inserted before Unbound/Keyboard/Gamepad.
internal enum ModeBindingKind { Unbound, Keyboard, Gamepad, Mouse }
internal enum ModePressBehavior { Momentary, Toggle }
internal enum ModePurpose { SingleBinding, ShortcutMenu }
// Stable identifiers are the catalog Id strings "left"/"right"/"middle"; this
// enum is the in-process identity shared by binding, session and mouse output.
internal enum ModeMouseButton { Left, Right, Middle }
[JsonConverter(typeof(ModeMenuAppearanceConverter))]
internal enum ModeMenuAppearance { Ring }

// Only the complete ring remains. Value 1 and the historical name are accepted
// when reading an old settings file, normalized in memory to Ring and written
// back as 0; they never select another runtime appearance.
internal sealed class ModeMenuAppearanceConverter : JsonConverter<ModeMenuAppearance>
{
    public override ModeMenuAppearance Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int value) && value is 0 or 1)
            return ModeMenuAppearance.Ring;
        if (reader.TokenType == JsonTokenType.String && reader.GetString() is string name &&
            (name.Equals("Ring", StringComparison.OrdinalIgnoreCase) || name.Equals("Islands", StringComparison.OrdinalIgnoreCase)))
            return ModeMenuAppearance.Ring;
        throw new JsonException("Invalid mode menu appearance.");
    }
    public override void Write(Utf8JsonWriter writer, ModeMenuAppearance value, JsonSerializerOptions options) =>
        writer.WriteNumberValue((int)ModeMenuAppearance.Ring);
}
internal sealed record ModeMenuAction(ModeBindingKind Kind = ModeBindingKind.Unbound, string Key = "")
{
    public ModeKey? Selected => ModeKeys.All.FirstOrDefault(k => k.Kind == Kind && k.Id == Key);
    // Ring slots keep the original keyboard/gamepad/unbound contract. Mouse
    // buttons are a single-binding use only, so a hand-edited slot is invalid.
    public bool IsValid => ModeKeys.IsMenuKind(Kind) && (Kind == ModeBindingKind.Unbound ? Key == "" : Selected is not null);
    public string Name => Selected?.Name ?? "未绑定";
}
// Six named immutable values keep equality, disk merging and slot order explicit.
internal sealed record ModeMenuSlots
{
    public ModeMenuAction Top { get; init; } = new();
    public ModeMenuAction UpperRight { get; init; } = new();
    public ModeMenuAction LowerRight { get; init; } = new();
    public ModeMenuAction Bottom { get; init; } = new();
    public ModeMenuAction LowerLeft { get; init; } = new();
    public ModeMenuAction UpperLeft { get; init; } = new();
    public ModeMenuAction this[int i] => i switch { 0=>Top,1=>UpperRight,2=>LowerRight,3=>Bottom,4=>LowerLeft,5=>UpperLeft,_=>throw new ArgumentOutOfRangeException(nameof(i)) };
    public bool IsValid => Enumerable.Range(0,6).All(i=>this[i]?.IsValid == true);
    public ModeMenuSlots With(int i,ModeMenuAction action) => i switch {
        0=>this with {Top=action},1=>this with {UpperRight=action},2=>this with {LowerRight=action},
        3=>this with {Bottom=action},4=>this with {LowerLeft=action},5=>this with {UpperLeft=action},_=>throw new ArgumentOutOfRangeException(nameof(i)) };
    public static readonly string[] Directions = ["上", "右上", "右下", "下", "左下", "左上"];
}
internal sealed record ModeBinding(ModeBindingKind Kind = ModeBindingKind.Unbound, string Key = "", ModePressBehavior Behavior = ModePressBehavior.Momentary)
{
    public ModePurpose Purpose { get; init; }
    public ModeMenuAppearance Appearance { get; init; }
    public ModeMenuSlots Slots { get; init; } = new();
    public bool IsValid => Enum.IsDefined(Purpose) && Enum.IsDefined(Appearance) && Slots?.IsValid == true && Enum.IsDefined(Kind) && Enum.IsDefined(Behavior) &&
        (Kind == ModeBindingKind.Unbound ? Key == "" : ModeKeys.All.Any(k => k.Kind == Kind && k.Id == Key));
    public ModeKey? Selected => ModeKeys.All.FirstOrDefault(k => k.Kind == Kind && k.Id == Key);
    public string Summary => Purpose == ModePurpose.ShortcutMenu ? "电脑快捷菜单 · 六个按钮" : (Selected?.Name ?? "未绑定") + " · " + (Behavior == ModePressBehavior.Toggle ? "切换按住" : "普通按钮");
}
internal sealed record ModeKey(ModeBindingKind Kind, string Id, string Name, string Group, ushort VirtualKey = 0, ushort ScanCode = 0,
    XboxGamepadState Gamepad = default, ModeMouseButton Mouse = default);

internal static class ModeKeys
{
    public static readonly IReadOnlyList<ModeKey> All = Create().AsReadOnly();
    private static List<ModeKey> Create()
    {
        var keys = new List<ModeKey>();
        void K(string id, string name, int vk, string group, int scan = 0) => keys.Add(new(ModeBindingKind.Keyboard, id, name, group, (ushort)vk, (ushort)scan));
        for (char c = 'A'; c <= 'Z'; c++) K(c.ToString(), c.ToString(), c, "字母");
        for (char c = '0'; c <= '9'; c++) K(c.ToString(), c.ToString(), c, "数字与符号");
        foreach (var k in new (string, string, int)[] { ("Minus","- _",0xBD),("Equal","= +",0xBB),("LeftBracket","[ {",0xDB),("RightBracket","] }",0xDD),
            ("Backslash","\\ |",0xDC),("Semicolon","; :",0xBA),("Quote","' \"",0xDE),("Backquote","` ~",0xC0),("Comma",", <",0xBC),("Period",". >",0xBE),("Slash","/ ?",0xBF),("IntlBackslash","ISO \\ |",0xE2) }) K(k.Item1,k.Item2,k.Item3,"数字与符号");
        foreach (var k in new (string, int)[] { ("Space",0x20),("Enter",0x0D),("Tab",9),("Esc",0x1B),("Backspace",8),
            ("Caps Lock",0x14),("Num Lock",0x90),("Scroll Lock",0x91),("Print Screen",0x2C),("Pause",0x13),("Application",0x5D) }) K(k.Item1,k.Item1,k.Item2,"常用与锁定");
        foreach (var k in new (string, string, int)[] { ("LeftShift","左 Shift",0xA0),("RightShift","右 Shift",0xA1),("LeftCtrl","左 Ctrl",0xA2),("RightCtrl","右 Ctrl",0xA3),
            ("LeftAlt","左 Alt",0xA4),("RightAlt","右 Alt",0xA5),("LeftWin","左 Windows",0x5B),("RightWin","右 Windows",0x5C) }) K(k.Item1,k.Item2,k.Item3,"左右修饰键");
        for (int i=1;i<=24;i++) K("F"+i,"F"+i,0x6F+i,"功能键");
        foreach (var k in new (string,int)[] { ("Left",0x25),("Up",0x26),("Right",0x27),("Down",0x28),("Insert",0x2D),("Delete",0x2E),("Home",0x24),("End",0x23),("Page Up",0x21),("Page Down",0x22) }) K(k.Item1,k.Item1,k.Item2,"方向与导航");
        for(int i=0;i<=9;i++) K("Numpad"+i,"数字小键盘 "+i,0x60+i,"数字小键盘");
        foreach(var k in new (string,string,int)[] { ("NumpadMultiply","数字小键盘 *",0x6A),("NumpadAdd","数字小键盘 +",0x6B),("NumpadSubtract","数字小键盘 -",0x6D),("NumpadDecimal","数字小键盘 .",0x6E),("NumpadDivide","数字小键盘 /",0x6F) }) K(k.Item1,k.Item2,k.Item3,"数字小键盘");
        K("NumpadEnter","数字小键盘 Enter",0x0D,"数字小键盘",0xE01C);
        foreach (var k in new (string,string,XboxGamepadButtons)[] { ("A","A",XboxGamepadButtons.A),("B","B",XboxGamepadButtons.B),("X","X",XboxGamepadButtons.X),("Y","Y",XboxGamepadButtons.Y),
            ("LB","LB",XboxGamepadButtons.LeftShoulder),("RB","RB",XboxGamepadButtons.RightShoulder),("Back","View / Back",XboxGamepadButtons.Back),("Start","Menu / Start",XboxGamepadButtons.Start),
            ("LS","左摇杆按下",XboxGamepadButtons.LeftStick),("RS","右摇杆按下",XboxGamepadButtons.RightStick),("DpadUp","方向键 ↑",XboxGamepadButtons.DpadUp),("DpadDown","方向键 ↓",XboxGamepadButtons.DpadDown),
            ("DpadLeft","方向键 ←",XboxGamepadButtons.DpadLeft),("DpadRight","方向键 →",XboxGamepadButtons.DpadRight) })
            keys.Add(new(ModeBindingKind.Gamepad,k.Item1,k.Item2,"手柄",Gamepad:new(k.Item3)));
        keys.Add(new(ModeBindingKind.Gamepad,"LT","LT（全按 / 零）","手柄",Gamepad:new(LeftTrigger:255)));
        keys.Add(new(ModeBindingKind.Gamepad,"RT","RT（全按 / 零）","手柄",Gamepad:new(RightTrigger:255)));
        // Single-binding mouse buttons: stable Ids, one physical button per binding.
        foreach (var (id,name,button) in new[] {
            ("left","鼠标左键",ModeMouseButton.Left),("right","鼠标右键",ModeMouseButton.Right),("middle","鼠标中键（滚轮按下）",ModeMouseButton.Middle) })
            keys.Add(new(ModeBindingKind.Mouse,id,name,"鼠标",Mouse:button));
        return keys;
    }
    // Ring slots intentionally never accept the single-binding mouse kind.
    internal static bool IsMenuKind(ModeBindingKind kind) =>
        kind is ModeBindingKind.Unbound or ModeBindingKind.Keyboard or ModeBindingKind.Gamepad;
    public static XboxGamepadState Merge(XboxGamepadState a, XboxGamepadState b) => a with {
        Buttons = a.Buttons | b.Buttons, LeftTrigger = Math.Max(a.LeftTrigger,b.LeftTrigger), RightTrigger = Math.Max(a.RightTrigger,b.RightTrigger) };
}
