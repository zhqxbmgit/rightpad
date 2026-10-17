# Mode 单键绑定交付报告 — 2026-10-17

日期与时间采用 Asia/Shanghai。本报告记录用户授权的实现、测试及受控部署阶段；
**该阶段没有 Git commit、push、历史清理、正式布局重置或 Motion 参数调整。**
后续源码审查、提交与推送以 Git 历史为准，不扩大本报告的验证范围。

## 结果与操作入口

现有 `rightpad.input.mode` 区域已经可在 Receiver 的 **Controls → Mode · 单键绑定** 中
配置为一个键盘键或手柄键，支持普通按钮及切换按住。分类、大按钮、选中状态、
选择器返回与底部统一 Save 均经过实际点击检查，不要求实体键盘输入。

交付后的正式配置是 **未绑定 / 普通按钮**。A、B、右 Ctrl、数字小键盘 Enter、LT
等均为隔离安全目标上的临时验证绑定，已通过统一 Save 清除，未留下默认游戏动作。

完整使用与支持清单见 [MODE_CONTROL.md](MODE_CONTROL.md)，字节格式及状态语义见
[MODE_CONTACT_PROTOCOL.md](MODE_CONTACT_PROTOCOL.md)。

## 实现与必要架构变化

- 保留 Android 原控件 stable ID、正式矩形和布局格式。DOWN 锁定归属，MOVE 不转交；
  结束后的重复 UP/MOVE 消耗而不取消已经建立的切换保持。Mode 不走鼠标采集或通用
  Screen Control 手势/触觉路径。既有 B、X 和 Windows 确认的触控板反馈保留。
- Receiver 保存完整不可变 `ModeBinding`，草稿与正式值分开，沿用磁盘先行的统一 Save。
  失败不发布；相同配置或无关 Save 不重复动作。有效改绑/行为变化释放旧输出并使旧
  接触代次失效，下一次新接触才可使用新配置。运行中的保持不写盘。
- type8/RPMD 在现有 worker、socket 和端口上增加有界转换队列、确认重传、固定租约和
  生命周期代次。Android 不解释绑定或切换业务状态。没有认证、配对或保留位挪用。
- 原生 bridge 在既有 mouse/gamepad ABI2 上增量增加 keyboard extension v1。
  仍使用固定 libvirtualhid revision `53e1a949fc0784af716b782ddfa6c647cafd1f05`；
  核对了该版本的 HID 键盘页映射，create 检查真实设备节点，set 使用映射白名单，
  不以 SendInput fallback 隐藏限制。没有升级系统驱动、broker 或许可组件。
- Mode 键盘拥有独立设备及按下记录；手柄复用原单个 Xbox360，在 Receiver 内与原
  Screen Controls 分别记录贡献，按钮 OR、LT/RT 取最大值。失败状态与鼠标隔离。
- M-R1/C-Z1 算法、灵敏度、重建、量化、MotionClock 输出节奏、B/X 已接受的手势及
  参数均未修改。用户原先保存的 C 参数也保持原值，没有恢复为历史默认值。

## 真实支持范围及限制

键盘目录共 **117 项**，包含常规 104 键范围、F13–F24 和 ISO 额外符号键；左右
修饰键、主 Enter/数字小键盘 Enter、数字小键盘及导航键分开编码。
手柄提供 **16 项**：A/B/X/Y、LB/RB、View/Back、Menu/Start、方向四键、左右摇杆
按下、LT/RT（255/0）。全部目录映射与释放经过 fake 测试及固定供应商实现映射核对。

Fn、厂商硬件键、媒体/消费控制键和鼠标按键不在选择器中。Guide 的原生逻辑位虽存在，
标准公开 XInput 按钮接口没有定义其应用行为，本轮没有完成它的 Windows/游戏行为
验证，因此不提供该选项，不声称底层完全不能编码。
参考 [Microsoft XINPUT_GAMEPAD 定义](https://learn.microsoft.com/en-us/windows/win32/api/xinput/ns-xinput-xinput_gamepad)。

符号选择是一个物理键，不是自动输入带 Shift 的字符。Windows 布局、锁定键和修饰
状态决定实际字符。锁定键、Pause、Print Screen、Windows、Application 等仍按系统
特殊语义工作；本轮没有逐个真机测试所有特殊键在所有应用中的行为。
保持键盘键时可以观察到 Windows 常规重复；Rightpad 没有自行运行连点定时器。

## 构建与自动化验证

所有前置构建/故障测试采用隔离输出，不覆盖正在运行的文件。证据根目录：
`C:\rightpad\windows\test-results\mode-binding-20261017`。

| 检查 | 结果与证据 |
|---|---|
| 修改前 Windows Release 基线 | **734/734**，`baseline-tests.log` |
| 候选 Windows Release 全量 | **743/743**，`tests.log`；包含新增 9 组 Mode 行为测试 |
| Windows/native 构建 | 成功，`build.log`、`native-build.log` |
| 原生 fake mouse/Xbox/keyboard | **764 checks**，CTest **1/1**，`native-tests.log` 及原生 build 的 `Testing/Temporary/LastTest.log` |
| Android 主 APK、测试 APK | 构建成功，`android-final-build.log`、`android-terminal-fix-build.log`、`android-merge-build.log` |
| Android JVM 套件 | 通过，`java-tests.log`，保留各套件单独的检查数，不合并成不准确的总数 |
| 最终接触结束修正后的定向 JVM 检查 | ModeContactTests **56 checks**；ReservedControlTests **197 checks**，`final-targeted-java.log` |
| 最终源码 Android lint | **0 errors、14 warnings**，既有告警；`final-lint.log` 及 `android/app/build/reports/lint-results-debug.txt` |

Mode Windows/Java 测试覆盖未绑定、短按最小可见时间、持续按住、UP、CANCEL、
DOWN 切换与 UP 保持、重复/乱序/迟到消息、丢失转换重试、失效代次、静止刷新与租约、
队列上限、改绑/改行为、相同配置、无关 Save、Save 失败、后端错误、presence/Stop/
Dispose、新接触恢复、全部目录映射、按钮与 trigger 贡献合并。原有 Motion、鼠标、
配置、布局和手柄回归也包含在全量结果中。

首轮候选有两项旧断言失败，原因是它们把完整设置字段数固定为 11；新增
`modeControl` 后为 12。更新对应 schema 断言后全量 743/743 通过。
最终没有把这两次失败隐藏或删除，原构建与日志文件仍保留。

续办收尾还核对了固定依赖提交与供应商源码未变（`dependency-revision-final.txt`、
`dependency-diff-final.log`），新文档相对链接与空白检查通过，仓库 `git diff --check`
退出码为 0（`final-diff-check.log`）。本次收尾没有重新停止、替换或部署运行程序。

## 真实设备与实际输出证据

真机测试 `ModeDeviceSmoke` 从当前实际 View 读取正式矩形并注入单指 MotionEvent，
经生产 Sender、当前 Receiver 和实际 HID/Xbox 设备输出。测试不写正式布局，也不
将历史测试矩形装入正式布局；没有创建布局试用事务。

临时 Windows 观察窗口用于键盘 Raw Input、XInput、鼠标单击与拖动记录。
窗口现已关闭，`actual-input.log` 在 17:05:17 记录 `observer_closed`。

| 真实检查 | 观察结果 | 证据 |
|---|---|---|
| A 普通按钮 | 短按按下/释放；长按；移出 Mode 后 UP 正常释放 | `device-keyboard-momentary.log`、`actual-input.log` |
| A 切换按住 | 首次 DOWN 开启，正常 UP 后保持，第二次 DOWN 释放 | `device-keyboard-toggle.log`、Receiver `mode_output` 记录 |
| 右 Ctrl | Raw Input scan `001D`，扩展按下 flags `0002`、释放 `0003` | `device-rightctrl.log`、`actual-input.log` |
| 数字小键盘 Enter | scan `001C`，扩展 flags `0002/0003`，区别于主 Enter | `device-numpad-enter.log`、`actual-input.log` |
| B 切换、重复终止事件 | XInput `2000` 持续约 1.67 s，第二次 DOWN 返回 `0000`；重复 UP/MOVE 未取消或反复切换 | `device-gamepad-toggle-orphans.log`、`actual-input.log` |
| Mode 与 B/X 合并 | Mode 保持 B 时原 B 释放不撤销 B；X 贡献为 `6000`，释放 X 后回到 `2000`，最后 Mode 关闭回 `0000` | `device-gamepad-merge.log`、`actual-input.log` |
| LT 普通按钮 | 三个测试接触均出现 `LT=255` 并回到 `LT=0`，包含持续接触及移出后释放 | `device-gamepad-lt.log`、`actual-input.log` |
| 原 B/X | 单击与长按均观察到既有 B/X 输出和释放 | `device-bx.log`、`actual-input.log` |
| 鼠标移动 | 生产 Motion 输出及 cursor 位置变化；最终无输出错误 | `device-mouse-move.log`、运行快照 |
| 鼠标单击 | 手机注入短按后观察窗口在 17:04:01 记录一次 `mouse_click`，与先前的定位点击分开 | `device-mouse-click.log`、`actual-input.log` |
| 现有双击拖动 | 17:04:22 第二次接触进入拖动，17:04:23 正常释放，观察器记录 `moves=86` | `device-mouse-drag.log`、`actual-input.log` |

键盘上述记录的设备路径为 `HID#VID_1209&PID_0002...`，证明使用实际 HID 键盘。
日志中另外存在 device 为空的系统/gamepad 虚拟键消息，未把它们当作该 HID 键盘证据。
手柄始终是原单个 Xbox360 设备，没有为 Mode 创建第二个手柄。

目录全部 117+16 项具有映射核对及自动化依据；**真实 Windows 输入观察仅覆盖上表
代表项，没有声称逐个真机执行了全部 117 个键和 16 个手柄映射。**

## 部署与独立恢复保护

部署前持久备份现用 Receiver 产物、原生 DLL、APK、settings 和布局至证据目录下
`recovery`，保存 `recovery-hashes.json`；APK、原生 DLL 和布局的备份哈希与现用一致。
布局 SHA-256 为：

```text
0AAB5C746BD726CB6D32B0B40E5161238F7D91269E912F471F10B902328A8C9D
```

独立临时计划任务 `Rightpad-Mode-Recovery-20261017` 在首次 Stop 前已启动并 armed，
不依赖 Receiver、APK 或聊天进程。守护 PID 14648 由调度器进程 2644 启动，身份记录
在 `watch-identity.json`。部署主控连续 180 s 无进展时，其脚本可回退固定六个 Windows
产物、以 `adb install -r` 回装已备份 APK、调用原有独立启动器并启动 Android Activity。
不写布局或设置。没有在唯一输入链路上故意制造故障来测试回退。

该任务最初被自动审批拒绝；补齐用户明确要求独立恢复的授权、备份一致性证据，收紧为
非提权、无开机触发、两小时上限及退出时删除自身注册后复审通过。
Windows 默认 Restricted 阻止脚本启动，随后获批仅对该进程使用 RemoteSigned；
没有修改系统执行策略。全部过程未把断开后的恢复操作交给用户。

16:55 通过正常 UI Stop 停止旧 Runtime，再替换本轮六个产物，由既有
`Rightpad Receiver Dev` 调度器启动新 Receiver；新 PID **6120**、wrapper PID **19584**，
Medium integrity / Default desktop。Android 使用保留数据的 `install -r` 更新，并在
完成每个 instrumentation 后恢复生产 MainActivity。详情见 `deployment.log`。

最终部署指纹如下；产品版本号没有人为增加，使用构建指纹识别本轮交付：

| 产物 | SHA-256 |
|---|---|
| `Rightpad.Receiver.dll` | `BD011ADCB4EF94086B2FDF069AF4D5A6DA10E418F1453264D0D3F805B70D06AB` |
| `Rightpad.VirtualHid.dll` | `1F1D0C4F40D1171BD6A6153593C6C9DC24FA785EA4A56133CA126E089F1E83CC` |
| 已安装生产 APK / 当前 `app-debug.apk` | `DFE25FDE0F358DF72E3C473713F367C98AE3C07F8870D6E0483BE840E416ADED` |

已核对 Windows 文件与候选构建一致，手机 `pm path` 对应 APK 的设备端哈希与当前
本地 APK 一致。原生桥仍为 ABI2 + keyboard extension v1，不需要更换其他运行依赖。

实际控制验证完成后写入部署完成标记，独立任务退出并自行注销。续办收尾时显式查询
确认：临时任务/守护不存在；原有隐藏的 `Rightpad Receiver Dev` 任务仍存在且 Running；
观察器窗口和进程已关闭。证据为 `watch-finished`、`final-scheduled-tasks.json`、
`final-cleanup-status.json`。没有删除恢复包、失败日志或历史材料。

保留的人工/后续代理恢复入口是证据目录中的 `Recovery.ps1 -Mode Restore`，恢复到
本轮前的已知可用 Receiver/DLL/APK。它不是要求用户自己执行的恢复步骤，也不是
仍在后台运行的持续监控。该脚本仅用于已命名部署路径，并沿用既有调度器启动流程。

## 最终状态与数据保护

18:16 的复核记录 `runtime-final.json` 显示：PID **6120**、Runtime **Running**、
connection **Connected**、生产 libvirtualhid mouse、Xbox available、mouse failures **0**、
gamepad failures **0**、LastError 为空。手机的 resumed/focused Activity 为生产
`com.rightpad.capture/.MainActivity`。

`final-output-state.json` 的只读 Win32/XInput 检查显示：测试过的 A、右 Ctrl、
Enter/数字小键盘 Enter 及鼠标左/右/中键没有按住；唯一 Xbox slot0 的按钮、LT/RT 和
全部摇杆值均为 0。该瞬时检查与先前的真实拖动释放记录相互补充，不代替实际控制测试。

`settings-layout-final.json` 显示原设置逐项保留，变更的原字段列表为空；仅新增正式
`modeControl={kind:0,key:"",behavior:0}`。当前手机布局文件字节哈希与任务前一致，
未知属性和 stable ID 没有重写。测试未创建任何布局试用，因此没有待保留的测试布局。
原有布局编辑“草稿—试用—保留—超时恢复”流程不变，上一轮未提交的布局修复保留。

## 验证边界

本轮完成的是源代码、构建、自动化和代表性真实输入功能验证；没有真人盲操作、
长时间真实游戏体验、所有游戏/反作弊兼容性或全按键逐项实机验收。
网络丢失、乱序、后端错误和 Save 失败主要在隔离状态机/fake 环境验证，没有为了
故障证据破坏用户唯一输入通道。保留的自动回退保护已启动和收尾确认，但没有实际
执行一次生产故障回退演练。

Mode generation 跨进程使用 UTC ticks 排序；人为显著回拨主机时间再重启 Receiver
的特殊场景未实测，可能使 Android 保守保持 Mode 不输出，详见协议文档。
以上边界不应被解释为已通过真人游戏验收。
