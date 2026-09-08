# rightpad Android → Wi-Fi UDP → Windows Receiver 真实手指测量报告

测量日期：2026-09-08（Asia/Shanghai）

## 1. 结论

工程判断：**A. 当前链路已经足够稳定，不需要增加额外网络时序机制。**

本次 A–D 有效测试的 3,809 个逻辑包全部被 Windows Receiver 接受，
`sequenceGapEstimate=0`、`oldPackets=0`、`invalidPackets=0`；Android 端没有
`queue_overflow` 或 sender error。每组的 4 个 duplicate 都是 Protocol v1
规定的 DOWN/UP 三份相同 datagram 所产生的预期重复，不是网络异常。

存在少量 transport timing variation：相邻 MOVE 的 `arrival interval - source interval`
绝对值 P99 为 4.66–10.64 ms，最坏为 20.06 ms；3,797 个相邻 MOVE 间隔中有
14 个（0.37%）出现“source 至少 4 ms、arrival 小于 1 ms”的追赶到达。所有这些
事件前后 sequence 仍连续。它们不足以证明需要 jitter buffer、interpolation、
fixed-rate output scheduler 或其他时序整形。

Windows arrival 的部分大间隔不是网络长尾：A/B/C/D 的最大 MOVE arrival interval
分别为 157.55/128.30/368.31/2130.45 ms，而对应最大 source interval 分别已达到
141.42/116.73/366.85/2115.76 ms。尤其 D 的约 2.13 秒间隔是手指保持按住不动、
Android 没有新 MOVE 的结果，不能解释为丢包。

## 2. 测试环境

- 手机：Xiaomi 23127PN0CC，Android 16 / API 36。
- 屏幕：1200 × 2670，480 dpi，竖屏触摸面。
- 手机网络：`601_5G`，Wi-Fi 6 / 802.11ax，5300 MHz；测前/测后 RSSI 约
  -36/-41 dBm，协商链路速率 2401 Mbps。
- Windows：64 位，Windows NT build 26200；.NET SDK 8.0.422。
- PC LAN 地址：192.168.110.248；手机 Wi-Fi 地址：192.168.110.127。
- UDP：Protocol v1，目的端口 50000。
- ADB：仅有 Wi-Fi ADB（`192.168.110.127:5555`），Windows 没检测到 USB ADB
  设备。正式动作期间没有持续拉取 Logcat；CSV 在手机本地记录，Receiver 日志
  写入 PC 本地文件，结束后才一次性导出。
- Android APK SHA-256：
  `46815D754FCF18EC9840EC5CA58F42C788521BA732B119CB21F2E1665570CA7F`。
- Receiver DLL SHA-256：
  `10A27B348EA8D5D2B86D88A1061F07FB15B32FAA5BAE8E4D180C12915759B82D`。
- 构建：Android `assembleDebug` 成功；Windows Receiver 14 项自动测试全部通过。
- 端到端内容校验：现有 `Verify-SenderE2e.ps1` 通过，逐条确认 CSV、Logcat、
  Receiver 的 float32 位模式、纳秒时间戳、sample 顺序、packet grouping、sequence
  和 duplicate accounting 一致。

## 3. 测试切分

正式 CSV 共出现 6 个 session。按执行顺序和持续时间映射：

| 测试 | Session | 动作 | Session 时长 |
|---|---:|---|---:|
| A | 1 | 稳定连续移动 | 12.475 s |
| B | 2 | 快速左右往返 | 10.374 s |
| C | 3 | 慢速微动 | 10.980 s |
| D | 4 | 移动—停止—移动 | 11.156 s |

Session 5 只有 DOWN/UP 两个样本、持续 10 ms；session 6 持续 185 ms。它们发生在
D 结束 8.17 秒之后，属于完成测试后操作手机产生的附带触摸，不代表任何指定测试。
两段原始数据仍保留，但从 A–D 分组统计中排除。全程可靠性统计同时给出，确保排除
操作没有隐藏网络异常。

## 4. Android Touch Sample

`eventTimeNs interval` 是同一 session 内相邻原始样本时间差，只统计正值。四组均无
零间隔或负间隔。分位数使用 R-7 线性插值。

| 测试 | 总 sample | historical | current | mean ms | median ms | P95 ms | P99 ms | max ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| A | 2,580 | 1,293 | 1,287 | 4.837 | 4.214 | 7.977 | 8.650 | 184.199 |
| B | 2,064 | 1,027 | 1,037 | 5.029 | 4.217 | 8.136 | 31.417 | 109.423 |
| C | 2,344 | 1,174 | 1,170 | 4.686 | 4.208 | 7.953 | 8.519 | 357.542 |
| D | 623 | 308 | 315 | 17.936 | 4.211 | 8.139 | 371.905 | 2106.562 |

约 4.21 ms 的 median 对应约 237–238 samples/s 的常规采样节奏。有效 sample rate
不能仅取 median 的倒数，因此同时按时间跨度计算：

| 测试 | MOVE window sample rate | 活跃移动片段 sample rate | 说明 |
|---|---:|---:|---|
| A | 210.02 Hz | 225.24 Hz | 连续窗口内有少数 source 长间隔 |
| B | 200.89 Hz | 202.90 Hz | B 的 P99 source sample interval 为 31.42 ms |
| C | 220.56 Hz | 235.91 Hz | 357.54 ms 最大值来自极慢动作中的无新样本区间 |
| D | 68.26 Hz | 227.83 Hz | 68.26 Hz 包含设计内的按住静止，活跃片段值才有意义 |

`MOVE window sample rate = (MOVE samples - 1) / first-to-last MOVE eventTime span`。
“活跃移动片段”只累计相邻 MOVE 中坐标确有变化、时间差为正且不超过 100 ms 的
间隔；该固定阈值用于排除 D 的明确静止段和重新开始移动前的空档。A–C 两个 rate
都报告，避免阈值掩盖实际 source 长间隔。

## 5. Protocol packet 与 MOVE 发送频率

Android 每个 MotionEvent MOVE 产生一个逻辑 MOVE packet，包内包含全部 historical
sample 和一个 current sample。发送日志的 3,834 个 `packet_sent` 与 Receiver 全程
3,834 个 accepted logical packet 完全一致。

| 测试 | 总逻辑 packet | MOVE packet | logical rate Hz | MOVE rate Hz | active MOVE rate Hz |
|---|---:|---:|---:|---:|---:|
| A | 1,287 | 1,285 | 103.08 | 104.69 | 114.23 |
| B | 1,037 | 1,035 | 99.86 | 100.88 | 101.95 |
| C | 1,170 | 1,168 | 106.46 | 110.04 | 117.91 |
| D | 315 | 313 | 28.15 | 34.39 | 114.67 |

D 的整体 packet rate 包含按住静止，不表示发送器降速；去掉大于 100 ms 的 source
空档后，active MOVE rate 为 114.67 Hz。A–C 整体 MOVE 发送频率约 101–110 Hz。

MOVE `sampleCount`：

| 测试 | min | median | P95 | max | 精确计数（sampleCount: packets） |
|---|---:|---:|---:|---:|---|
| A | 1 | 2 | 2 | 3 | 1: 8；2: 1,261；3: 16 |
| B | 1 | 2 | 2 | 3 | 1: 19；2: 1,005；3: 11 |
| C | 2 | 2 | 2 | 3 | 2: 1,162；3: 6 |
| D | 1 | 2 | 2 | 3 | 1: 8；2: 302；3: 3 |

典型 MOVE packet 明确为 **2 samples**；P95 在所有测试中也是 2，最大为 3。

## 6. Windows accepted packet arrival interval

下表按每组所有相邻 accepted logical packets 计算，不含 duplicate/old/invalid：

| 测试 | accepted | mean ms | median ms | P95 ms | P99 ms | max ms |
|---|---:|---:|---:|---:|---:|---:|
| A | 1,287 | 9.660 | 8.325 | 11.147 | 80.300 | 157.547 |
| B | 1,037 | 9.996 | 8.300 | 13.387 | 86.615 | 128.295 |
| C | 1,170 | 9.391 | 8.347 | 10.837 | 17.048 | 368.305 |
| D | 315 | 35.483 | 8.385 | 17.106 | 1040.893 | 2130.451 |

这些 P99/max 不能单独解释为 Wi-Fi jitter。输入静止或 Android 没有产生新 MotionEvent
时，本来就没有 packet。必须与 packet 内 event timestamp 的 source interval 对照。

## 7. Transport timing variation / jitter proxy

本报告没有计算 `Windows receive timestamp - Android eventTimeNs`，因为两个 monotonic
clock 没有共同原点。允许的 proxy 定义为：

`相邻 accepted MOVE 的 Windows arrival interval - 相同两包末 sample 的 source interval`

| 测试 | source median/P95/P99/max ms | arrival median/P95/P99/max ms |
|---|---|---|
| A | 8.314 / 11.120 / 26.038 / 141.418 | 8.322 / 11.129 / 29.887 / 157.547 |
| B | 8.315 / 12.811 / 77.496 / 116.725 | 8.300 / 13.234 / 86.044 / 128.295 |
| C | 8.314 / 9.400 / 16.628 / 366.845 | 8.346 / 10.684 / 16.907 / 368.305 |
| D | 8.316 / 15.876 / 516.974 / 2115.763 | 8.385 / 16.567 / 513.398 / 2130.451 |

| 测试 | signed mean ms | signed median ms | signed P95 ms | signed P99 ms | min/max ms | abs P95/P99/max ms |
|---|---:|---:|---:|---:|---:|---:|
| A | -0.004 | -0.034 | 2.667 | 4.841 | -12.265 / 19.231 | 3.381 / 6.531 / 19.231 |
| B | -0.001 | -0.046 | 3.174 | 6.583 | -12.506 / 20.062 | 4.238 / 7.822 / 20.062 |
| C | 0.001 | 0.004 | 2.457 | 4.139 | -8.048 / 6.180 | 3.238 / 4.664 / 8.048 |
| D | -0.012 | -0.082 | 2.811 | 5.656 | -10.733 / 18.408 | 3.723 / 10.640 / 18.408 |

signed mean 接近零只说明这些短窗口中 arrival 与 source 的总跨度相近；它不是单向
延迟。Android 与 Windows 时钟频率的微小差异仍会混入 proxy，10–12 秒短测试无法
独立估计 clock drift。

## 8. Burst / jitter 异常值

使用明确但仅用于描述的 burst 指标：source interval ≥ 4 ms 而 arrival interval < 1 ms。

| 测试 | 相邻 MOVE 间隔数 | burst-like 间隔数 |
|---|---:|---:|
| A | 1,284 | 5 |
| B | 1,034 | 5 |
| C | 1,167 | 1 |
| D | 312 | 3 |
| 合计 | 3,797 | 14（0.37%） |

确实能观察到少量“先晚、随后追赶”的到达。例如：

- A sequence 906→907：arrival/source = 157.55/138.32 ms，proxy +19.23 ms；
  紧接 907→908 为 0.53/12.80 ms，proxy -12.27 ms。
- B sequence 2160→2161：100.02/79.95 ms，proxy +20.06 ms；
  紧接 2161→2162 为约 0.42/12.93 ms，proxy -12.51 ms。
- C 最大绝对 proxy 为 8.05 ms。
- D sequence 3598→3599 的 2130.45 ms arrival 对应 2115.76 ms source 静止间隔；
  proxy 为 +14.69 ms，随后到达发生追赶。

这些 burst-like 事件的 packet sequence 全部连续，且全程 gap 为零。它们可能包含
Wi-Fi、Android/Windows 调度以及 Receiver 同步逐包文本日志开销，现有测量无法把三者
分开。出现频率低、幅度有限，没有形成丢包或乱序证据，因此不属于需要新增缓存机制的
明显持续 burst。

## 9. Reliability

| 测试 | received datagrams | accepted logical | sequence gap | duplicate | old | invalid | Android queue overflow | sender error |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| A | 1,291 | 1,287 | 0 | 4 | 0 | 0 | 0 | 0 |
| B | 1,041 | 1,037 | 0 | 4 | 0 | 0 | 0 | 0 |
| C | 1,174 | 1,170 | 0 | 4 | 0 | 0 | 0 | 0 |
| D | 319 | 315 | 0 | 4 | 0 | 0 | 0 | 0 |

每组 4 个 duplicate 正好对应 DOWN 的两个额外副本和 UP 的两个额外副本。A–D 合计
accepted 3,809；包含两个测试后附带 session 的完整 Sender run 为：received 3,858、
accepted 3,834、accepted samples 7,657、duplicate 24、gap/old/invalid 均为 0。

Android 全日志中以下计数均为 0：`queue_overflow`、`send_failed`、`packet_rejected`、
`sender_open_failed`、`recording_failed`、`flush_failed`、`status=INTERRUPTED`。

## 10. 是否需要网络时序处理

本次数据不支持增加：

- jitter buffer；
- interpolation；
- fixed-rate output scheduler；
- prediction、retransmission、FEC 或其他网络时序整形。

理由不是“完全没有 jitter”，而是：没有 loss/gap/old/invalid/queue overflow；P99
transport variation 有限；最大 arrival 长间隔主要由 source 同步长间隔或用户静止
解释；少量追赶到达保持 sequence 连续且比例仅 0.37%。引入缓冲或插值会确定性增加
延迟和复杂度，而本次没有测得与之对应的具体网络问题。

## 11. 测量限制

1. 每组约 10–12 秒，是短时、单设备、单 AP、近距离良好信号测试，不能代表弱信号、
   AP 拥塞或长时间运行。
2. Android 当前每个原始 sample 写 CSV 并输出 Logcat；Windows Receiver 每包及每 sample
   同步输出文本。虽然测试期间没有无线拉取 Logcat，但日志生成和 PC 文件输出本身会
   影响 Android/Windows 调度。本次结果因此是“当前带诊断实现”的表现。
3. Receiver 的 `receiveElapsedMs` 是 `ReceiveAsync` 返回后读取的 Stopwatch 时间，不是
   NIC 硬件 arrival timestamp。proxy 混合了网络、内核 socket queue、进程调度和日志
   消费速度。
4. source/arrival interval 比较消除了时钟原点差异，但没有消除时钟频率漂移；短测试
   中漂移预计很小，仍不能称为精确单向网络延迟。
5. A/B/C 的少量长 source interval 可能来自真人动作微停、Android MotionEvent 生成或
   诊断开销。本任务只测量，未修改 Sender、Protocol、Motion/Gesture Engine 或 Backend。
6. D 的静止阶段没有新 MotionEvent 属于预期行为，不能计作网络 packet loss。

## 12. 可复现产物

- `android-touch.csv`：手机本地 CSV 原始样本。
- `android-logcat.txt`：测试结束后一次性导出的 Android 日志。
- `receiver.log`：Windows Receiver 原始输出。
- `android-wifi-after.txt`：测后 Wi-Fi 状态快照。
- `analyze.ps1`：职责单一的离线统计脚本，不参与运行时链路。
- `analysis.json`：脚本生成的完整机器可读统计。
