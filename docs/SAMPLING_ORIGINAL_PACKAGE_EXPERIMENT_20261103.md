# 正式原包名高采样率对照实验（2026-11-03）

## 结论

当前 Xiaomi 14 / HyperOS OS3.0.307.0.WNCCNXM / Android 16 上，既有正式 `com.rightpad.capture` APK 在开启游戏工具箱、从游戏空间启动并实际打开后关闭局内工具箱的条件下，真实滑动的主要驱动上报节奏约为 240 Hz；恢复关闭并重新从游戏空间启动同一正式 APK 后，回到约 120 Hz。

本机本次实测路径不需要将 applicationId 改成 FPS 游戏包名，也不需要为正式客户端新增 `android:appCategory="game"`、横屏或持续重绘代码。没有构建、重新签名、安装或覆盖任何 APK。

## 结果与方法

任务为 `20261103-sampling-original-toolbox-r1`。两轮都使用原包名、原安装、原布局及原采集实现，只手动改变游戏工具箱总开关。正式客户端此前已添加到游戏空间。本轮没有改变性能模式、刷新率、触控跟手性、稳定算法、边缘抑制或其他参数。

| 条件 | 采集窗口（Asia/Shanghai） | 原始报告数 | 首末真实接触跨度 | MOVE 间隔中位数 | 中位间隔倒数 | P05–P95 |
|---|---|---:|---:|---:|---:|---:|
| 原包名，工具箱开启且局内面板已检查 | 12:20:38–12:21:19 | 2,367 | 11.943 s | 4.196 ms | 238.32 Hz | 3.929–4.536 ms |
| 原包名，工具箱恢复关闭后重新启动 | 12:21:59–12:22:40 | 1,097 | 11.047 s | 8.414 ms | 118.85 Hz | 8.142–8.780 ms |

每轮设定 40 秒采集窗口。只读 `getevent -lt /dev/input/event7`，数据来自用户真实单指滑动。MOVE 间隔只比较同一单指接触内相邻、坐标发生变化且不含接触转换的报告，长间隔仍保留在整体统计中。

开启时 97.04% 的间隔在 3 至小于 6 ms；关闭时 97.71% 在 6 至小于 10 ms。两轮均为一次单指接触，无多指报告和 SYN_DROPPED 标记。Receiver 接受样本增量分别为 2367、1097，与两轮原始报告数相等；鼠标和 gamepad failure 计数均为 0。

包含停顿的整段有效平均频率分别为 201.53 Hz、100.86 Hz，不将其解释为固定采样档位。这里测量的是驱动向系统上报的节奏，不是芯片内部扫描率或端到端延迟。

## 身份与局内工具箱证据

- 两轮前台 Activity 都是 `com.rightpad.capture/.MainActivity`，启动来源记录 `com.miui.securityadd`。
- DockWindowManagerService 的 `mBoosterPkgName` 为 `com.rightpad.capture`，原包名实际打开局内工具箱，面板“性能模式”选中。
- Android `cmd game list-modes com.rightpad.capture` 返回该包不是 game type，明确要求 Manifest 声明游戏类别。因此正式包未因本轮测试而新增游戏分类。
- APK SHA256 在测试前后保持 `EEC4D19C1B7ED24BCAAFBA36A7EAD52C5EA24E6508E5EDFA9CEEB935DFD33A2C`。
- 原始布局 SHA256 保持 `C82C99AF3586141919D09757176FBC56BE93EBCE0BABADCFBB0329C03A65933C`。
- 为安全操作覆盖 B 区域的系统工具箱手柄，先打开客户端 Settings 对话框，使 inputModal 屏蔽输入，再执行工具箱 UI 操作。未进入布局编辑。准备快照至真实采集开始期间，acceptedSamples 和 gamepadSuccesses 增量均为 0。
- 实际展开局内面板属于开启条件的一部分。本轮仍未单独隔离“只开总开关，从不展开局内面板”的效果，也没有跟踪具体 HAL/驱动控制调用。

## 恢复与保留

本轮重新备份当时正式 APK 和布局，刷新当前 Receiver 进程身份、二进制与 settings 哈希，未假设其与上一轮相同。独立恢复任务 guard PID 22540 完成完整真实健康检查并确认 armed 后，才改变开关。

12:23:06 最终验证：工具箱已恢复原关闭状态；正式 MainActivity 前台，PID 26847，当前 Sender `AD42CE00D97BBA7A` 与 Receiver 匹配，界面连接目标 192.168.110.248。

本轮当前 Receiver 始终为 PID 19808，启动时间 2026-11-03 11:08:57（Asia/Shanghai），Runtime RunId 1。二进制及 settings 哈希、UDP 50000 归属保持不变；没有重启或部署 Receiver。

三个既有实验安装全部保留，未执行安装、卸载或清数据；原 APK、布局和用户配置不变。未执行 Git 写操作。

## 证据位置

根目录：`C:\rightpad-artifacts\test-results\20261103-sampling-original-toolbox-r1`。

- `evidence/original-on/`、`original-off/`：原始事件、逐报告/间隔 CSV、统计 JSON、手机日志、启动来源及 Receiver 前后快照。
- `evidence/original-toolbox-open.png`、`dock-original-panel-open.txt`：正式包名局内工具箱证据。
- `evidence/original-game-category.json`：正式应用未声明为游戏类别的只读查询结果。
- `evidence/toolbox-original-off-ui.xml`、`toolbox-enabled-ui.xml`、`toolbox-restored-off-ui.xml`：总开关变化。
- `evidence/guard-*.json`、`preparation.json`、`restored.json`、`final-health.json`：本轮独立保护与最终恢复。
- `recovery/android/`：本轮新建的正式 APK 集合和原始布局备份。

本轮只验证当前手机与配置下可达的上报节奏，没有做长时稳定性、功耗或游戏手感验收。
