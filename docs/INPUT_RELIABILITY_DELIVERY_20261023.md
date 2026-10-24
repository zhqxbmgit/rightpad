# 三项输入可靠性修复交付（2026-10-23）

本轮已完成三项实现、隔离构建、适用回归、两端恢复准备、独立保护下的实际部署和
只读收尾。最终 Windows PID 24940，Android PID 8838；Receiver Running／Connected，
手机 INPUT／XBOX／CONFIG 均 GOOD。没有做 Git commit/push/reset/clean，没有操作游戏、
ADB 触摸注入或合成生产鼠标／键盘／手柄输入。真实使用和游戏体验仍由用户验收。

**问题与最终改动。**

| 项目 | 修复前 | 最终行为及主要文件 |
|---|---|---|
| 普通接触取消 | ACTION_CANCEL 仅本地记录，stopCapture 清 owner/pointer；持续 heartbeat 可维持旧拖动 | TouchCaptureView.stopCapture 提交独立取消；UdpTouchSender 管理有界取消围栏；新增两端 TouchCancellation 编解码；UdpReceiver 清理 Motion/Gesture/按钮及旧包资格；ModeSession/ModeMenu 只清本次菜单 Touch 资格 |
| 共享反馈 socket 异常 | IOException／SecurityException 后等身份改变，同身份 setActive 不重建 | HapticFeedbackListener 原 worker 在同身份退避重建，逐次核对 identity 与 socket 代号，保留原点击 gate 去重；MainActivity 连接取消 ACK 和失败重同步 |
| heartbeat 吞掉 Touch | poll 出包后 heartbeat 失败直接 continue，Touch 未尝试发送 | 首次未接入时显式安全放弃旧队列并记录限频原因；至少一次成功后周期失败仍尝试该 Touch，保持 DOWN/UP 三份、MOVE 一份 |

普通 Touch 与 heartbeat 编码器没有修改；新消息没有借用 UP 或 RPHF。
LeftButtonController、GestureProcessor、Motion 算法及参数本身无需修改，取消复用既有
Reset 和 CancelPendingAndRelease。没有修改 B/X 参数、Ring 槽位选择和确认规则、光标
权威、锚点／12 DIP、settings／布局、后端选择、驱动／Broker／许可或默认录制策略。

**取消协议、丢失和隔离。**

完整契约见 [TOUCH_CANCELLATION_PROTOCOL.md](TOUCH_CANCELLATION_PROTOCOL.md)。新增
50000 上的 22 字节 v2/type9，包含 run、session、原 DOWN sequence、取消 sequence；
取消 sequence 从原 Touch 顺序域取下一值。50002 上新增独立 28 字节 RPTC v1 ACK，
精确回显四项身份。原类型1..8、原五类反馈均保持自己的字节含义。

Receiver 只对当前 presence 的 run 和来源 IPv4 处理取消；对同 DOWN 的错误 session／
来源、旧 DOWN 身份不清理。晚于取消边界的新输入已接纳时，只确认旧边界，不释放新
接触。取消先到而原 DOWN 未到也先建立 sequence 围栏，拒绝随后迟到的旧 DOWN。
有效取消清 Motion、Gesture、双击资格、排队点击和已保持左键；菜单只清该 Touch
资格并保持打开。重复取消幂等，取消不续期 presence，不生成普通点击、菜单动作或
点击确认。原按钮输出失败锁存保持；取消不会掩盖实际释放失败。

Android 最多一项取消待确认，立即清队列并禁用新 capture；已出队旧包还检查取消
边界。原 Sender worker 每 100 ms 最多一次、窗口 1500 ms，不追赶、不重放动作。
ACK 丢失重传相同消息。等待时 heartbeat、B/X safety Neutral 和 Mode 收尾仍可运行。
耗尽后停止旧路由所有流量，由 UI 在同一目标创建新 run、清理路由和反馈／配置／布局
事务缓存后恢复。新 run heartbeat 使 Receiver 按既有规则清旧输入；UI／通信持续失败
时，旧流量不再续命，既有 2000 ms presence 超时负责释放。该时间是机制配置，不是
对任意网络延迟或系统调度的硬实时保证。

暂停也由 Sender 自己收尾，不只调整 onPause 顺序。目标变化／关闭时最多保留一个
旧路由最终取消尝试，且旧路由停止 heartbeat，取消不会串到新路由。普通新 DOWN 可
在确认或失败重同步完成后恢复。新 Receiver 配旧 Android 兼容原行为；旧 Receiver
不会把新 type9 当成 UP，新 Android 无 ACK 时有界重同步。本轮实际先更 Receiver。

**heartbeat 与监听恢复的区别。**

首次 heartbeat 尚未成功时维持原接入门槛。已经出队包遇到这一条件，会显式进行
admission abort，清旧队列，对仍活跃接触建立取消围栏，不把 DOWN/UP 重新排队，不
在网络恢复后重放旧点击；该条件有单独限频日志。首次失败的空闲等待由 500 ms
heartbeat 截止时间约束，避免配置请求零截止时间导致高频空转。正常运行中的周期
IOException 则不撤销既有接入，继续尝试当前有效 Touch。Touch 自身发送失败仍按
best-effort UDP 处理，不提供必达保证，也没有新增无限缓存或 Touch 重传队列。

共享监听器按 100/200/400/800/1600/2000 ms 退避并封顶，在原线程重建，不启用
SO_REUSEADDR。暂停、close、目标／run 改变关闭 socket 并中断旧身份等待。socket
代号在接收、分发和 UI 执行处校验；失败 socket 的旧 UI 工作作废。重建不重新配置
HapticFeedbackGate，因此已消费点击不重放，期望 UP 和原去重继续有效。错误日志
首项及每 32 项记录。RPHF／RPCT／RPST／RPLY／RPMD 均保留原身份、顺序／事务处理。

**实际验证。**

| 验证 | 实际结果 |
|---|---|
| Windows 最终隔离 Release 构建 | 0 警告、0 错误；原生依赖和 Tests 项目完成 |
| Windows 全量 fake／loopback 回归 | total=785，passed=785，failed=0 |
| Android 主机入口 | 实际运行 26 个 main 测试类，全部退出成功；新增 InputReliability 为 3 个故障组全部通过 |
| Android 最终 assembleDebug／lintDebug | BUILD SUCCESSFUL；lint 0 错误、14 警告 |
| 静态与产物检查 | git diff --check 无空白错误；两端 APK 签名一致；候选与实际加载模块哈希一致 |
| JSON 检查 | 检查时 43 个本轮 JSON 全部可解析，最大 8435 字节；显式字段，不序列化 PSDrive／Provider |

Windows 新用例覆盖已保持双击拖动释放、排队点击撤销、无新增点击／确认、取消先于
DOWN、重复／旧来源／旧 run／旧接触／延迟取消、迟到尾包、正常新 tap/drag 恢复、
静止 heartbeat 超过诊断超时仍保持、取消丢失后 presence／新 run 收尾。现有菜单 UDP
路由用例还增加真实取消报文，确认不出键、不确认、不泄漏鼠标左键且菜单保持打开。
全量回归覆盖 M-R1/C-Z1、Motion 生命周期、Gesture、Mode、B/X 既有行为。

Android 新用例使用真实 UdpTouchSender、回环 socket 与可注入 IOException，分别让
周期失败发生在已出队 DOWN/MOVE/UP 发送之前，验证 3/1/3；首次失败验证不发送 Touch
和有界等待。取消验证真实报文重复、错误 ACK、精确 ACK、新 DOWN、耗尽和新 run。
监听器用端口暂占、关闭 receive socket 和 SecurityException 注入，验证同身份恢复、
退避、暂停／目标 run／close 围栏、六分支分发与已消费反馈去重。原主机用例继续覆盖
多指／丢 pointer 的 router 取消以及源码入口接线；没有在生产 Activity 上制造多指故障。
所有本轮网络故障测试使用隔离动态端口和回环地址，输出为 fake。

失败尝试及重测已记录于外部 evidence/attempt-history.json。包括旧测试不认识新取消
消息或顺序边界、重同步测试读取缓冲区中的旧 heartbeat、JVM Log 对故意 IOException
报错、布局文本管道破坏字节等；对应修正后全量重测通过。保护准备还遇到 here-string
语法、PowerShell 7 JSON 日期转换及导入函数空 PSScriptRoot，均在 armed 之前发现，
当时正式 PID 19128 和输入保持运行。只读按键探针一次 Add-Type 调用失败产生的
result=null 文件保留为 failed-attempt，不作为成功证据；随后有效探针重测成功。

**部署与现场版本。**

2026-10-23 18:12（Asia/Shanghai）完成。部署前真实旧 PID=19128，创建时间
11:36:46.524054，正式路径、当前用户、Session 1、50000 归属及实际加载模块均重新
采集；启动器旧记录不匹配，未把旧 current.json 当作现场身份。

独立 Guard 18:12:29 ready；18:12:30 armed，核对旧进程退出并确认 UDP 50000 释放。
旧进程没有可用可见主窗口，本轮按精确路径、用户、会话、PID／创建时间复核后终止
该残留进程，**没有声称优雅退出**。正式旧目录以 hold 形式保留，没有覆盖破坏恢复源。
通过正式独立启动器启动新 Receiver；先观察旧 Android 自然 Connected，再安装 APK
并恢复 MainActivity 前台。整个 Android 更新期间保持同一个新 Receiver PID。

| 现场项 | 实际值 |
|---|---|
| Receiver PID／创建时间 | 24940；18:12:40.321871（进程查询精度与 CIM 最后小数位略不同，原始证据保留） |
| Runtime RunId／Android sender run | 1／979CBC3E7AC14F02 |
| 用户／会话／权限／桌面 | BAODINE77777777\zhq／Session 1，与 explorer 一致／Medium S-1-16-8192／Default |
| UDP 50000 | 0.0.0.0:50000，由 PID 24940 持有 |
| Android | com.rightpad.capture/.MainActivity，PID 8838，前台 resumed |
| Receiver DLL SHA256 | F844594D8C9D12F15178C096804222F1FF16BEF6F261B35F4FB121C9A779D521 |
| Receiver EXE SHA256 | B444947422873175F8B45A9206A9D870FC2853696FBB10496EE4417C546E33CE |
| 实际加载 VirtualHid DLL SHA256 | 56829F9EC344C1C7F0C5C35C8F8BBE59AF4936ACB6ADC2B32D8931D77343BE5E |
| 已安装 APK SHA256 | 493808D6852CC55EA624EDFB80095222B9C9085CE74D51FA29433CB916F4A898 |
| 两端 APK 签名证书 SHA256 | 978223c60c5473cfc61a1999596285c93d664595eac71f9573e71fc781705f1d |
| 原 APK 恢复源 SHA256 | F47BC9E45680D0DF4E21AAC99FA2722C30C0CE1D5F010BB5E22133CFC8A277BA |

apphost EXE 哈希与旧版相同不代表 DLL 相同；本轮已核对新实例实际加载的 Receiver.dll
和 VirtualHid.dll 模块路径／哈希，及实际安装路径 APK 哈希。没有驱动／ABI 源码修改。

**配置、实际操作证据与保护收尾。**

部署前现场 settings SHA256 为
`C3E4B9CBE21246FD004BC55DDEA360C32B88D5D13CA9F5AE20D36B7F69B4C7F1`；
手机布局 SHA256 为
`0560DEA68780DDFF60249429A1EAB669CAF685E0719F2E4F61C38AB724F3A0CC`。
18:17 收尾再核对均相等。本轮未更改正式配置、布局或临时绑定。

只读日志确认新 sender run 从 sequence 0 接入；18:12:44.530 INPUT／XBOX／CONFIG
均 GOOD。部署后被动观察到用户自然操作的 Motion 输出和多次 RPHF click_accepted，
没有由 agent 合成输入。最后健康快照 Running／Connected、MouseOutputFailures=0、
GamepadFailures=0、LastError=null。18:15:34 的有效只读 GetAsyncKeyState／XInputGetState
快照无按下虚拟键，唯一可用 Xbox360 为全中性，其余索引为未连接；这只是一次状态
观测，不替代双击拖动真人验收。

独立保护的真实证明层次如下：

- 脚本语法、完整 Windows 程序及依赖、settings、原 APK、原始布局、ADB 可执行与 DLL
  备份及哈希检查已完成；不复用通用 Recovery.ps1 的直接覆盖方式作为唯一保障。
- 最终独立预检 PID 12936，父 PID 2920=Schedule，Session 1、Medium、Default、JobFlags=0；
  能找到实际恢复包和手机。手机私有临时文件上完成原始布局字节写回／哈希核对并删除夹具。
- 独立决策演练五种组合通过，没有执行真实生产回滚。
- 实际部署 Guard PID 29164，父 2920，JobFlags=0，先 ready 后 armed；300 秒有界窗口，
  任一端失败／超时会恢复旧 Windows 完整目录、settings、原 APK／布局并启动两端。
  恢复使用 staging/hold，允许正式目标目录缺失；两端恢复尝试相互隔离，某端失败仍
  尝试另一端及前台启动。
- 18:12:46 独立核对新进程／端口、已安装 APK 哈希、配置／布局、MainActivity 前台及
  新鲜 Connected 健康快照后放行。guard-released.json 明确 actualRollback=false。
- 三个本轮预检／决策／Guard 临时计划任务均以 LastTaskResult=0 结束，导出 XML 后撤销。
  正式 Rightpad Receiver Dev 任务继续运行。保护不依赖 Codex 或当前终端继续存活。

**未演练真实双端回滚，不把独立预检、夹具写回、决策演练或健康放行冒充为真实恢复。**
未故意破坏唯一控制设备、断网或抢占生产端口。未来若需要恢复，应由代理重新建立
独立保护环境后使用保留包，不能要求失去控制的用户执行命令。

**范围与后续真人验收。**

实现不宣称修复任意 UDP 丢包。正常 UP 的所有副本均丢失而 Android 没有明确取消，
仍属于未覆盖情形；任意长的网络延迟、应用／系统进程完全失效也没有必达承诺。
没有进行生产取消／ACK 丢失、生产监听异常的故障注入，证据来自离线 fake／回环用例。
没有做真实回滚；没有获得真人双击拖动、真实取消释放、反馈体感和真实游戏验收。
现有 14 条 lint 警告本轮未扩大整改。没有宣称降低延迟、解决游戏卡顿或改善手感。

保留所有既有未提交／未跟踪内容、历史证据、holds 和恢复包。本轮新增源码与测试
均未提交。AGENTS、通用目录／恢复／启动器设计和其他项目未做本轮重构。

**产物与证据索引。**

- 外部任务：`C:\rightpad-artifacts\test-results\20261023-input-reliability-r1`
- 完整索引：`C:\rightpad-artifacts\test-results\20261023-input-reliability-r1\EVIDENCE_INDEX.md`
- 两端恢复包及专用脚本：`C:\rightpad-artifacts\test-results\20261023-input-reliability-r1\recovery`
- Windows 原版恢复目录：`C:\rightpad-artifacts\test-results\20261023-input-reliability-r1\recovery\backup-20261023-175721-867`
- 正式旧目录 hold：`C:\rightpad\windows\Rightpad.Receiver\bin\Release\net8.0-windows.pre-input-reliability-20261023181230747`
- 新实例运行日志：`C:\rightpad-artifacts\receiver-runtime\20261023-181240-271-25848\receiver.log`

关键证据为 evidence 下的 windows-final-build.log、windows-tests.log、android-jvm.log、
android-final-build-lint.log、attempt-history.json、independent-preflight.json、
independent-decision.json、guard-ready.json、deployment-armed.json、
old-exited-port-released.json、intermediate-new-receiver-old-android.json、deployment-release.json、
guard-released.json、final-running-identity.json、final-state.json、final-health-snapshot.json、
read-only-input-state.json、temporary-tasks-removed.json。失败探针文件已在文件名标注
failed-attempt，最初文本布局导出不是恢复源，实际恢复仅使用 android/layout.raw。
