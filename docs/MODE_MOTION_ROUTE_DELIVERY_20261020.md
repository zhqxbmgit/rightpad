# Mode 菜单移动/点按分流交付报告 — 2026-10-20（第二轮）

时间均为 Asia/Shanghai。本轮在已有未提交菜单实现上增量开发，完成隔离构建、全量回归、
受控部署、生产链路手机验证、正式设置/布局恢复和最终释放检查。没有 Git commit、push、
回退、历史修改、git clean 或资料清理；此前未提交/未跟踪文件均保留。旧交付报告
（2026-10-18/19）保持为历史版本证据，不代表当前移动路由。

## 当前契约

- 菜单用途下，有效 Mode DOWN 开关菜单；正常 UP、保持、重传、乱序、迟到转换不额外
  开关。关闭不发 Esc、暂停键、取消键或普通鼠标键。
- **移动**：菜单打开期间，每个已接受 Touch 包只进入原 Motion/HID 移动处理一次。使用
  当前选择的 Motion 引擎、正式 SensitivityX/Y、重建、量化、现有输出设备；不新增
  菜单速度、DPI 位移倍数、补偿、预测或私有操作指针。手机移动会让游戏收到鼠标移动，
  视角可能随之移动；这是本轮授权内的明确取舍。
- **点按**：菜单在普通 Gesture 单击/双击拖动产生鼠标按钮事件之前分流。菜单期间点按
  只执行当前方向槽位的**一次**键盘/手柄按下与释放，执行后菜单保持打开。中央、未绑定
  方向、连续点按都不产生普通鼠标左键，也不进入拖动。没有新增系统 Hook，不采用“先发
  左键再拦截”。
- **选择**：Windows 系统光标是唯一操作指针。打开位置固定为锚点与视觉中心，打开不
  跳光标；12 DIP（含边界）中性区、六方向顺序、方向边界、无外圈上限和固定 Ring 外观
  保留。Motion 异步输出，菜单在维护循环中持续读取实际系统位置（约每 10 ms 及收包时），
  高亮与点按均以实际位置为准，不采用请求目标或预测位置。
- **生命周期**：打开清旧拖动、排队点击和旧 Motion 链；进入前已保持的鼠标键正常释放，
  并以独立 `left_button_safety_release: reason=menu_enter` 记录，与菜单点按的
  `mode_output` 分开。关闭、断线、故障、配置变化后旧点按资格、旧 UP 和队列不能补出
  普通点击/拖动；关闭后普通输入只从新 DOWN 边界恢复。原 Mode 开关、去重、租约、
  输出失败门闩、B/X 边界保护、隐藏/约束/读取失败/显示变化处理保留。
- 原单键绑定、普通按钮/切换按住、六槽绑定、统一磁盘优先 Save、Android/手机布局/静态
  M/协议/端口、M/C、Motion 参数与灵敏度、已接受 B/X 参数、触觉、输出设备/驱动/原生
  ABI/Broker/许可全部未改。菜单只保留 Ring，不恢复 Islands。

## 修改范围

| 文件 | 变更 |
|---|---|
| `windows/Rightpad.Receiver/ModeSession.cs` | `RouteTouch` 拆分为 movement 与 gestures 两个回调；菜单打开时先执行且只执行一次 movement，再只做菜单点按识别；普通 Gesture 不接收菜单包 |
| `windows/Rightpad.Receiver/ModeMenu.cs` | 删除菜单专用 `TouchSessionProcessor` 导航与 `IModeMenuCursor.Move` 调用；`IModeMenuCursor` 只保留 Capture/Read；点按仍以实际位置执行槽位脉冲 |
| `windows/Rightpad.Receiver/WindowsModeMenuCursor.cs` | 改为只读适配器：移除 SetCursorPos、写后差异/持续零进展判定与移动状态；保留锚点、真实位置、隐藏/约束/显示/DPI/API 失败检查 |
| `windows/Rightpad.Receiver/UdpReceiver.cs` | 将正常路径拆为 `Movement()` 与 `Gestures()`；菜单归属门只调用 movement；清输入时向按钮控制器传 reason |
| `windows/Rightpad.Receiver/LeftButtonController.cs` | `CancelPendingAndRelease(reason)` 记录独立安全释放行（held/dragging/pendingClicks） |
| `windows/Rightpad.Receiver/Runtime/ReceiverRuntime.cs` | 传入带 reason 的安全释放回调 |
| `windows/Rightpad.Receiver.Tests/ModeMenuTests.cs` | 重写并扩展路由、等价位移、无按钮、安全释放、UDP 生产路由、六方向、恢复等回归 |
| `windows/Rightpad.Receiver.Tests/ModeMenuOverlayProbe.cs` | 适配只读光标；生产移动用受控测试模拟，不再由菜单 SetCursorPos 导航 |
| `windows/Rightpad.Receiver.Tests/LeftButtonControllerTests.cs`、`PresenceTests.cs` | 适配新安全释放/取消回调签名 |
| `AGENTS.md`、`docs/ARCHITECTURE.md`、`docs/MODE_CONTROL.md`、`docs/MODE_SHORTCUT_MENU.md` | 当前契约同步为“移动走原 Motion/HID、点按前置分流、只读系统光标” |

未修改插件、Android、布局、协议/端口、Motion/M/C、B/X、触觉、原生 ABI/驱动/Broker/
许可；没有迁移产物目录，没有 Git 操作。

## 构建与自动化

隔离输出目录：`windows/test-results/mode-motion-route-20261020/verify-build/`。

| 检查 | 结果/证据 |
|---|---|
| Release 构建 | 0 警告、0 错误；`build-final.log` |
| Windows 全量回归 | **765/765，0 失败**；`tests-final.log` |
| 候选 DLL | `69766F1B1E433E578410B5164EF85CAE64171BB301F81D9B1EC801D7514864C9` |
| 原生依赖 | `Rightpad.VirtualHid.dll` 仍为 `1F1D0C4F…F1E83CC` |

新增/重写的关键回归：

- 菜单移动只经原 Motion 一次，普通 Gesture 全程为 0；
- 相同固定配置、相同初始 Motion 状态和等价输入下，菜单与普通路径 HID 相对位移、
  总量、Tick/漏 tick 完全一致；
- 菜单点按、连续点按、中央、未绑定向均不产生普通鼠标按钮；
- 槽位键盘/手柄动作各一次按下与释放，菜单保持打开；
- 进入前拖动/待点击安全释放并独立记录；
- 进入前旧拖动、关闭后旧 UP/MOVE、断线/故障/配置变化不泄漏点击或拖动；
- 关闭后新接触恢复普通单击和双击拖动；
- UdpReceiver 生产路由下每个接受包只进入 motion 一次，重复序列不进入；
- 原单键、目录、Save、B/X、租约、呈现超时、输出失败等既有回归全部通过。

## 生产链路实测

环境：现有 Android 实例 PID 20316，设备 1200×2670、density 480；现有 Sender/type8/
RPMD/真实 libvirtualhid 后端。**ADB 单指注入，不是物理手指、真人盲操作或游戏手感验收。**
生产 Receiver：PID 31900（2026-10-20 18:19:58 启动），M_R1，Sensitivity 12/12。

手机同一段位移：`(900,1200) → (920,1210)`，每次前把光标置于 (1920,1032)。

| 路径 | Receiver/HID 累计差 | Raw Input 合计 | 系统光标位移 | 左键 |
|---|---|---|---|---|
| 普通（菜单关闭） | 240,120 | 240,120 | 1920,1032→2400,1272 = 480,240 | 0 |
| 菜单打开 | 240,120 | 240,120 | 1920,1032→2400,1272 = 480,240 | 0 |
| 关闭后普通 | 240,120 | 240,120 | 1920,1032→2400,1272 = 480,240 | 0 |

三段 HID 与屏幕位移逐项相同；`verified-evidence.json` 从 `result.json`、`actual-input.log`
和 Receiver 日志重算并全部通过。

菜单证据：

- `mode_menu_anchor x=1920 y=1032 dpi=192 neutralRadius=24`；打开后光标仍为 (1920,1032)，
  无跳转。菜单导航/六方向期间 Receiver 无 `mode_output`、无 HID 左键。
- 六方向以 280 物理像素半径逐向新落指/抬手，实际光标与选择分别为：
  slot0 (1920,752)、slot1 (2160,890)、slot2 (2160,1172)、slot3 (1920,1310)、
  slot4 (1680,1172)、slot5 (1680,892)；全部命中对应方向，菜单保持打开。
- 上槽 A 点按：`mode_output: binding=A held=True` → `held=False`，菜单保持打开；
  Raw Input 键盘 `scan=001E flags=0000` 与 `flags=0001` 各一次。该观察窗口内 HID 鼠标
  按钮事件 **0**、目标窗口左键按下 **0**。整个本轮 Receiver 运行只有一次 A 按下。
- 右下（未绑定）点按：无新 `mode_output`、无键盘/鼠标按钮，菜单保持打开。
- 显式再点 Mode 关闭；没有发送 Esc/鼠标点击。

关闭后普通输入：

- 新接触单点：Raw Input 左键 `0001`→`0002`；Receiver `clicksTriggered` 6→7；置顶观察窗
  记录左键按下/释放。
- 双击拖动：`doubleTapDeltaNs=39000000`（39 ms）、`dragging=True`、`dragStarts=2`、
  `dragEnds=2`；Raw Input 左键按下→多次移动→释放，观察窗 `mouse_drag_up moves=179`，
  光标 y +480。
- 最终 `GetAsyncKeyState`：本轮操作结束时（18:26:51）左右中键、A/G、Shift/Ctrl/Alt/F13
  全部未按下；XInput slot0 buttons/LT/RT/摇杆全 0。见 `native-final-release-182651.json`。
  此后用户恢复实际使用；18:35:42 一次真实接触使 LButton 正常按下，18:35:47 接触间读数
  再次全部释放，说明没有本轮的卡住输出。`native-final-release.json` 保留该后续读数。

## 部署、恢复与现场保护

部署前（18:18）逐字节备份实际运行目录、`settings.json`、手机正式布局记录，记录哈希：

- 旧 Receiver PID 24540，Session 3，DLL `6C536056…B1C5`；
- 备份：`recovery/backup-20261020-1830/`（`release/` 全文件 + settings + 布局 + manifest +
  checksums）；manifest 记录旧/候选 DLL、EXE、settings、布局哈希。

独立恢复保障：

- `recover2.ps1` 支持 Preflight/Health/Plan/Recover；校验 manifest、全部文件哈希、
  启动器任务身份，健康检查要求 UDP50000 单一所有者、路径、候选或备份 DLL、窗口响应。
- 只读预检通过（healthy PID 24540）；`-HealthOverride Unhealthy` 的 Plan 输出
  `wouldRestore=true`，未执行任何进程/文件改动。
- 临时计划任务 `Rightpad Motion Route Guard 20261020` 由当前用户 InteractiveToken、
  Limited 运行；Guard PID 21124、Parent 2432（svchost/Task Scheduler）、Session 3，
  独立于本 shell。`guard-ready.json`（18:19:27）记录预检通过，早于 18:19:49 停止旧
  Receiver；Guard 参数为 240 s 部署窗口、30 s 连续不健康阈值、600 s 目标健康观察。
  部署与验证期间健康检查持续通过，**未触发回滚**。
- Guard 于 18:33:28 以 `result=completed`、退出码 0 正常结束（`guard-result.json`，
  记录 healthySeconds=500）；随后注销临时任务并确认 Guard/观察器进程均已退出。

受控部署：

- 18:19:49 通过项目启动器 `RightpadReceiverTask.ps1 -Mode Stop` 停止旧 PID 24540，
  确认进程退出、UDP50000 释放；
- 复制候选 DLL/PDB（复制后哈希核对），`-Mode Start` 独立启动；
- 新 PID 31900，Session 3，当前用户，Medium（S-1-16-8192），Default 桌面；
- `0.0.0.0:50000` 归 31900；生产 libvirtualhid；M_R1、Sensitivity 12/12；
- Android 既有 heartbeat 自然连接，`senderRunId=7DB4BCB67984B693`；Android PID 20316
  未变，未重装/重启 APK。

验证期间通过 Receiver UI 把 Mode 用途临时切为“电脑快捷菜单”并统一 Save；结束后用同一
UI 流程恢复“单键绑定”并 Save。最终 `settings.json` 与备份**逐字节相同**：
`0855C715B3F347827C0BC5B81CF729312A6928FFA75513672242CAB87910C5E6`；用途单键、
原绑定未绑定/普通按钮、上槽 A、其余五槽未绑定、灵敏度/M_R1/CZ1/B/X/未知字段全部保留。
手机正式布局设备端 md5 `326e3c1f2155a00e2996831f65de64e2`、时间戳
`2026-10-19 18:45:16.720526822 +0800`，本轮未改动。

最终运行状态：Connected；Mode 运行状态“未绑定 · 普通按钮 · 已释放”；最终日志无
`receiver_error`、`mode_output_error`、`mode_menu_fault`、`left_button_error` 或
`gamepad_unavailable`。临时观察器已正常关闭，无残留测试进程。

## 观测边界与未验证项

- 明确区分：Receiver/HID 原始输出（生产日志 `totalDx/Dy` 与 Raw Input）、系统光标
  （DPI-aware GetCursorPos）、窗口消息（受控观察窗）。窗口消息只证明该受控目标收到
  按钮，不证明所有游戏兼容；本报告不从一层观测推断全部游戏。
- 首次普通单击/拖动时观察窗被最大化的 Notepad 覆盖：Raw Input 与 Receiver 日志仍有效，
  但窗口消息进入 Notepad（未保存其文件）；随后把观察窗置顶并重复单击/拖动，保留
  `extra-input.json` 的目标窗口证据。菜单点按无左键的证据来自 Raw Input 与 Receiver
  日志，不依赖该窗口层。
- `mode_menu_selection` 记录的是异步 Motion 输出过程中的实际光标位置；六方向最终实际
  位置与选择均正确，不能把单条中间日志当作请求目标。
- 未新增安装/进入游戏；未验证真实游戏、独占/无边框全屏、反作弊、所有 DPI/多显示器、
  真人盲操作与长期手感。ADB 注入是生产链路自动化，不是物理手指测试。
- 真实桌面的光标隐藏/内部约束/显示变化探针本轮未重新执行；适配器只读版本的隐藏、
  抑制、内部 Clip、显示/DPI 变化和读 API 失败由自动化回归覆盖，既有 2026-10-19/20
  受控探针证据保留但不冒充本轮新结果。
- 未故意破坏唯一控制设备来测试完整故障注入；恢复能力通过只读预检、离线 Plan 和独立
  Guard 健康观察证明，Guard 未实际回滚。
- 本轮没有 Git commit、push、回退、历史修改或清理。

## 证据路径

- 证据根：`windows/test-results/mode-motion-route-20261020/`（索引见 `README-evidence.md`）。
- 生产日志：`windows/test-results/receiver-runtime/20261020-181958-539-33708/receiver.log`。
- 恢复包：`windows/test-results/mode-motion-route-20261020/recovery/backup-20261020-1830/`。
