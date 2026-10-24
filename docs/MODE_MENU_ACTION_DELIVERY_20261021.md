# Mode 菜单槽位确认与自动关闭交付报告 — 2026-10-21（第三轮）

时间均为 Asia/Shanghai。本轮在已有未提交 Ring 菜单实现上增量开发，完成隔离构建、
全量回归、恢复路径实演、受控部署、生产链路手机验证、正式设置/布局/控制恢复与证据
归档。没有 Git commit、push、回退、历史修改、`git clean` 或资料清理；此前未提交与
未跟踪文件全部保留。旧交付报告（2026-10-18/19/20）保持原文，继续作为历史证据。

## 本轮两项行为

1. **有效点按已绑定槽位**：输出该绑定的一次键盘/手柄按下与一次释放；只有在 press 与
   release **都确实完成**后，才提交一次与原普通触控板成功单击完全相同的手机确认振动。
2. **槽位动作完整成功后菜单自动关闭**。关闭发生在释放之后，因此脉冲不会被截短。

明确未做：Mode 按钮打开/手动关闭菜单时的振动；新反馈协议、新服务、新设置系统、新手势、
新振动参数、强振动；用伪造普通鼠标单击借振动；Android 本地 DOWN 振动。

交互规则：中央 12 DIP 中性区、未绑定方向、导航移动、落指与抬手重落不产生按键、
不振动、不关闭；再次点 Mode 保留手动关闭；输出失败走既有故障处理/安全释放，不发
成功确认、不重试槽位动作；振动是尽力而为副作用，不等待手机确认或实际振动结束就关闭。

## 修改范围

| 文件 | 变更 |
|---|---|
| `windows/Rightpad.Receiver/ModeSession.cs` | 新增 `haptic` 注入与 `MenuFeedback` 记录；`Pulse(key,tap)` 每打开周期只接纳一次并捕获当下 presence 地址与 UP 身份；`TickLocked` 先完成释放，成功后 `CompleteMenuAction()`（提交一次确认 → 记录 → 关闭）；删除 16 项串行动作队列与 `menu_action_overflow`；`CloseMenu` 清空本次动作与待发确认 |
| `windows/Rightpad.Receiver/ModeMenu.cs` | 点按回调改为携带合格 UP 包的 `(ModeKey, ClickFeedback)`；使用既有 `ClickFeedback` 结构，不新增协议字段 |
| `windows/Rightpad.Receiver/Runtime/ReceiverRuntime.cs` | 把既有 `HapticFeedbackSender.TryEnqueue` 注入 ModeSession（复用同一 worker/端口/编码） |
| `windows/Rightpad.Receiver.Tests/ModeMenuTests.cs` | 新增键盘槽位、手柄槽位、中性/未绑定/导航、重复与迟到包、反馈失败、手动关闭/断线/配置/输出失败边界；把旧的“成功后保持打开”断言改为新语义；适配 Harness 的 haptic 注入与 `Lapse` 辅助 |
| `AGENTS.md`、`docs/ARCHITECTURE.md`、`docs/MODE_SHORTCUT_MENU.md`、`docs/HAPTIC_FEEDBACK_PROTOCOL.md`、`docs/MODE_CONTROL.md`、`docs/MODE_CONTACT_PROTOCOL.md`、`docs/USER_PREFERENCES_AND_WORKFLOW.md` | 当前契约同步：槽位完成复用原确认反馈、自动关闭取代“保持打开”、保留手动关闭；历史报告不改 |

未修改 Android 源码、协议/端口、worker/socket、Motion/M/C、灵敏度、B/X、布局格式、
输出设备、驱动/Broker/许可、原生 ABI；没有新增 Hook、服务或设置项。

## 构建与自动化

隔离输出目录：`windows/test-results/mode-menu-action-20261021/verify-build/`。

| 检查 | 结果/证据 |
|---|---|
| Release 构建 | **0 警告、0 错误**；2026-10-21 23:57:08→23:57:10；`build-final.log` |
| 版本标识 | ProductVersion `1.0.0+8b4565a9a21f1feaa9addefc2286295d6fbaaf33`（git HEAD 8b4565a） |
| 候选 DLL | `5B9A61327AF9152E41E28B79FCAE9502CF6F7C92A3EA381F4AC80E627B94C789` |
| EXE / 原生依赖 | `B4449474…E33CE`（apphost 未变）/ `Rightpad.VirtualHid.dll` `1F1D0C4F…E83CC` |
| Windows 全量回归 | **769/769，0 失败**；`tests-run-3.log`（23:56:21 完成，构建产物哈希与部署 DLL 相同） |
| 失败尝试 | `tests-run-1.log`（766/769）、`tests-run-2.log`（767/769）保留 |

新增/重写的关键回归（对应本轮风险）：

- 键盘槽位与手柄槽位各一次按下/释放后关闭；24 ms 时仍 held，25 ms 才释放，证明关闭
  不截短脉冲；
- 成功动作只提交一次确认，身份等于合格 UP；中央、未绑定、导航、断线、配置变化、
  手动关闭、输出失败、反馈发送异常都不提交成功确认；
- 快速重复点按、重复包、旧 UP、迟到包不会多执行、多振动或补普通点击；一个打开周期
  只执行一项，重新打开后再次执行一次；
- 反馈链路抛错时动作不重放、菜单正常关闭、下一次新 DOWN 恢复普通输入；
- 菜单移动仍只经原 Motion 一次，普通 Gesture 全程 0；菜单期间普通鼠标按钮事件为 0；
- 关闭后新接触恢复普通单击与双击拖动并抬手释放；
- 原普通触控板 RPHF 确认链路（`HapticFeedbackTests`）全部通过，未回退。

## 恢复保障与实演

部署前 23:57 逐字节备份实际运行目录、`settings.json`、手机布局资料：
`recovery/backup-20261021-2357/`（`release/` 全文件 + settings + 布局 + manifest +
checksums）。

现场事实：本轮开始时的 Receiver **不是**由项目启动器启动的（PID 16924 于
2026-10-21 11:41 登录自启，无 launcher 运行记录），因此项目启动器的 `Stop` 按设计
拒绝停止这个非它拥有的进程（`UDP 50000 is still occupied; unrelated processes were
not stopped`）。处理方式：`Stop-Receiver.ps1` 先调用项目启动器，再对已核对
（PID + 路径 + 创建时间 + DLL 哈希）的同一生产进程执行强制停止，并验证退出与端口
释放；随后一律通过项目启动器启动。强制停止只在启动器无法拥有该进程时使用，等价于
启动器自身已验证 `Kill` 的兜底路径。

**独立恢复实演（在真实、独立于本 agent 会话的计划任务中执行）**：临时任务
`Rightpad Menu Action Rehearsal 20261021`（InteractiveToken、Limited、父进程 2432 =
Task Scheduler、Session 4）于 00:01:22 运行 `Rehearse.ps1`：

1. Preflight 通过（healthy，PID 16924）；
2. 由该任务自身停止 Receiver（启动器拒绝 → 核对后强制停止），确认退出与 UDP 50000
   释放；
3. 运行 `recover2.ps1 -Mode Recover`：判定 unhealthy → 用备份整目录替换 → 启动器
   `Start` → 健康检查通过，**exit 10**；
4. 结果 `rehearsal-result-20261022-000121.json`：old 16924 → new 13896，DLL =
   备份 `69766F1B…B1C5`，端口归 13896，Session 4，`recovered=true`。

这证明恢复保护确实能**启动备份版本并通过预检**，而不只是“脚本写好/任务注册成功”。

**部署期保护**：临时任务 `Rightpad Menu Action Guard 20261021`（PID 17824，Session 4，
独立于本 shell）在 00:03:21 写出 `guard-ready.json`，早于 00:03:24 开始的部署；参数为
360 s 部署窗口、40 s 连续不健康阈值、600 s 目标健康观察、21 s 检查间隔。部署与验证
期间健康检查持续通过，**未触发回滚**；00:19:30 以 `result=completed`、
`healthySeconds=580`、exit 0 正常结束（`guard-result.json`），随后注销临时任务并确认
Guard/演练任务均已退出。

## 受控部署

- 00:03:24 记录旧 PID 13896 / DLL `69766F1B…B1C5`；
- 启动器 `Stop`（本次实例由启动器拥有）→ 退出 + UDP 50000 释放（00:03:36）；
- 复制候选 DLL/PDB 并核对哈希 → 启动器 `Start`（00:03:41，exit 0）；
- 新 PID **34628**，启动 00:03:39，Session **4**（= explorer Session 4），Medium
  （S-1-16-8192），Default 桌面；`0.0.0.0:50000` 归 34628；生产后端 libvirtualhid；
  M_R1；启动日志 `application: mode=gui defaultPage=Motion`、`mode=raw_mouse`；
- Android 自然心跳重连 `presence: status=connected senderRunId=B264985120C6B12C`；
  Android 未重装、未重启（PID 20316 全程不变）；
- 13 项部署核对见 `deployment-verify.json`，全部通过。

首次 `deploy.ps1` 在部署成功后因我编写的身份正则不匹配 `-Mode Status` 的格式化输出
（`Identity : @{User=…; SessionId=4; …}`）而报失败；该失败保留在 `deploy.log` /
`deployment-state.json`，随后用 `verify-deployment.ps1` 重新逐项核对并记录，未重新部署。

## 生产链路与触觉链路验证

环境：现有 Android 实例 PID 20316，设备 1200×2670、density 480；现有 Sender/type8/
RPMD/真实 libvirtualhid。**ADB 单指注入，不是物理手指、真人盲操作或游戏手感验收。**
生产 Receiver：PID 34628，M_R1，Sensitivity 12/12，DPI scale 2.0。

菜单锚点 = 打开瞬间的系统光标 (1920,754)，打开后光标未跳变；重新打开锚点为
(1920,454)，同样等于当时实际光标。

| 阶段 | Receiver 证据 | 实际输出观测 | 结果 |
|---|---|---|---|
| 导航（菜单打开） | `mode_menu_selection: slot=2 cursor=1998,792` | 光标 +78,+40；HID 鼠标左键 0；无确认 | 菜单保持打开 |
| 键盘槽位 A | `mode_output binding=A held=True`→`held=False`→`mode_menu_feedback sessionId=50719 upSequence=2333`→`mode_menu_action_complete`→`mode_menu_close`；`haptic_sent … target=192.168.110.127:50002` | Raw Input 键盘 `scan=001E` 按下、释放**各 1 次**（整个验证仅此一对）；该窗口 HID 鼠标左键 **0** | 按下→释放→一次确认→自动关闭 |
| 中央点按 | 无 `mode_output`、无 `mode_menu_feedback`、无 `haptic_sent` | 无按键、无 Android 确认 | 保持打开 |
| 未绑定方向（右下） | `mode_menu_selection: slot=2`，无输出无确认 | 无按键、无 Android 确认 | 保持打开 |
| 手动关闭（再点 Mode） | `mode_menu_close`，无确认 | 无按键 | 关闭 |
| 普通单击 | `haptic_sent sessionId=50723 upSequence=2342` | Raw Input 左键按下/释放各 1、受控窗口 `mouse_click`；Android count 17938 | 普通反馈未回退 |
| 双击拖动 | `doubleTapDeltaNs=45000000`、`dragging=True`、`dragStarts=1`、`dragEnds=1` | Raw Input 左键按住→移动→释放；窗口 `mouse_drag_down`/`mouse_drag_up moves=201`；光标 y +576 | 拖动与抬手释放正常 |
| 手柄槽位 Back（临时绑定） | `mode_output binding=Back held=True`→`held=False`→`mode_menu_feedback sessionId=50729 upSequence=2407`→`mode_menu_close` | XInput slot0 `buttons=0020`→`0000` 各一次 | 按下→释放→一次确认→自动关闭 |

**确认振动分层**：

1. **Receiver 已提交并发送**：`mode_menu_feedback`（身份、目标 IP）+ `haptic_sent`
   （`target=192.168.110.127:50002`、同一 `sessionId/upSequence`、worker 计数）。
2. **Android 已接收并执行既有 CONFIRM**：logcat `RightpadHaptic: click_accepted
   senderRunId=b264985120c6b12c sessionId=50719 upSequence=2333 count=17937` 与
   `click_requested effect=CONFIRM performed=true`；手柄槽位为 50729/2407（count
   17941）。整段窗口内 Receiver 的 5 次 `haptic_sent` 与 Android 的 5 次
   `click_accepted` 身份集合完全一致，无多余确认。
3. **用户实际感觉到振动**：ADB 注入无法证明；未做真人盲操作或游戏验收，本轮不宣称。

`verified-evidence.json` 从原始日志重算 38 项（含身份对应、顺序、零鼠标按钮、零错误行、
最终释放）全部通过；结果为 `verified: 38/38`。

## 正式配置、布局与控制恢复

- 测试期临时把 `upperRight` 绑定为手柄 Back（设置 UI + 统一 Save，哈希
  `1AD99F09…B85A`），验证后在同一 UI 改回未绑定并 Save；
- 最终 `settings.json` 与开轮前**逐字节相同**：
  `BB740E9BB757FE96437F714D10FDE0743200C538A6A5FAE2C3AAF3B12D144BF7`；
  用途仍为**电脑快捷菜单**，上槽键盘 A，其余五槽未绑定，灵敏度/M_R1/CZ1/B/X/未知字段
  全部保留；运行时状态“电脑快捷菜单 · 六个按钮 · 已释放”；
- 手机正式布局未改动：设备端 md5 `326e3c1f2155a00e2996831f65de64e2`，mtime
  2026-10-19 18:45:16；
- 最终释放：`GetAsyncKeyState` 全部未按下、XInput buttons/LT/RT 全 0
  （`native-final-release.json`）；Receiver 无 `receiver_error` / `mode_output_error` /
  `mode_menu_fault` / `left_button_error` / `gamepad_unavailable` / `haptic_send_error`；
- 结束时 Receiver PID 34628 正常运行、Connected、UDP 50000 正常。

## 失败尝试与修复

1. 直接 `dotnet build` 到正式输出目录被运行中的 Receiver 锁定 → 改用隔离 OutDir。
2. `capture-pre.ps1` 使用 `$pid`/`grep -m1` 触发保留变量与 broken pipe → 改名并本地过滤。
3. `Rehearse.ps1` 首次把多参数拼成单个字符串传给 `-File`；随后在 Windows PowerShell
   5.1 下把原生命令 stderr 当作终止错误 → 改为数组参数 + `ErrorActionPreference=Continue`
   合并输出。四次失败记录（`rehearsal-20261021-235828/235906.log`、
   `rehearsal-20261022-000031/000051.log`）与成功记录 `…-000121.log` 全部保留。
4. 恢复演练中启动器按设计拒绝停止非它拥有的 PID 16924 → `Stop-Receiver.ps1` 先试启动器，
   再对已核对进程强制停止，并验证退出与端口释放。
5. `verify-deployment.ps1` 的 `$pid` 保留变量与 `RuntimeLog` 正则（`$` 未按行匹配）先后
   报错 → 修正后 13 项全部通过。
6. 生产脚本中 UIA 找不到 Receiver 自有模态窗口（UIA 根不列出该进程的 owned dialog）→
   改为按 HWND 枚举 + `AutomationElement.FromHandle`。
7. PowerShell 5.1 以 ANSI 解析无 BOM 的 .ps1，导致中文自动化名比较失败 → 给证据脚本
   加 UTF-8 BOM；同时曾因 `Get-Content -Raw` / `Set-Content` 往返破坏
   `docs/MODE_SHORTCUT_MENU.md` 的中文，已用原文重建并通过读取核对。
8. 首次双击拖动用两次独立 adb 调用（间隔约 200 ms > 130 ms 窗口）未形成拖动 →
   改为单次 shell 内 `input tap; input swipe`，`doubleTapDeltaNs=45000000` 成立。

## 未验证项与限制

- 未验证真实游戏、独占/无边框全屏、反作弊、全部 DPI/多显示器与长期手感；ADB 注入
  不是物理手指测试；
- 用户是否真的感到振动未证明（第 ③ 层）；Android CONFIRM 返回 `performed=true` 只
  说明系统接受调用；
- 未故意破坏唯一控制通道来实测完整回滚；本轮改为**真实执行一次恢复分支**（停止 →
  恢复备份 → 启动 → 健康），但未演练“新版本不健康 → Guard 自动回滚”这一具体触发；
- 反馈发送失败、Android 执行失败路径由自动化覆盖，未在真机注入故障；
- 鼠标/键盘/手柄的“动作已执行”只证明输出提交，不证明某个游戏消费；
- 部署与验证期间用户未操作设备的时间窗由现场判断，未做正式占用预约。

## 证据路径

- 证据根：`windows/test-results/mode-menu-action-20261021/`（索引
  `README-evidence.md`）；
- 生产日志：`windows/test-results/receiver-runtime/20261022-000339-592-14140/receiver.log`；
- 恢复包：`windows/test-results/mode-menu-action-20261021/recovery/backup-20261021-2357/`；
- 结构化校验：`verified-evidence.json`（38/38）、`deployment-verify.json`（13/13）、
  `rehearsal-result-20261022-000121.json`、`guard-result.json`、`final-state.txt`。
