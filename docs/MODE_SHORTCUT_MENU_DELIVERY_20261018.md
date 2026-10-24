# Mode 电脑快捷菜单交付 — 2026-10-18

时间均为 Asia/Shanghai。本轮完成源码、文档、自动化验证、受控部署和实际输入恢复。
**没有 commit、push、历史修改、git clean 或用户资料清理。** 原有四个未跟踪资料文件
保留；回归测试产生的 `panel-preview/` 也保留。功能契约见
[MODE_SHORTCUT_MENU.md](MODE_SHORTCUT_MENU.md)。

## 交付状态与操作

Controls → Mode 可选择原单键绑定或电脑快捷菜单。菜单配置窗口用两种可点击预览
选择完整环形/六向独立大按钮，六槽共用绑定。点击槽位使用原目录选择键盘、手柄或
未绑定，回主界面底部统一 Save 后才应用。全程实际用左键核对用途、两外观和六槽；
前三槽临时绑定键盘 A、手柄 A、LT，其余三槽明确选择未绑定。测试后均通过同一界面
撤销，正式设置恢复任务前的 **G / 普通按钮 / 单键用途**，六个新增槽位全部未绑定。

没有写入参考图中的游戏默认动作。菜单打开状态不落盘。Android 源码、APK、正式布局
和静态 M 标签均未改；没有为 Receiver 更新重启 Android，没有更新 Driver/Broker/许可。
M-R1、C-Z1、用户保存 C 值、灵敏度和已接受 B/X 行为/参数保留。

## 本轮证据与自动化

证据根目录：`C:\rightpad\windows\test-results\mode-menu-20261018`。

| 检查 | 本轮结果 | 证据 |
|---|---|---|
| 第一轮 Windows 全量 | 752/752 | `tests-first.log` |
| 加入生产 UDP/C 清理与呈现超时保护后的全量 | 754/754 | `tests-second.log` |
| 最终候选全量 | **754/754，0 失败** | `final-tests.log` |
| 最终 Release/native 依赖构建 | **0 警告、0 错误**；native 无源码变化 | `final-build.log` |
| 11 组新增菜单检查 | 全部通过，包含共享六槽、两外观、协议去重、输出和生命周期 | 同上 |
| 两外观几何/渲染 | 280、480、720 视口；负坐标/边缘及 100/150/200% 几何 | `render/` |
| 隔离真实浮层可行性 | 本机 200% DPI；隐藏并约束测试目标光标，35 个观测点菜单打开且前台仍为目标 | `overlay-probe.log`、`overlay-probe-render.png` |
| 保存 | 旧 G/Toggle 兼容、草稿隔离、失败保留、未知属性、相同配置不重开 | `final-tests.log` |

新增自动化还覆盖六槽一次完整脉冲、25 ms 最小可见时间、快速点击串行、关闭/配置
变化撤销排队工作、未绑定无输出、中性/菜单外点击、丢失/乱序/重复/迟到 Mode 事件、
600 ms lease、presence/new run/CANCEL/Stop/Dispose、后端/显示错误及呈现 2 s 超时、
恢复时仅新 DOWN 进入正常路径。生产 UdpReceiver 级检查使用现有 ResampledMotion
C 路径验证进入清理及菜单导航/点击不进入正常输出。B/X 检查验证旧刷新不能复活，
Neutral 后新按压恢复，以及新 Sender 清除旧 run 的模态屏障。

原 743 组 Windows 回归参与每次全量：单键普通/切换、Motion 指纹、鼠标手势、设置、
布局、协议及手柄。没有把历史报告测试数量或原生测试数量加到本轮结果中。

失败及修正证据均保留：首次还原因沙箱不能读取用户 NuGet.Config 失败
（`first-build.log`），改用已还原依赖及获批构建；首次编译的 WPF/WinForms 同名类型
歧义修正见 `first-compile.log`；补充测试最初使用错误构造签名，见 `build4.log`，
后续修正并通过全量。DPI 使用 WPF PerMonitorV2 manifest，WinForms 仅供托盘使用，
因此明确抑制不适用的 WinForms DPI 配置诊断 WFAC010。

## 真实设备输出与输入归属

本轮通过 ADB `input` 在当前正式手机矩形内注入单指操作，经过生产 View/Sender、
type8/RPMD、Touch UDP、生产 Receiver 和真实 HID/Xbox 输出；不写正式布局。
这些是功能注入证据，**不是用户真人盲操作、长期舒适度或游戏体验验收**。

`actual-input.log` 记录 Raw Input、实际设备路径、XInput、鼠标单击/拖动；
`verification-stages.log` 给出每段操作的时间。初次代表性输出在首个部署候选上完成；
最终候选只增加新 Sender 清除 B/X 旧屏障的边界修正，最终 PID 上又复验菜单、
实际鼠标、G 和 B/X。没有把首个候选 PID 冒充最终进程。

| 实际检查 | 观察结果 |
|---|---|
| 两种外观 | `ring-actual.jpg`、`islands-actual.jpg`；相同前三槽 A/A/LT，方位一致 |
| 手机 Mode | 打开、正常 UP 后保持、长接触只开一次、再点关闭；无 Esc/原 G/鼠标点击 |
| 手机菜单导航/点击 | 独立指针移动、中心/菜单外点击消耗；导航没有 HID Mouse 位移/左键 |
| 菜单键盘 A | `VID_1209&PID_0002`，scan 001E，flags 0000→0001 |
| 菜单手柄 A | 原 slot0 Xbox360，buttons 1000→0000 |
| 菜单 LT | LT 255→0，菜单保持打开 |
| 原 G 普通/切换 | scan 0022，普通短按/长按释放；切换 UP 后保持、第二次 DOWN 释放 |
| 原 B/X | 2000/4000→0000；保留已接受手势和单个设备 |
| 配置安全中断 | 菜单打开时统一 Save 更改用途，菜单关闭并使旧代次失效 |
| 第一次关闭后拖动 | 96 ms 正常双击间隔，观察区域 `mouse_drag_up moves=403` |
| 最终 PID 关闭后移动/单击/拖动 | 84 ms 正常双击间隔；定位刷新后的最终观察区域 `mouse_drag_up moves=355`，HID Mouse PID_0003 按下/移动/释放 |
| 最终按键与手柄 | G 最终释放；所有本轮观察到的 HID 键盘键均释放；HID 左键未保持；slot0 buttons=0000，LT=RT=0 |

最终释放与设置一致性由 `verify_evidence.py` 从实际日志重算，结果为
`final-output-and-settings.json`，不是仅依据后端成功返回。最后运行快照为 Running /
Connected，LastError=null，mouseOutputFailures=0、gamepadFailures=0。

### 观察到的系统手柄附带消息

本机菜单 Xbox A 动作同时出现 `handle=0 / device=空 / buttons=0400` 系统滚轮消息。
为区分来源，观察器增加了实际设备路径；使用既有 B 下滑产生相同 Xbox A，也复现
相同系统消息。既有 B 点按还出现 handle0 的系统右键 0004/0008。这些不是 Receiver
HID Mouse PID_0003 的导航位移/左键输出，证据见 22:39:07、22:41:09、22:41:23 的记录。

没有凭这些现象断言具体由哪个外部映射程序生成，也没有改系统手柄映射。不能声称
菜单手柄动作在当前桌面环境没有任何附带鼠标效果；选择键盘动作没有出现该手柄
转换现象。外部映射和目标游戏的最终行为仍需分别验证。本轮没有用 Hook/注入隐藏
这一限制。

## 部署身份和保护

最初实时 PID 为 **19556 / Session 1**，其启动器文件却记录旧 Session 2，不能沿用。
任务前备份实际运行目录、依赖、设置和正式布局。独立临时计划任务
`Rightpad-Menu-Watch-20261018` 由 Windows 调度器启动：PID24076、Parent2432、Session1，
InteractiveToken/Limited，无触发器、3 h 上限。每 1 s 检查；armed 后 300 s 无进展或
部署后 Receiver 意外消失时，恢复固定六个备份产物并调用项目独立启动器。
不会重置设置/布局，不依赖 Receiver 或聊天进程。没有故意破坏唯一输入链演练回退。

两次替换都先在 Overview 实际点击 Stop，确认 Runtime Stopped/Not listening，旧进程
退出及端口释放后复制六个产物；新进程由 `windows/tools/RightpadReceiverTask.ps1`
独立交互任务启动。旧 GUI 的常规关闭会进入托盘，因此启动器只对已核对身份且
Runtime 已停止的残余进程进行它原有的退出处理。

- 第一次候选 PID **28248**，22:30；`deployment.log`。
- **最终 PID 29260**，wrapper6344，22:46；`deployment-final.log`、`final-status.txt`。
- 当前用户 BAODINE77777777\zhq，与 explorer 同为 Session1，Medium `S-1-16-8192`、
  Default desktop，`0.0.0.0:50000` 由 PID29260 持有；production/libvirtualhid，M_R1。
- Android MainActivity 保持 RESUMED。没有 APK 更新、force-stop 或应用重启；自然重连
  的新 Sender run `3E607D537A78FFF1` 被正常接纳。
- 最终 Receiver DLL SHA256：`B3353B7A3B303730A4C050A968E69D5A0020A78C3144F36479768D7F1C2CE24C`。
- 原生 DLL SHA256 保持 `1F1D0C4F40D1171BD6A6153593C6C9DC24FA785EA4A56133CA126E089F1E83CC`，
  mouse/gamepad ABI2 与 keyboard extension v1 不变。

实际控制/释放完成后写入 `completed`。保护正常退出并注销自身，测试观察窗口正常
关闭（22:51:44 `observer_closed`）。项目原有 Receiver Dev 任务继续运行。恢复脚本、
备份和所有日志保留；没有启动常驻服务或新的产品 watchdog。

## 设置、布局和恢复资料

`settings-final.json` 与任务前备份删除三个新增菜单字段后完全相等。原 G/普通按钮、
Sensitivity12/12、M_R1、C Amax160000/Tau30 以及其他 C/B/X 值均保留；没有强制恢复
历史默认值。未知属性由 Save 递归合并保留，隔离测试另行覆盖扩展属性。

手机正式布局逐字节备份 `recovery/screen-controls.properties` 的 SHA256 与收尾设备端
一致：`4C2EF09915914409BAA91A7AE69A8E81B5EF61DB30BA6081AAB38069E5294C33`。
它与历史报告不同，正是本轮开始时的实际用户布局；没有回写任何历史矩形。

恢复资料在证据根目录的 `recovery/`，人工/后续代理恢复入口为 `Recovery.ps1 -Mode Restore`。
脚本仅恢复六个已备份 Windows 产物并通过批准启动器启动，不改设置或手机布局。
这是保留的恢复资料，不要求用户在失去输入后手工运行；部署期间已有独立保护。

## 验收边界

实际显示和输出验证使用普通 Windows WPF 安全观察窗口，本机为 200% DPI。隔离
几何/渲染覆盖小视口、负坐标及多缩放；不能写成多物理显示器/所有 DPI 的实机通过。
未发现本轮适合直接进行安全验证的已打开游戏，未安装、购买或进入游戏场景。
无边框游戏、独占全屏、所有游戏、反作弊、真人盲操作、长期体验和真实生产故障
破坏/自动回退演练均未作为通过项。普通桌面浮层不保证独占全屏可见。
