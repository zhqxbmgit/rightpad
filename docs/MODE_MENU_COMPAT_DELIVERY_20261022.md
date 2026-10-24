# Mode 菜单兼容修复交付 — 2026-10-22（第四轮：未绑定确认与真实光标可用性）

时间均为 Asia/Shanghai。本轮只做 Windows Receiver 的 Ring 菜单修复、测试、当前契约
同步与隔离构建。**未部署**：没有停止、启动或重启 Receiver；没有修改
`%LOCALAPPDATA%\rightpad\settings.json` 或任何正式配置；没有使用 ADB、注入或真实光标
探针；没有操作 `C:\leftpad`；没有 Git commit/push/checkout/reset；没有删除、迁移、
压缩任何已有文件或旧证据；没有修改 Android、Motion/M/C、灵敏度、B/X、协议、端口、
驱动、Broker 或许可；没有让持久 Receiver 成为 shell 子进程。

## 本轮两项替代契约

用户已明确用本轮行为取代旧文档/旧测试中的相反文字；历史交付报告原样保留，未修改。

**A. 未绑定槽位有效点按**：提交一次与原普通触控板单击相同的手机确认（既有
Haptic Feedback v1 / RPHF / Android CONFIRM 与去重；不新增协议、字段、波形、服务或
设置），然后关闭菜单。不输出任何键盘键、手柄按钮或普通鼠标点击，不伪造按下，不需要
25 ms 等待；反馈失败只记日志，仍关闭且不重放；每次打开周期最多接纳一次有效槽位选择，
已绑定与未绑定都计入。中央 12 DIP 中性区仍不确认、不关闭。

**B. 光标可见性/约束不再是否决条件**：光标 hidden/suppressed 不等于坐标不可用，
`ClipCursor` 约束也不等于坐标读取失败；只要真实坐标仍可读取，菜单必须能打开并持续
运行。真实 `GetCursorPos`/`GetMonitorInfo`/`GetDpiForMonitor` 失败、打开锚点监视器
区域/DPI 变化、2 s 呈现超时、租约/presence/断线、配置变化、输出失败与手动 Mode
关闭仍照旧关闭并围栏旧输入。

## 根因证据

- 现场日志（只读，未改动）：
  `windows/test-results/receiver-runtime/20261022-000339-592-14140/receiver.log`。
- 该文件逐行实读：`mode_menu_open` **20** 条、`mode_menu_close` **20** 条、
  `mode_menu_fault` **8** 条；8 条全部是
  `flags=0x0 visible=False suppressed=False ... 目标隐藏了系统光标`，其中 7 条
  `phase=refresh closeReason=menu_navigation_error`、1 条
  `phase=maintenance closeReason=menu_cursor_error`。任务简报记为 10 次；本报告按
  该文件实际计数为 8，差异如实保留，未改日志。
- `mode_reset: reason=lease_expired` 与 presentation timeout 均为 **0** 条；没有
  lease/presentation 关闭。其余 12 次关闭不是 cursor fault。
- 典型行（截断）：
  `mode_menu_fault: phase=refresh closeReason=menu_navigation_error ...
  stage=read actual=3435,155 flags=0x0 visible=False suppressed=False
  clip=0,0,3840,2160 display=opening=MenuArea { ... 3840x2160 Scale=2 } ... message=目标隐藏了系统光标；已退出菜单...`
- 只读参考 `C:\leftpad`（HEAD `6dd1664`）：`CursorPositionProvider.cs` 只用
  `GetCursorPos`，`Ds4Service` 直接使用 `Cursor.Position`，不检查可见性或
  `ClipCursor`。

结论：菜单退出的直接条件是“隐藏光标”标志，而不是坐标读取失败；`ClipCursor`
校验是另一个没有证据支持的否决条件。两者均已从适配器移除。

## 实现

| 文件 | 变更 |
|---|---|
| `windows/Rightpad.Receiver/WindowsModeMenuCursor.cs` | 位置改为 `GetCursorPos`；删除 `CURSOR_SHOWING`/suppressed 拒绝与 `ClipCursor` 校验；`IModeMenuCursorApi` 只保留 `Position`/`Area`；诊断精简为 `actual`/`display`/`win32`；保留打开锚点的 `MonitorFromPoint`/`GetMonitorInfo`/`GetDpiForMonitor` 稳定性检查；真实读取失败仍抛带上下文的 `IOException`/`Win32Exception`；无 `SetCursorPos`/`ShowCursor`/`ClipCursor`/`SetCapture`，不再 P/Invoke `GetCursorInfo`/`GetClipCursor` |
| `windows/Rightpad.Receiver/ModeMenu.cs` | `Click` 在有效槽位读取最终系统位置；已绑定槽位继续 `execute(key, ClickFeedback)`；`Selected==null` 调用新的未绑定完成回调并携带合格 UP 的 `ClickFeedback`；中央/中性区返回 -1，不确认不关闭 |
| `windows/Rightpad.Receiver/ModeSession.cs` | 新增 `UnboundTap`：先置 `menuActionAccepted=true`，用 `presence.RemoteIp` 与触发 tap 的 UP 身份 best-effort 调用既有 haptic 委托，记录 `mode_menu_unbound` / `mode_menu_feedback` / `mode_menu_unbound_complete`，随后 `CloseMenu()`；不 `SetHeld`、不等待、不发键、不触发输出锁存；`CloseMenu` 继续 `freshTouch=true` 并经 `MenuBoundary` 清理 Motion/Gesture |
| `windows/Rightpad.Receiver.Tests/ModeMenuTests.cs` | 改写未绑定/隐藏/约束断言；新增六方向未绑定、未绑定反馈失败、按下失败等用例；适配器只读反射检查 |
| `windows/Rightpad.Receiver.Tests/ModeInputObserver.cs`、`Program.cs` | 新增仅测试用的 `--mode-observer-hidden <dir>`：无边框全屏、`Cursor=None`、测试专用 `ShowCursor(false/true)`、20 Hz `GetCursorInfo` flags+pos、Raw Input/XInput/foreground；原 `--mode-observer` 行为不变 |
| `windows/Rightpad.Receiver.Tests/ModeMenuOverlayProbe.cs` | 手工环境探针改为断言 clip/hidden 不再关闭，避免保留相反断言（本轮未运行真实光标探针） |
| `windows/Rightpad.Receiver.Tests/Program.cs` | 新增 `--mode-menu` 针对性测试入口 |
| `AGENTS.md`、`docs/ARCHITECTURE.md`、`docs/MODE_SHORTCUT_MENU.md`、`docs/HAPTIC_FEEDBACK_PROTOCOL.md` | 当前契约同步；历史交付报告未改 |
| `docs/MODE_MENU_COMPAT_DELIVERY_20261022.md` | 本报告 |

未改 WPF Overlay 结构（`ModeMenuWindow` 的 NoActivate/TopMost/ClickThrough 保持不变）。
已绑定 `Pulse`/`CompleteMenuAction`、25 ms 脉冲、失败不发确认与关闭顺序保持。

辅助工程变更：`windows/Rightpad.Receiver/app.manifest` 与 `Rightpad.Receiver.csproj`
的 `ApplicationManifest` 只把当前契约已声明的 PerMonitorV2 清单显式化（部署前的
WPF 进程实测已是 PerMonitorAware，候选 EXE 与线上逐字节相同 `B444…`）；不是本轮
菜单行为变更，也不涉及光标写入。

## 构建与测试

隔离证据根：`windows/test-results/mode-menu-compat-20261022/`。

| 检查 | 结果/证据 |
|---|---|
| 工具链 | .NET SDK **8.0.422**，Release |
| Receiver 构建 | **0 警告、0 错误**；OutDir=`.../build/`；`build-receiver.log` |
| Tests 构建 | **0 警告、0 错误**；OutDir=`.../tests/`；`build-tests.log` |
| 针对性 `--mode-menu` | **total=30 passed=30 failed=0**，exit 0；`tests-targeted.log` |
| Windows 全量回归 | **total=773 passed=773 failed=0**，exit 0；`tests-full.log` |
| 隐藏光标 observer 重建后全量 | **total=773 passed=773 failed=0**，exit 0；`tests-full-observer.log` |

```powershell
windows\test-results\mode-menu-compat-20261022\tests\Rightpad.Receiver.Tests.exe --mode-menu
windows\test-results\mode-menu-compat-20261022\tests\Rightpad.Receiver.Tests.exe
```

全量计数是本轮实际值，不沿用历史 769。本轮新增 4 个用例（六方向未绑定、未绑定反馈
失败、未绑定每周期一次、按下失败），另有旧用例改写。多次重建（收紧未绑定日志条件、
补充每周期一次用例、新增隐藏光标 observer 后重建）后的重测均为 0 失败；本轮没有失败
用例日志可保留，最终日志对应最终构建。

候选产物（隔离输出，未部署）：

| 产物 | 字节 | SHA256 |
|---|---:|---|
| `build/Rightpad.Receiver.exe` | 151552 | `B444947422873175F8B45A9206A9D870FC2853696FBB10496EE4417C546E33CE` |
| `build/Rightpad.Receiver.dll` | 510464 | `1E5BBAD2FECE72B0859F0E851EF626294372BFC7449ACA882230A9E66FE0B335` |
| `tests/Rightpad.Receiver.Tests.exe` | 152064 | `B446ED31B1B09F6CD1D5BA1CB1BF8F13C627507C1568F25723CE96D8854BA3C9` |
| `tests/Rightpad.Receiver.Tests.dll` | 1040896 | `36323CE2E6C8186E3DD2AFADEC706FFA452CE2FAC95BBABAD2833503B3603CB7` |

`tests/Rightpad.Receiver.dll` 与 `build/Rightpad.Receiver.dll` 哈希相同。apphost EXE
未随 C# 变化（与生产 EXE 同哈希），版本 ProductVersion 仍为
`1.0.0+8b4565a9a21f1feaa9addefc2286295d6fbaaf33`。

### 关键测试覆盖

- 六个方向未绑定有效点按：各恰好一次确认，确认身份等于触发 UP，零键盘/手柄/普通
  鼠标按钮，自动关闭；重复/迟到 UP 不再确认或点击；关闭后下一次新 DOWN 恢复普通
  单击（`UnboundDirections`）。
- 中央与 12 DIP 边界点按：无输出、无确认、保持打开；导航/抬手重落不误关
  （`NoActionTaps`、`IdleMinute`）。
- 未绑定反馈抛错：仍关闭、记 `mode_menu_feedback_error`、不重放、下一次新 DOWN
  恢复（`UnboundFeedbackFailure`）。
- 未绑定每周期一次：自动关闭前重入第二次未绑定完成被既有 `menuActionAccepted`
  拒绝，只有一次确认、一次关闭（`UnboundOncePerCycle`）。
- 已绑定 24 ms 仍 held、25 ms 释放后才确认并关闭（`KeyboardSlot`）；手柄槽位同
  （`GamepadSlot`）。
- 按下失败/释放失败不发确认且安全释放（`FailedPress`、`PulseLifecycle`、`Failures`）。
- 坐标读取失败、显示器/DPI 变化安全关闭并 fence，环境恢复后新 Mode 接触可重试；
  输出设备门闩保持（`CursorFailures`、`CursorHealth`）。
- 适配器反射：无 `SetCursorPos`/`ShowCursor`/`ClipCursor`/`SetCapture`/
  `GetCursorInfo`/`GetClipCursor`；`IModeMenuCursor` 只有 `Capture`/`Read`
  （`CursorHealth`）。
- 菜单移动仍只走原 Motion 一次、普通鼠标按钮 0、UDP 生产路由
  （`Routing`、`Equivalence`、`Ownership`、`UdpRoute`）。
- 其余 Mode/Gesture/Output/Haptic 回归包含在 773 项全量中，未修改游戏、手势、
  灵敏度、B/X 参数。

“每次打开周期一次”由已绑定与未绑定共用的 `menuActionAccepted` 实现；已绑定由
`RepeatPackets` 覆盖，未绑定由 `UnboundOncePerCycle`（在自动关闭前重入第二次完成，
被拒绝）、六方向循环（每次打开只出现一次确认）及关闭后的重复/迟到包覆盖。

## 未验证项与风险

- 三款游戏的用户实测仍未进行；本轮没有启动或操作游戏。自动化模拟、源码对照和桌面
  验证不能替代真实游戏结论。

| 游戏 | 模式 | 状态 |
|---|---|---|
| 瑞奇与叮当：时空跳转 | 无边框窗口 | 待用户实测 |
| 地平线：西之绝境 | 无边框窗口 | 待用户实测 |
| 毁灭战士：黑暗时代 | 无边框窗口 | 待用户实测 |

- 游戏若持续把系统光标重置到固定点或持续改写 `ClipCursor`，即使不再因可见性/约束
  退出，仅靠真实光标仍可能选不出方向；本轮没有引入虚拟菜单指针或解除游戏约束，
  这一结构性限制需由三款游戏实测确认。
- 手机是否实际感到未绑定/已绑定确认振动，由用户验收；logcat 的
  `click_requested ... performed=true` 只证明 Android 接受并执行了既有 CONFIRM 调用。
- 部署 Guard 的 600 s 健康观察已通过，但“候选版本不健康 → Guard 自动回滚”这一
  具体触发未真实演练；同一 Recover 路径（停止 → 备份替换 → 启动器启动 → 健康）已在
  独立实演中真实执行并通过。
- 旧日志实际为 20 次打开/20 次关闭/8 条 `mode_menu_fault`（任务简报记 10 条）；
  以文件实读为准，历史日志未修改。
- ADB 注入不是物理手指、真人盲操作或游戏手感验收。

## 部署与恢复

### 恢复保障与实演

- 持久备份：`recovery/backup-20261022-1336/`（release 全文件 + settings + 布局 +
  manifest + checksums；旧 DLL `5B9A…`、EXE `B444…`、settings `C3E4…`、布局 md5
  `326e…`）。
- 独立实演：计划任务 `Rightpad Menu Compat Rehearse 20261022`（PID 22480，父进程
  2432 = Task Scheduler，Session 5）于 13:37:18 执行 Preflight 0 → 核对并停止 HKCU
  实例 19736 → `recover2 -Mode Recover`（备份）→ 启动器启动 → 健康 0；旧 19736 →
  新 29688，DLL `5B9A…`，端口归 29688。见 `rehearsal-result-20261022-133718.json`。
- 部署 Guard：`Rightpad Menu Compat Guard 20261022`（PID 30536，父 2432，Session 5）
  13:38:13 写出 ready（Preflight 0），部署窗口 40 s；健康观察 600 s，
  `guard-result.json` = `completed`、exit 0、`rollback=none`（13:51:55）。
- 未演练：由“候选版本不健康”真实触发 Guard 自动回滚；备份替换 → 启动 → 健康路径
  已由上述实演真实执行。

### 受控部署

- 13:38:24 核对旧实例 29688（StartUtc `2026-10-22T05:37:41.1290626Z`，DLL `5B9A…`）；
  启动器 Stop exit 0，13:38:35 退出并释放 UDP 50000。
- 复制候选 `Rightpad.Receiver.dll`（510464 B，`1E5B…B335`）与 PDB；启动器 Start
  exit 0。
- 新 PID **31232**，启动 13:38:38（`2026-10-22T05:38:38.8272886Z`），Session 5 =
  explorer，Medium `S-1-16-8192`，Default；`0.0.0.0:50000` 归 31232；libvirtualhid；
  M_R1；launcher 运行目录 `20261022-133838-796-27928`。
- EXE 未变 `B444…E33CE`；`deployment-verify.json` 32/32 通过，含 Android app PID
  20316 未重启、布局 md5 未变、settings 哈希未变、Connected、无
  `mode_menu_fault`/`mode_output_error`/`receiver_error`。

### 生产链路

最终 `phone-verify-menu-20261022-134404/phone-verify-menu.json`：**7/7 阶段、40/40
断言通过**：

- 打开锚点 = 实际光标 (1909,747)，无 fault；
- 导航 + 抬手重落：菜单保持打开，无 close/fault；
- 中央：无输出、无确认、不关闭；
- 未绑定 Bottom：一次 `mode_menu_unbound` + 一次 `mode_menu_feedback`/`haptic_sent`、
  关闭；观察窗口 0 键盘 0 鼠标按钮；Android `click_accepted 55690/2434` →
  `click_requested CONFIRM performed=true`，身份与 Receiver UP 一致；
- 已绑定 Top=Esc：一次按下/一次释放，2 ms 键状态采样 32.29 ms（≥25 ms），一次确认
  后关闭；Android `55691/2443`；无普通鼠标按钮；
- 普通单击 1 次、双击拖动 down/up 各 2 次，所有按键释放；
- 隐藏光标：`GetCursorInfo flags=0`，菜单在 (1909,1737) 打开并保持 ≥5 s，选择更新，
  未绑定确认关闭；结束后 flags=0x1。
- 最终 Receiver 日志：12 打开/12 关闭、7 未绑定、5 已绑定、12 次确认、0
  fault/output_error/receiver_error；最后状态 `mode_menu_close`。

**确认振动分层**：Receiver 已提交/发送（`mode_menu_feedback` + `haptic_sent`）；
Android 已接收并调用既有 CONFIRM（logcat 身份 + `performed=true`）；用户是否实际
感到振动仍待用户验收。ADB 注入不是物理手指或游戏体验验收。

## 证据路径

- 证据根与索引：`windows/test-results/mode-menu-compat-20261022/README-evidence.md`
- 构建：`build-receiver.log`、`build-tests.log`、`candidate-build.json`
- 回归：`tests-targeted.log`（30/30）、`tests-full.log`（773/773）、
  `tests-full-observer*.log`
- 恢复：`recovery/backup-20261022-1336/`、`rehearsal-result-20261022-133718.json`、
  `guard-ready.json`、`guard-result.json`、`guard-20261022-133809.log`、
  `recovery-verify/`
- 部署：`stop-receiver.json`、`stop-receiver-rehearsal.json`、
  `deployment-verify.json`、`verify-deployment.log`
- 生产链路：`phone-verify-menu-20261022-134404/`（含 `stages.log`、
  `receiver-slices.log`、`observer-*.log`、`logcat-haptic.log`）
- 失败但保留的验证尝试：`phone-verify-menu-20261022-133920/`（脚本 `$TouchX`）、
  `-133937`（Raw Input 时间戳）、`-134147`（时间解析）、`-134206`（拖动目标）、
  `-134242`（logcat 计数）、`-134324`（观察窗口前台）
- 旧根因日志：`windows/test-results/receiver-runtime/20261022-000339-592-14140/receiver.log`
  （只读，未修改）

本轮未执行 Git 写入、Android 构建/部署、配置写入、旧证据删除/迁移/压缩。
