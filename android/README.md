# rightpad Android Touch Capture and Screen Controls

验证原始触摸采集、记录与 Protocol v2 UDP 传输：

```text
Android Touch Digitizer → MotionEvent → Historical + Current Samples
                       → TouchSample → Logcat / CSV
                                     → Protocol v2 Encoder → UDP → Windows Receiver
```

应用使用 Java 和 Android 原生 Activity/View。UI v1 由同一个全屏
`TouchCaptureView` 使用 Canvas 绘制；控件外区域保留原始触摸采集。
顶栏显示系统真实电量；电源按钮短按后通过正常 Activity 生命周期退出并移除任务。
Settings 齿轮打开 `Settings → Edit Controls Layout`，编辑面板使用原生 View。
rightpad 在前台运行时会保持屏幕唤醒，离开前台后恢复系统默认超时行为。
每条原始样本同时输出到 Logcat，并按原始顺序记录到应用私有目录中的 CSV。
没有第三方运行时依赖、测试框架、服务、鼠标运动处理或 Android 鼠标输出。
Screen Controls 使用 Android 本地 SlideControl 逻辑和通用 full-state gamepad
transport；Mouse 手势仍由 Windows 识别。

## Screen Controls（Phases 1–4 已实现）

控件/编辑器的 logical state 已接入通用
`GamepadAggregator → GamepadState → GAMEPAD_STATE`，映射 BASE=B、UP=Y、DOWN=A。
Android 复用原 UdpTouchSender 的 worker/socket，独立 gamepad sequence、最新状态槽、
变化三份副本及 100ms held refresh。Windows 通过独立 300ms lease 释放丢失的 UP。
布局编辑器、默认参数和 25ms pulse scheduler 均未改写。完整协议与安全规则见
[GAMEPAD_PROTOCOL.md](../docs/GAMEPAD_PROTOCOL.md)。

唯一注册实例为 `xbox.b.slide`，标签 `B`。注册集中在 `ScreenControls`，
`ControlRect`、`ScreenControlDefinition`、`ScreenControlInstance`、
`ScreenControlRouter`、`SlideControlGesture`、`ScreenControlLayoutEditor`、
`ScreenControlLayoutStore` 和 `ScreenControlEditorPanel` 均不按 B 命名或存储。
同一套 editor/store/router 支持按 stable ID 添加实例；纯 Java 测试使用其他 ID
及多个实例验证复用。当前定义的行为是 SlideControl，不引入未需要的插件层。

- DOWN 为 Pending，逻辑输出 Neutral；短按 UP 输出 BASE，保持 25 ms 后 Neutral。
- 长按 400 ms 输出 BASE held；上滑 0.7 dp / 下滑 3.0 dp 输出 SLIDE_UP / SLIDE_DOWN。
- Design A：长按后仍可滑动，先释放 BASE 再进入方向状态；首次方向成立后锁定至 UP/CANCEL。
- 逻辑语义分别对应 Xbox B / Y / A，经 30-byte v2 type5 GAMEPAD_STATE 驱动
  Windows libvirtualhid Xbox360 ABI2 backend。`RightpadControl` Logcat 记录逻辑变化。
- 四个参数在 DOWN 复制为不可变 snapshot，dp 当次配置转换为 px；没有 Android
  behavior preferences。Receiver 是行为参数唯一真源；Controls 成功 Save 后发布
  epoch/revision snapshot，Android 仅 runtime cache，下一次 DOWN 使用新配置。
- 一个可取消的 UI deadline callback 驱动长按和 pulse。新 DOWN 替换未完成 pulse，
  不排队快速点击。CANCEL、pause、stop、进入设置/编辑及 View detach 全部 Neutral。
- Active contact 为实心 `#FF3B30`，UP 后恢复 `#00C853`；文本白色，矩形无圆角、
  透明、阴影或渐变。默认 64 dp 正方形，按 View bounds 限制尺寸。

DOWN 只选一次 SETTINGS / POWER / SCREEN_CONTROL / MOUSE / NONE。进入/离开
矩形不转移 owner。第二 pointer 或 pointer ID 丢失取消整个 gesture，直到新 DOWN。
普通 control 的绘制和命中使用同一份整数 `ControlRect`，没有 hit slop；编辑模式下
显示 4 边和 4 角 handles（通常 7 dp，最小矩形时缩小以保持可操作）。

编辑仅修改 committed 的 draft copy。拖主体 Move，拖边/角自由缩放，最小 20×20 px，
不锁比例。X/Y 为 View 内矩形左/上边缘，Width/Height 为宽高，全部整数 px。
拖动立即更新字段；合法数字立即更新同一 draft；非法或越界数字显示 field error、
禁用 Save、保留最后合法 preview。拖动/缩放实时 clamp，数字输入不做 silent clamp。
编辑时所有触摸由 editor/面板消耗，不提交 Mouse Touch v2，也不运行 gameplay action。
`Panel ↕` 把面板在屏幕顶部/底部切换，允许编辑原先被面板覆盖的区域；软键盘不改变
布局坐标系，底部面板随 IME inset 上移以保持数字字段与 Save 可见。
返回手势与 Cancel 相同；真正 View 尺寸变化时丢弃 draft 并重新加载布局。

Save 再次验证所有矩形，先同步写临时文件并 atomic rename，成功后才 committed=draft
并退出。磁盘失败保留 draft 与原 committed。Cancel 丢弃 draft。Reset 仅把 definition
default 放入 draft，需要 Save 才持久化。存储在私有 `files/screen-controls.properties`：

```properties
version=1
controls.xbox.b.slide.xRatio=...
controls.xbox.b.slide.yRatio=...
controls.xbox.b.slide.widthRatio=...
controls.xbox.b.slide.heightRatio=...
```

Ratio 相对于完整可用 View 的宽/高；读取时 normalized → integer px → validate/clamp。
无效字段/未知版本回到该 definition 的默认值。新分辨率下重新计算，并保证完整位于
View bounds 内。布局文件不包含行为参数。

自动验证：`tests/Test-ProtocolV2Encoder.ps1` 同时运行既有测试和新增 49 个纯 Java
control/router/layout 场景。真机测试使用独立 APK，无第三方测试依赖，生产 APK 不包含
测试 runner。运行前确保设备解锁且无真人同时触摸；UI smoke 临时使用测试矩形
（100,600,240,160）验证重建/重启恢复，并在 finally 恢复原始布局文件，不清应用数据：

```powershell
.\gradlew.bat assembleDebug assembleDebugAndroidTest lintDebug
adb install -r app/build/outputs/apk/debug/app-debug.apk
adb shell am start -S -n com.rightpad.capture/.MainActivity
adb install -r app/build/outputs/apk/androidTest/debug/app-debug-androidTest.apk
adb shell am instrument -w com.rightpad.capture.test/com.rightpad.capture.ScreenControlsSmoke
adb shell am start -S -n com.rightpad.capture/.MainActivity
```

`ScreenControlsSmoke` 在真实 Activity/View 上验证颜色、Settings 入口、Mouse 工具类型拖动、
边/角缩放、数字输入、Save disabled、边界、持久化、Cancel/Reset、定时器及输入隔离。
截图在 Android 外部应用 files 目录的 `controls-*.png`。ADB/UI 注入验证功能与路径，
不代表真实手指触摸采样质量或游戏手感验收。完整链路另以生产 XInput observer
验证 B/Y/A、minimum dwell、lease 和 Design A；人工清单见
[SCREEN_CONTROLS.md](../docs/SCREEN_CONTROLS.md)，状态为
**MANUAL HUMAN ACCEPTANCE REQUIRED**。

Tap BASE 将 DOWN snapshot 的 TapHoldMs（默认 25 ms）作为 generic minimumDwellMs
传输；Sender 保留 minimum wire hold，Receiver 另以本地单调 deadline 保护普通
Neutral。CANCEL、pause、editor、target/run replacement、close 使用 FORCE_NEUTRAL，
新 Y/A 全状态立即替换 B；LongPress 和 Slide 的 minimumDwellMs 为 0。
不通过增加 Tap Hold 掩盖 jitter，也不使用 Mouse MotionClock 调度 gamepad release。

配置复用既有 UDP 50002 feedback listener；type6 request 使用 Sender 的 UDP 50000，
unknown 每 1 秒、known 每 5 秒恢复丢失的 snapshot，不增加 socket/thread。
完整协议见 [CONTROL_CONFIG_PROTOCOL.md](../docs/CONTROL_CONFIG_PROTOCOL.md)。
构建/部署保留当前本机布局，不能用历史测试矩形覆盖用户后来保存的布局。

## 文件职责

Phase 5A.2 的 Screen Control 本地反馈由 `ScreenControlFeedback` 和
`ScreenControlHapticFeedback` 提供：DOWN 的 PRESS、首次 MOVE 方向成立的
DIRECTION_COMMIT 在 API ≥26 使用固定 10 ms / 255 one-shot，旧 API 使用 10 ms。
不优先使用 HEAVY_CLICK，不自动延长振动。Manifest 保留 VIBRATE，无运行时权限弹窗。
LongPress、UP、CANCEL、Tap pulse、编辑器、Settings、Power 不触发此反馈。
纯逻辑 SlideControlGesture 保持不变。Phase 5A.4 将已通过验证的 Windows RPHF CLICK
末端执行改为独立的 `TouchpadClickFeedback` / `TouchpadClickHapticFeedback`：
仅调用一次 `View.performHapticFeedback(HapticFeedbackConstants.CONFIRM)`，
恢复系统调校；移除 Touchpad one-shot/legacy Vibrator 分支，无重试或 fallback。Receiver 确认、RPHF
格式、identity validation、dedupe 完全不变，Touchpad DOWN/MOVE 不在本地震动。
Screen Control 仍为 10 ms / 255，没有共享强度参数，VIBRATE permission 保留。
6 ms / 120 真人清脆感验收未通过后，用户明确选择恢复 CONFIRM；不得自动调整波形。
两种震感能否盲操作区分必须由用户在真机确认，不能仅凭日志判定。

| 文件 | 职责 |
|---|---|
| `settings.gradle` | 声明构建仓库及唯一的 app 模块 |
| `build.gradle` | 固定 Android Gradle Plugin 8.13.0 |
| `gradle.properties` | Gradle JVM 内存及 UTF-8 编码配置 |
| `gradlew` / `gradlew.bat` | 标准 Gradle Wrapper 启动脚本 |
| `gradle/wrapper/gradle-wrapper.jar` | Wrapper 启动组件 |
| `gradle/wrapper/gradle-wrapper.properties` | 固定 Gradle 8.13 分发配置 |
| `app/build.gradle` | 应用 ID、SDK 版本和 Java 编译配置 |
| `app/src/main/AndroidManifest.xml` | Activity、竖屏、INTERNET/ACCESS_NETWORK_STATE 及 Android 16 LNP 权限声明 |
| `MainActivity.java` | 显示采集区域、订阅系统电量、管理记录器和 Sender 生命周期，在暂停时终止本地采集 |
| `TouchCaptureView.java` | 全屏 Canvas UI；固定 owner 路由 Settings/Power/controls，采集 Mouse 区单指原始触摸 |
| `ScreenControls.java` | control 注册、Canvas 绘制、UI deadline adapter，逻辑状态不联网 |
| `ControlRect.java` / `ScreenControlLayoutEditor.java` / `ScreenControlLayoutStore.java` | 通用整数矩形、单一 draft、normalized 持久化 |
| `ScreenControlEditorPanel.java` | 原生 X/Y/Width/Height 输入及 Save/Cancel/Reset |
| `SlideControlGesture.java` / `ScreenControlRouter.java` | 纯 Java 手势状态机和固定 owner |
| `TouchSample.java` | 不可变原始采样数据 |
| `TouchSampleLogger.java` | 输出采样日志及会话内相邻采样间隔 |
| `TouchRecordWriter.java` | 按采集顺序将原始样本写入应用私有目录中的 CSV |
| `ProtocolV2Encoder.java` | 精确的 v2 Touch / 10-byte heartbeat Little Endian 编码 |
| `UdpTouchSender.java` | 单线程、有界 FIFO、单 socket 发送，DOWN/UP 各发送三份相同数据 |
| `HeartbeatSchedule.java` | 500 ms deadline 算术；暂停禁用，超期不 burst |
| `ReceiverDiscoveryClient.java` | 独立 Wi-Fi-bound UDP 50001 discovery；NetworkCallback、选择及生命周期 |
| `DiscoveryProtocol.java` / `DiscoverySelection.java` / `DiscoverySchedule.java` | 固定二进制协议、first valid selection、2500 ms 超时及 probe 时序 |
| `DiscoveryBroadcasts.java` / `ConnectionDisplay.java` | IPv4 prefix broadcast 计算及真实连接文案 |
| `tests/ProtocolV2EncoderTest.java` | 无框架的固定字节、高位 runId 及字段有效性测试 |
| `tests/UdpTouchSenderTest.java` | 真实 loopback socket 的生命周期、sequence、心跳不饥饿测试 |
| `tests/Test-ProtocolV2Encoder.ps1` | 用 JDK 编译并执行 encoder 和 Sender 测试；Log stub 不进入 APK |
| `tests/Verify-SenderE2e.ps1` | 逐条对比真机 CSV、Logcat、Receiver 日志及重复包统计 |
| `.gitignore` | 忽略构建产物、IDE 文件及本机路径配置 |
| `README.md` | 构建、安装和人工验收说明 |

Java 文件位于 `app/src/main/java/com/rightpad/capture/`。
`local.properties` 是本机 SDK 路径配置，不提交版本控制。

## 构建

环境：

- Android Studio，或可运行 Gradle 8.13 的 JDK（建议 JDK 17 或 21）。
- Android SDK Platform 36。
- Android SDK Build-Tools 35.0.0（AGP 默认使用）。
- Android SDK Platform-Tools（安装和 Logcat 使用）。
- 手机 Android 14 / API 34 或更高版本；真实纳秒事件时间 API 从 34 开始公开。

Android Studio 打开本目录 `C:\rightpad\android`，完成 Gradle Sync 后运行 app。
SDK Manager 中安装上述 SDK 组件。Gradle JDK 可使用兼容的 Android Studio 内置 JBR。

PowerShell 构建：

```powershell
Set-Location C:\rightpad\android
$env:JAVA_HOME = 'C:\Program Files\Android\Android Studio\jbr'
.\gradlew.bat assembleDebug
```

将 `local.properties` 中的 `sdk.dir` 设置为本机 SDK 的实际路径，例如：

```properties
sdk.dir=C\:/Users/zhq/AppData/Local/Android/Sdk
```

第一次构建可能需要从官方仓库下载 Gradle 和 Android 构建组件。
工程没有 application/library 依赖项，也没有添加测试依赖。

APK 输出：

```text
C:\rightpad\android\app\build\outputs\apk\debug\app-debug.apk
```

开发阶段每次成功重新构建 APK 后，默认完成以下整个流程：

```text
Build
→ adb install -r
→ force/restart app
→ launch com.rightpad.capture/.MainActivity
→ verify foreground Activity
→ keep current v2 WPF Receiver / Runtime running
→ verify UDP 50000
→ verify Disconnected/Connected and new senderRunId / sequence 0 baseline
→ end-to-end smoke
```

只要测试手机可通过 ADB 访问，就必须覆盖安装本次新构建的 APK，再重新启动应用并
确认 MainActivity 位于前台。保持当前 v2 WPF PID 和 Receiver Runtime RunId 不变，
验证连接状态、新 Sender baseline、UDP 50000 和端到端 RAW / Single Tap。
不能因为手机上已经安装或正在运行 rightpad 而跳过安装。只有 Windows 程序本身需
启动/更新时，才使用 `windows/tools/RightpadReceiverTask.ps1` 独立交互 launcher；
不得通过重启 Receiver 掩盖 Android Sender 恢复问题。

可选静态检查命令：

```powershell
.\gradlew.bat lintDebug
```

## 安装与 Logcat

在手机启用 USB 调试，连接电脑并接受手机上的调试授权。
以下命令中的 SDK 路径应替换为本机实际路径：

```powershell
Set-Location C:\rightpad\android
$adbPath = 'C:\Users\zhq\AppData\Local\Android\Sdk\platform-tools\adb.exe'
& $adbPath devices -l
& $adbPath install -r .\app\build\outputs\apk\debug\app-debug.apk
& $adbPath shell am start -S -n com.rightpad.capture/.MainActivity
& $adbPath logcat -v brief -s 'RightpadTouch:I' '*:S'
```

Logcat 会先显示缓冲区内该标签的旧记录，再持续显示新记录。
若需要只看当前进程，可用 `adb shell pidof com.rightpad.capture` 查询 PID，
并在 logcat 命令中增加 `--pid=<实际PID>`。
多台设备连接时，通过 adb 的 `-s <设备序列号>` 指定目标。
这仅用于开发工具选择目标，应用没有多设备功能。

也可在 Android Studio 的 Logcat 中选择应用进程并过滤 `tag:RightpadTouch`。
在应用空白区域内用一根手指触摸、移动、抬起；系统状态栏和导航区域不属于采集区域。

## 日志含义

以下是格式示例，不是真机测量结果：

```text
session=1 pointer=0 action=DOWN source=current x=120.25 y=360.5 eventTimeNs=1000000000 dtNs=NA historySize=0
session=1 pointer=0 action=MOVE source=historical x=121.0 y=361.25 eventTimeNs=1004000000 dtNs=4000000 historySize=2
session=1 pointer=0 action=MOVE source=historical x=122.0 y=362.0 eventTimeNs=1008000000 dtNs=4000000 historySize=2
session=1 pointer=0 action=MOVE source=current x=123.25 y=363.0 eventTimeNs=1012000000 dtNs=4000000 historySize=2
session=1 pointer=0 action=UP source=current x=123.25 y=363.0 eventTimeNs=1020000000 dtNs=8000000 historySize=0
```

- `x/y`：相对于采集 View 的原始浮点像素坐标，未缩放、平滑或转换成位移。
- `eventTimeNs`：`MotionEvent.getEventTimeNanos()` 或 `getHistoricalEventTimeNanos()` 的返回值，
  以纳秒表示 `SystemClock.uptimeMillis()` 时间基准；不是回调到达时间、打印时间或日期时间。
- `action`：采样所属的 DOWN/MOVE/UP/CANCEL。
- `source`：historical 或 current。
- `historySize`：本次 MotionEvent 含有的历史样本数，不包括当前样本。
- `dtNs`：同会话内当前采样事件时间减去前一条采样事件时间；新会话第一条为 NA。
- `pointer`：Android 指针 ID。采集过程中不切换到其他手指。
- `session`：当前 View 生命周期内递增的触摸编号，同步作为 Protocol v2 的 sessionId。

每个 MOVE 先按 Android 提供的顺序记录全部历史样本，再记录当前样本。
一次 MOVE 若 `historySize=N`，应有 N 条 historical 和一条 current。
不会请求非批处理输入，不主动丢弃历史采样，也不使用墙上时钟。

## 人工验收

1. 落指：出现 DOWN，坐标和 eventTimeNs 有值，dtNs=NA。
2. 连续移动：出现 MOVE；当 historySize 大于 0 时，确认 historical 在 current 之前，
   数量匹配，历史采样使用各自的事件时间。
3. 抬指：出现 UP；重新落指后 session 增加，dtNs 重新从 NA 开始。
4. 保持一根手指移动一段时间：检查 MOVE 样本的 dtNs 分布，包含历史采样，
   不要把 Logcat 每秒打印行数或 MotionEvent 回调次数当成触摸硬件采样率。
5. 第二根手指落下：输出 `status=INTERRUPTED reason=multiple_pointers`，
   不记录第二根手指，剩余事件忽略到下一次新的单指 DOWN。
6. 采集中离开应用：输出 `status=INTERRUPTED reason=activity_paused`；
   返回应用并重新触摸时，从新会话开始。
7. 系统发出 ACTION_CANCEL 时，记录 CANCEL 并结束当前会话。
   生命周期中断只记录诊断行，不伪造 CANCEL 采样。

在一段连续移动且无明显停顿的采样中，若共有 N 条采样，事件时间跨度为 T 纳秒，
可用 `(N - 1) * 1_000_000_000 / T` 估计这段日志中的有效采样频率（N > 1、T > 0）。
同时检查间隔分布，不能只用平均值判断稳定性。

限制：

- 历史样本由设备及系统批处理决定，historySize=0 是合法情况；不会伪造样本。
- API 提供纳秒事件时间，但不保证硬件具备纳秒准确度；重复时间戳与 dtNs=0 仍原样保留。
- 直接逐条 Logcat 输出有开销，日志结果不等于关闭日志后的最终输入性能。
- 模拟器或 adb 注入事件不能证明手机触摸屏的真实采样质量，需真机手指操作验收。

## CSV 记录

应用每次创建 Activity 时在私有 `files` 目录创建一个新文件：

```text
/data/user/0/com.rightpad.capture/files/touch-recording-YYYYMMDD-HHMMSS.csv
```

同一秒内发生文件名冲突时追加 `-1`、`-2` 等数字后缀，避免覆盖已有记录。
文件名时间只用于数据管理；CSV 中的 `eventTimeNs` 始终来自 MotionEvent 的纳秒 API。旧的毫秒 CSV 不迁移。

表头：

```csv
sessionId,sampleIndex,action,source,x,y,eventTimeNs
```

每个文件的 `sampleIndex` 从 0 开始连续递增。CSV 不合并、不删除、不排序样本，
也不修改坐标或时间戳。收到 UP、CANCEL 或采集中断时刷新缓冲；Activity 销毁时关闭文件。

文件实际路径通过 `RightpadRecord` 标签输出：

```powershell
& $adbPath logcat -v brief -s 'RightpadRecord:I' '*:S'
```

Debug APK 可通过 `run-as` 检查私有目录。以下命令只读取手机文件：

```powershell
& $adbPath shell run-as com.rightpad.capture ls -l files
& $adbPath shell run-as com.rightpad.capture cat files/touch-recording-实际文件名.csv
```

应用没有文件导出功能，也不申请外部存储或文件访问权限。

## UDP Prototype 与自动验证

Production 没有固定 Receiver IP，也没有 fallback。连接任一地点 Wi-Fi 后打开 app，
通过独立 Discovery v1 UDP 50001 自动找到当地 Receiver，以 OFFER datagram source
IPv4 + 50000 作为 Touch 目标。两台 PC 预期不同时出现在一个 LAN；无需输入 IP、
选择主机或在 DHCP 变化后重新构建。搜索时显示 `搜索中 / —`，连接时显示
`已连接 / 实际 source IPv4`。Android 重新部署或 Sender 重启后，现有 v2 Receiver
通过新 senderRunId 自动建立 Touch sequence 基准，保持自身进程和 Runtime 不变。

每个 DOWN/UP 编码一个样本，MOVE 将全部 historical + current 编为一个逻辑包，
顺序与 CSV/Logcat 相同。Touch CANCEL 只在本地记录并终止采集，不伪造 Touch UP；
独立 gamepad 路径同时发送 FORCE_NEUTRAL。
sequence 从 0 开始，每个逻辑包递增一次，跨 session 延续；不处理 uint32 回绕。
DOWN/UP 各发送三份完全相同的字节（同一 sequence），MOVE 只发送一次。

触摸回调创建样本、记录及编码；仅编码后的字节进入容量为 8 的 FIFO。
一个专用 Thread 使用一个 DatagramSocket 顺序发送，UI 不执行网络调用。
队列满时丢弃最旧的待发送逻辑包并记录 queue_overflow droppedSequence。
这包括可能丢弃 DOWN/UP；三份冗余不保证交付，也不恢复队列已丢弃的包。
CSV/Logcat 仍保留全部原始样本。发送错误记录日志，无 ACK 或重传协议。
Activity.onCreate 创建无目标 Sender，使用 ThreadLocalRandom.nextLong 生成完整 64-bit
senderRunId，不持久化。目标变化先停止 capture，清空队列和 session gate，换 runId，
sequence 归零，先发 heartbeat 再接受新 DOWN。无目标时不发送、不积累 Touch。
onResume 立即 probe，由 fresh OFFER 确认后启用心跳；onPause 禁用心跳、
清空待发 Touch 和停止本地采集，保持同一 Sender/runId/sequence，不伪造 UP。
同目标且 resume 后 fresh OFFER 确认的 pause/resume 保留 runId/sequence；确认超时、Wi-Fi 变化和 target transition
会重新建立 run。Power/onDestroy 关闭 discovery、sender 和共享 haptic/config listener。

现有线程使用 timed poll 等待 Touch、gamepad refresh/hold、config request 或
500 ms heartbeat deadline，直接同一个 socket
发送心跳，心跳不进入 Touch queue。忙时仍检查 deadline；超期仅发一次，不补发历史。
Touch header 为 20 bytes；HEARTBEAT 恰为 10 bytes，只有 version=2/type=4/runId。
心跳没有 sequence，只发一份。Receiver 由心跳或 accepted Touch 续 2000 ms presence；
超时清旧输入并显示 Disconnected。原有 Touch silence 2 秒诊断独立保留。
Discovery 单独使用 Wi-Fi Network.bindSocket，不全局绑定进程，不进入 Touch hot path。
广播包含 limited broadcast 和按实际 IPv4 prefix 计算的 directed broadcast，去除重复。
Connected 每秒 probe，2500 ms 没有当前 receiverId OFFER 则 Searching 并清 target。
不采用 first-IPv4/NIC heuristic，不含 cloud 或手动 host UI。

权限为 INTERNET、ACCESS_NETWORK_STATE 和带 neverForLocation 的 NEARBY_WIFI_DEVICES。
Android 16 LNP 当前是 opt-in；正常模式不无条件弹 Nearby Devices 授权框，拒绝权限
会明确记录 discovery_permission_denied。没有 location、存储权限或第三方依赖。
筛选 RightpadDiscovery/RightpadUdp 查看低频状态、target、耗时和错误。
详细协议、生命周期和 Android 16 官方权限依据见
[DISCOVERY_PROTOCOL.md](../docs/DISCOVERY_PROTOCOL.md)。

运行 tests/Test-ProtocolV2Encoder.ps1（JAVA_HOME 指向 JDK）执行编码及 Sender 测试，
再运行 gradlew.bat assembleDebug lintDebug。安装、重启 Android 后，通过
当前保持运行的 v2 WPF 自动接受新 Sender run，检查 PID / Runtime RunId 不变。
双端恢复后，可用 adb shell input tap 600 1200 和
adb shell input swipe 500 1500 650 700 1500 自动验证链路。

固定字节测试与 Windows Decoder 测试共用的已知 52 字节 MOVE 示例逐字节对照，
覆盖 header offset、Little Endian、uint64 时间戳及 float32 位模式，另检查 DOWN/UP、
重复时间戳、多个样本和非法输入。测试只依赖 JDK，不引入框架。
ADB 注入可以验证真机上的采集/传输链路，不能替代真实手指 Motion Dataset。
真实硬件采样精度、Wi-Fi 长时间稳定性和最终延迟仍需后续测量。

采集同一次 Sender 运行的 CSV、Logcat 和正式 Receiver 日志后，可运行：

```powershell
.\tests\Verify-SenderE2e.ps1 -CsvPath <CSV路径> -LogcatPath <Logcat路径> -ReceiverLogPath <Receiver日志路径>
```

该验证要求从 sequence=0 开始的完整无丢包实验，并实际观察到 historical 样本。
它逐条检查 float32 位模式、纳秒时间、包内顺序，以及重复包不增加 acceptedSamples。
ADB 注入前需确保手机屏幕唤醒、应用已处于前台；命令成功不等于应用收到触摸。

## Phase 6C Controls 配置

Receiver 是 B/X behavior 的唯一真相源。RPCT v2 使用 36-byte header、
12-byte B record 和 14-byte X/LR record，当前完整快照为 62 bytes。
Android 先验证完整 B+X，再原子替换内存缓存；ACTION_DOWN 捕获配置，当前手势
不受中途 Save 影响。保留合法 v1 B-only 读取用于升级过渡，但不标记 B+X 完整同步。
type6 request 仍为 26 bytes，GAMEPAD_STATE 仍为 v2/type5、30 bytes。
B/X layout 独立保存在 Android；Controls Save 不改布局、手势分类或触觉策略。
详细约定与真实 Save 验收见 [CONTROL_CONFIG_PROTOCOL.md](../docs/CONTROL_CONFIG_PROTOCOL.md)。

## API 依据

- [Android MotionEvent](https://developer.android.com/reference/android/view/MotionEvent)
- [Android Gradle Plugin 8.13 兼容性](https://developer.android.com/build/releases/agp-8-13-0-release-notes)
