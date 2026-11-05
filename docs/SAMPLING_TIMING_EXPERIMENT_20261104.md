# Xiaomi 14 高采样率实际链路时序检查（2026-11-04）

本轮承接高采样率稳定性及全量／隔点离线重放，使用已安装正式 Android 和当前 Receiver 的既有诊断，不修改、构建或部署生产代码，不改变 Motion 参数，不执行 Git 写入，不注入游戏／生产触控输入。真实触摸由用户本人完成。

结果：约 240 Hz 的原始时间戳确实到达 Receiver；本次完整接触的非 DOWN 样本未晚于各自 M8 映射播放时刻。仍存在长报告间隔，播放会按既有语义停在已知端点等待真实后续样本。没有证据据此增加固定播放延迟，或引入自适应、预测、额外滑行。

## 证据和有效范围

任务目录：`C:\rightpad-artifacts\test-results\20261104-sampling-timing-r1`。

- `high-round1`：没有触控输入，排除。
- `high-round2`：原始内核记录 4009 个报告、接触 20.656610 秒；中位间隔 4.193 ms。接收端手动快照保存晚于该接触超过 60 秒，无法对齐，保留为 `receiver-trace-expired`，不用于链路结论。
- `high-round3`：有效 Receiver 完整接触为 Runtime 1／Sender `FF948B7A181AD843`／Session 14，含 DOWN、4669 MOVE、UP，共 4671 个样本，Android 时间跨度 **24.196312 秒**。
- 此轮内核采集窗口结束时手指仍在滑动，内核记录只覆盖该接触前 **12.746960 秒**，共有 **2491** 个 SYN_REPORT；不能将此数量与完整 Receiver 接触数量直接比较。Capture 前后快照计数也有独立时刻，不作为严格同窗计数。
- 内核与 Receiver 重叠部分 **2491/2491** 原始时间戳逐一精确相同（内核微秒转纳秒），两向未匹配均为 0；SYN_DROPPED 为 0。未对未采到的后半段作内核一致性声明。
- Receiver 快照于 `2026-11-04T09:48:00.1545421Z` 保存，包含 69157 条记录、最近约 59.752 秒；完整 DOWN/UP 均在其中。环容量 262144，本接触没有因容量或时间保留范围被截断。

主要结果文件位于 `evidence\high-round3`：`receiver-trace\trace.csv`、`timing-analysis.json`、`sample-margins.csv`、`coverage-gaps.csv`、`getevent.txt`。原始 Receiver 导出 `C:\rightpad\windows\test-results\hitch\20261104-094800-154-eb6486d4` 也保留。

## 方法及不可测边界

离线脚本：`tools/SamplingIdentityExperiment/AnalyzeTiming.mjs`，只读取采集文件并写分析产物。可复算：

```powershell
node C:\rightpad\tools\SamplingIdentityExperiment\AnalyzeTiming.mjs C:\rightpad-artifacts\test-results\20261104-sampling-timing-r1\evidence\high-round3
```

按正式 M-R1 的每接触 DOWN 锚点与固定 M8 规则复算，单位采用快照实际 QPC frequency 10000000：

```text
mappedPlayQpc = round(downReceiveQpc + 0.008 × frequency
                    + (sampleAndroidNs - downAndroidNs) × frequency / 1e9)
marginMs = (mappedPlayQpc - sampleReceiveQpc) × 1000 / frequency
```

脚本检查单一身份、唯一完整 DOWN/UP、严格递增 Android 时间、无映射异常回退；保留非递减播放时刻规则。所有 NativeSubmit 与非零 Output 按顺序、dx/dy、提交结束时间和对应输出时间逐项核对。百分位为 nearest rank。

**两端时钟没有同步。** 播放余量及相对 DOWN 的到达漂移可以复算，无法由此计算绝对触摸到 PC 的单向延迟。现有记录不能分离 Android 回调等待、编码／队列、实际 socket send 与 Wi-Fi；异步日志写出时间不能充当这些阶段时间戳。NativeSubmit 只包围托管层原生适配器调用，不是 Windows 游戏消费时间或独立 HID 报告时间。

MOVE 间隔仅计算相邻 MOVE，排除 DOWN→首 MOVE 及末 MOVE→UP；真实停手、转向、微小位移阈值及手机驱动策略仍可能影响间隔，长间隔本身不能诊断为丢包或漏采。

## 实测结果

| 量 | 中位数 | P95 | P99 | 最大值 |
|---|---:|---:|---:|---:|
| Android 原始 MOVE 样本间隔 | 4.195 ms | 4.560 ms | 41.737 ms | 154.860 ms |
| Windows 接收 Accepted MOVE 包间隔 | 4.2225 ms | 6.2098 ms | 41.8380 ms | 160.5871 ms |
| M8 映射播放余量（MOVE＋UP） | 16.6787 ms | 17.7790 ms | 18.0594 ms | 18.3563 ms |
| Tick 记录点相对计划 deadline 的偏差 | 0.3184 ms | 0.6106 ms | 0.7384 ms | 1.3791 ms |
| 原生鼠标适配器调用耗时 | 0.0932 ms | 0.1183 ms | 0.1630 ms | 1.8529 ms |

4.195 ms 对应约 **238.38 Hz** 的常见运动报告节奏，不表示任意 1 秒都固定收到 240 个坐标，也不表示硬件持续扫描频率已被直接测量。

### 样本、播放余量及空档

- 4669 个 Accepted MOVE 包，每包均 1 个样本；该身份同时记录 4 个 Duplicate 包，由既有接收去重处理。
- 4670 个非 DOWN 样本中，播放余量小于 0 的数量为 **0**，小于 1 ms 的数量为 **0**；最小 **7.619 ms**。
- 本次余量中位数超过 8 ms，是因为后续样本相对 DOWN 的到达漂移中位数为 **−8.679 ms**，DOWN 锚定后后续样本较早抵达；固定配置仍是 M8，未变成 M16。没有同步时钟，不能据此断言 DOWN 的绝对传输耗时或全链路延迟。
- 完整接触的 MOVE 相邻间隔超过 20 ms 共 **55** 个。在内核重叠部分，相同原始报告序列已含 **27** 个超过 20 ms 的 MOVE 间隔，最大 **150.672 ms**；这些间隔已经存在于内核报告中，不能归因为 UDP 单独造成。其余未覆盖部分不能作此归因。
- 有 **60** 次下一样本接收晚于前一样本的映射播放时刻，所计算的无后续样本覆盖区间合计 **3619.3653 ms**，最大 **144.4621 ms**。这是样本时间覆盖定义，不等同于鼠标实际静止时间，也不是人手或驱动故障结论。
- “样本没有迟到”与“播放有等待空档”可以同时成立：每个样本都及时到达自己的播放点，但相邻真实样本时间相隔太久时，已有端点播完仍需等待。既有 M-R1 到端点后停止时钟，后续真实输入重新启用；不外推、不凭旧速度生成位移。

### Windows 调度和输出

- 共 **20608** 个 Tick／Output 机会，既有 missed 字段合计 **4** 个。Tick 记录点在 Motion 锁内，使用实际 Stopwatch；missed 使用 Tick 的 actualWake 计算，两者不能混为完全同一计时点。
- 相邻 Tick 最大间隔 **144.8418 ms**，超过 20 ms 共 **47** 个，**47/47** 都与上述没有后续样本覆盖的区间重叠。结合端点停车源码和 deadline 偏差，这些长间隔不能直接称为 Windows 线程挂起或掉了同等数量的 Tick。
- 非零鼠标输出及对应 NativeSubmit 各 **18471** 次，原生提交失败 **0**。非零输出间隔中位数 **1.034 ms**，P99 **4.6103 ms**，最大 **145.8970 ms**。零位移、端点等待和真实报告空档都会影响这个数，不能宣称实际输出全程固定 1000 Hz。
- 最后一次 Output 位于 UP 包接收后 **13.3284 ms**；这描述重建尾端完成，不是物理抬手到游戏响应延迟，也不是额外惯性。

## 结论及后续方向

本次实测支持既有代码审计及离线重放结果：高采样原始时间戳被实际运输到 Receiver，M-R1 使用较密的真实轨迹节点进行固定重建，并可产生约 1 ms 间隔的非零位移。未观察到这些样本因为 M8 播放余量耗尽而迟到；原生调用耗时和少量漏调度也没有显示为本轮长空档的主要解释。

仍不能断言“充分利用所有硬件扫描能力”或“240 Hz 已让游戏体验提高某个百分比”。需要进一步区分手指真实停顿、坐标量化／变化上报和驱动报告抑制；若再分解手机线程／队列／Wi-Fi，需要增加轻量诊断时间戳并另行进行受保护验证。当前证据不支持直接修改 M8、扩大缓冲、预测、平滑参数或生产输出策略。

## 恢复与边界

- 测试前游戏工具箱关闭；测试中开启、从游戏空间启动正式 rightpad，并通过实际侧栏确认性能选项。完成后 UI `sw_toolbox=false` 与服务 `Gtb tools on:false` 双重确认恢复。
- 运行游戏空间 UI 前已独立 guard 完成真实健康预检并确认 armed；结束由 guard 恢复正式 rightpad 前台、核对 APK／布局／连接及新 Sender 心跳。未安装／卸载 APK，旧实验安装未重新引入。
- Receiver 始终 PID **10716**、创建 `2026-11-04T11:05:51.1929555+08:00`、Runtime **1**；正式文件、设置哈希及 UDP 所有者按本轮 plan 检查一致。Android 重启只用于恢复前台与完整身份验证。
- `evidence\restored.json` 在 `2026-11-04T09:49:36.2609325Z` 确认恢复；`final-health.json` 再次完整核对。guard 正常退出，计划任务 LastTaskResult **0**，本轮临时保护任务已注销，恢复产物保留。
- 正式 Android／Receiver 源码与二进制没有由本轮变更；新增内容只有离线分析工具与报告。既有其他任务未提交改动保持原状，无 Git 写入。
