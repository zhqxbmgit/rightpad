namespace Rightpad.Receiver;

internal enum ModeBindingKind { Unbound, Keyboard, Gamepad }
internal enum ModePressBehavior { Momentary, Toggle }
internal sealed record ModeBinding(ModeBindingKind Kind = ModeBindingKind.Unbound, string Key = "", ModePressBehavior Behavior = ModePressBehavior.Momentary)
{
    public bool IsValid => Enum.IsDefined(Kind) && Enum.IsDefined(Behavior) &&
        (Kind == ModeBindingKind.Unbound ? Key == "" : ModeKeys.All.Any(k => k.Kind == Kind && k.Id == Key));
    public ModeKey? Selected => ModeKeys.All.FirstOrDefault(k => k.Kind == Kind && k.Id == Key);
    public string Summary => (Selected?.Name ?? "未绑定") + " · " + (Behavior == ModePressBehavior.Toggle ? "切换按住" : "普通按钮");
}
internal sealed record ModeKey(ModeBindingKind Kind, string Id, string Name, string Group, ushort VirtualKey = 0, ushort ScanCode = 0,
    XboxGamepadState Gamepad = default);

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
        return keys;
    }
    public static XboxGamepadState Merge(XboxGamepadState a, XboxGamepadState b) => a with {
        Buttons = a.Buttons | b.Buttons, LeftTrigger = Math.Max(a.LeftTrigger,b.LeftTrigger), RightTrigger = Math.Max(a.RightTrigger,b.RightTrigger) };
}
