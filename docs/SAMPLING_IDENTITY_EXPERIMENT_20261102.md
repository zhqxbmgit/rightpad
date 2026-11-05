# Android 包名与触控上报频率实机实验（2026-11-02）

后续证据：2026-11-03 的[游戏工具箱单变量实验](SAMPLING_TOOLBOX_EXPERIMENT_20261103.md)在同一 PUBG 实验 APK 上观察到工具箱开启条件约 240 Hz、恢复关闭后约 120 Hz。本文保留上一轮“工具箱关闭条件”的原始结果，不应将其外推为开启工具箱后也无提升。

## 结论与适用范围

在本次 Xiaomi 14 / HyperOS OS3.0.307.0.WNCCNXM / Android 16 实测中，仅将同一客户端的 applicationId 换成 `com.tencent.tmgp.pubgmhd` 或 `com.tencent.tmgp.dfm`，并从小米游戏空间启动，没有观察到持续 240 Hz 或更高的驱动上报。三个独立实验构建的主要节奏均约 120 Hz。

游戏空间确实识别了两个 FPS 包名：它们安装后自动进入游戏列表，DFM 条目显示对应游戏背景和更新入口。自有包名对照组需要手动加入。这证明包名影响了游戏空间的识别，但不能据此断言高采样触控策略已经生效。

本次结果仅覆盖当前系统版本、现有系统/游戏空间设置及这些自然滑动样本。没有查明厂商是否还检查签名、游戏运行场景或其他条件；不据此否定其他系统版本或条件下包名匹配的可能性。

## 实验隔离

- 任务：`20261102-sampling-identity-r1`。
- 手机：23127PN0CC / houji；硬件序列号 c2e6a5e3；ADB 192.168.110.127:5555。
- 三个构建均声明 `android:appCategory="game"`，保留 namespace 和 Activity 完整类名 `com.rightpad.capture.MainActivity`。
- 三个 APK 的 DEX 文件 SHA256 完全一致。仅安装身份、可辨识标签及对应打包资源身份有差别；未修改输入采集、Motion、协议或发送逻辑。
- 三个实验包都复制同一份正式布局，逐一核对原始字节 SHA256；正式 `com.rightpad.capture` 安装及数据未被覆盖。
- 正常启动，不开启应用详细诊断。通过游戏空间的“启动游戏”启动，逐一记录 `launchedFromPackage=com.miui.securityadd`、前台 Activity、自然发现连接和手机/Receiver 的 senderRunId 一致性。
- 各轮只运行一个实验 Sender；切换后停止上一实验进程。采集使用只读 `getevent -lt /dev/input/event7`，由用户真实手指操作，不使用 ADB 触控注入。
- 未更改系统触控、刷新率、性能模式或游戏工具箱开关。仅为对照组新增了游戏空间条目。

## 测量结果

每轮采集窗口 40 秒；以下“触控跨度”仅为窗口内首末真实触控报告间隔。MOVE 间隔统计只比较同一单指接触内相邻、坐标发生变化且不含 DOWN/UP 转换的报告，包含停顿导致的长间隔。

| 构建 / applicationId | 触控报告数 | 触控跨度 | MOVE 间隔中位数 | 中位间隔倒数 | P05–P95 | 6–10 ms 间隔占比 |
|---|---:|---:|---:|---:|---:|---:|
| A / `com.rightpad.sampling.control` | 1,022 | 10.737 s | 8.411 ms | 118.89 Hz | 8.157–8.864 ms | 96.47% |
| PUBG / `com.tencent.tmgp.pubgmhd` | 1,245 | 13.949 s | 8.417 ms | 118.81 Hz | 8.159–8.756 ms | 96.93% |
| DFM / `com.tencent.tmgp.dfm` | 1,348 | 13.982 s | 8.411 ms | 118.89 Hz | 8.165–8.736 ms | 97.47% |

执行顺序为 DFM → PUBG → A。三个窗口均未见多指报告或 SYN_DROPPED 标记；这不等于证明所有层级都无事件丢失。每轮 Receiver 接受样本增量分别与该轮原始触控报告数相等（1348、1245、1022）。偶发短间隔不能作为持续高采样档位的证据。

对比此前正式客户端的单独测量，中位间隔为 8.413 ms；它同样约为 120 Hz，但并非本轮交错重复基线，因此不把微小差异解释为因果效应。

这是驱动向系统报告的节奏，不是触控芯片内部扫描率，也不是端到端延迟。没有自动输入或人体游戏手感验收。

## 保护、恢复与保留状态

独立保护任务通过 Task Scheduler 运行，PID 15728，父进程与 Schedule 服务 PID 均为 2376；当前用户、交互 Session 1、Default desktop、Medium integrity，未处于 KILL_ON_JOB_CLOSE Job。任务在完整真实健康检查、正式 APK 集合及布局备份核对后生成 ready，并于 23:26:00（Asia/Shanghai）确认 armed；随后才尝试安装。

首个安装尝试因未处理 USB 安装确认而失败。用户明确授权点击允许后，安装辅助脚本读取实际系统弹窗，逐一点击“继续安装”；未勾选“不再提示”。三个安装均返回 Success。

23:35:27 独立任务执行恢复。随后再次检查，正式客户端 PID 为 19406、MainActivity 前台，界面“已连接 192.168.110.248”；当前 senderRunId `02F746A29AC6DCAE` 与 Receiver 匹配。正式 APK 集合及布局 SHA256 保持不变。

Receiver 全程仍为 PID 20276、相同启动时间、同一路径和 Runtime RunId 1；二进制与 settings 哈希、UDP 50000 归属通过核对。各轮鼠标与 gamepad output failure 计数均为 0。未重启或部署 Receiver，未操作 Git。

用户要求暂不清理安装，因此三个实验 APK 均保留安装，进程均已停止；正式 rightpad 保持前台。没有卸载任何实验包。

## 证据与复现入口

产物根目录：`C:\rightpad-artifacts\test-results\20261102-sampling-identity-r1`。

- `candidate/{control,pubg,dfm}.apk`：三个签名 APK。
- `candidate/identity/`：本轮显式加载的 Gradle init 脚本和 Manifest overlay；普通构建不加载它们。
- `evidence/apk-inventory.json`、`*-manifest.txt`、`*-signature.txt`：身份、最终 Manifest、DEX 哈希及签名校验。
- `evidence/{control,pubg,dfm}/`：原始 getevent、逐报告/间隔 CSV、统计 JSON、手机日志、启动来源、Receiver 前后快照及游戏加速服务状态。
- `evidence/guard-*.json`、`preparation.json`、`restored.json`、`final-health.json`：独立保护和最终恢复证据。`restored.json` 的即时 Receiver 快照可能尚处于上一 Sender 的短暂交接阶段，最终当前 Sender 身份以 `final-health.json` 为准。
- `recovery/`：冻结的独立恢复脚本、计划、ADB 依赖、正式 APK 与原始布局备份。
- 源码中的 `tools/SamplingIdentityExperiment/` 仅为本次显式实验入口，不改变正常构建配置。

再次安装同包名实验前应重新确认没有正版游戏、保持安装集合身份核对，并使用新任务身份与独立恢复计划；不要覆盖本轮证据或直接复用已结束的 armed 记录。
