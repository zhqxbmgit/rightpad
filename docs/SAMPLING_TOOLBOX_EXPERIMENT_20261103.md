# 游戏工具箱单变量实机实验（2026-11-02 至 2026-11-03）

后续证据：当日中午的[正式原包名实验](SAMPLING_ORIGINAL_PACKAGE_EXPERIMENT_20261103.md)已确认既有 `com.rightpad.capture` 也可在工具箱开启条件下约 240 Hz、恢复关闭后约 120 Hz，无需修改包名或新增游戏类别。本文保留此前 PUBG 实验结果。

## 结果

在当前 Xiaomi 14 / HyperOS OS3.0.307.0.WNCCNXM / Android 16 上，同一个 `com.tencent.tmgp.pubgmhd` 实验 APK：开启游戏工具箱、从游戏空间启动并实际进入局内工具箱后，用户真实滑动的主要驱动上报节奏约为 240 Hz；恢复关闭并重新从游戏空间启动后，回到约 120 Hz。

| 条件 | 采集窗口（Asia/Shanghai） | 报告数 | 真实接触跨度 | MOVE 间隔中位数 | 中位间隔倒数 | P05–P95 |
|---|---|---:|---:|---:|---:|---:|
| 工具箱开启、已检查局内面板 | 11-03 00:02:39–00:03:20 | 2,552 | 13.557 s | 4.191 ms | 238.61 Hz | 3.955–4.530 ms |
| 工具箱恢复关闭、重新启动同一 APK | 11-03 00:04:36–00:05:16 | 1,034 | 10.611 s | 8.416 ms | 118.82 Hz | 8.139–8.781 ms |

开启条件下 96.59% 的 MOVE 间隔在 3 至小于 6 ms；关闭条件下 97.28% 在 6 至小于 10 ms。两轮均是一次单指接触，无多指报告、无 SYN_DROPPED 标记。Receiver 接受样本增量分别为 2552 和 1034，与原始报告数相等。

包含停顿和长间隔的整段有效平均频率分别约为 191.81 Hz 和 98.97 Hz，不能将它们认作固定采样档位。这里测量的是 `/dev/input/event7` 的 SYN_REPORT 时间戳节奏，不是芯片内部扫描率、显示帧率或端到端延迟。

## 范围与变量

- 新任务身份 `20261102-sampling-toolbox-r1`，开始于 11-02 23:56，结束于 11-03 00:06，证据不覆盖前轮。
- 使用上一轮保留的 PUBG 实验 APK，包名、签名、DEX、布局与采集逻辑保持不变，无新构建或安装。
- 唯一手动改变的设置为游戏工具箱总开关，false → true → false。未更改性能模式、系统刷新率、触控稳定算法、跟手性、边缘抑制或方向。
- 实验 Activity 始终竖屏，未为客户端增加持续重绘。
- 两次都通过游戏空间“启动游戏”进入，核对启动来源、前台、自然发现连接以及当前手机/Receiver Sender 匹配。
- 开启条件还包含实际打开、检查并关闭局内工具箱。没有额外隔离“仅打开总开关但从不展开局内面板”这一子条件，因此结果不区分这两步的独立贡献，也没有证明具体 HAL/驱动控制路径。

## 局内界面与服务字段

开启开关后，`Gtb tools on` 从 false 变为 true。DockWindowManagerService 记录 `onGameEnter: com.tencent.tmgp.pubgmhd`，记录性能配置 level=1，并创建真实侧边工具箱。打开的面板明确选中“性能模式”。

实际检查了工具箱主面板、显示增强与更多工具页面，未见触控增强、手势跟踪或快速点击响应入口。该结果仅描述本机当前版本和此应用下检查到的页面，不能外推为所有小米设备均无此功能。

在工具箱已实际打开及约 240 Hz 采集时，服务仍显示：

```text
Gtb tools on: true
Gtb performance on: false
Gtb isOpenGameBooster: false
```

所以，后两个字段不能被直接当作“性能模式未选中”或“所有游戏加速未运行”的充分判据；本轮也不要求它们变成 true 才观察到约 240 Hz。

同一运行的手机日志仍出现 `DynamicFPS DF top: com.tencent.tmgp.pubgmhd : 0.000000`。没有将这个未核实语义的数值解释为实测 FPS，也没有据此推断触控必然被限制在 120 Hz。

为避免系统侧边栏手柄覆盖 B 区域时产生自动输入，先只打开客户端 Settings 对话框，使 inputModal 屏蔽真实输入，再执行工具箱 UI 操作；没有进入布局编辑或修改布局。准备快照至真实采集开始期间，Receiver acceptedSamples 增量与 gamepadSuccesses 增量均为 0。采样只来自用户实际手指，不含注入样本。

## 恢复与保留

使用全新的独立 Scheduled Task 保护，guard PID 3088。完整真实健康、保留 APK 身份和正式 APK/布局检查通过并确认 armed 后才改变开关。恢复逻辑检查手机与 Receiver 的当前 Sender 一致，避免用交接前快照误判恢复成功。

工具箱已通过同一设置界面恢复为原来的关闭状态，界面 checked=false 与服务 `tools on: false` 均已验证。独立任务恢复正式 `com.rightpad.capture`，00:06:13 再次验证 MainActivity 前台、PID 20878、自然连接，Sender `FEE72EB8CEC70AE6` 与 Receiver 匹配。

Receiver 仍为原 PID 20276、原启动时间、Runtime RunId 1、原二进制及 settings 哈希、原 UDP 50000 归属；两轮鼠标/gamepad failure 均为 0。正式 APK 集合和原始布局哈希未改。未重启或部署 Receiver，未执行 Git 写操作。

三个实验安装全部保留，进程已停止：`com.rightpad.sampling.control`、`com.tencent.tmgp.pubgmhd`、`com.tencent.tmgp.dfm`。未执行卸载、清数据或安装清理。

## 证据与尚未验证的问题

证据根目录：`C:\rightpad-artifacts\test-results\20261102-sampling-toolbox-r1`。

- `evidence/pubg/`：工具箱开启条件原始 getevent、CSV、统计和 Receiver/手机日志。
- `evidence/pubg-off/`：恢复关闭后的同 APK 对照。
- `evidence/toolbox-open.png`、`panel-display-entry.png`、`panel-more-tools.png`：实际局内界面。
- `evidence/gamebooster-*.txt`、`dock-panel-open.txt`、`dock-inspection.log`：开关前后与局内识别证据。
- `evidence/toolbox-enabled-ui.xml`、`toolbox-restored-off-ui.xml`：开关 UI 状态。
- `evidence/guard-*.json`、`restored.json`、`final-health.json`：独立保护、恢复及最终健康状态。
- `recovery/`：本轮冻结的脚本、ADB 依赖和计划；正式备份引用上一轮已核验文件。

尚未验证正式自有包名是否仅开启工具箱也能达到约 240 Hz、DFM 在此条件下是否相同，以及总开关与局内面板展开各自的作用。也未对当前条件做长时游戏体验或端到端延迟验收。
