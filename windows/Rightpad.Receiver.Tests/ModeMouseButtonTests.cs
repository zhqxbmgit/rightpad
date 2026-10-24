using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Rightpad.Receiver;

namespace Rightpad.Receiver.Tests;

// Mode single-binding mouse buttons: selection, configuration compatibility,
// Ring exclusion, press behavior, and the independent source merge over one
// Virtual HID mouse. All output here is a fake; no operating-system input is
// produced and no native device is opened.
internal static class ModeMouseButtonTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> Cases => new (string, Action)[]
    {
        ("Mode mouse catalogue draft save and Ring exclusion", Catalogue),
        ("Mode mouse picker offers three large buttons and only single binding", Picker),
        ("Mode mouse old configuration compatibility and invalid fallback", Configuration),
        ("Mode mouse momentary short tap stationary hold and safety release", Momentary),
        ("Mode mouse toggle dedupe late messages lease and re-sync", Toggle),
        ("Mode mouse left hold survives touchpad click and drag finish", LeftHoldVsTouchpad),
        ("Mode mouse revoke while touchpad drag keeps left button", TouchpadDragVsModeRevoke),
        ("Mode mouse right hold movement and ordinary left click", RightHold),
        ("Mode mouse merged buttons independent release and move preservation", Merge),
        ("Mode mouse cancel lease presence binding stop and dispose", Lifecycle),
        ("Mode mouse output failure bridge mismatch and cleanup", Failure)
    }.Select(entry => (entry.Item1, (Func<Task>)(() => { entry.Item2(); return Task.CompletedTask; })));

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    private static Task Sta(Action action)
    {
        var done = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try { action(); done.SetResult(); }
            catch (Exception exception) { done.SetException(exception); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return done.Task;
    }

    private static IEnumerable<object> Logical(DependencyObject obj)
    {
        yield return obj;
        foreach (var child in LogicalTreeHelper.GetChildren(obj))
            if (child is DependencyObject nested)
                foreach (var descendant in Logical(nested)) yield return descendant;
    }

    // Records every submitted edge. A failing submission is still recorded, which
    // mirrors an ambiguous native report that may or may not have reached the OS.
    private sealed class Output : IMouseOutput
    {
        public readonly List<string> Events = new();
        public string BackendName => "fake";
        public string ButtonSupport { get; set; } = "left,right,middle";
        public MouseOutputStats Stats => default;
        public bool FailAll; public ModeMouseButton? FailButton; public bool FailOnlyDown;
        public void Move(int dx, int dy) => Events.Add($"move {dx} {dy}");
        public void LeftDown() => SetButton(ModeMouseButton.Left, true);
        public void LeftUp() => SetButton(ModeMouseButton.Left, false);
        public void SetButton(ModeMouseButton button, bool down)
        {
            Events.Add($"{button} {(down ? "down" : "up")}");
            if (FailAll || (FailButton == button && (!FailOnlyDown || down))) throw new IOException("injected HID submission failure");
        }
        public void Dispose() => Events.Add("dispose");
        public int Count(string expected) => Events.Count(item => item == expected);
    }

    private sealed class Harness : IDisposable
    {
        public readonly Output Output = new();
        public readonly MouseButtonMerge Merge;
        public readonly RuntimeSettingsStore Store;
        public readonly LeftButtonController Buttons;
        public readonly ModeSession Mode;
        public readonly List<string> Logs = new();
        public long Now = Stopwatch.GetTimestamp();
        public uint Sequence, Contact;
        public ulong Generation;
        public ulong Run = 7;
        public Harness(ModeBinding? binding = null)
        {
            Merge = new(Output);
            Store = new(RuntimeSettings.Default with { ModeControl = binding ?? new(ModeBindingKind.Mouse, "left") });
            Buttons = new(() => Merge.Set(MouseButtonSource.Gesture, ModeMouseButton.Left, true),
                () => Merge.Set(MouseButtonSource.Gesture, ModeMouseButton.Left, false), Logs.Add, () => { });
            Mode = new(Store, _ => true, (_, _) => { }, log: Logs.Add, clock: () => Now,
                mouse: (button, down) => Merge.Set(MouseButtonSource.Mode, button, down));
            Presence(Run, true);
            Arm();
        }
        public void Presence(ulong run, bool connected) { Run = run; Mode.UpdatePresence(new(run, Now, connected, "127.0.0.1")); }
        public void Arm() { Generation = Mode.Generation; Sequence = Contact = 0; Packet(new(Run, Generation, 0, 0, ModeContactPhase.Idle)); }
        public void Packet(ModeContact packet) => Mode.Process(packet, IPAddress.Loopback, Now);
        public ModeContact Down() { var packet = new ModeContact(Run, Generation, ++Sequence, ++Contact, ModeContactPhase.Down); Packet(packet); return packet; }
        public void Up() => Packet(new(Run, Generation, ++Sequence, Contact, ModeContactPhase.Up));
        public void Cancel() => Packet(new(Run, Generation, ++Sequence, Contact, ModeContactPhase.Cancel));
        public void Advance(int milliseconds) { Now += Stopwatch.Frequency * milliseconds / 1000; Mode.Tick(Now); }
        // The ordinary touchpad click path, as the real GestureProcessor invokes it.
        public void Tap() { Buttons.Click(25); Thread.Sleep(60); }
        public void BeginDrag() => Buttons.BeginDrag();
        public void EndDrag() => Buttons.EndDrag();
        public void Dispose() { Buttons.Dispose(); Mode.Dispose(); }
    }

    private static void Catalogue()
    {
        var mouse = ModeKeys.All.Where(key => key.Kind == ModeBindingKind.Mouse).ToArray();
        Check(mouse.Length == 3, "exactly three mouse buttons");
        Check(mouse.Select(key => key.Id).SequenceEqual(new[] { "left", "right", "middle" }), "stable string identifiers");
        Check(mouse.Select(key => key.Mouse).SequenceEqual(new[] { ModeMouseButton.Left, ModeMouseButton.Right, ModeMouseButton.Middle }), "button identity");
        Check(mouse.All(key => key.Group == "鼠标"), "one mouse group");
        Check(ModeKeys.All.Select(key => (key.Kind, key.Id)).Distinct().Count() == ModeKeys.All.Count, "catalog ids unique");
        var right = new ModeBinding(ModeBindingKind.Mouse, "right", ModePressBehavior.Toggle);
        Check(right.IsValid && right.Selected?.Mouse == ModeMouseButton.Right, "single binding accepts mouse");
        Check(right.Summary == "鼠标右键 · 切换按住", "saved summary text: " + right.Summary);
        Check(new ModeBinding(ModeBindingKind.Mouse, "middle").Selected!.Name.Contains("中键"), "middle label");
        // Ring slots keep the keyboard/gamepad/unbound contract.
        Check(!ModeKeys.IsMenuKind(ModeBindingKind.Mouse), "mouse is not a menu kind");
        foreach (var id in new[] { "left", "right", "middle" })
        {
            Check(!new ModeMenuAction(ModeBindingKind.Mouse, id).IsValid, "menu slot rejects " + id);
            Check(!new ModeBinding(ModeBindingKind.Keyboard, "G") { Slots = new ModeMenuSlots { Top = new(ModeBindingKind.Mouse, id) } }.IsValid,
                "whole binding invalid for a mouse slot " + id);
        }
        Check(new ModeMenuAction(ModeBindingKind.Keyboard, "A").IsValid && new ModeMenuAction(ModeBindingKind.Gamepad, "LT").IsValid &&
            new ModeMenuAction().IsValid, "existing menu kinds unchanged");
        Check(Enum.IsDefined((ModeBindingKind)0) && (int)ModeBindingKind.Unbound == 0 && (int)ModeBindingKind.Keyboard == 1 &&
            (int)ModeBindingKind.Gamepad == 2 && (int)ModeBindingKind.Mouse == 3, "append-only kind numbering");
        // Draft and saved summaries stay separate until Save succeeds.
        string root = Path.Combine(Path.GetTempPath(), "rightpad-mode-mouse-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        string path = Path.Combine(root, "settings.json");
        var store = new RuntimeSettingsStore(RuntimeSettings.Default);
        var file = new SettingsFileStore(path);
        var model = new SettingsViewModel(store, file);
        model.Mode.Select(ModeBindingKind.Mouse, "right"); model.Mode.Behavior(ModePressBehavior.Toggle); model.Mode.Purpose(ModePurpose.SingleBinding);
        Check(model.Mode.Draft == right && store.Current.ModeControl == new ModeBinding(), "selection edits draft only");
        Check(model.Mode.Summary == "草稿：鼠标右键 · 切换按住", "draft summary: " + model.Mode.Summary);
        Check(model.Mode.SavedSummary == "已保存：未绑定 · 普通按钮", "saved summary unchanged: " + model.Mode.SavedSummary);
        Check(model.SaveAsync().GetAwaiter().GetResult(), "save");
        Check(store.Current.ModeControl == right, "publish after disk save");
        var json = JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("modeControl");
        Check(json.GetProperty("kind").GetInt32() == 3 && json.GetProperty("key").GetString() == "right" &&
            json.GetProperty("behavior").GetInt32() == 1 && json.GetProperty("purpose").GetInt32() == 0, "stable on-disk shape");    }

    // The selector itself: three large mouse buttons, no slot access, draft-only.
    private static void Picker()
    {
        Sta(() =>
        {
            string root = Path.Combine(Path.GetTempPath(), "rightpad-mode-mouse-picker-" + Guid.NewGuid()); Directory.CreateDirectory(root);
            try
            {
                string path = Path.Combine(root, "settings.json");
                var store = new RuntimeSettingsStore(RuntimeSettings.Default);
                var model = new SettingsViewModel(store, new SettingsFileStore(path));
                var window = new ModeKeyPickerWindow(model.Mode, ModeBindingKind.Mouse);
                try
                {
                    Check(window.Title.Contains("鼠标键"), "picker title: " + window.Title);
                    window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -10000; window.Top = -10000;
                    window.ShowActivated = false; window.ShowInTaskbar = false; window.Show(); window.UpdateLayout();
                    var keys = Logical(window).OfType<Button>().Where(b => AutomationProperties.GetAutomationId(b).StartsWith("ModeKey_")).ToArray();
                    Check(keys.Length == 3, "three mouse buttons: " + keys.Length);
                    Check(keys.Select(AutomationProperties.GetAutomationId).SequenceEqual(new[] { "ModeKey_left", "ModeKey_right", "ModeKey_middle" }),
                        "button order " + string.Join(",", keys.Select(AutomationProperties.GetAutomationId)));
                    Check(keys.All(b => b.MinWidth >= 140 && b.MinHeight >= 64), "large tap targets");
                    Check(keys.Select(b => (string)b.Content).SequenceEqual(new[] { "鼠标左键", "鼠标右键", "鼠标中键（滚轮按下）" }), "clear labels");
                    keys[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(model.Mode.Draft == new ModeBinding(ModeBindingKind.Mouse, "right"), "click selects the right button in the draft");
                    Check(store.Current.ModeControl == new ModeBinding(), "picker never publishes without Save");
                    Check(Logical(window).OfType<Button>().Any(b => (b.Content as string) == "完成选择 · 返回统一 Save"), "unified Save return button");
                }
                finally { window.Close(); }
                // A Ring slot can never be opened for the mouse kind.
                try { new ModeKeyPickerWindow(model.Mode, ModeBindingKind.Mouse, 0); Check(false, "mouse slot picker must be rejected"); }
                catch (ArgumentOutOfRangeException) { }
            }
            finally { Directory.Delete(root, true); }
        }).GetAwaiter().GetResult();
    }

    private static void Configuration()    {
        string root = Path.Combine(Path.GetTempPath(), "rightpad-mode-mouse-config-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        ModeBinding Load(string name, string json)
        {
            string path = Path.Combine(root, name); File.WriteAllText(path, json);
            return SettingsFileStore.Load(path).Settings.ModeControl;
        }
        Check(Load("old.json", "{\"sensitivityX\":9}") == new ModeBinding(), "missing modeControl keeps unbound default");
        Check(Load("k.json", "{\"modeControl\":{\"kind\":1,\"key\":\"LeftCtrl\",\"behavior\":1}}") ==
            new ModeBinding(ModeBindingKind.Keyboard, "LeftCtrl", ModePressBehavior.Toggle), "legacy keyboard unchanged");
        Check(Load("g.json", "{\"modeControl\":{\"kind\":2,\"key\":\"RT\",\"behavior\":0}}") ==
            new ModeBinding(ModeBindingKind.Gamepad, "RT"), "legacy gamepad unchanged");
        Check(Load("m.json", "{\"modeControl\":{\"kind\":3,\"key\":\"middle\",\"behavior\":1}}") ==
            new ModeBinding(ModeBindingKind.Mouse, "middle", ModePressBehavior.Toggle), "mouse binding loads");
        foreach (var (name, json) in new[]
        {
            ("sid.json", "{\"modeControl\":{\"kind\":3,\"key\":\"side\"}}"),
            ("case.json", "{\"modeControl\":{\"kind\":3,\"key\":\"Left\"}}"),
            ("empty.json", "{\"modeControl\":{\"kind\":3,\"key\":\"\"}}"),
            ("behavior.json", "{\"modeControl\":{\"kind\":3,\"key\":\"left\",\"behavior\":7}}"),
            ("kind.json", "{\"modeControl\":{\"kind\":9,\"key\":\"left\"}}"),
            ("number.json", "{\"modeControl\":{\"kind\":3,\"key\":7}}"),
            ("slot.json", "{\"modeControl\":{\"kind\":3,\"key\":\"left\",\"slots\":{\"top\":{\"kind\":3,\"key\":\"middle\"}}}}")
        })
        {
            var loaded = Load(name, json);
            Check(loaded == new ModeBinding(), "invalid configuration falls back to unbound: " + name);
            Check(loaded.Selected is null, "invalid configuration selects no button: " + name);
        }
        // Unrelated settings in the same malformed-binding file are preserved.
        string mixed = Path.Combine(root, "mixed.json");
        File.WriteAllText(mixed, "{\"sensitivityX\":11,\"modeControl\":{\"kind\":3,\"key\":\"side\"}}");
        var mixedLoaded = SettingsFileStore.Load(mixed).Settings;
        Check(mixedLoaded.SensitivityX == 11 && mixedLoaded.ModeControl == new ModeBinding(), "other settings preserved");
        Check(SettingsFileStore.Load(mixed).Warning is not null, "invalid field reported");
        // Unknown fields keep the existing recursive merge contract on an explicit Save.
        string merge = Path.Combine(root, "merge.json");
        File.WriteAllText(merge, "{\"modeControl\":{\"vendorNote\":\"keep\",\"kind\":0},\"unknown\":42}");
        var store = new RuntimeSettingsStore(SettingsFileStore.Load(merge).Settings);
        var model = new SettingsViewModel(store, new SettingsFileStore(merge));
        model.Mode.Select(ModeBindingKind.Mouse, "left");
        Check(model.SaveAsync().GetAwaiter().GetResult(), "save mouse");
        string text = File.ReadAllText(merge);
        Check(text.Contains("vendorNote") && text.Contains("\"unknown\": 42") && store.Current.ModeControl == new ModeBinding(ModeBindingKind.Mouse, "left"),
            "unknown settings preserved around the mouse binding");
        // A failed Save never applies the mouse binding.
        string blocked = Path.Combine(root, "blocked"); Directory.CreateDirectory(blocked);
        var failing = new SettingsViewModel(store, new SettingsFileStore(blocked));
        failing.Mode.Behavior(ModePressBehavior.Toggle);
        Check(!failing.SaveAsync().GetAwaiter().GetResult() && store.Current.ModeControl == new ModeBinding(ModeBindingKind.Mouse, "left") &&
            failing.HasUnsavedChanges, "failed Save keeps the committed binding");
    }

    private static void Momentary()
    {
        using var h = new Harness();
        var down = h.Down();
        Check(h.Mode.Held && h.Output.Events.SequenceEqual(new[] { "Left down" }), "one press edge: " + string.Join(",", h.Output.Events));
        for (int i = 0; i < 20; i++) { h.Advance(100); h.Packet(down); }
        Check(h.Mode.Held && h.Output.Count("Left down") == 1 && h.Output.Count("Left up") == 0, "stationary hold never times out");
        h.Up();
        Check(!h.Mode.Held && h.Output.Count("Left up") == 1, "a hold longer than the minimum releases on normal UP");
        // Extreme short tap: DOWN and UP in the same instant still completes once.
        h.Down(); h.Up();
        Check(h.Mode.Held && h.Output.Count("Left down") == 2 && h.Output.Count("Left up") == 1, "short tap keeps the minimum hold");
        h.Advance(24); Check(h.Mode.Held && h.Output.Count("Left up") == 1, "24 ms is still inside the hold");
        h.Advance(2); Check(!h.Mode.Held && h.Output.Count("Left up") == 2, "release boundary crossed");
        // Safety interruptions bypass the wait.
        h.Down();
        Check(h.Output.Count("Left down") == 3, "pressed again");
        h.Cancel();
        Check(!h.Mode.Held && h.Output.Count("Left up") == 3, "cancel releases immediately");
        Check(h.Mode.Status.Contains("已释放") || h.Mode.Status.Contains("等待同步"), "status after cancel: " + h.Mode.Status);
        h.Arm(); h.Down(); h.Presence(h.Run, false);
        Check(!h.Mode.Held && h.Output.Count("Left up") == 4, "presence safety release");
    }

    private static void Toggle()
    {
        using var h = new Harness(new ModeBinding(ModeBindingKind.Mouse, "right", ModePressBehavior.Toggle));
        var down = h.Down();
        Check(h.Mode.Held && h.Output.Count("Right down") == 1, "toggle presses");
        for (int i = 0; i < 10; i++) h.Packet(down);
        Check(h.Output.Count("Right down") == 1, "repeated DOWN does not toggle twice");
        h.Up();
        for (int i = 0; i < 10; i++) { h.Advance(100); h.Packet(new(7, h.Generation, h.Sequence, h.Contact, ModeContactPhase.Up)); }
        Check(h.Mode.Held && h.Output.Count("Right up") == 0, "normal UP keeps the toggle");
        h.Packet(down);
        Check(h.Output.Count("Right down") == 1, "late old DOWN is not a new toggle");
        Check(h.Mode.Acknowledged == 2, "late old DOWN not acknowledged");
        // A distinct contact toggles off, and the release edge is exactly one.
        h.Down();
        Check(!h.Mode.Held && h.Output.Count("Right up") == 1, "second valid DOWN revokes the hold");
        h.Up();
        // Lease expiry releases a Toggle hold, and re-sync never restores it.
        var token = h.Generation;
        h.Down(); Check(h.Mode.Held && h.Output.Count("Right down") == 2, "re-armed toggle");
        h.Advance(601);
        Check(!h.Mode.Held && h.Output.Count("Right up") == 2, "lease expiry releases");
        Check(h.Mode.Generation != token, "generation changed on lease expiry");
        h.Packet(new(7, token, 5, 3, ModeContactPhase.Down));
        Check(!h.Mode.Held && h.Output.Count("Right down") == 2, "old generation cannot press");
        h.Arm(); h.Down();
        Check(h.Mode.Held && h.Output.Count("Right down") == 3, "fresh contact after sync");
    }

    private static void LeftHoldVsTouchpad()
    {
        using var h = new Harness(new ModeBinding(ModeBindingKind.Mouse, "left", ModePressBehavior.Toggle));
        h.Down(); h.Up();
        Check(h.Mode.Held && h.Output.Count("Left down") == 1, "Mode holds the left button");
        // The physical left button is already down: a tap must not manufacture a
        // release/press pair, and the Mode contribution must survive the tap.
        h.Tap();
        Check(h.Output.Count("Left down") == 1 && h.Output.Count("Left up") == 0, "tap adds no edge while Mode holds left");
        Check(h.Mode.Held && h.Buttons.Failure is null, "tap does not clear the Mode hold");
        h.BeginDrag(); h.EndDrag();
        Check(h.Output.Count("Left up") == 0 && h.Mode.Held, "drag finish does not release the Mode hold");
        Check(h.Merge.ModeMask == 1 && h.Merge.GestureMask == 0 && h.Merge.DeviceMask == 1, "source masks stay explicit");
        h.Down();
        Check(!h.Mode.Held && h.Output.Count("Left up") == 1, "only the Mode revoke releases the shared left button");
        // The touchpad's own click still completes normally after that.
        h.Tap();
        Check(h.Output.Count("Left down") == 2 && h.Output.Count("Left up") == 2, "ordinary click restored");
    }

    private static void TouchpadDragVsModeRevoke()
    {
        using var h = new Harness(new ModeBinding(ModeBindingKind.Mouse, "left", ModePressBehavior.Toggle));
        h.BeginDrag();
        Check(h.Output.Count("Left down") == 1, "drag presses left");
        h.Down(); h.Up();
        Check(h.Mode.Held && h.Output.Count("Left down") == 1, "Mode press adds no second edge");
        h.Down(); h.Up();
        Check(!h.Mode.Held && h.Output.Count("Left up") == 0, "Mode revoke keeps the drag hold");
        Check(h.Merge.DeviceMask == 1, "device still holds left for the drag");
        h.EndDrag();
        Check(h.Output.Count("Left up") == 1, "drag end releases exactly once");
        // A touchpad safety cancel likewise only revokes its own contribution.
        h.Down(); h.Up();
        Check(h.Output.Count("Left down") == 2, "Mode pressed again");
        h.Buttons.CancelPendingAndRelease("menu_enter");
        Check(h.Mode.Held && h.Output.Count("Left up") == 1, "touchpad cancel leaves the Mode contribution");
        h.Down(); h.Up();
        Check(!h.Mode.Held && h.Output.Count("Left up") == 2, "Mode revoke after the cancel");
    }

    private static void RightHold()
    {
        using var h = new Harness(new ModeBinding(ModeBindingKind.Mouse, "right", ModePressBehavior.Toggle));
        h.Down(); h.Up();
        Check(h.Mode.Held && h.Output.Count("Right down") == 1, "right button held");
        h.Output.Move(12, -7); h.Output.Move(3, 4);
        Check(string.Join(",", h.Output.Events).Contains("move 12 -7") && h.Output.Count("Right up") == 0, "movement keeps the right button state");
        h.Tap();
        Check(h.Output.Count("Left down") == 1 && h.Output.Count("Left up") == 1 && h.Output.Count("Right up") == 0,
            "ordinary left click works during the right hold: " + string.Join(",", h.Output.Events));
        Check(h.Mode.Held, "right hold unaffected by the left click");
        h.Cancel();
        Check(!h.Mode.Held && h.Output.Count("Right up") == 1, "cancel releases only the right button");
        Check(h.Merge.DeviceMask == 0, "device fully released");
    }

    private static void Merge()
    {
        var output = new Output();
        var merge = new MouseButtonMerge(output);
        merge.Set(MouseButtonSource.Gesture, ModeMouseButton.Left, true);
        merge.Set(MouseButtonSource.Mode, ModeMouseButton.Left, true);
        Check(output.Events.SequenceEqual(new[] { "Left down" }), "second source adds no press edge");
        merge.Set(MouseButtonSource.Gesture, ModeMouseButton.Left, false);
        Check(output.Count("Left up") == 0, "gesture release keeps the Mode hold");
        merge.Set(MouseButtonSource.Mode, ModeMouseButton.Left, false);
        Check(output.Count("Left up") == 1, "last source releases once");
        // A release with no device state never submits a stray edge.
        merge.Set(MouseButtonSource.Mode, ModeMouseButton.Left, false);
        merge.Set(MouseButtonSource.Gesture, ModeMouseButton.Left, false);
        Check(output.Events.Count == 2, "no redundant release submission");
        // Right and middle are independent and may be held together.
        merge.Set(MouseButtonSource.Mode, ModeMouseButton.Right, true);
        merge.Set(MouseButtonSource.Mode, ModeMouseButton.Middle, true);
        merge.Set(MouseButtonSource.Mode, ModeMouseButton.Right, false);
        Check(output.Events.SequenceEqual(new[] { "Left down", "Left up", "Right down", "Middle down", "Right up" }),
            "independent buttons: " + string.Join(",", output.Events));
        Check(merge.DeviceMask == 0b100, "middle still held");
        merge.Set(MouseButtonSource.Mode, ModeMouseButton.Middle, false);
        Check(output.Count("Middle up") == 1 && merge.DeviceMask == 0, "middle released");
        // The same source pressing twice is idempotent.
        merge.Set(MouseButtonSource.Gesture, ModeMouseButton.Left, true);
        merge.Set(MouseButtonSource.Gesture, ModeMouseButton.Left, true);
        Check(output.Count("Left down") == 2, "same source re-press adds no edge");
        // An ambiguous failed press is still releasable by the same source.
        output.FailButton = ModeMouseButton.Right; output.FailOnlyDown = true;
        try { merge.Set(MouseButtonSource.Mode, ModeMouseButton.Right, true); Check(false, "failure must propagate"); }
        catch (IOException) { }
        Check(merge.DeviceMask == ((1 << 1) | (1 << 0)), "ambiguous right press tracked for cleanup");
        output.FailButton = null;
        merge.Set(MouseButtonSource.Mode, ModeMouseButton.Right, false);
        Check(output.Count("Right up") == 2 && (merge.DeviceMask & (1 << 1)) == 0, "ambiguous press released");
        merge.Set(MouseButtonSource.Gesture, ModeMouseButton.Left, false);
        Check(output.Count("Left up") == 2, "left released");
    }

    private static void Lifecycle()
    {
        // A pending minimum-hold release belongs to its old configuration and can
        // never release the newly bound button.
        using (var pending = new Harness(new ModeBinding(ModeBindingKind.Mouse, "left", ModePressBehavior.Momentary)))
        {
            pending.Down(); pending.Up();
            Check(pending.Mode.Held && pending.Output.Count("Left up") == 0, "short tap release is pending");
            pending.Store.Publish(pending.Store.Current with { ModeControl = new ModeBinding(ModeBindingKind.Mouse, "right", ModePressBehavior.Momentary) });
            Check(!pending.Mode.Held && pending.Output.Count("Left up") == 1, "binding change flushes the pending release once");
            pending.Arm(); var down = pending.Down();
            Check(pending.Output.Count("Right down") == 1, "new binding presses");
            pending.Advance(40);
            Check(pending.Mode.Held && pending.Output.Count("Right up") == 0, "old release deadline cannot release the new button");
            pending.Packet(down); pending.Up(); pending.Advance(30);
            Check(!pending.Mode.Held && pending.Output.Count("Right up") == 1, "new contact completes normally");
        }
        using var h = new Harness(new ModeBinding(ModeBindingKind.Mouse, "left", ModePressBehavior.Toggle));
        h.Down(); h.Up();
        Check(h.Mode.Held, "toggle held");
        // The ordinary Touch cancellation hook only owns the touchpad lifecycle.
        h.Mode.CancelTouch();
        Check(h.Mode.Held && h.Output.Count("Left up") == 0, "Touch contact cancel does not clear a Mode toggle");
        // An unrelated Save does not release, re-press or re-toggle.
        h.Store.Publish(h.Store.Current with { SensitivityX = 13 });
        Check(h.Mode.Held && h.Output.Count("Left down") == 1 && h.Output.Count("Left up") == 0, "unrelated Save is inert");
        h.Store.Publish(h.Store.Current);
        Check(h.Mode.Held && h.Mode.Generation == h.Generation, "identical configuration keeps the generation");
        // A real binding change releases the old output and fences the old contact.
        var previous = h.Generation;
        h.Store.Publish(h.Store.Current with { ModeControl = new ModeBinding(ModeBindingKind.Mouse, "right", ModePressBehavior.Toggle) });
        Check(!h.Mode.Held && h.Output.Count("Left up") == 1 && h.Mode.Generation != previous, "binding change releases and fences");
        h.Packet(new(h.Run, previous, 3, 2, ModeContactPhase.Down));
        Check(!h.Mode.Held && h.Output.Count("Right down") == 0, "old contact not reinterpreted as the new button");
        h.Arm(); h.Down();
        Check(h.Mode.Held && h.Output.Count("Right down") == 1, "next explicit DOWN uses the new binding");
        // Sender run change and presence loss are separate safety interruptions.
        h.Presence(8, true);
        Check(!h.Mode.Held && h.Output.Count("Right up") == 1, "sender run change releases");
        h.Arm(); h.Down();
        Check(h.Output.Count("Right down") == 2, "new run arms again");
        h.Presence(8, false);
        Check(!h.Mode.Held && h.Output.Count("Right up") == 2, "presence loss releases");
        // Dispose releases the remaining contribution.
        h.Presence(8, true); h.Arm(); h.Down();
        Check(h.Output.Count("Right down") == 3, "pressed before dispose");
        h.Mode.Dispose();
        Check(h.Output.Count("Right up") == 3 && h.Mode.Status.Contains("已停止"), "stop releases the mouse contribution");
    }

    private static void Failure()
    {
        // A failed press is truthful, latched, and still cleaned up.
        using (var h = new Harness(new ModeBinding(ModeBindingKind.Mouse, "left", ModePressBehavior.Toggle)))
        {
            h.Output.FailAll = true;
            h.Down();
            Check(!h.Mode.Held && h.Mode.Status.Contains("输出失败"), "failure status: " + h.Mode.Status);
            Check(h.Output.Count("Left down") == 1 && h.Output.Count("Left up") == 1, "ambiguous press released: " + string.Join(",", h.Output.Events));
            h.Output.FailAll = false;
            h.Up(); h.Down();
            Check(!h.Mode.Held && h.Output.Count("Left down") == 1, "output latch blocks later presses");
            Check(h.Logs.Any(line => line.Contains("mode_output_error")), "failure logged");
        }
        // The bridge extension contract: an older bridge is left-only and says so.
        Check(Receiver.NativeVirtualHidMouse.ButtonSupportText(0) == "left" &&
            Receiver.NativeVirtualHidMouse.ButtonSupportText(1) == "left,right,middle", "button support text");
        try { Receiver.NativeVirtualHidMouse.EnsureButtonExtension(0); Check(false, "missing extension must fail"); }
        catch (IOException e) { Check(e.Message.Contains("mouse button extension unavailable") && e.Message.Contains("matching bridge"), "actionable bridge mismatch: " + e.Message); }
        Receiver.NativeVirtualHidMouse.EnsureButtonExtension(1);
        // Output-level right/middle release and cleanup ordering without a device.
        var native = new VirtualHidTests.FakeNative();
        var mouse = new LibVirtualHidMouseOutput(native);
        Check(mouse.ButtonSupport == "left,right,middle", "reported support");
        mouse.SetButton(ModeMouseButton.Right, true);
        mouse.SetButton(ModeMouseButton.Middle, true);
        mouse.Move(4, 5);
        mouse.Dispose();
        Check(native.Events.Select(item => item.Kind).SequenceEqual(new[] { "right-down", "middle-down", "move", "right-up", "middle-up", "dispose" }),
            "every held button released before destroy: " + string.Join(",", native.Events.Select(item => item.Kind)));
        // A failing cleanup reports after destroy instead of silently succeeding.
        native = new() { FailRight = true };
        mouse = new(native);
        try { mouse.SetButton(ModeMouseButton.Right, true); Check(false, "press failure must propagate"); }
        catch (IOException) { }
        try { mouse.Dispose(); Check(false, "cleanup failure must propagate"); }
        catch (IOException e) { Check(e.Message.Contains("cleanup failed"), "cleanup failure reported"); }
        Check(native.Events.Select(item => item.Kind).SequenceEqual(new[] { "right-down", "right-up", "dispose" }), "destroy still ran");
        // A bridge without the extension refuses before any right/middle edge.
        mouse = new(new VirtualHidTests.FakeNative { ButtonSupport = "left" });
        Check(mouse.ButtonSupport == "left", "left-only bridge is visible");
        mouse.Dispose();
    }
}
