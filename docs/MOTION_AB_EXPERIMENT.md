# Motion 第一轮 A/B 实验（2026-09-13）

本轮是实验原型，等待真人游戏评价，未定稿、未 commit、未 push。目标是 relative displacement semantics + gimbal-like trajectory quality。A 为现有 RAW；B 为固定 250 Hz 输出机会、固定 12 ms 播放延迟、累计位置线性重采样。没有加入滤波、加速度、动态增益、预测、惯性或 glide。

## 1. 起点与变更范围

开始分支 main，HEAD 与 fetch 后 origin/main 都是 `0927a688fb9d4d4668c394ee1f0a3f92b5dd02bb`，工作树干净，ahead/behind 0/0。本轮保持该提交基线，变更留在工作树。未修改 Android、Touch v2、discovery、手势算法、点击振动协议、用户设置或其他仓库。

新增源码：`Motion/ITouchMotion.cs`、`Motion/ResampledMotion.cs`、`Motion/MotionClock.cs`、`Diagnostics/MotionTrace.cs`；新增测试/回放入口：`ResampledMotionTests.cs`、`MotionReplay.cs`。

接线变更：Receiver `Program.cs`、`Runtime/ReceiverRuntime.cs`、`UdpReceiver.cs`、`RawSampleLogger.cs`、`TouchSessionProcessor.cs`、`IMouseOutput.cs`、`LibVirtualHidMouseOutput.cs`、`NativeVirtualHidMouse.cs`；开发启动器 `windows/tools/RightpadReceiverTask.ps1`；测试 `Program.cs` 和 `LauncherTests.ps1`；`ViewModels/MainViewModel.cs` 与 `Views/MotionView.xaml` 将现有只读模式文字接到实际启动模式，避免 B 运行时仍显示写死的 RAW。

开发入口：`--dev-motion-mode RAW|RESAMPLED_250HZ`；可选 `--dev-motion-trace-dir <directory>`。默认 RAW、仪表关闭；没有增加正式持久设置或模式选择器。项目独立启动器接受 `-DevMotionMode` 和 `-DevMotionTrace`。启动日志明确 mode、4 ms、12 ms 和 trace 状态。使用默认参数重新启动仍是 A；本次留给真人测试的实例显式使用 B。

## 2. A 的兼容性

`TouchSessionProcessor` 只有实现 `ITouchMotion` 的声明变化，其处理函数体不变；`RawMotionProcessor` 完全未改。接口默认 `ProcessAt` 将数据转给原有 `Process`，RAW 仍在包到达时依次处理每个 historical/current sample，采用原截断与 residual、原设置快照和生命周期语义。原有 187 项回归测试保留且通过。

因此 A 的输入→位移/按钮行为保留。可选 trace 会增加观测开销；不能把启用观测后的墙钟时间称为和旧版本逐纳秒一致。A/B 成对采集都开启相同 trace 与外部见证，最终真人实例关闭 trace。

## 3. B 的数学模型与 residual

每次 DOWN 建立新会话。Android 点为 `p_i=(x_i,y_i)`，该段接收时取得的 Windows 设置为 `S_i=diag(sx_i,sy_i)`：

```text
Q_0 = (0, 0)
Q_i = Q_(i-1) + S_i · (p_i - p_(i-1))

P(t) = Q_i + (Q_(i+1)-Q_i) · (t-T_i)/(T_(i+1)-T_i)
       where T_i <= t < T_(i+1)
```

累计目标 Q 已包含每段增益；以后更改灵敏度不会重新缩放积压轨迹。每次输出机会取 `delta=P(t)-P_previous`，交给原 `RawMotionProcessor` 的单位增益实例：

```text
z = residual + delta
integer = trunc(z)       // each axis, toward zero
residual = z - integer
```

只有这个原有量化器拥有整数 residual。没有第二个 fraction accumulator，没有为了凑 250 Hz 发送假 ±1。零整数 delta 不调用 Move。每次会话结束/reset 清 residual；UP 后可能舍去不足 1 count 的量化余数。因轨迹分段/符号变化和浮点顺序，B 与 A 的整数终点可相差 1 count；不能声称两者逐个输出或每个整数终点相同。

## 4. 时间映射与 buffer

以本会话 DOWN 的 Android `a0` 和 Windows 接收 QPC `r0` 固定锚定：

```text
T_i = r0 + 12 ms + (a_i - a0) converted from ns to QPC ticks
deadline_k = r0 + 12 ms + k · 4 ms
```

锚点在会话内不追踪网络抖动，不估计时钟偏差，不调整 L。不能拿 Android 绝对时间减 Windows QPC 当网络单向延迟。12 ms 是相对这个接收锚点的播放延迟，不是从手指到游戏画面的完整延迟。

trajectory buffer 为固定容量 4096 的 `Point(Time,X,Y)` 环形数组。每个 MOVE 包中的 historical/current 点按到达顺序入队，保留批次内转向；tick 消耗已经过去的点，保留插值左端。没有把一个包简单折成总 dx/dy。

相等时间戳：同一时间节点保留最后到达的累计 Q，记录 duplicate；零时长内部路径无法表达，明确不承诺保留。时间倒退：记录 backward，直到下一 DOWN 使用 `max(lastPointTime, receiveQpc+12ms)` 的到达顺序单调 fallback，不排序。早于原点、极端溢出、映射超过接收时间 1 秒的异常时间线也触发相同固定 fallback；这个 1 秒界限是异常输入保护，不是自适应延迟。正常映射已经过期则计 late sample，保留固定锚点。

容量满时丢最老中间点、保留累计终点账本并记录 overflow。这是有界过载退化，不能声称中间路径无损；出现 overflow 的采集必须单独解释。本轮两套回放和真机 B 均为 0 overflow。

## 5. 输出时钟、missed deadline、starvation

独立 `MotionClock` 线程使用 Windows `CreateWaitableTimerExW` 的 high-resolution flag，依据 QPC 绝对相位计算下一次 one-shot wait。使用 stop/changed/timer 等待句柄，不用 `Task.Delay(4)`、Sleep 累积调度或忙轮询。

若当前 deadline 已到，先在当前实际 wake 时间求值一次；`missed=floor((wake-deadline)/4ms)`，下一 deadline 直接推进 `(missed+1)*4ms`。不重放错过的历史 ticks。固定相位意味着晚醒之后下一正常相位可能很近；这不等同于补发 missed ticks，真实输出突发仍需测量。

没有未来插值右端时停在最后已知 Q：最后一个已知 endpoint 可输出一次，然后 park，记录 starvation start/end。新点到达时恢复原固定相位，不外推，不保持旧速度。starvation 统计包括手指停住/测试故意等待，并不直接等于丢包或网络故障。UP 会终结该段 starvation。

## 6. UP fence 与生命周期

`ResampledMotion` 的局部 gate 同时保护入队、tick 选择、generation 检查、量化和实际同步 Move 调用。UP 顺序：

1. 把真实 UP 坐标按同一增益账本加入 Q。
2. 在 gate 内提升 generation、撤销 deadline，禁止旧 tick。
3. 同步补发 `Q_final-P_previous` 的净位移，经过同一个 residual。
4. 记录 fence、清会话；返回到原 `UdpReceiver` 后才调用原 Gesture UP。

UP 与正在执行的 tick 互斥；UP 返回后，旧 generation 即使醒来也不会写 Move。UP flush 本身发生在 UP 包接收之后、fence 之前，不能把它误统计成 stale Move。同步补量耗时仍是按钮处理链上的真实成本；没有额外等待播放结束，也没有延迟 LeftUp 来掩盖末尾补量。

新 DOWN、会话/run 不匹配、sender run 切换、presence timeout、runtime stop/cancellation、dispose 和输出异常均撤销 generation 和 pending。记录 `||Q_pending-P_played||` 的 lifecycle abort discarded distance（净向量长度，不是未播放路径长度）。重置事件可能记录 0；正常 UP 清理不计作丢弃。Runtime 在按钮/native backend 清理前取消并 join 时钟，避免 dispose 后写共享鼠标；时钟异常使 Runtime Error 可见。没有增加全局锁或改变手势锁所有权。

## 7. 验证

Debug 与 Release 最终构建均 0 warnings、0 errors；完整测试均 **222/222**，含原 187 项回归和新增 35 组：

- 单点、2 点/4 点 batch、匀速线性、batch 内反向。
- duplicate、backward fallback、极端时间戳、late packet、固定锚点/L。
- starvation park、迟到 deadline 跳过、不补 burst。
- 正/负/XY endpoint、fractional residual、50 组随机多 batch 与 RAW 终点预算、设置快照。
- 慢/快后静止、立即 UP flush、UP 后旧 generation。
- 新 DOWN、mismatch、dispose、输出异常、正→负与负→正、buffer 容量边界。
- UP/tick 并发 fence；实际 UDP run/presence/dispose 清理；tap/drag/rearm 顺序。
- CLI 模式参数、trace 环/freeze、真实 Windows timer + fake native failure 的 Runtime Error/cleanup。

Launcher 原有断言保留，并覆盖实验参数/带空格 trace 路径。没有依赖已安装驱动才能通过的 fake 单元测试。最终只读模式文字修正之后又跑了一遍上述 Debug/Release 全套。没有重建、重装或重启 Android。

证据根目录：`C:\rightpad\windows\test-results\motion-ab-20260913`。`debug-build.log`、`debug-tests.log`、`release-build.log`、`release-tests.log` 为最终结果。`analyze.py`、`metrics.json`、原始 CSV、启动/停止/端口释放/身份证据均保存在该 ignored 目录。

## 8. 离线 A/B（相同输入、相同到达模型）

第一份使用已存手机录制 `touch-recording-20260913-194725.csv`：106,356 包，204,043 个运动样本，2,376 个完整接触。以每个 MotionEvent 的 current sample 时间作为包到达代理，**没有实测 callback/network 延迟**；B tick 是理想 deadline，不是 Windows 调度测量。

| 指标 | A RAW | B 250 Hz / L=12 ms |
|---|---:|---:|
| 非零 Move 次数 | 142,016 | 158,955 |
| 接触内间隔 <1 ms | 39.323% | 0.090% |
| 活跃 0–30 ms 间隔 p50 / p95 | 8.301 / 16.621 ms | 4.000 / 16.000 ms |
| 每轴终点绝对误差最大 | 0.8047 count | 1.0000 count |
| 输出路径长度总和 | 863,583.83 counts | 859,534.15 counts |
| 原输入路径总和 | 867,947.96 counts | 867,947.96 counts |

B UP 待补净距离 p50/p95/p99/max = 0.299 / 10.680 / 18.613 / 33.562 counts。B stop tail（最后变化样本代理到达→最后输出）p99=8.505 ms、max=15.000 ms。A/B 整数 endpoint 最大差 1 count；路径长度不是守恒量，重采样、反向相消和二维量化会改变它。B duplicate=15、backward=0、late=8、overflow=0、abort discarded=0；5,178 次 starvation 总时长 386.904 s，其中包含长停顿；ideal missed=0 不证明系统调度无 miss。

第二份用本次 A 的 **实际 Windows 接收 QPC** 与样本同时回放给 A/B：985 包、1,773 个运动样本、33 个完整接触。A/B 非零 Move=1,549/1,672；突发比例=41.173%/0.427%；两者终点误差均 0、两者终点差 0。输入路径 12,534.715 counts；A/B 输出路径 12,528/12,504 counts。B UP flush max=72.001 counts、stop tail max=12.073 ms、late=42、starvation=100、overflow=0。这个配对隔离两次实采输入差异，但仍使用理想 ticker。

## 9. 当前真实手机链路 A/B

同一手机、现有 APK/PID 30720、同一 Windows PC、相同 gain 6/6、同一 libvirtualhid、相同仪表和外部 observer。每组 6 次 slow/medium/fast-stop/reverse/fast-up（30 个运动接触），另有 Single Tap→Drag→Rearm 3 个接触。系统 `cmd input` 脚本相同，实际网络与 MotionEvent 时序不同；不把它称为真人手指或传感器硬件采样。slow/medium/fast-up 用系统 swipe；stop/reversal 为较稀疏 MOVE，因此这些停止/反向结果是第一轮小样本证据。

最初独立 app_process 注入器被 Android 终止，失败采集没有计入结果；使用系统输入脚本完成替代。没有调整 L、gain 或生产输入路径来迁就测试。

外部 WinForms 空白见证窗口通过有时限的独立交互 Scheduled Task 运行，测试期间前台不变。记录全部鼠标设备，在结果里按设备 handle 隔离 `VID_1209&PID_0003`；两组其他鼠标报告均为 0。保存 WM_INPUT handler entry QPC、移动和按钮/零位移报告；不以 managed Move 次数代替 HID report 数。

| 指标 | A（live-A2，PID 13420） | B（live-B2，PID 12680） |
|---|---:|---:|
| 采集时长 | 27.848 s | 27.767 s |
| 接受的触摸样本 / 包 | 1,806 / 985 | 1,804 / 984 |
| 包内样本数 p50 / max | 2 / 2 | 2 / 2 |
| 包内时间跨度 p50 / max | 4.0 / 5.5 ms | 4.0 / 5.5 ms |
| managed 非零 Move | 1,549 | 1,688 |
| Raw Input 非零移动 | 1,549 | 1,688 |
| Raw Input 零位移/按钮报告 | 6 | 6 |
| 左键 down / up | 3 / 3 | 3 / 3 |
| Raw Input 接触内 <1 ms 比例 | 41.173% | 0.181% |
| managed 接触内 <1 ms 比例 | 41.173% | 0.121% |
| Raw Input 活跃间隔 p50 / p95 / p99 | 7.486 / 9.160 / 11.014 ms | 4.040 / 8.100 / 8.344 ms |
| Raw Input 活跃间隔标准差 | 4.119 ms | 1.355 ms |
| native Move 调用耗时 p50 / p95 / p99 | 0.129 / 0.239 / 0.306 ms | 0.125 / 0.219 / 0.284 ms |
| native Move 调用耗时 max | 0.845 ms | 4.577 ms |
| 同窗口 Receiver CPU 时间 | 187.500 ms | 390.625 ms |
| CPU（单核时间占比） | 0.673% | 1.407% |

突发分母为每个接触内非零输出的所有相邻间隔，包括该接触的长停顿；另列 0–30 ms 活跃窗口。这个筛选是报告统计口径，不是算法参数。全间隔分布、min/max 和样本量在 metrics.json。非零输出不必每 4 ms 一次，量化为 0、starvation 和停顿都会拉长间隔。

B 共 1,870 次 tick；活跃 tick 间隔 p50/p95/p99=4.017/4.344/4.485 ms。deadline lateness p50/p95/p99/max=0.230/0.510/0.630/3.841 ms，missed=0。buffer depth p50/max=5/7，时域 backlog p50/max=8.966/16.604 ms（最小 -16.082 ms 表示已过期）；位置 backlog p50/p95/max=2.656/40.040/69.122 counts。34 个 late sample；2 个 duplicate（无位移 tap 的同时间 DOWN/UP），backward=0，overflow=0，abort discarded=0。

99 次 starvation，duration p50/p95/max=78.705/222.411/232.739 ms，包含脚本故意停住和稀疏 MOVE，不能直接归因为网络丢包。

## 10. UP、停止、反向、路径的实际代价

B 的 33 次 UP：待补净距离 p50/p95/p99/max=2.200/70.655/82.689/88.233 counts；flush→fence 耗时 p50/p95/max=0.090/0.162/0.207 ms。快速移动立即 UP 的 6 次 flush 分别为 53.160、70.908、88.233、53.858、53.279、70.487 counts。这是需要真人特别判断的末尾补量，不能用低 burst 数字掩盖。

6 次 fast-stop（最后变化样本 **Windows 接收**→最后非零 Raw Input）：A 0.197–0.555 ms，B 1.357–10.396 ms。B 的 managed 同口径为 1.060–10.314 ms；不是从手指物理停止计时，没有声称物理 stop tail 小于这些数字。

6 次指定反向（首个反向样本 Windows 接收→首个负向 Raw Input）：A 0.288–0.339 ms，B 0.650–4.082 ms。这 6 次均未发现首个反向样本接收之后的旧正方向 managed counts。系统 swipe 末端还出现了小反向修正，部分被净 UP flush 相消；完整 contacts.json 保留这些记录，不将它们混成指定 reverse 测试，也不宣称所有高频反向细节无损。

两组 33 个接触的净终点误差均 0，Raw Input 与 managed 每接触净位移误差均 0。B 的 33 个 fence 后 stale managed Move 均 0。仅凭终点一致无法证明路径一致；旧实触录制已经表明 B 路径长度更短，受限于固定 4 ms 采样、整数化、late 和 UP 净补量。

## 11. 仪表成本与未测边界

可选 MotionTrace 使用 262,144 条 struct 环，写入仅加短锁/存储，不格式化或磁盘 I/O；每秒低频检查 freeze.request，冻结后后台导出 CSV/metadata。两组均没有覆盖丢失（Overwritten=0）。采集层包括接受样本 Android timestamp/receive QPC、enqueue、目标 deadline、tick wake、插值坐标、logical delta/residual、managed/native Move begin/end、UP/fence、reset、backlog、starvation、timestamp 异常。

Native duration 在真实桥接 P/Invoke 前后取 QPC，成对记录在调用后写入，避免把 trace 写入算进 native duration。未改 native bridge 实现，因而没有 VHF submit/kernel report timestamp。WM_INPUT 时间是 observer 消息处理时刻，也受调度/消息队列影响；不能当作 USB polling rate、VHF submit interval、游戏 Raw Input 消费时刻或 photons latency。虽然本次移动次数相等，未据此建立一般性的 Move:report 一对一保证。

trace 全过程分配：A 138,237,440 bytes / 673.004 s；B 43,191,448 bytes / 111.007 s，包含固定 trace 数组、WPF 启动、工作量和等待时长差异。**这不是成对 28 秒窗口的 allocation 指标，不能据此比较算法每样本分配或得出 B 更省内存。** 同窗口 CPU 由外部 witness 读取 Receiver 进程 CPU 差值；它仍包含 WPF/网络/仪表，单次顺序 A/B 不足以做性能归因。

本轮没有精确物理触点时间、Android callback/UDP send 逐点标记、同步的跨设备单向网络延迟、native 内部 VHF submit、kernel report 时间、游戏读取/渲染时间、无观测扰动的每样本 allocation；所以没有提供捏造的这些数值，也没有声称低延迟、250 Hz HID 硬件 polling 或更好游戏手感。原始样本与 Windows 输出均有可复算证据，局限与上述区分一并保留。

## 12. 最终运行与真人测试

最后一次 Release 部署的身份、PID、端口、GUI/Android 状态、源码/二进制哈希与 E2E 记录保存在证据目录的 `final-*` 和最终见证目录中。最终实际模式必须为 `RESAMPLED_250HZ`、250 Hz 输出机会、12 ms 固定 L、trace=off、production libvirtualhid，且 Connected。已按 Stop→旧 PID 退出→UDP 50000/50001 释放→独立任务 Start 的顺序执行；不是 Codex shell 的持久子进程。Android PID 30720 保持前台，靠原 discovery/heartbeat 自然恢复，不需要重启。

最终已核实实例：**PID 20652**，2026-09-13 23:13:04 本地时间启动，用户 `Z88888888\zhqqq`，SessionId=2（与 explorer 相同），Medium `S-1-16-8192`，Default desktop，libvirtualhid，`0.0.0.0:50000` 和 `:50001` 都由该 PID 持有。GUI 实测显示 `RESAMPLED_250HZ` 与 `Connected`；最终 senderRunId=`E0203DFAA346ED96`。日志在 `windows/test-results/receiver-runtime/20260913-231304-288-23924/receiver.log`。

最终 Receiver DLL 的 staged/deployed SHA256 一致：`3489E874612F7122583D85FF717CF3A933E9CF5228466D3526946DEAB39195EE`。A/B 采集使用的 DLL 为 `C26AF6B2FED4575DC89EF92BAC6D4334C08D07309663E5316B89B6BA9847266A`；后续变更仅新增实际模式的只读显示，运动/时钟/量化/后端代码没有再变。最终版本重新通过全部测试并重新部署/E2E。

最终 `live-FinalReady` smoke 为 Pass：11 条 Raw Input，5 条真实非零位移、3 次左键 down 和 3 次 up，左右移动净 0，无解析错误、无残留左键，前台始终是惰性见证窗口。Receiver 的 drag_start/drag_end 和 Android 的 `click_accepted`、`confirm_requested performed=true` 均已确认；可用 Android Sender 日志中没有 sender error 或 queue_overflow。测试后的设置文件 SHA256 与起点相同。所有本轮临时 witness Scheduled Tasks 已清理，独立 Receiver Dev 任务继续运行。

真人接下来只需要在实际游戏里做：匀速横扫、慢速微操、快速大幅转向、快速左右反向、突然停止、快移后立即抬手。分别判断速度稳定性、细小瞄准、拖后、旧方向残留、停止干净程度和末尾跳动。自动证据支持进入这一步，尚不支持宣布 B 胜出或 Motion Engine 定稿。

## 用户要求的 32 项报告索引

1 起点：第 1 节；2 文件：第 1 节；3 A 兼容：第 2 节；4 数学：第 3 节；5 buffer：第 4 节；6 时钟映射：第 4 节；7 timer：第 5 节；8 missed：第 5/9 节；9 starvation：第 5/9 节；10 timestamp 异常：第 4/8/9 节；11 UP fence：第 6 节；12 stale Move：第 6/10 节；13 lifecycle：第 6 节；14 residual：第 3 节；15 新增测试：第 7 节；16 构建测试：第 7 节；17 离线：第 8 节；18 当前真机：第 9 节；19 interval：第 8/9 节；20 burst：第 8/9 节；21 lateness：第 9 节；22 starvation：第 8/9 节；23 UP 分布：第 10 节；24 stop tail：第 10 节；25 reversal：第 10 节；26 endpoint/path：第 8/10 节；27 native duration：第 9/11 节；28 CPU/allocation：第 9/11 节；29 未测指标：第 11 节；30 当前 mode：第 12 节与 final 证据；31 PID/backend/connection：final 证据；32 真人动作：第 12 节。
