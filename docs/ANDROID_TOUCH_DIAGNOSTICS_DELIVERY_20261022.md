# Android 原始触摸诊断默认关闭与有界录制交付

日期：2026-10-22，Asia/Shanghai。范围：全项目审查第 4 项；仅 Android、相关测试和当前文档。

源码、自动化、隔离构建和 Android 更新已经完成。最终 App 普通启动在前台运行，现有
Receiver 未重启。物理移动、单击、双击拖动、振动、盲操作及游戏体验由用户最终验收；
本报告不把 Connected、自然输入日志或自动化通过视为真人手感通过。

## 已确认的原路径

- 现场 `MainActivity.onCreate` 无条件尝试创建 `TouchRecordWriter`。当前 Android 配置
  compile/target SDK 36、min SDK 34、Java 17，没有控制录制或逐样本日志的构建开关。
  已安装版本为可调试的 `com.rightpad.capture`、versionCode 1、versionName 0.1。
- `TouchCaptureView.captureEvent` 在 UI 线程提取全部 historical/current 样本，逐样本
  调用 `recordWriter.write` 和 `TouchSampleLogger.log`，随后才调用 `udpSender.submit`。
  编码及原 Touch 队列入队也在 UI 线程；实际 DatagramSocket 发送在既有 Sender 线程。
- BufferedWriter 表头 write/flush 在 Activity 创建时同步执行；逐样本写缓冲可触发
  底层文件写。UP 和 stopCapture 的 flush 同样在 UI 线程；样本写入可能发生在该批
  输入 submit 之前。UP 自身的显式 flush 在该次 captureEvent 返回之后，不把它误写成
  “UP flush 必然在 UP submit 之前”。销毁时 close 也在 UI 线程。
- 每个样本先构造详细 `Log.i` 字符串；Sender 线程对成功 Touch 包及 gamepad 状态
  构造成功日志。它们没有默认关闭或单次上限。
- 原 CSV 以秒级日期时间命名，冲突追加数字后缀，用 createNewFile 避免覆盖。没有
  时间／条数／字节上限，也没有轮转清理。写／flush 失败会尝试关闭并停用该 writer，
  创建失败后仍可继续输入，但这些同步操作仍占据调用线程。
- 没有发现可直接复用的触摸详细诊断开关；因此采用明确的一次性开发启动参数。

本轮只读清单观察到 82 个旧 CSV。首次完整清单共 669,255,570 字节，安装前共
670,165,782 字节；其中当时活跃的 `touch-recording-20261019-123657.csv` 从
166,528,379 增至 167,438,591 字节，两次清单间增加 910,212 字节。只枚举名称和体积，
没有读取全部旧录制内容。上述证据确认同步额外工作与文件增长，不能确认当前卡顿根因。

## 实际实现与入口

`MainActivity` 普通启动时 `diagnostics=null`，不访问录制目录、不创建 writer、诊断
队列或线程。采集 UI 先按原样提交 Touch，再在诊断启用时非等待 offer 不可变样本引用。
Sender 保留真实发送顺序／份数，成功发送后只在诊断启用时 offer 元数据；文件 I/O
没有搬到 Sender。默认分支不构造逐样本／逐包成功日志字符串。

开发执行者开启诊断：

```powershell
adb shell am start -S -n com.rightpad.capture/.MainActivity --ez rightpad.diagnostic.touch true
```

这是会重启 Android Sender 的开发操作，仍须先准备独立恢复保护。无需用户手改文件或
输入命令。普通启动不带参数即关闭；参数不持久化，onCreate 消费参数，系统恢复的
Activity（savedInstanceState 非空）不重新开启。没有设置页、配置同步、服务、Socket、
WakeLock、常驻线程池或通用日志框架。

固定边界：60 秒或 10,000 条诊断事件，先到者停止。事件包括样本和 Touch/gamepad
成功发送记录；满队列的尝试也计入条数。容量 256，只在显式诊断期间运行一个
`RightpadTouchDiagnostic` 线程。最多 10,000 行样本，详细 Logcat 共用相同边界。
旧文件不覆盖、不删除、不轮转；每次再次显式开启创建新文件。

worker 独占文件创建、表头 flush、样本写入和最终 close/flush。队列满只丢诊断并计数；
不阻塞等待磁盘，不改写或主动丢弃真实 Touch。条数到达时允许处理已入队诊断，但仍受
时间边界限制。Pause、目标丢失／切换、模态输入切换、detach、Power、Sender close、
Activity destroy 终止诊断并丢弃队列；Resume 不续录。UI 不 join 或排空。已开始的
文件调用可能完成，随后 worker 关闭资源；不能把异步 stop 解释成能够强制撤销 OS
中已进行的写调用。文件创建、写入、关闭／flush 失败仅警告并结束诊断，不重放输入。

CSV 字段不变：`sessionId,sampleIndex,action,source,x,y,eventTimeNs`。sampleIndex
为本次诊断观察的原样本序号；诊断丢失时会有缺口。CSV 是有界的尽力证据，不能默认
当成完整无损输入。结束摘要包含 reason/samples/attempted/dropped；dropped 只计
满队列损失，不包括生命周期或时间结束时丢弃的尾部。既有严格 E2E 比对器仅适用于
无诊断丢失、无截断且从 Touch sequence 0 起完整采集的材料。

没有修改采集、采样顺序／时间／坐标、编码、Touch 队列或副本规则；单指、竖屏、固定
unbuffered、Protocol v2、run/session/sequence、发现／心跳及既有生命周期仍保留。
没有调整 B/X、Mode、反馈、Motion、M/C、Sensitivity、重建、量化或 Virtual HID。

## 自动化与实测范围

主机 Windows JDK 17 模式（`javac --release 17`）执行新增 `TouchDiagnosticsTests`：
8 组全部通过。包括默认零文件；带两条历史样本的五样本 fixture 字段／顺序；条数上限；
时间上限；唯一文件名及旧文件保留；阻塞写入／满队列；创建、写入、close/flush 失败；
暂停／目标切换／关闭；生产 Activity/View/Sender 实际调用关系检查。

阻塞 sink 的测试中，诊断线程仍被阻塞时，loopback UDP 已收到完整真实输入及副本；
stop 返回后才放开 sink，队列旧工作被丢弃。关闭／开启／停止／再次关闭诊断时，同一个
实际 UdpTouchSender 对相同 fixture 生成预期完整字节，run/session/sequence 正确，
DOWN/UP 各三份、MOVE 一份，无额外 Touch。失败前后也继续收到预期 UDP。这些是
主机替身及真实 loopback socket 功能证据，不是 Android 端到端延迟或手感测量。

既有 `android/tests/Test-ProtocolV2Encoder.ps1` 回归全程退出 0，覆盖 Mode contact、
布局保护、编码／Sender、UI/power、发现、触觉协议／listener、Screen Controls、布局、
gamepad/dwell、targetless、B/X 配置、反馈、LR、health、profile、fixed unbuffered 和
reserved control。新增诊断测试已接入该入口；最终诊断源码另以隔离入口执行 8/8。
没有运行手机注入式 instrumentation，也没有运行无关 Windows 全量回归。

隔离 Gradle 输出位于证据目录 `gradle-build`，使用 `--no-daemon --max-workers=1`，
最终 `assembleDebug lintDebug` 成功。lint 0 error、14 warning，涉及现有 target API、
竖屏／清单、图标、View 构造器和编辑器字符串，另有既有 API 弃用提示；本轮未扩展修复。
最终部署源码包含条数边界并发收尾和 Activity 恢复不续录的复核修正。

| 确实测到的项目 | 结果与限制 |
|---|---|
| 主机五样本 fixture | 诊断 CSV 272 字节；默认分支无文件，0 字节；包含两条 historical |
| 手机普通启动 | 首次普通启动未新增文件；后续普通运行有自然输入，CSV 清单和体积不增长 |
| 手机显式诊断 | Android 16/API 36，time_limit，2,305 样本，4,672 诊断尝试，满队列丢弃 0 |
| 手机文件边界 | 125,049 字节，停止后再隔 3 秒仍为 125,049 字节 |
| 自然录制字段检查 | 序号连续、会话内时间非递减；2,305 current、0 historical；不据此推断硬件采样率 |
| 文件清单 | 原 111 个私有文件（含 82 旧 CSV）名称／大小保留；仅增加本轮显式诊断 CSV |

没有做耗时分布、CPU 或文件系统调用次数前后基准；没有测手机触摸到 Windows 输出的
端到端延迟，不报告减少了多少毫秒，也不认定已解决卡顿。主机 fixture 与手机自然录制
是不同数据集，不能拿两者字节数做前后性能比较。自然录制没有历史样本；历史批次的
正确性由主机 fixture 验证，未为取得历史样本注入手机手势。

## 独立保护与部署

安装前从当前设备拉取 `android-before.apk`，精确导出正式
`screen-controls.properties`，备份 Receiver settings 和本轮涉及源码／文档并保存哈希。
备份与候选包名、versionCode/versionName 相同；两个 APK 均通过 apksigner 验证，证书
SHA-256 均为 `978223c60c5473cfc61a1999596285c93d664595eac71f9573e71fc781705f1d`。
同签名、同版本号具备双向 `adb install -r` 覆盖条件；未卸载、清数据或修改布局。

Android 专用临时 Scheduled Task 使用当前用户 InteractiveToken / Limited、隐藏
PowerShell 窗口，无持久触发器。最终预检 PID 30552、父 PID 2432（任务调度器），
在 18:36:36 独立验证 ADB、当前已安装备份哈希、备份位置、签名、候选、布局、原
Receiver 和 settings。18:38:29 独立任务记录 armed，早于 install。安装窗口若失败、
显式请求回退或 240 秒未验证，任务自行执行备份 APK 覆盖、恢复必要布局并重新启动
MainActivity；操作不依赖 App、Codex 或当前终端继续运行。

实机 `adb install -r` 返回 Success；MainActivity 普通启动前台正常，之后仅为验证
本任务显式开启一次诊断，达到边界后恢复无诊断参数的普通启动。全程无 ADB tap/swipe、
无游戏启动。18:39:49 独立任务再次核对最终安装哈希、布局、前台及原 Receiver 后
记录 `candidate_verified` 并退出 0；18:41:50 撤销本轮临时任务，恢复包和脚本保留。
真实故障回滚分支没有执行，不把预检、健康放行写成实际回滚演练。

Receiver 始终为 PID 22204、17:32:46 启动的原实例，Session 5、Runtime RunId 1，
UDP 50000 仍归它所有，未改 Windows 程序、重建或重启 Receiver。原启动上下文为
当前用户 Medium / Default 桌面，保持同一实例。最终 Android PID 21068，新 Sender run
`99C8220F512055A7` 由 Heartbeat 自然接纳；Receiver 记录 Connected，以及随后自然
Touch 的 sequence 0/session 1 基线。既有累计 click/drag/反馈记录延续。
这证明重连和输入接纳，不证明用户已经认可实际控制或游戏体验。

最终只读 Logcat 中无逐样本／逐包成功日志和 Sender 错误；`ps -T` 的 CMD/COMM
字段能看到既有 Sender/Haptic/Discovery worker，没有诊断 worker。

## 版本、数据及恢复位置

证据与恢复包统一保留在：

`C:\rightpad\windows\test-results\android-touch-diagnostics-20261022`

| 内容 | 路径或哈希 |
|---|---|
| 当前 APK | `gradle-build/app/outputs/apk/debug/app-debug.apk`，0.1 / code 1 |
| 当前 APK SHA-256 | `F47BC9E45680D0DF4E21AAC99FA2722C30C0CE1D5F010BB5E22133CFC8A277BA`；已和安装文件核对 |
| 本轮安装前 APK | `android-before.apk` |
| 备份 APK SHA-256 | `DFE25FDE0F358DF72E3C473713F367C98AE3C07F8870D6E0483BE840E416ADED` |
| 正式布局备份 | `screen-controls.properties`，备份／手机更新后 SHA-256 一致 |
| 布局 SHA-256 | `0560DEA68780DDFF60249429A1EAB669CAF685E0719F2E4F61C38AB724F3A0CC` |
| 正式 Receiver settings | `receiver-settings-before.json`，更新后正式文件哈希一致 |
| settings SHA-256 | `C3E4B9CBE21246FD004BC55DDEA360C32B88D5D13CA9F5AE20D36B7F69B4C7F1` |
| 源码／文档备份 | `before-source/`、`source-before.json` |
| 独立保护 | `AndroidGuard.ps1`、`StartAndroidGuard.ps1`、`guard-ready.json`、`guard-armed.json`、`guard-finished.json`、`guard-cleanup.json` |
| 安装／前台／日志 | `deployment-attempt2.txt`、`install.txt`、`foreground-default-final.txt`、`logcat-default-final.txt` |
| 最终状态／数据清单 | `final-state.json`、`data-inventory-check.json`、`normal-no-growth-check.txt` |
| 自然诊断 | `device-diagnostic.csv`、`device-csv-check.json`、`device-diagnostic-summary.json` |
| 测试／构建 | `diagnostics-tests-deployable.txt`、`android-regression.txt`、`android-build-deployable.txt` |

旧文件只核对名称和大小，未全量读取／哈希旧内容；不宣称所有旧证据已做内容级校验。
JSON 使用明确的字符串／数值／布尔与有界摘要，大日志独立存放，没有直接序列化
PowerShell 文件／进程扩展对象或嵌入大日志。

必要失败尝试保留：首次安装前脚本因为 PowerShell 7 JSON 自动日期转换丢失时区，
误判预检时间并在安装前退出；`deployment.txt` 保留该结果。改为字符串日期和
DateTimeOffset 后重跑成功，手机此前未受影响。首次独立预检对应的候选在后续源码
复核后重建，旧预检以 `pre-rebuild-*` 副本保留，部署使用重新独立完成的最终预检。
末次只读线程查询最初无输出，随后 ps NAME 字段也不足以判定线程；最终改用 CMD/COMM
完成补检。初始状态副本及 `thread-check-correction*.json` 保留，最终状态引用正确证据。

## 交接与待验收

后续正常打开 App 即为不录制的版本，不需要用户关闭开关。开发者再次取详细证据时
使用上述显式启动参数并先建立恢复保障；触摸期间不要求用户执行命令。建议用户自然
确认普通移动／停止、单击及确认反馈、双击拖动和抬手释放，再确认常用 B/X、Mode，
最后在自己的游戏中比较连续视角、停顿和卡顿。遇到异常可报告现象和大致时间，后续
先读现有日志；不能为了取证改变采样、发送或 Motion 手感。

本轮没有 Git commit/push/reset、历史修改、git clean、旧证据删除／迁移、旧恢复包
清理；只对本轮临时 Scheduled Task 做了安全撤销。保留现场已有未提交／未跟踪成果。
没有修改驱动、Broker、许可、设备、协议、手机正式布局、DeepSeek Harness 或电脑操作插件。
