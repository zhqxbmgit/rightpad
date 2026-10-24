# Ring 白色箭头呈现节奏修正 — 2026-10-22

时间为 Asia/Shanghai。用户在上一轮真实光标可见标记部署后反馈：“游戏里可以看见
光标，但是这个光标移动非常地卡顿”，并进一步确认“主要是白色箭头卡顿”。本轮仅
修正菜单绘制与呈现调度，保留真实光标快照作为唯一位置源，不改变 Motion 或选槽。
上一轮交付报告与全部已有工作区修改、未跟踪内容、旧证据均保留。

## 发现与证据边界

原 `ModeMenuPresenter` 用 `DispatcherTimer(Render)`，Interval=16 ms；每次 Present
不论快照是否变化都调用 `InvalidateVisual()`，在 OnRender 中重建六个扇区、所有
FormattedText、说明条及箭头。该结构在没有标记时只影响高亮显示，标记移动后暴露了
低且不均匀的呈现节奏和不必要的整环重建。

本轮离线 STA Dispatcher 测量原配置的 100 个 tick 间隔：平均 **24.994 ms**，中位数
**29.7919 ms**，p95 **34.1986 ms**，最大 **46.4666 ms**。标称 16 ms 并未形成固定
16 ms 的显示节奏。测试不读取真实光标、不打开窗口、不访问 Receiver 或游戏；它
证明原定时方式存在节奏问题，**不是游戏现场帧间隔测量，也不能完整归因游戏卡顿**。

另外，将本轮修改前保存的实际渲染器源码与修正版进行离线 UI 绘制准备对比；不使用
Git HEAD，不开窗口、不输出输入。每轮 1000 次位置更新，三轮交替运行：

| 绘制准备路径 | 三轮耗时（ms） | 三轮分配量（bytes） |
|---|---|---|
| 原每次整环重建 | 599.8572 / 627.1954 / 397.7563 | 129224104 / 129226520 / 129232952 |
| 保留箭头，仅更新变换 | 0.3239 / 0.3625 / 0.2057 | 136040 / 136040 / 136040 |

该对比测量 Drawing/Layout 的 UI 准备成本；**排除 GPU、DWM、屏幕和游戏显示延迟**。
不能把该倍率宣称为 FPS 提升或真人体验已通过。

## 实现

生产改动仅在 `C:\rightpad\windows\Rightpad.Receiver\Views\ModeMenuWindow.cs`：

- 原计时器仅在菜单关闭时发现下一次打开。菜单打开后停止该计时器，订阅同一 WPF
  Dispatcher 的 `CompositionTarget.Rendering`，在绘制机会消费既有最新 MenuSnapshot。
  关闭/故障退订事件并恢复原计时器，Dispose 停止计时器并退订事件；没有并行增加
  定时器、轮询、线程或独立窗口。
- 箭头保持原白色主体/黑色外轮廓、20×27 DIP 和尖端 (0,0)，作为同一 FrameworkElement
  的顶层 `DrawingVisual` 子图形保留。纯位置变化仅修改 MatrixTransform；Ring 的
  高亮、按下、绑定、Placement 或预览变化才触发菜单内容重绘。
- 去除 Present 中每次无条件的 `InvalidateVisual()`。父元素 `ClipToBounds=true`
  覆盖子图形，自然裁切规则保持。屏幕坐标扣监视器原点后按当前浮层比例变换，箭头
  几何按 Area.Scale 缩放；最新快照位置直接映射，无私有累计、插值、外推、补帧、
  平滑、延迟或补偿。静态预览及 null 快照隐藏保留箭头。
- 保留 MenuPresented、原 2 s 呈现超时、绘制故障处理、非激活/穿透、固定锚点、
  12 DIP 中性区、方向边界、无外圈选择上限及全部输入完成/安全释放规则。

新增一个有效回归在 `C:\rightpad\windows\Rightpad.Receiver.Tests\ModeMenuTests.cs`：
120 次连续坐标变化立即对应最新快照热点，未变化的 GlyphRun 引用保留；方向变化
仍更新文字，预览/关闭隐藏标记。原坐标、DPI、热点、顶层、裁切、预览像素检查继续
执行。当前契约更新于 `MODE_SHORTCUT_MENU.md`、`ARCHITECTURE.md`、
`USER_PREFERENCES_AND_WORKFLOW.md`；历史报告不改写。

没有修改 Motion/M/C、灵敏度、量化、HID、菜单移动一次分流、Gesture 隔离、点按
阈值、已绑定按下/释放时序、未绑定确认关闭、中央保持、RPHF/CONFIRM/身份/去重、
Mode 原单键与手动开关、B/X、Android、正式设置、布局、设备、驱动、Broker 或许可。
没有改变系统光标，没有游戏 Hook/注入，没有访问 leftpad。

## 构建与回归

本轮证据根：`C:\rightpad\windows\test-results\mode-menu-cursor-cadence-20261022`。

| 检查 | 结果 |
|---|---|
| Receiver 隔离 Release | 0 警告、0 错误，`build-receiver.log` |
| Tests 隔离 Release | 0 警告、0 错误，`build-tests.log` |
| 菜单专项 | **34/34**，exit 0，`tests-targeted.log` |
| Windows 全量 | **777/777**，exit 0，`tests-full.log` |
| 离线渲染检查 | 原 3 项与新增 retained-update 均通过，`render\` 保留图像 |
| 离线准备开销对比 | `render-preparation-benchmark.json` 及基准源码 |
| 原定时器间隔测量 | `dispatcher-before.json`，100 个间隔 |

源码未变且检查通过后，未重复全量回归。证据用基准工程初次缺少 System.IO 引用导致
编译失败，补引用后通过；原 `build-benchmark.log` 和 `build-benchmark-retry.log` 均
保留。此错误不涉及生产源码/测试，不影响正在运行的旧 Receiver。

## 本轮恢复保护与部署

- 重新核对当前实例 **2664**、创建时间 `2026-10-22T08:07:21.0512861Z`、生产路径与
  当前 DLL `6593DDB2…3C54AD`，重新备份全部现场 Release 文件和正式 settings：
  `C:\rightpad\windows\test-results\mode-menu-cursor-cadence-20261022\recovery\backup-20261022-163514`。
  `manifest.json` 与 `checksums.sha256` 记录逐文件大小/哈希和新候选；未沿用旧备份。
- 复用上一轮恢复/Guard/独立启动工具副本。独立计划任务
  **Rightpad Cursor Cadence Guard 20261022**：当前用户 Interactive/Limited、无触发器、
  隐藏运行。PID **32724**，父 **2432/svchost.exe**，Session 5。
  **16:35:45.606** 在计划任务环境完成本轮备份/候选/现场健康预检并 ready，早于
  **16:35:58.478** 开始停止旧实例；独立于 Codex、当前终端与 Receiver。
- 旧 Receiver 通过批准启动器 Stop，exit 0，无强制停止；**16:36:10.362** 确认退出
  且 UDP 50000 释放。仅覆盖候选 DLL/PDB，经
  `C:\rightpad\windows\tools\RightpadReceiverTask.ps1 -Mode Start` 独立启动，exit 0。
- 新 PID **11692**，创建 **2026-10-22 16:36:13.233844 +08:00**
  (`2026-10-22T08:36:13.233844Z`)；实际生产 EXE：
  `C:\rightpad\windows\Rightpad.Receiver\bin\Release\net8.0-windows\Rightpad.Receiver.exe`。
- 实际映射 Receiver DLL **512000 字节**，SHA256：
  **`AA7525E359D4BA18BBBE58DB7B1A4FD53875CCC557DD1E0BD6209891503E6C6B`**，
  与候选及测试 DLL 一致。apphost EXE 保持 `B444…E33CE`，用 DLL 哈希区分运行代码。
- 用户 `BAODINE77777777\zhq`，Session **5** 与 explorer 相同，Medium
  `S-1-16-8192`，Default 桌面；包装器由计划任务服务启动、即时 Job 限制为 0。
  UDP `0.0.0.0:50000` 唯一归属 11692；实际加载原生产 libvirtualhid bridge，
  M_R1 保持；Android 自然发现新 Receiver、以既有心跳重新连接。
- 正式 settings **1348 字节**，SHA256
  **`C3E4B9CBE21246FD004BC55DDEA360C32B88D5D13CA9F5AE20D36B7F69B4C7F1`**，
  与本次现场备份一致；没有临时配置或布局改动。
- 部署核对 **23/23**，`deployment-verify.json`；只读键盘/鼠标 GetAsyncKeyState 与
  XInput 全按钮/扳机/轴释放观察通过，`input-release.json`。这不证明实际移动、
  单击、双击拖动的真人体验已通过。

Guard 配置沿用部署窗口90秒、失败门限40秒、每5秒检查、累计健康目标120秒；最终
结果：**16:39:48.216** 正常完成，24 次健康检查、累计健康120秒，exit0，rollback=none。
临时任务收尾与 Receiver 持续运行核对见 `final-state.json`。未刻意制造故障、
未真实触发本轮自动回滚；恢复包保留，恢复路径不依赖用户在失去输入后执行命令。

## 用户再次验收

自动化证明绘制准备开销减少、位置/预览/菜单与安全规则未回归，部署状态正确。
**是否已经消除游戏中的箭头卡顿，仍待用户再次操作确认**。本轮未启动、操作或截图
游戏，未操作手机、使用 ADB 或注入手势，也未测修正版在实际游戏中的帧间隔。

建议只复测：在刚才同一游戏打开菜单，连续缓慢和稍快地移动手指，观察箭头是否比
上一版连贯、与高亮对应；随后点按/Mode 关闭，确认普通移动、单击和拖动正常。

未执行 Git commit/push/reset/checkout/历史修改、文件清理或迁移；仅收尾本轮临时
Guard 任务。所有历史文件与本轮恢复包保留。

证据索引：
`C:\rightpad\windows\test-results\mode-menu-cursor-cadence-20261022\README-evidence.md`。
运行日志：
`C:\rightpad\windows\test-results\receiver-runtime\20261022-163613-199-32820\receiver.log`。
