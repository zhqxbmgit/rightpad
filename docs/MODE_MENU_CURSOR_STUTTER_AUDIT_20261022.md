# Ring 标记仍卡顿：只读复核 — 2026-10-22

本记录仅描述本次工作的前置只读阶段，不改写此前交付报告。该阶段运行的 Receiver DLL 为
`AA7525E359D4BA18BBBE58DB7B1A4FD53875CCC557DD1E0BD6209891503E6C6B`，PID11692，
创建时间2026-10-22 16:36:13 +08:00。本次重新核对该PID仍运行、响应，生产DLL哈希相同。

用户明确反馈：白色标记仍非常卡顿；能显示系统鼠标的游戏正常，且这些游戏里的白色
标记本身也流畅。指定卡顿代表游戏为《地平线：西之绝境》。因此**上一轮离线开销
优化未解决实际问题**；777项回归通过不等于实际流畅度通过。

只读保存并分析现有Receiver日志及两份flight日志，未打开或操作游戏、未截图，未操作
手机/ADB，未改变真实光标、配置或输入。本次未构建/部署/重启，未启动性能追踪或
持续采样任务，没有Git写入、旧资料清理或迁移。

## 可确认的内容

- 标记由Receiver的WPF透明窗口绘制，不是系统光标图形；它仍使用同一MenuSnapshot
  的真实位置，代码未增加另一套位移或位置预测。
- 已保存日志中，PID11692在HorizonForbiddenWest前台共有36个约1秒间隔的快照，
  其中10个在最近100ms内有Motion输出，10个位置各不相同，坐标读取失败0。
  该游戏片段的Clip为整个3840×2160区域。
- 该片段中20对有实际HID位移的相邻快照，坐标呈现相应变化；按端点不在屏幕左右
  边缘筛选后17对，其中16对坐标变化与现有2倍屏幕映射的差值在8px以内。
  另1对Y残差291px，1秒内可能发生触边/改变方向等，现有记录不能确认原因。
  这些数据**不支持直接断言地平线把光标锁死或反复居中**，也不能排除短时跳变。
- 现有日志没有标记逐帧回调、快照观察时间或最终屏幕呈现时间，无法从这些日志确定
  是位置短时停顿、UI更新停顿还是WPF/DWM呈现停顿。1秒快照不能证明帧间连续。
- 用户对不同游戏的比较使呈现环境/游戏负载成为需要测量的方向，但“隐藏光标本身
  导致卡顿”“GPU饱和”“WPF限制帧率”均不是已确认根因。

## 官方资料核对

Microsoft说明GetCursorPos读取屏幕坐标：[GetCursorPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getcursorpos)。
WPF说明Rendering事件频率受系统及负载影响，不能把订阅该事件当作固定刷新率保证：
[CompositionTarget逐帧绘制说明](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/how-to-render-on-a-per-frame-interval-using-compositiontarget)。
WPF的像素渲染和硬件/软件管线仍有独立成本：
[WPF硬件性能说明](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-taking-advantage-of-hardware)。

这些资料说明需区分输入位置与最终呈现，但不能替代地平线现场测量。下一项必需证据
是同一故障片段的观察/快照更新时间、标记更新与呈现间隔；不应再凭离线分配量或
标称刷新率直接部署另一版并称已修复。

## 证据

根目录：`C:\rightpad\windows\test-results\mode-menu-cursor-stutter-audit-20261022`。

- `captured\receiver.log`：本次只读保存的现有运行日志。
- `captured\flight-recorder.log`、`flight-recorder.previous.log`：已有记录副本。
- `Analyze.ps1`、`analysis.log`、`existing-log-analysis.json`：显式字段统计，含采样限制。
- `horizon-hid-position-residuals.json`：相邻位置与原HID位移的粗粒度核对。

当前结论为**实际卡顿未解决，逐帧根因未确认**。本次保持现有Receiver和唯一输入
通道运行；不撤销已交付确认/关闭规则，不改Motion或真实光标权威。

后续为补齐这一数据缺口，已另外实现并受保护部署只记时间/计数的诊断版，见
[诊断版交接](MODE_MENU_CURSOR_FRAME_DIAGNOSTIC_20261022.md)。本记录中的“未构建/
部署”等仅指上述前置只读阶段，不是后续诊断版实施范围。
