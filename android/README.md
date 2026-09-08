# Android Touch Capture Prototype

验证原始触摸采集、记录与 Protocol v1 UDP 传输：

```text
Android Touch Digitizer → MotionEvent → Historical + Current Samples
                       → TouchSample → Logcat / CSV
                                     → Protocol v1 Encoder → UDP → Windows Receiver
```

应用使用 Java 和 Android 原生 Activity/View，只有一个空白触摸区域。
每条原始样本同时输出到 Logcat，并按原始顺序记录到应用私有目录中的 CSV。
没有第三方运行时依赖、测试框架、统计界面、设置、服务、运动处理、
手势识别或鼠标输出。

## 文件职责

| 文件 | 职责 |
|---|---|
| `settings.gradle` | 声明构建仓库及唯一的 app 模块 |
| `build.gradle` | 固定 Android Gradle Plugin 8.13.0 |
| `gradle.properties` | Gradle JVM 内存及 UTF-8 编码配置 |
| `gradlew` / `gradlew.bat` | 标准 Gradle Wrapper 启动脚本 |
| `gradle/wrapper/gradle-wrapper.jar` | Wrapper 启动组件 |
| `gradle/wrapper/gradle-wrapper.properties` | 固定 Gradle 8.13 分发配置 |
| `app/build.gradle` | 应用 ID、SDK 版本和 Java 编译配置 |
| `app/src/main/AndroidManifest.xml` | Activity、启动入口、竖屏及 UDP 必需的 INTERNET 普通权限 |
| `MainActivity.java` | 显示采集区域、管理记录器和 Sender 生命周期，在暂停时终止本地采集 |
| `TouchCaptureView.java` | 采集单指 DOWN/MOVE/UP/CANCEL，逐条提取历史和当前样本 |
| `TouchSample.java` | 不可变原始采样数据 |
| `TouchSampleLogger.java` | 输出采样日志及会话内相邻采样间隔 |
| `TouchRecordWriter.java` | 按采集顺序将原始样本写入应用私有目录中的 CSV |
| `ProtocolV1Encoder.java` | 将同一事件的原始样本编码为冻结的 Little Endian 数据包 |
| `UdpTouchSender.java` | 单线程、有界 FIFO、单 socket 发送，DOWN/UP 各发送三份相同数据 |
| `tests/ProtocolV1EncoderTest.java` | 无框架的固定字节及字段有效性测试 |
| `tests/Test-ProtocolV1Encoder.ps1` | 用 JDK 编译和执行编码测试 |
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
```

只要测试手机可通过 ADB 访问，就必须覆盖安装本次新构建的 APK，再重新启动应用并
确认 MainActivity 位于前台。不能因为手机上已经安装或正在运行 rightpad 而跳过安装。

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
- `session`：当前 View 生命周期内递增的触摸编号，同步作为 Protocol v1 的 sessionId。

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

目标常量为 UdpTouchSender.RECEIVER_IPV4 = "192.168.110.248"，端口 50000。
这是本次 Windows 到 Xiaomi 14 的局域网 IPv4。PC 地址变化后需要修改常量并重建；
没有自动发现或配置系统。启动 Receiver 后再启动 Android；
Sender 重启后也应重启 Receiver，以建立新的 sequence 基准。

每个 DOWN/UP 编码一个样本，MOVE 将全部 historical + current 编为一个逻辑包，
顺序与 CSV/Logcat 相同。CANCEL 只在本地记录并终止采集，不发送任何替代事件。
sequence 从 0 开始，每个逻辑包递增一次，跨 session 延续；不处理 uint32 回绕。
DOWN/UP 各发送三份完全相同的字节（同一 sequence），MOVE 只发送一次。

触摸回调创建样本、记录及编码；仅编码后的字节进入容量为 8 的 FIFO。
一个专用 Thread 使用一个 DatagramSocket 顺序发送，UI 不执行网络调用。
队列满时丢弃最旧的待发送逻辑包并记录 queue_overflow droppedSequence。
这包括可能丢弃 DOWN/UP；三份冗余不保证交付，也不恢复队列已丢弃的包。
CSV/Logcat 仍保留全部原始样本。发送错误记录日志，无 ACK 或重传协议。
Activity 销毁时丢弃待发包并关闭 socket、唤醒线程；暂停仅停止本地采集，
不会伪造 UP。Receiver 原有 2 秒 timeout 仍只是诊断，没有 heartbeat。

唯一新增权限是 UDP 必需的 android.permission.INTERNET（普通权限，无授权弹窗）。
没有存储权限、网络状态权限或第三方依赖。筛选 RightpadUdp 可查看启动、
每个逻辑包的发送份数和错误；packet_sent 仅证明系统接受发送，交付以 Receiver 为准。

运行 tests/Test-ProtocolV1Encoder.ps1（JAVA_HOME 指向 JDK）执行编码测试，
再运行 gradlew.bat assembleDebug lintDebug。在另一个终端启动正式 Receiver：
dotnet run --project ..\windows\Rightpad.Receiver --configuration Release。
安装、启动 Android 后，可用 adb shell input tap 600 1200 和
adb shell input swipe 500 1500 650 700 1500 自动验证链路。

固定字节测试与 Windows Decoder 测试共用的已知 44 字节 MOVE 示例逐字节对照，
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

## API 依据

- [Android MotionEvent](https://developer.android.com/reference/android/view/MotionEvent)
- [Android Gradle Plugin 8.13 兼容性](https://developer.android.com/build/releases/agp-8-13-0-release-notes)
