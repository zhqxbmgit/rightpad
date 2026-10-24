# Mode Android 本地 PRESS 交付（2026-10-24）

本轮任务：`20261024-mode-local-press-r1`。核对与安装日期均为 2026-10-24，Asia/Shanghai。
**已实现、主机验证、构建／lint，并在独立恢复保护就绪后完成正式 Android 覆盖更新。**
Windows Receiver 没有构建、替换或重启。用户真实振动体感、正常操作体验及真人游戏
仍由用户验收；没有执行真实回滚。

## 实际行为及代码位置

`android/app/src/main/java/com/rightpad/capture/TouchCaptureView.java` 的原 Mode DOWN
分支保留 MODE 归属、`modeDownTime` 与原 Sender 调用。只有
`udpSender.modeDown()` 返回 true 后才调用：

```java
controls.feedback.emit(ScreenControlFeedback.Event.PRESS,
        ScreenControlFeedback.Style.STRONG_ONE_SHOT);
```

Mode 新增 Android 本地、接纳后的一次 PRESS 振动，复用 B 的 10 ms／255 策略；
不代表 Receiver 执行成功。原 `ScreenControlFeedback` 实例、异常隔离及 Android
后端均直接复用，未修改其波形或参数：API 26+ oneShot(10 ms,255)，已有 pre-26
分支 legacy(10 ms)。产品 minSdk 仍 34，现场设备 API 36。

Android 不知道 Receiver 的单键／菜单用途、绑定种类或 Toggle 保持状态。普通按钮、
Toggle 开启／解除、菜单打开／手动关闭及已同步的未绑定单键，都只取决于同一个
本地接纳结果。没有新增这些业务状态镜像。

一次接纳只请求一次。重复 DOWN、未同步／过期／不可 capture／已 touching 的拒绝
不请求；MOVE、长按、UP、CANCEL、异常 pointer、编辑／工具、生命周期、ACK、
重传与维护不新增反馈。振动后端不可用或抛 RuntimeException 时，既有策略吞掉失败，
不重试、不等待，原 DOWN／UP／CANCEL 队列及发送继续。

`ScreenControls.java` 只更新了旧“无反馈策略”的注释。Mode 没有接入 SCREEN_CONTROL、
SlideControlGesture、GamepadAggregator 或普通 Touch。stable ID `rightpad.input.mode`、
静态 M 和布局未变。B 的 PRESS／首次方向、X 的 SYSTEM_CLICK、Touchpad Windows
确认后 CONFIRM、Ring 已绑定／未绑定槽位确认及中央不确认不关闭均保留。
Mode 本地 PRESS 不使用 RPHF 身份，不替代槽位确认，不给 UP／Toggle 状态变化／
菜单自动关闭加反馈。电脑声音仍彻底移除。

没有修改 UdpTouchSender、ModeContactStream、type8/RPMD、Touch、RPHF 或其他线格式，
也没有新增设置、权限、socket、线程、服务、WakeLock、输出设备或 Motion 参数。

## 验证与修正记录

| 项目 | 最终结果与边界 |
|---|---|
| 当前 `Invoke-Development.ps1 -Action TestAndroidJvm` | 27 个 main 测试类全部退出成功 |
| 新增 `ModePressFeedbackTests` | 94 项检查通过；提取源码中的完整 `onTouchEvent` 和 `stopCapture` 原文，使用真实 router／ModeContactStream／ScreenControlFeedback，Android View 外壳、Sender 外壳及反馈后端为 fake |
| 单次／拒绝／重传 | 接纳后一次；重复 MotionEvent、已 touching、未同步、过期、capture 拒绝无额外调用；刷新／重传／重复 ACK 不振动 |
| 取消／故障 | MOVE、长按、UP、CANCEL、多 pointer、错误 pointer、生命周期不多振；后端异常／缺硬件／服务异常仍可 claim DOWN、UP、CANCEL |
| 归属与隔离 | Mode 不进入普通 Touch、unbuffered acquisition、B/X／滑动分派；编辑器、Settings、Power 和 input modal 隔离 |
| 既有反馈及协议回归 | B 策略 30、Touchpad CONFIRM 7、ModeContact 56、ReservedControl 197 等原用例通过；RPHF 实际 UDP listener、LR、gamepad、配置、普通取消及 listener 故障恢复回归通过 |
| assembleDebug／lintDebug | BUILD SUCCESSFUL；lint 0 errors、14 existing warnings |
| 手机 instrumentation／自动生产输入／游戏 | 均未执行；没有通过 ADB tap／swipe／keyevent 或真实 Mode 自动点按取证 |
| Windows／原生构建 | 未执行，本轮没有对应生产代码变更 |

首次主机回归在 `ReservedControlTests` 失败：旧文本定位取第一个 `if (reservedContact)`，
新增 DOWN 块后误取该块。仅将定位约束到后面的 MOVE／UP／CANCEL 分支，保留
无普通 Touch、无 gamepad、无多余反馈断言；重跑全部通过。首次日志保存为
`evidence/android-jvm-attempt1.log`，最终为 `evidence/android-jvm.log`。
已有 instrumentation 的零反馈断言明确标注为“未同步 Mode DOWN”，计数未放松，
未在唯一手机上运行该 instrumentation。

恢复资料准备阶段的失败发生在安装前：初次脚本语法检查发现 typed catch 缺空格，
已修正；PowerShell 7 自动解析 JSON 日期使严格字符串比较失败，改为规范化 UTC；
一次 Windows PowerShell 只读复核未带进程级 ExecutionPolicy Bypass，未加载脚本；
随后 PS5.1 默认 ANSI 读取无 BOM UTF-8 JSON 失败，改为显式 UTF-8。相关失败摘要、
首次脚本副本及最终成功日志都保留在 evidence。没有因此更改系统执行策略、ACL、
安装旧版本或重启生产程序。真正的独立 guard 首次启动即通过非破坏预检。

## 当前设备、安装包和布局

现场 ADB `devices -l` 有一个可用网络设备 `192.168.110.127:5555`，另有
`R5CRA1K581W offline`。仅使用已存在的可用连接，未重配对、kill-server 或改网络。
目标硬件 serial 为 `c2e6a5e3`，型号 `23127PN0CC`／houji，API 36。

包名 `com.rightpad.capture`。更新前 `pm path` 只有 base.apk，**没有 split APK**；
旧完整安装集合即这一份，已 pull 并验证长度／设备和本地 SHA-256。

| 项目 | 结果 |
|---|---|
| 旧 APK | 429803 字节；`493808D6852CC55EA624EDFB80095222B9C9085CE74D51FA29433CB916F4A898` |
| 最终候选与实际安装 APK | 219107 字节；`3DB4AC5581C2026147F55F9608626762F869D4BA3339178B089685C5418D8FE3` |
| 新旧及实际安装签名证书 SHA-256 | `978223C60C5473CFC61A1999596285C93D664595EAC71F9573E71FC781705F1D`，apksigner verify 成功且一致 |
| 当前实际版本 | versionName 0.1，versionCode 1；版本号未递增，按哈希区分 |
| 更新前／后 Android PID | 8838 → 12662（本轮时点） |
| 布局前／后 | 696 字节，原始字节完全一致；`0560DEA68780DDFF60249429A1EAB669CAF685E0719F2E4F61C38AB724F3A0CC` |

使用 `install -r` 保留数据覆盖安装，并以 `am start -S -n
com.rightpad.capture/.MainActivity` 必要启动；实际 APK 再次 pull、哈希及签名核对。
MainActivity 前台 resumed，原布局未写入或恢复，因为逐字节检查始终一致。
没有卸载、清数据、导出签名私钥或强改签名。

## Receiver 未变与自然重连

Receiver 保持 PID **31572**，创建 **2026-10-24T14:42:13.9714541Z**，Runtime RunId **1**。
路径 `C:\rightpad\windows\Rightpad.Receiver\bin\Release\net8.0-windows\Rightpad.Receiver.exe`。
正式目录全部八个文件逐一记录并反复核对，哈希未变：

- Receiver DLL：`CB6CD7423F9459E913200B2AC3DECE8BD9CC9D0D93AD32BF0947650345E26F81`。
- 原生桥接：`21A5556AF12F0FBF17085E23E59A4F97EBCBA883D6FFD8E5851EF5DD88E0F721`。
- 正式 settings：`B02A5414C2FB2C8453DB77102821D9D9EEE9C8BEBD4C2058786BC03196B088D5`。

用户／explorer Session 1、Medium `S-1-16-8192`、Default 桌面，UDP `0.0.0.0:50000`
仍由 31572 持有；正式 `Rightpad Receiver Dev` 任务仍运行。没有改变绑定、鼠标来源
合并、Ring、Sensitivity、Motion、驱动、Broker、许可或系统依赖。

Android 新 Sender `26649425012F37B3`（旧 `5EC86A90F2A0A8ED`）已自然发现并恢复
Connected。手机日志含 `target_changed ... sequence=0`、`foreground=true` 和
`listener_active ... port=50002`，没有 Sender queue_overflow／open／send 错误。
独立放行快照：Running／Connected，lastError=null，mouseOutputFailures=0、
gamepadFailures=0，累计统计保持，用户自然操作的 acceptedInputPackets／输出继续增长。
这属于被动工程健康证据，不声称自动完成真人功能／游戏验收。
手机日志另有 MIUI `SettingTrigger` 的系统反射异常，不属于 Rightpad Sender 故障；
未改系统或以此扩大任务。

## 独立恢复保护的完整顺序

恢复范围仅 Android。完整旧 APK 集合、布局原始字节、ADB EXE／两份 DLL、候选及
所需脚本均带长度和哈希；保护每次核对设备硬件身份。它不依赖 App、Receiver 输入、
Codex 会话或当前终端。安装、健康探测及恢复共同使用本轮 Local named mutex，
恢复不与部署同时操作设备；客户端超时有界、失败重试最多三次，不无限等待。
恢复脚本支持单包 `install -r` 与完整 splits `install-multiple -r`，必要时用备份原字节
恢复布局并启动 Activity，再核对旧包、布局及自然重连。未执行真实回装证明。

本轮临时任务 `Rightpad-Android-Mode-Press-20261024-R1`，guard PID **31988**，
创建 **2026-10-24T15:12:01.3103123Z**。nonce
`3be94f57cace459194b7665179c54de3`。
真实上下文：父进程／Scheduler PID 均 **2484**，当前用户 Session 1、Medium／Default，
Job flags=0。短启动进程 **19568** 已退出后，guard 仍持续新鲜心跳。

以下时间为本地 +08:00，原始 JSON 保存 UTC：

| 顺序 | 本轮记录 |
|---|---|
| 非破坏预检 | 完整文件可读及哈希、正确设备、run-as、Receiver 身份、mutex 实际获取／释放、恢复命令审查、4 项决策检查通过；没有写手机或故意制造故障 |
| 新鲜 ready | 23:12:11.832，`guard-ready.json`；核对 nonce／PID／创建时间／planHash |
| armed | 23:12:59.662，`deployment-armed.json`，恢复截止 23:16:59.663 |
| 同一 guard 确认 | 23:13:00.126，`guard-armed.json` |
| 安装实际开始 | 23:13:00.511，在确认之后；部署前再次核对候选哈希 |
| 安装／启动 | install 返回 Success；MainActivity 启动、实际包／签名／布局与自然新 sender 检查通过 |
| 独立健康放行 | 23:14:08.985，连续 **62.29 秒／25 次**检查通过；`actualRollback=false` |
| 收尾 | 23:14:23.383，guard 自行退出、LastTaskResult=0，保存任务 XML 后仅注销本轮临时任务；正式 Receiver 任务保留 |

没有依靠历史 ready 或仅注册成功进入更新窗口。没有真实回滚，没有故意破坏设备、
断开 ADB 或让用户手动执行恢复。保护健康放行不等于所有恢复故障分支已在真机验证。

## 交付及证据绝对路径

- 任务根：`C:\rightpad-artifacts\test-results\20261024-mode-local-press-r1`。
- 候选：`C:\rightpad-artifacts\test-results\20261024-mode-local-press-r1\candidate\mode-local-press.apk`。
- 完整旧包：`C:\rightpad-artifacts\test-results\20261024-mode-local-press-r1\recovery\android\base.apk`。
- 原始布局：`C:\rightpad-artifacts\test-results\20261024-mode-local-press-r1\recovery\android\layout.raw`。
- 恢复计划及冻结脚本：`C:\rightpad-artifacts\test-results\20261024-mode-local-press-r1\recovery\plan.json`，同目录 Common／Guard／Deploy／StartProtection 脚本。
- 源码脚本：`C:\rightpad\tools\ModeLocalPressDeployment`；新的独立轮次必须重新生成 plan、任务身份和 nonce，不能直接复用本轮已结束的 ready／armed 文件。
- JVM 编译及提取夹具：`C:\rightpad-artifacts\builds\android\20261024-mode-local-press-r1\jvm-tests`。
- APK 构建及 lint：`C:\rightpad-artifacts\builds\android\20261024-mode-local-press-r1\app\build`。
- 证据：`C:\rightpad-artifacts\test-results\20261024-mode-local-press-r1\evidence`，包含新旧及实际安装签名、原始布局 after、实际安装 APK、构建／回归、所有保护阶段、计划任务 XML、收尾及 Receiver 状态。

当前契约已同步 AGENTS、SCREEN_CONTROLS、MODE_CONTROL、MODE_CONTACT_PROTOCOL、
HAPTIC_FEEDBACK_PROTOCOL、MODE_SHORTCUT_MENU 与 NEW_CHAT_HANDOFF。历史报告未批量
改写。既有未提交／未跟踪工作保留；没有 Git commit／push／reset／clean，没有操作
C:\leftpad、DeepSeek Harness 或电脑操作插件。

待用户确认：Mode 有效新按下时的短振是否符合预期、与 B 的主观感觉及正常单指操作
体验。代码／fake 与请求日志不能替代体感确认，未自动触发生产 Mode 或游戏补证据。
