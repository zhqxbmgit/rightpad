using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace Rightpad.Receiver;

public sealed class RemoteLayoutView : UserControl
{
    private readonly RemoteLayoutDraft draft = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly Func<RemoteLayoutClient?> getClient;
    private readonly Func<long> timestamp;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Width = 260, FontSize = 13, Margin = new(10, 2, 12, 2) };
    private readonly TextBlock geometry = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(2, 12, 0, 0) };
    private readonly Canvas preview = new() { Background = Brushes.Black, Height = 0, Width = 0 };
    private readonly Grid previewViewport = new() { Margin = new(42, 2, 42, 2) };
    private readonly Grid body = new() { Margin = new(0, 6, 0, 4) };
    private readonly Grid editingGroup = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly WrapPanel selection = new();
    private readonly TextBlock[] values = new TextBlock[4];
    private readonly List<Button> editors = [];
    private readonly Button tryButton, keepButton, revertButton, discardButton;
    private readonly TextBlock stepText = new() { Width = 76, FontSize = 12, TextAlignment = TextAlignment.Center };
    private int step = 1;
    private long lastAt;
    private bool refreshRequested;
    private string notice = "";
    private string selectionKey = "", previewKey = "";
    private byte pendingKind;
    private RemoteLayoutClient? Client => getClient();

    internal RemoteLayoutView(Func<RemoteLayoutClient?> getClient, Action returnToReceiver, Func<long>? timestamp = null)
    {
        this.getClient = getClient;
        this.timestamp = timestamp ?? Stopwatch.GetTimestamp;
        var root = new Grid { Margin = new(12, 8, 12, 8) }; Content = root;
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new());
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        System.Windows.Automation.AutomationProperties.SetAutomationId(status, "RemoteLayoutStatus");
        var actions = new WrapPanel(); root.Children.Add(actions);
        actions.Children.Add(Button("返回 Receiver", returnToReceiver));
        actions.Children.Add(status);
        actions.Children.Add(Button("刷新手机现状", () => { refreshRequested = true; Client?.Poll(this.timestamp(), true); }));
        discardButton = Button("放弃修改", () => {
            var s = Client?.Capture(this.timestamp()).Snapshot;
            if (s is not null) draft.Load(s);
            else if (draft.Basis is not null) draft.Load(draft.Basis);
            notice = "已放弃电脑草稿"; Render();
        });
        actions.Children.Add(discardButton);
        tryButton = Button("试用布局", () => Begin(RemoteLayoutProtocol.Try)); actions.Children.Add(tryButton);
        keepButton = Button("保留此布局", () => Begin(RemoteLayoutProtocol.Keep)); actions.Children.Add(keepButton);
        revertButton = Button("立即恢复原布局", () => Begin(RemoteLayoutProtocol.Revert)); actions.Children.Add(revertButton);
        Grid.SetRow(body, 1); root.Children.Add(body); body.Children.Add(editingGroup);
        editingGroup.ColumnDefinitions.Add(new()); editingGroup.ColumnDefinitions.Add(new() { Width = new(284) });
        var previewFrame = new Border { Child = preview, BorderBrush = new SolidColorBrush(Color.FromRgb(103, 110, 120)),
            BorderThickness = new(1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        // Frame and callout gutters are outside the mapped phone Canvas.
        previewViewport.Children.Add(previewFrame); editingGroup.Children.Add(previewViewport);
        body.SizeChanged += (_, _) => Render();
        previewViewport.SizeChanged += (_, _) => Render();
        var fields = new StackPanel { Margin = new(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(fields, 1); editingGroup.Children.Add(fields); fields.Children.Add(selection);
        string[] names = ["X", "Y", "宽度", "高度"];
        for (int i = 0; i < 4; i++)
        {
            int field = i; var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = names[i], Width = 45, VerticalAlignment = VerticalAlignment.Center });
            var minus = Button("−", () => Adjust(field, -step)); var plus = Button("+", () => Adjust(field, step));
            minus.Width = plus.Width = 56; editors.Add(minus); editors.Add(plus);
            System.Windows.Automation.AutomationProperties.SetAutomationId(minus, "Layout" + names[i] + "Minus");
            System.Windows.Automation.AutomationProperties.SetAutomationId(plus, "Layout" + names[i] + "Plus");
            values[i] = new TextBlock { Width = 70, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 18 };
            row.Children.Add(minus); row.Children.Add(values[i]); row.Children.Add(plus); fields.Children.Add(row);
        }
        var steps = new WrapPanel { Margin = new(0, 8, 0, 0) };
        foreach (var (label, number) in new[] { ("1 px", 1), ("10 px", 10), ("50 px", 50) })
            steps.Children.Add(Button(label, () => { step = number; Render(); }));
        steps.Children.Add(stepText); fields.Children.Add(steps); fields.Children.Add(geometry);
        var help = new TextBlock { Text = "黑色：鼠标区域　绿色：可编辑　白框：选中　橙色：固定工具。仅电脑草稿；试用后生效，60 秒未保留自动恢复。", FontSize = 12, TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(help, 2); root.Children.Add(help);
        Unloaded += (_, _) => SetPollingActive(false);
        timer.Tick += (_, _) => Tick();
    }
    internal void SetPollingActive(bool active)
    {
        if (!active) {
            long now = timestamp();
            var client = Client;
            client?.Suspend(now);
            if (timer.IsEnabled) { timer.Stop(); client?.Poll(now, true); }
            return;
        }
        if (active == timer.IsEnabled) return;
        // Reopening queries actual state without discarding a draft or creating a new trial.
        Client?.Poll(timestamp(), true);
        Tick(); timer.Start();
    }
    private static Button Button(string label, Action action)
    {
        var b = new Button { Content = label, MinHeight = 42, MinWidth = 48, Margin = new(2), Padding = new(8, 4, 8, 4) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(b, "Layout" + label);
        b.Click += (_, _) => action(); return b;
    }
    private void Adjust(int field, int delta)
    {
        notice = draft.Adjust(field, delta) ? "草稿未应用：手机布局未改变" : "已到边界：最小尺寸 20 px，矩形须在 View 内";
        Render();
    }
    private void Begin(byte kind)
    {
        var basis = kind == RemoteLayoutProtocol.Try ? draft.Basis : Client?.Capture(timestamp()).Snapshot;
        if (basis is null || Client?.Begin(kind, basis, draft.Items, timestamp()) != true)
        { notice = "操作未开始：请刷新手机现状后重试"; Render(); return; }
        pendingKind = kind;
        notice = kind == RemoteLayoutProtocol.Try ? "等待手机实际应用" : kind == RemoteLayoutProtocol.Keep ? "正在保存：等待手机写盘结果" : "等待手机恢复原布局";
        Render();
    }
    private void Tick()
    {
        Client?.Poll(timestamp());
        var captured = Client?.Capture(timestamp());
        if (captured is { Snapshot: { } s } && captured.Value.At != lastAt)
        {
            lastAt = captured.Value.At;
            if (refreshRequested) { draft.Load(s); refreshRequested = false; notice = "已读取手机实际布局"; }
            else draft.Observe(s);
            if (!captured.Value.Uncertain) { pendingKind = 0; notice = ""; }
        }
        Render();
    }
    private void Render()
    {
        var captured = Client?.Capture(timestamp()); var s = captured?.Snapshot;
        bool active = s?.State is 1 or 2;
        bool editable = s is not null && !active && pendingKind == 0 && !draft.Conflict;
        tryButton.IsEnabled = editable && draft.Dirty && draft.Basis is not null;
        keepButton.IsEnabled = s?.State == 2 && captured?.Remaining > 0 && pendingKind == 0;
        revertButton.IsEnabled = active && pendingKind != RemoteLayoutProtocol.Revert;
        discardButton.IsEnabled = !active && pendingKind == 0;
        foreach (var b in editors) b.IsEnabled = editable;
        string state = pendingKind switch {
            RemoteLayoutProtocol.Try => "等待手机应用",
            RemoteLayoutProtocol.Keep => "正在保存",
            RemoteLayoutProtocol.Revert => "等待手机恢复",
            _ => s?.State switch {
                0 => "已读取布局", 1 => "等待手机应用", 2 => $"试用中 · 剩余 {Math.Ceiling((captured?.Remaining ?? 0) / 1000.0)} 秒",
                3 => "已保存", 4 => "已恢复原布局", 5 => "操作失败",
                _ => captured?.Connected == true ? "等待手机响应，操作禁用" : "未连接，操作禁用" }
        };
        if (editable && draft.Dirty) state = "草稿未应用";
        string error = s?.Error switch { 1 => "版本过期，请刷新并放弃旧草稿", 2 => "整份布局校验失败（边界、完整性或鼠标区域）", 3 => "手机布局编辑忙，请稍后刷新", 4 => "手机写盘失败，已恢复原正式布局", 5 => "试用已到期", 6 => "试用已取消", _ => "" };
        status.Text = (captured?.Connected == true ? "已连接 · " : "") + state + (captured?.Uncertain == true ? " · 结果待确认" : "") +
            (draft.Conflict ? "\n手机版本或几何已改变：请刷新现状，旧草稿禁止应用" : "") +
            (error.Length > 0 ? "\n" + error : "");
        status.ToolTip = notice;
        stepText.Text = "步长 " + step + " px";
        var r = draft.Selection?.Rect;
        int[] numbers = r is null ? [0, 0, 0, 0] : [r.X, r.Y, r.Width, r.Height];
        for (int i = 0; i < 4; i++) values[i].Text = r is null ? "—" : numbers[i].ToString();
        string nextSelectionKey = draft.Selected + string.Join(";", draft.Items.Select(i => i.Id + i.Label));
        if (selectionKey != nextSelectionKey)
        {
            selectionKey = nextSelectionKey;
            selection.Children.Clear();
            foreach (var item in draft.Items)
            {
                var button = Button((draft.Selected == item.Id ? "● " : "") + item.Label, () => { draft.Selected = item.Id; Render(); });
                selection.Children.Add(button);
            }
        }
        DrawPreview(s);
    }
    private void DrawPreview(LayoutMessage? s)
    {
        double availableWidth = Math.Max(0, body.ActualWidth);
        double idealWidth = s is null ? availableWidth : Math.Max(0, body.ActualHeight - 6) * s.Width / s.Height + 86 + 284;
        editingGroup.Width = Math.Min(availableWidth, idealWidth);
        string nextPreviewKey = s is null ? "offline" : $"{previewViewport.ActualWidth}:{previewViewport.ActualHeight}:{s.Epoch}:{s.Geometry}:{s.Revision}:{s.Width}:{s.Height}:" + draft.Selected + string.Join(";", draft.Items.Concat(s.Items.Where(i => i.Flags == 1)).Select(i => i.ToString()));
        if (nextPreviewKey == previewKey) return;
        previewKey = nextPreviewKey;
        preview.Children.Clear();
        if (s is null) { geometry.Text = "尺寸未知；预览不代表实时布局"; return; }
        double scale = Math.Max(0, Math.Min((previewViewport.ActualWidth - 2) / s.Width, (previewViewport.ActualHeight - 2) / s.Height));
        preview.Width = s.Width * scale; preview.Height = s.Height * scale;
        geometry.Text = $"View {s.Width} × {s.Height} px · 版本 {s.Revision} · 几何 {s.Geometry}";
        var callouts = new List<(TextBlock Label, double X, double Y, bool Left)>();
        foreach (var item in draft.Items.Concat(s.Items.Where(i => i.Flags == 1)))
        {
            var rect = item.Rect;
            var label = new TextBlock { Text = item.Label, Foreground = Brushes.White, TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, FontSize = 14, TextWrapping = TextWrapping.NoWrap };
            label.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
            bool outside = rect.Width * scale < label.DesiredSize.Width + 6 || rect.Height * scale < label.DesiredSize.Height + 4;
            var tile = new Border { Width = rect.Width * scale, Height = rect.Height * scale,
                Background = item.Flags == 1 ? Brushes.DarkOrange : item.Id == draft.Selected ? Brushes.SeaGreen : Brushes.DarkGreen,
                BorderBrush = Brushes.White, BorderThickness = new(item.Id == draft.Selected ? 2 : 0.5),
                CornerRadius = new(item.Flags == 1 ? rect.Width * scale / 2 : 0),
                Child = outside ? null : label };
            Canvas.SetLeft(tile, rect.X * scale); Canvas.SetTop(tile, rect.Y * scale);
            if (item.Flags == 0) tile.MouseLeftButtonUp += (_, e) => { draft.Selected = item.Id; Render(); e.Handled = true; };
            preview.Children.Add(tile);
            if (outside)
            {
                // Callouts describe small controls without enlarging their real mapped rectangles.
                bool left = rect.X + rect.Width / 2.0 < s.Width / 2.0;
                double middle = (rect.Y + rect.Height / 2.0) * scale;
                callouts.Add((label, (left ? rect.X : rect.X + rect.Width) * scale, middle, left));
            }
        }
        foreach (var side in callouts.GroupBy(c => c.Left))
        {
            var ordered = side.OrderBy(c => c.Y).ToArray();
            var tops = new double[ordered.Length];
            for (int i = 0; i < ordered.Length; i++)
                tops[i] = Math.Max(i == 0 ? 0 : tops[i - 1] + ordered[i - 1].Label.DesiredSize.Height + 3,
                    ordered[i].Y - ordered[i].Label.DesiredSize.Height / 2);
            for (int i = ordered.Length - 1; i >= 0; i--)
                tops[i] = Math.Min(tops[i], (i == ordered.Length - 1 ? preview.Height : tops[i + 1] - 3) - ordered[i].Label.DesiredSize.Height);
            for (int i = 0; i < ordered.Length; i++)
            {
                var c = ordered[i];
                double top = tops[i];
                preview.Children.Add(new Line { X1 = c.X, X2 = c.Left ? -6 : preview.Width + 6,
                    Y1 = c.Y, Y2 = top + c.Label.DesiredSize.Height / 2,
                    Stroke = Brushes.LightGray, StrokeThickness = 1, IsHitTestVisible = false });
                Canvas.SetLeft(c.Label, c.Left ? -c.Label.DesiredSize.Width - 10 : preview.Width + 10);
                Canvas.SetTop(c.Label, top);
                c.Label.IsHitTestVisible = false; preview.Children.Add(c.Label);
            }
        }
    }
}
