# Mode 单键绑定鼠标键交付报告（2026-10-23）

任务标识：`20261023-mode-mouse-buttons`  
日期：2026-10-23，Asia/Shanghai  
工作目录：`C:\rightpad`  
产物根：`C:\rightpad-artifacts`  
执行环境：真实 Windows 桌面、danger-full-access、当前交互用户会话

> **状态：本任务分两个阶段完成。P1 只做源码/测试/候选/保护就绪（未部署）；P2 先修正
> 用户复核发现的保护缺陷并重新演练，随后按用户明确授权执行正式部署。**
> P2 部署已实际停止旧 Receiver（PID 24940）、替换完整运行包并由批准的独立启动器
> 启动新实例（PID 10808）；保护任务在 armed 窗口内健康放行后已注销。
> 正式 `settings.json` 与手机布局未被改动；未做真人游戏验收，未执行真实回滚。
> 下面的“源码实现”“自动化验证”“正式部署”“工程健康”“真人验收”“真实回滚”
> 分别陈述，不能互相替代。

## 0. 两阶段状态

| 项目 | P1（2026-10-23 19:29–19:57） | P2（2026-10-23 20:00–20:08） |
|---|---|---|
| 源码/测试/候选 | 完成 | 未改源码；只改保护与部署脚本 |
| 正式部署 | **未执行** | **已执行并部署成功** |
| 运行实例 | 旧版 PID 24940 | 新版 PID 10808（旧版完整包保留为 hold） |
| 独立保护 | 预检/决策/ready+idle | 预检（含互斥体）/决策/ready→armed→release→cleanup |
| 真实回滚 | 未执行 | 未执行（部署未触发回滚） |
| 真人验收 | 未进行 | 未进行（等待用户） |

## 1. 授权范围内的实现

只给 Receiver 的 Mode“单键绑定”增加鼠标左键、右键、中键（滚轮按下）：

- 新增 `ModeBindingKind.Mouse = 3`（只追加，0/1/2 含义不变），稳定标识
  `left` / `right` / `middle`。
- 三种键都支持普通按钮（Momentary）与切换按住（Toggle）。
- Ring 快捷菜单六个槽位继续只接受键盘/手柄/未绑定；鼠标不进入菜单目录、槽位校验和
  槽位选择器，且 `ModeSession.Pulse` 另有防御检查。
- 没有新增协议字段、端口、Android 控件、布局或手势；Android 未改动、未构建、未安装。
- 没有升级或修改供应商依赖、驱动、Broker、许可组件、系统环境、ACL 或执行策略；
  libvirtualhid 仍固定在 `53e1a949fc0784af716b782ddfa6c647cafd1f05`。
- 没有 Git commit/push/reset/clean，没有修改 `C:\leftpad`、DeepSeek Harness 或电脑
  操作插件，没有操作游戏，没有 ADB 注入或合成生产输入。

### 1.1 鼠标按键来源独立合并

原实现只有“移动 + 左键按下/释放”，且触摸板点击由 `LeftButtonController` 直接驱动
鼠标输出。本轮新增 `MouseButtonMerge`：

- 普通触摸板 Gesture 与 Mode 各持有自己的贡献位；提交给设备的是二者 OR 的有效状态。
- 仅当有效状态变化时提交按下/释放边沿；同一来源重复按下、另一来源已按住同键都不再
  发边沿。
- 触摸板点击/拖动收尾不释放 Mode 保持的键；Mode 撤销不释放触摸板拖动保持的左键。
- 左/右/中可同时保持；移动报告由固定版本 Windows HID 后端原样携带按键字节。
- 提交失败保留“可能已按下”的保守状态供清理/Dispose 释放；失败进入既有统计与门闩，
  不无限重试、不重放旧按键。
- 全局停止/销毁：先撤来源贡献，再由鼠标输出释放仍按下的键，最后销毁设备。

### 1.1.1 既有按钮调度、失败与锁顺序

- `LeftButtonController`（原点击调度：排队点击、25 ms 最小保持、双击拖动、取消队列）
  未改语义；它的 `down`/`up` 现在只声明/撤销 Gesture 来源的左键贡献。
- 同键已被 Mode 保持时，触摸板 `Click` 仍会成功“提交”并在 25 ms 后撤销自己的贡献，
  但有效状态不变，因此不会产生按下/释放边沿；排队点击与拖动结束也不会误释放 Mode。
- 失败语义保持：鼠标输出在调用原生前把该键记为“可能已按下”，失败后由所有者清理
  （触摸板：`ReleaseBestEffort`；Mode：撤销自身贡献）或设备 Dispose 释放；失败计数与
  原有门闩不变，不做无限重试、不重放旧按键。
- 锁顺序：`ModeSession` → `MouseButtonMerge` → `LibVirtualHidMouseOutput`；
  触摸板定时器/取消为 `LeftButtonController` → 合并器 → 输出；接收循环移动只取输出锁。
  合并器从不同调 `ModeSession`/`LeftButtonController`，因此没有反向锁边。
- 旧代次收尾：绑定变化先 `Reset`（释放旧输出、清零待释放期限、切换代次），旧期限或
  旧接触不能撤销新代次或新按钮；不会全局清除安全锁存。

### 1.2 原生桥接（兼容增量）

固定 libvirtualhid 的 Windows HID 鼠标描述符本来就是 5 键（Button 1–5），
`hid_mouse_button_bit` 映射 left→bit0、right→bit1、middle→bit2，后端在每次
移动/滚轮/按键报告中原样携带 `buttons_` 字节。因此不需要更换供应商依赖或后端。

桥接以增量方式扩展，保留旧入口：

- 新增 `rightpad_vhid_mouse_api_version()` 返回 1。
- 新增 `rightpad_vhid_button(handle, button, down)`，button 0=左、1=右、2=中；
  未知编码返回 `invalid_argument`，不会退回左键。
- `rightpad_vhid_left_down/up`、`rightpad_vhid_move`、gamepad ABI2、键盘扩展含义不变。
- 托管侧 `NativeVirtualHidMouse` 先探测扩展版本：没有扩展时左键照常，右键/中键在
  使用时返回“mouse button extension unavailable … deploy the matching bridge”，
  不猜测按钮、不静默换后端。启动日志增加 `mouse_buttons: backend=… support=…`。

## 2. 改动文件

生产代码：

| 文件 | 内容 |
|---|---|
| `windows/Rightpad.Receiver/ModeBinding.cs` | Mouse kind、三个目录项、菜单类型限制 |
| `windows/Rightpad.Receiver/ModeSession.cs` | 鼠标回调、按下/释放、失败清理、菜单防御 |
| `windows/Rightpad.Receiver/MouseButtonMerge.cs` | 新增：两来源按键合并 |
| `windows/Rightpad.Receiver/IMouseOutput.cs` | 增量 `SetButton`、`ButtonSupport` |
| `windows/Rightpad.Receiver/LibVirtualHidMouseOutput.cs` | 三键状态、逐键清理、支持度 |
| `windows/Rightpad.Receiver/NativeVirtualHidMouse.cs` | 扩展探测/版本检查/`rightpad_vhid_button` |
| `windows/Rightpad.Receiver/WindowsMouseOutput.cs` | 开发用 SendInput 的右/中键（非新降级路径） |
| `windows/Rightpad.Receiver/Runtime/ReceiverRuntime.cs` | 同一输出设备接入合并与 Mode |
| `windows/Rightpad.Receiver/Views/ControlsView.xaml(.cs)` | “选择鼠标键”入口与说明 |
| `windows/Rightpad.Receiver/Views/ModeKeyPickerWindow.cs` | 鼠标标题、槽位禁用防御 |
| `windows/native/Rightpad.VirtualHid/bridge.h/.cpp` | 新增导出，旧导出保留 |
| `windows/native/Rightpad.VirtualHid/mouse_session.hpp` | 三键位掩码、关闭时逐键释放 |
| `windows/native/Rightpad.VirtualHid/bridge_tests.cpp` | 右/中键、未知编码、关闭释放 |

测试：

| 文件 | 内容 |
|---|---|
| `windows/Rightpad.Receiver.Tests/ModeMouseButtonTests.cs` | 新增 11 项回归 |
| `windows/Rightpad.Receiver.Tests/VirtualHidTests.cs` | fake 支持三键与右键失败注入 |
| `windows/Rightpad.Receiver.Tests/ModeMenuTests.cs` | fake `SetButton`/`ButtonSupport` |
| `windows/Rightpad.Receiver.Tests/Program.cs` | 注册新测试组 |

文档：`docs/MODE_CONTROL.md`（当前契约）、`docs/MODE_CONTACT_PROTOCOL.md`（协议未变）、
`docs/NEW_CHAT_HANDOFF.md`（状态段落）、本报告。

## 3. 自动化验证（实际结果）

| 项目 | 结果 |
|---|---|
| Windows Release 构建（任务身份） | 0 警告、0 错误 |
| Windows 全量回归 | **796 / 796 通过，0 失败，0 跳过** |
| 新增鼠标键回归 | 11 项全部通过 |
| 原生桥接隔离构建 | 成功（`Invoke-Development.ps1 -Action BuildNative`） |
| 原生 fake API 测试 | `bridge_tests` **773 项检查通过** |
| 现场备份 + 包内 `Recovery.ps1 -Preflight` | PASS |
| 独立保护 Task Scheduler 预检 | PASS（context/备份/候选/现场身份/恢复预检/决策 6 例） |
| 独立保护决策演练 | PASS（idle/wait/release/wait/recover/recover） |
| 独立保护 ready + idle（P1 演示） | PASS（ready 后 60 s 未 armed，自动 idle，`mutations=false`；P2 起 armed 到期必须恢复） |
| 部署脚本 DryRun（保护运行中） | PASS（stopped/replaced/started=false；P2 最后一次 `-StartGuard`） |
| 正式部署 | **P1：未执行 / P2：已执行成功（见 §7B）** |
| 真实回滚 | **未执行（P2 部署未失败）** |
| 真人验收 | **未进行** |

新增 11 项回归覆盖：

1. 鼠标目录、三个稳定标识、摘要文本、草稿/已保存分离、统一 Save 的磁盘形状、
   Ring 槽位拒绝与整绑定回退。
2. 选择器 UI：三个大按钮、顺序/标签/尺寸、点选取草稿、不提前发布、统一 Save 返回、
   槽位选择器拒绝鼠标。
3. 旧配置兼容与非法回退：旧 kind 0/1/2、鼠标 kind 3、未知标识/空标识/非法 behavior/
   未知 kind/数字 key/含鼠标槽位全部安全回退且不选任何按钮，其他设置保留。
4. 普通按钮：单次按下、静止长按不超时、最小保持、极短点按、CANCEL 与 presence 立即释放。
5. 切换按住：重复 DOWN 不重复切换、UP 保持、迟到旧 DOWN 不切换、第二次 DOWN 撤销、
   租约失效、旧代次不能重新按下、重新同步后需新 DOWN。
6. Mode 左键保持时触摸板点按/拖动收尾不误解除，撤销后普通点击恢复。
7. 触摸板拖动保持时 Mode 同键撤销/取消不误解除，拖动结束只释放一次。
8. 右键保持 + 移动 + 普通左键点击；撤销只释放右键。
9. 合并器：双来源无重复边沿、独立释放、右/中同时保持、失败后的保守清理。
10. 生命周期：Touch 取消 vs Mode 取消、无关 Save 不清除、绑定变化释放并隔离旧接触、
    待释放期限不释放新键、run/presence/Stop/Dispose。
11. 输出失败（状态真实、清理释放、门闩阻止后续按下）、桥接扩展缺失的明确错误、
    右/中键 Dispose 顺序与清理失败上报。

### 3.1 失败与修正过程（保留）

- 首次编译：`Check(merge.DeviceMask == (1 << 1) | (1 << 0))` 缺少括号（CS0019）；已改为
  `((1 << 1) | (1 << 0))`。
- 首次编译：新增测试字段 `TouchSequence/TouchId` 未使用（CS0649）且未接入触摸；已删除。
- 首次运行 5 项失败，全部是测试自身错误，不是实现错误：
  1. 长按 2 s 后正常 UP 已超过最小保持时间，应立即释放；断言写反，已修正并另建
     “极短点按”分支验证 24/26 ms 边界。
  2. 连续两次 DOWN 未先 UP，被 Mode 协议按“已 touching”拒绝；补 UP。
  3. 右/中失败清理用例把“已出现过一次 Right up”算成 0；改为累计 2。
  4. 换 sender run 后测试仍用旧 run 7 发包被拒；harness 增加 `Run`/`Presence()`。
  5. Dispose 逐键释放顺序是 left→right→middle，期望写成 middle→right；已按实现修正。
- 脚本解析：`"Recovery start attempt $attempt: …"` 与 `"FAIL $Mode: …"` 触发
  PowerShell 变量限定符解析错误；改为 `$($attempt)` / `$($Mode)`，两脚本 parse-errors=0。
- 流程失误：用 `--receiver-touch-mode` 子集跑过一次官方 `TestWindows`，覆盖了
  `windows-tests.log`；随后重跑全量并另存 `windows-tests-full.log`，两者均为
  796/796 的完整日志。

## 4. 候选包与旧版备份

### 4.1 候选（完整运行包，不是只复制托管 DLL）

`C:\rightpad-artifacts\test-results\20261023-mode-mouse-buttons\candidate\Release`

| 文件 | 字节 | SHA-256 |
|---|---:|---|
| `Rightpad.Receiver.dll` | 529920 | `ADF44E359068C9B916408DC6EF7A224605AD01F658DE96C5C7717B468E7E180C` |
| `Rightpad.VirtualHid.dll` | 3939840 | `21A5556AF12F0FBF17085E23E59A4F97EBCBA883D6FFD8E5851EF5DD88E0F721` |
| `Rightpad.Receiver.exe` | 151552 | `B444947422873175F8B45A9206A9D870FC2853696FBB10496EE4417C546E33CE` |
| `Rightpad.Receiver.runtimeconfig.json` | 458 | `C71152BF25E40D647B2440C5B39BE157A3D356106BE9D5B678AB97BB87B4E782` |
| `Rightpad.Receiver.deps.json` | 443 | `E0857058407572B189FE53D649CBF2C482E2CB0B988731A7078096ACAF5261CE` |

EXE 是 apphost，与旧版哈希相同属预期；版本区分以 Receiver DLL 与原生桥接为准。
完整清单见 `evidence\candidate.json`。

### 4.2 旧版完整包备份

`C:\rightpad-artifacts\test-results\20261023-mode-mouse-buttons\recovery\backup-20261023-194403-146`

| 文件 | 字节 | SHA-256 |
|---|---:|---|
| `release\Rightpad.Receiver.dll` | 524288 | `F844594D8C9D12F15178C096804222F1FF16BEF6F261B35F4FB121C9A779D521` |
| `release\Rightpad.VirtualHid.dll` | 3937334 | `56829F9EC344C1C7F0C5C35C8F8BBE59AF4936ACB6ADC2B32D8931D77343BE5E` |
| `release\Rightpad.Receiver.exe` | 151552 | `B444947422873175F8B45A9206A9D870FC2853696FBB10496EE4417C546E33CE` |
| `settings.json` | 1348 | `C3E4B9CBE21246FD004BC55DDEA360C32B88D5D13CA9F5AE20D36B7F69B4C7F1` |

备份包含 EXE、托管 DLL、deps/runtimeconfig、原生桥接、许可文本、settings 与包内
`Recovery.ps1`；`manifest.json` 记录相对路径、字节数、SHA-256、正式目录、settings
目标、launcher 路径及其哈希。未移动或改写任何历史 holds/恢复包。

## 5. 现场只读核对（2026-10-23 19:44，时点值）

| 项目 | 值 |
|---|---|
| Receiver PID | 24940 |
| 创建时间 | 2026-10-23T10:12:40.3218717Z（18:12:40 +08:00） |
| EXE | `C:\rightpad\windows\Rightpad.Receiver\bin\Release\net8.0-windows\Rightpad.Receiver.exe` |
| 用户 / Session / 完整性 / 桌面 | `BAODINE77777777\zhq` / 1 / Medium (S-1-16-8192) / Default |
| UDP 50000 | `0.0.0.0:50000`，OwningProcess=24940，唯一正式实例 |
| 启动器记录 | `C:\rightpad-artifacts\receiver-runtime\20261023-181240-271-25848`，与真实进程一致 |
| 运行日志 | 同上目录 `receiver.log` |
| 正式 settings | `C:\Users\zhq\AppData\Local\rightpad\settings.json`（未改动） |
| 正式绑定现状 | `purpose=1`（快捷菜单），单键未绑定；Ring 顶部 `Esc`；本轮不改变 |
| 健康快照 | 2026-10-23 19:52 Running / Connected，MouseOutputFailures=0、GamepadFailures=0、LastError=null |

证据：`evidence\field-survey.json`。

## 6. 独立恢复保护

### 6.1 任务与记录

- 计划任务名：**`Rightpad Mode Mouse Buttons Guard 20261023`**
- 身份：当前用户 InteractiveToken、Limited(0)、无触发器、隐藏、ExecutionTimeLimit
  PT1H、MultipleInstances=IgnoreNew；动作 `powershell.exe -File …\ProtectionGuard.ps1 -Mode Guard`。
- 计划清单：`recovery\guard-plan.json`（任务身份、用户/会话、正式目录、settings、
  launcher 哈希、备份、候选全量哈希、旧实例 PID+创建时间、互斥名、期限）。
- 脚本：`recovery\ProtectionGuard.ps1`（Preflight/Decide/Guard/Recover）、
  `recovery\RunGuardTask.ps1`（注册/运行，带所有权断言）。

### 6.2 真实独立环境预检

`Task Scheduler` 实际启动（父进程 = Schedule 服务 PID 2920，非任何 agent 终端）：

- `evidence\independent-preflight.json`（P2 重新执行）：context 通过（用户/Session/Medium/
  Default、jobFlags=0、父进程=Schedule 服务）、**互斥体探针 `created/acquired/released=true`**、
  备份 10 文件逐一哈希通过、候选 8 文件哈希通过、现场正式文件与备份一致、
  settings/launcher 哈希一致、现场 PID/创建时间/UDP 归属一致、包内 `Recovery.ps1
  -Preflight` PASS、决策 6 例通过、`mutations=false`，任务 `LastTaskResult=0`。
- `evidence\independent-decision.json`：6 例（idle / wait / release / wait / recover /
  recover）通过，`mutations=false`，任务结果 0。
- P1 的 ready+idle 演示（`guard-ready.json` + `guard-idle.json`）：ready 写入后 60 s 内无
  `deployment-armed.json`，自动 idle 退出，`mutations=false`，Receiver 未被触碰。
  **P2 已改为：armed 到期仍未 release 时按 recover 处理，不再写 idle 退出。**
- 任务运行记录：`evidence\guard-task-runs.json`；日志：`evidence\guard.log`。

独立预检不修改任何运行实例或配置；它不是“真实恢复成功”。

### 6.3 决策与防竞态

- 判定：未 armed → idle；已发布且健康 → release；armed 到期仍不健康/未发布 → recover；
  其余 wait（`independent-decision.json` 记录 6 例）。P2 起，循环结束时若仍 armed 但既无
  release 也无 recover，**按 recover 处理**，不再写 idle 当作“未部署”。
- 健康（P2 起为结构健康）：正式 DLL/原生/EXE 哈希匹配候选、唯一正式进程且 PID+创建时间
  匹配发布记录、UDP 50000 归属该 PID、用户/Session/Medium/Default、启动日志关键行齐全
  且无 `receiver_error:`、飞行记录快照 Running/新鲜/LastError 为空/鼠标输出失败 0。
  `connectionState` 只记录，不否决。
- 互斥体：`Global\RightpadModeMouseButtons20261023`（真实受限计划任务里实测可创建、
  `WaitOne(0)` 可获取并成功释放）。P1 文档中写的 `Local\…` 是候选备用名，**未使用**；
  本机 `Global\` 可用，按“机器会话级命名空间、跨会话可见”保留，并在 preflight 记录
  `mutexNamespace`。
- 防竞态：部署与恢复共用该互斥（45 s 等待，进程退出自动释放）；恢复前必须核对 armed
  记录的旧 PID+创建时间与本轮清单一致；恢复只在 armed 之后、且健康失败或到期时发生；
  部署自回滚成功时保护任务只验证旧包健康后放行，不会再次停止健康实例；只处理本轮清单
  中的路径与实例。

边界：决策表的两侧是**函数级**演练；健康探测器（哈希/进程身份/端口/日志/快照）
在 P2 部署中真实运行过并健康放行，但没有对“真实部署后不健康”或“版本不符”的现场
运行过。真实恢复分支仍未执行。

## 7. 部署脚本（P2 已执行 Deploy）

`C:\rightpad-artifacts\test-results\20261023-mode-mouse-buttons\recovery\DeployRound.ps1`

默认 `-Mode DryRun`；`-Mode Deploy` 才是真实转换。全程无 `Read-Host`、无对话框、
无人工确认。

步骤与超时：

1. 读取计划与清单；`Assert-GuardReady`：`guard-ready.json` 通过且新鲜（≤120 s）、
   保护任务 State=Running、旧 PID 匹配；否则拒绝（**不停止任何东西**）。
2. 备份/候选/正式文件/settings/launcher 哈希核对，现场身份（PID+创建时间+路径+用户+
   会话+Medium+Default）与 UDP 归属核对。
3. 写 `deployment-armed.json`（含 `recoverAfterUtc = armed + 240 s`、guardPid、旧身份）。
4. `RightpadReceiverTask.ps1 -Mode Stop`（先尝试正常关闭，再仅对已核对身份终止残留），
   40 s 内确认进程退出 + UDP 50000 释放；否则失败并进入回滚。
5. 在正式父目录内暂存完整候选并逐一校验哈希；旧目录移为
   `.hold-mode-mouse-buttons-<时间>`；候选移入正式目录；复核 Receiver DLL、原生桥接、
   EXE 三个关键哈希。
6. `RightpadReceiverTask.ps1 -Mode Start`，40 s 内核对新 PID/创建时间/EXE 路径/用户/
   会话/Medium/Default/UDP 归属/无重复实例；日志要求 `mouse_backend: name=libvirtualhid`、
   `mode=raw_mouse`、`listening udp=0.0.0.0:50000`、`mouse_buttons: … support=left,right,middle`、
   无 `receiver_error:`。
7. 30 s 内观察 Android 自然重连。**停止前旧实例为 Connected 时，新实例必须在此窗口内
   自然重连，否则视为失败并回滚**；否则只记录观察。`preDeployConnected` 同时写入
   armed 与 release。
8. **60 s soak**：每 3 s 复核唯一实例、PID+创建时间、UDP 归属、快照 Running/新鲜/
   LastError 空/mouseOutputFailures=0、日志无 `receiver_error:`；任何一项失败进入回滚。
9. 写 `deployment-release.json`（新身份、三个哈希、settings 哈希、日志、重连、soak 次数）。
10. 等待 guard 写出 `guard-released.json`（≤120 s），然后注销本轮 guard 计划任务，
    确认 `Rightpad Receiver Dev` 仍存在且新 Receiver 仍在运行，写 `cleanup.json`。
11. 任一步失败 → `Invoke-Rollback`：停止新实例（如已启动）→ 恢复旧完整包与 settings →
    通过 launcher 启动 → 核对旧包哈希与健康 → 写 `deployment-rollback.json`；
    写 `deployment-failed.json`（含 `rolledBack`）后以非 0 退出。保护任务据
    `deployment-failed.json` 复核；旧实例仍不健康时由保护执行恢复。

`-StartGuard`：部署脚本自己完成「注册 guard（`-Mode Guard -DeadlineSeconds 900`）→
`Task.Run` 异步启动 → 轮询**本次实例**写出的 `guard-ready.json`（passed、≤120 s、
State=4、`at` 不早于启动时刻）」，超时在停止任何东西之前失败。启动前删除旧 ready，
避免上一次演示 guard 的记录被误当成本次就绪。

预算与期限：`deployTotalBudgetSeconds=300`（stop 40 + start 40 + 重连 30 + soak 60 +
余量）；`deployRecoverAfterSeconds=300`；`guardDeadlineSeconds=900`。保护窗口在任何
停止动作前开始，且在 release 后由 guard 健康放行结束。

**DryRun 演练**（共四次；最后一次为 P2 修正后的 `-Mode DryRun -StartGuard`）：
`PASS deployment dry run (no stop, no replace, no start)`。最终证据
`evidence\deploy-dry-run.json`：guard 由脚本启动、ready 来自本次实例、`guardMutexOk=true`、
本地互斥体探针 `created/acquired/released=true`、`preDeployConnected=true`、备份 10 /
候选 8 文件校验、正式文件与备份一致、launcher 视图一致（PID 24940 / libvirtualhid）、
候选整体暂存并校验后删除、正式父目录可写、`stopped/replaced/started=false`。

### 7A. P2 保护缺陷修正（用户复核后）

1. **保护启动竞态**：`-StartGuard` 由部署脚本注册并异步启动 guard，再等待本次实例的
   ready；plan 期限改为 900 s，`recoverAfter=300 s`，预算 300 s。部署不再先阻塞
   `RunGuardTask` 再 arm。
2. **互斥体真实受限上下文验证**：guard `Preflight` 与部署 arm 前都做创建 +
   `WaitOne(0)` + `Release`/`Dispose` 探针；不可用则 preflight 失败/部署在停止前退出。
   在真实计划任务中以 Limited 令牌实测 **`Global\RightpadModeMouseButtons20261023`
   `created=True acquired=True released=True ok=True`**，因此保留 `Global\` 命名空间
   （机器会话级，跨会话可见；本机未出现 `SeCreateGlobalPrivilege` 受限）。证据见
   `independent-preflight.json` 的 `mutex`/`mutexNamespace` 与 `guard-ready.json`。
3. **健康判定不再把 Android 掉线当失败**：改为结构健康（唯一实例+PID/创建时间、
   三个哈希、UDP 归属、用户/Session/Medium/Default、启动日志齐全且无 `receiver_error:`、
   快照 Running/新鲜/LastError 空/mouseOutputFailures=0）；`connectionState` 只记录。
   反向规则：**停止前 Connected 则新实例必须 30 s 内自然重连**，否则回滚。
   `Test-RestoredHealth` 同样改为结构健康并记录连接状态。
4. **release 前 60 s soak**：每 3 s 复核，全部通过才发布；任何失败进入回滚。本次实际
   `soakChecks=19`。
5. **收尾**：guard 写出 `guard-released.json` 后注销本轮 guard 任务，确认
   `Rightpad Receiver Dev` 存在且新 Receiver 运行，写 `cleanup.json`；保留备份、hold、
   日志与证据，不停新 Receiver。

部署过程中另发现并修正一个握手缺陷（记录在案）：`Start-GuardIfRequested` 最初会接受
上一轮演示 guard 留下的 ready（本次 9.2 s 前，20:05:11.675），而不是本次实例的新 ready。
已改为启动前删除旧 ready 并要求 `ready.at ≥ 启动时刻`。**该缺陷未影响本次部署的窗口
覆盖**：实际保护者是新 guard 实例 PID 33300，它在 20:05:30.157 写出自身 ready
（preflight context 20:05:29），旧 Receiver 在 20:05:32.475 才真正退出，因此“旧进程
已停而 guard 未就绪”的空窗没有发生；`guard.log` 记录了两条 ready 与最终的
`independent health release`。修正后未再演练，因为部署后现场已不满足部署前不变量
（正式包已是新版，preflight 会按设计失败）。

## 7B. 正式部署实际记录（2026-10-23 20:05–20:06，Asia/Shanghai）

| 项目 | 实际值 |
|---|---|
| 授权 | 用户 P2 明确授权本轮正式部署 |
| 执行方式 | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File DeployRound.ps1 -Mode Deploy -StartGuard`（本机没有 PowerShell 7，`pwsh` 不存在；用项目既有的 Windows PowerShell 5.1 主机，无任何 Read-Host/对话框） |
| 旧实例 | PID 24940，创建 2026-10-23T10:12:40.3218717Z，正常关闭优先后按身份核对终止，20:05:32 +08:00（UTC 12:05:32）确认退出 + 端口释放 |
| 新实例 | **PID 10808**，创建 **2026-10-23T12:05:42.0488541Z**（20:05:42 +08:00） |
| EXE 路径 | `C:\rightpad\windows\Rightpad.Receiver\bin\Release\net8.0-windows\Rightpad.Receiver.exe` |
| 用户 / Session / 完整性 / 桌面 | `BAODINE77777777\zhq` / 1 / Medium (S-1-16-8192) / Default |
| 实例数 / 端口 | 唯一正式实例；`0.0.0.0:50000` owner=10808 |
| 已加载模块 | `Rightpad.Receiver.dll` 与 `Rightpad.VirtualHid.dll` 均从正式目录加载（`Get-Process -Module`） |
| 正式三个哈希 | Receiver `ADF44E…E180C`、原生 `21A555…0F721`、EXE `B44494…33CE`（与候选一致） |
| 启动日志 | `C:\rightpad-artifacts\receiver-runtime\20261023-200542-001-32448\receiver.log` |
| 日志关键行 | `mouse_backend: name=libvirtualhid`、`mouse_buttons: backend=libvirtualhid support=left,right,middle`、`listening: udp=0.0.0.0:50000`、`motion_mode: name=M_R1`；`receiver_error:` 0 条 |
| Android 重连 | 停止前 Connected=true；新实例 **自然重连成功**，senderRunId `6547286BB9DA19ED`，心跳年龄约 0.1 s |
| soak | 60 s，19 次结构复核全部通过 |
| guard 证据 | ready `12:05:11.673Z`（演示实例）/ 本次实例 `12:05:29.48Z`；armed `12:05:21.854Z`（recoverAfter `12:10:21.854Z`）；release `12:06:46.500Z`；`guard-released.json` `12:06:46.970Z` healthy、connectionState=Connected、pid=10808 |
| 收尾 | `cleanup.json` `12:06:48.639Z`：guard 任务已注销、`Rightpad Receiver Dev` 存在且 State=4、Receiver 运行中 |
| 正式 settings | 哈希仍为 `C3E4B9CBE21246FD004BC55DDEA360C32B88D5D13CA9F5AE20D36B7F69B4C7F1`；`modeControl` 只读记录：`purpose=1`（快捷菜单）、`kind=0`、`behavior=0`、`key=""`、顶部槽位 `kind=1 key="Esc"` |
| 手机布局 | 未操作 Android，未运行 ADB；布局保持原样（依据交付证据与“未操作设备”的事实，不声称本轮重新核对） |
| 回滚 | 未发生（无 `deployment-failed.json` / `deployment-rollback.json`） |
| 旧版保留 | hold `C:\rightpad\windows\Rightpad.Receiver\bin\Release\.hold-mode-mouse-buttons-20261023200532643`；备份 `recovery\backup-20261023-194403-146` 保留 |

部署后复核证据：`evidence\post-deploy-verification.json`。

## 8. 部署后状态、未做与风险

- **正式部署：已完成**（P2，2026-10-23 20:05–20:06）；新版本已实际加载并在运行。
- **工程健康（部署后自动化观察）**：唯一实例、端口归属、三个哈希、上下文、日志关键行、
  60 s soak、Android 自然重连均通过；`lastError=null`、鼠标/手柄输出失败均为 0。
  这不等于“用户已恢复实际控制”。
- **真人验收：未进行**。左键 Toggle 后单指拖动、右键 Toggle 后视角控制、盲操作与游戏
  手感都需要用户后续确认。本轮没有向生产输入注入任何鼠标/键盘/手柄事件
  （新实例 `mouseOutputSuccesses=0`）。
- **真实回滚：未执行**。本次部署没有失败，没有触发 `Invoke-Rollback`；回滚分支只有
  DryRun/函数级验证，不能声称真实回滚成功。
- **未真实演练“部署后不健康”**：guard 的恢复分支只在决策表与预检逻辑上验证过。
- 健康判定改为结构健康后，手机临时离线不会触发回滚；但“停止前 Connected 时必须自然
  重连”仍是硬性条件（本次满足）。
- 鼠标键依赖新增桥接导出；部署脚本强制核对三个关键哈希，避免只更新托管 DLL。
  若桥接缺少扩展，左键仍可用，右键/中键会返回明确的扩展不可用错误。
- `MouseOutputStats.ButtonFailures` 现在包含右/中键失败；停机日志仍沿用历史字段名
  `leftButtonFailures=`，是既有标签，未改日志格式。
- Mode 已保持左键时触摸板点按不产生新边沿，但手机仍可能收到该次点按原有的普通点击
  确认（既有通道未改），属已接受的自然行为。
- 已知记录瑕疵：本次 `cleanup.json` 的 `remainingRightpadTasks` 字段用可见任务枚举
  （`GetTasks(0)`），因此没有列出隐藏的 `Rightpad Receiver Dev`；该任务已单独用
  `devTaskExists=true` 验证，`post-deploy-verification.json` 用 `GetTasks(1)` 复核为
  `["Rightpad Receiver Dev"]`。脚本已修正为包含隐藏任务。
- 部署前发现的 guard ready 握手缺陷已修正（见 §7A），修正后因现场已不满足部署前
  不变量而无法重新演练；下一次部署应使用修正后的脚本。

## 9. 证据索引

| 内容 | 路径 |
|---|---|
| 任务目录 | `C:\rightpad-artifacts\test-results\20261023-mode-mouse-buttons` |
| 构建日志 | `evidence\windows-build.log` |
| 全量回归日志 | `evidence\windows-tests.log`、`evidence\windows-tests-full.log` |
| 原生构建/测试 | `evidence\native-build.log`、`evidence\native-bridge-test.log` |
| 候选清单 | `evidence\candidate.json` |
| 现场调查 | `evidence\field-survey.json` |
| 保护预检/决策 | `evidence\independent-preflight.json`、`independent-decision.json` |
| 保护 ready / armed / released / cleanup | `evidence\guard-ready.json`、`deployment-armed.json`、`guard-released.json`、`cleanup.json`、`guard.log`、`guard-task-runs.json` |
| 部署 DryRun | `evidence\deploy-dry-run.json` |
| 部署过程 | `evidence\deploy.log`、`deploy-mutex.json`、`deploy-old-stopped.json`、`deployment-release.json` |
| 部署后复核 | `evidence\post-deploy-verification.json` |
| 旧版恢复包 | `recovery\backup-20261023-194403-146`（含 `manifest.json`、`Recovery.ps1`） |
| 保护/部署脚本 | `recovery\ProtectionGuard.ps1`、`recovery\RunGuardTask.ps1`、`recovery\DeployRound.ps1`、`recovery\guard-plan.json` |

## 10. 独立复核补充（Computer Use + 受限任务探针，2026-10-23 20:08–20:14）

本节由本轮 Computer Use 复核代理在部署后独立执行，用于补齐 Code agent 自述之外的证据。

### 10.1 互斥体“创建缺失对象”能力（此前证据不充分）

原 `independent-preflight.json` 的探针在部署持锁期间运行，记录为
`created=true, acquired=false, heldByOther=true` —— 这只证明能**打开已存在**的
`Global\` 对象，没有证明受限令牌能在对象不存在时**创建**它（恢复路径真正需要的能力）。

补充验证：注册一次性临时计划任务（当前用户 InteractiveToken / Limited / Session 1 /
无触发器），在独立进程中执行 `New-Object System.Threading.Mutex($false, …)`：

| 探针 | 结果 |
|---|---|
| 创建此前不存在的 `Global\RightpadProbeCreateAbsent20261023` | `createdNewForAbsent=true`，`acquired=true` |
| 打开且获取 `Global\RightpadModeMouseButtons20261023`（部署已退出、无人持有） | `planAcquired=true` |

说明：`Global\` 在本机受限上下文可用，恢复分支第一行不会因权限抛错；临时任务随即注销，
复查仅剩 `Rightpad Receiver Dev`。证据：`evidence\postdeploy-mutex-probe\result.json`。
该探针不修改任何正式文件或运行实例。

### 10.2 Receiver 界面检查（只改草稿，未 Save）

| 检查 | 实际观察 |
|---|---|
| Mode 用途切换 | 点“单键绑定”后出现单键区；已保存摘要仍为“电脑快捷菜单 · 六个按钮” |
| 单键区按钮 | 选择键盘键 / 选择手柄键 / **选择鼠标键** / 未绑定 |
| 鼠标选择器 | 标题“Mode · 选择鼠标键”，三个大按钮：鼠标左键、鼠标右键、鼠标中键（滚轮按下） |
| 选中状态 | 选中按钮蓝色边框，顶部草稿摘要即时更新（右键→“鼠标右键 · 普通按钮”；中键→“鼠标中键（滚轮按下）· 普通按钮”） |
| 行为切换 | 点“切换按住”后草稿为“鼠标中键（滚轮按下）· 切换按住” |
| 返回与 Save | “完成选择 · 返回统一 Save”回到主界面；全程未点击 Save |
| 运行状态行 | 按已保存配置显示“电脑快捷菜单 · 六个按钮 · 已释放” |
| Ring 槽位选择器 | 仅“选择键盘键 / 选择手柄键 / 未绑定”，**没有鼠标项** |
| 草稿恢复 | 结束前恢复为“电脑快捷菜单 · 六个按钮”，与已保存一致 |

`settings.json` 在检查前后 SHA-256 均为 `C3E4B9…C7F1`，mtime 仍为 2026-10-22 11:34:03，
即界面操作只影响内存草稿。证据：`evidence\independent-gui-verification.json`。

### 10.3 边界

以上补齐的是**能力与界面**证据，不改变 §8 的结论：真实鼠标右键/中键输出、左键
Toggle 单指拖动、游戏视角控制、盲操作手感与真实回滚仍由用户验收，未在本轮执行。