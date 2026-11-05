# Receiver I/O 中止异常取证交付（2026-11-03）

任务：`20261103-io-abort-diagnostics-r2`。本轮按用户“继续”执行故障证据保留、现版运行核对、最小异常取证开发与受保护更新。它不是 I/O 中止根因修复，也不实施此前讨论的 C 500 Hz 改造。

**交付后用户决定：放弃这次测试，之后准备换手机。** 后续复现与取证测试停止，不自动恢复排查；已部署取证版本及恢复包保留，没有据此再次停止或回退 Receiver。根因仍未确认。

## 故障与恢复事实

- 原 Receiver PID 19808，创建时间 2026-11-03 11:08:57.6109655 +08:00。
- Runtime RunId 1 于 18:38:14.6663310 +08:00 记录 `runtime_error`，消息为“由于线程退出或应用程序请求，已中止 I/O 操作。”；18:38:14.6744409 记录停止。
- 前一份快照 18:38:13.8592783 仍为 Running/Connected，最近接纳输入约 4.3563 ms，鼠标/手柄输出失败均为 0。原记录缺少异常类型、堆栈及原生错误码，不能确定首发失败操作。
- 19:08 检查时同一 PID 已运行 Runtime RunId 2，Connected。该次恢复不是本讨论代理触发，不归因为本轮代码或重启。
- 原始冻结文件：`C:\rightpad-artifacts\test-results\20261103-io-abort-recovery-r1\evidence\flight-recorder.previous.log`，SHA-256 `B1D0A05554BCA50CDBFDF697B8104657AD91FE930B1F7E7F30FEBE6AC4991CF5`。
- 从该冻结文件另外提取 18:38:10–18:38:17 的记录到本轮 evidence 下 `incident-20261103-183814.jsonl`，SHA-256 `459E12DC021037B8E09E653901D7BAF134943EEE468834E588FEC9BA53A1B3B9`。

此前检查中的“当前日志为空，故障记录缺失”判断已撤回；故障在上述冻结 previous 文件中。不能把读取时的中间观察写成日志停写根因。

## 源码范围

- `RuntimeFailureDiagnostics` 捕获异常类型、消息、HResult、Socket 原生错误码/枚举、完整 ToString 堆栈及所有 Aggregate/InnerException 分支。报告失败被隔离，原异常继续传播。
- UDP 触控接收失败记录运行取消与本次接收取消状态；循环异常在 finally 清理前记录，避免后续清理异常掩盖原始证据。
- 设备发现分别记录 receive/send 失败，并在清理前保存循环异常。
- Runtime 在 WhenAny 返回后、发出共同取消前，记录首次观察到完成的任务、两任务状态及已可用的任务异常；外层异常和 MotionClock 失败补充完整信息。
- `firstObservedTask` 表示 WhenAny 观察结果，不声称对几乎同时发生的底层错误建立全局精确顺序。
- 正常取消继续沿用旧处理；不新增吞错、重试、自恢复、计时器或逐样本日志，不修改 Motion、参数范围、输入映射、协议和输出设备。

## 自动化验证

最终 Release 隔离构建 0 错误、0 警告；完整 Windows fake/回环入口 **813/813**。

新增五项检查覆盖：异常树/995/堆栈与报告失败隔离；真实回环 Socket 接收错误传播与记录；正常取消不报错；Runtime 首个完成任务与取消前状态；第二个清理异常出现时仍保留原始失败证据。

中间 r1 构建为 812/812；最终 r2 额外覆盖清理异常，正式发布只使用 r2。没有用生产输入或游戏注入来验证。

## 独立保护及部署

复用既有受保护部署工具，由 `tools/ReceiverIoDiagnosticsDeployment/Prepare.ps1` 生成本轮独立任务副本；设置准备文件逐字节复制，禁止历史字段清理。健康覆盖选择跨两份滚动日志的最新快照，执行完整现版健康路径后才 ready；发布后独立连续观察至少 60 秒。

两次预检工具问题均在停止 Receiver 前被拦截：PowerShell 7 JSON 日期自动转换造成 ready 时间误判；桌面启动实例与陈旧启动器记录不一致。改用 Windows PowerShell 5.1，并明确记录 `oldLaunchKind=desktop`。未 armed 的第一份 guard 被停止后重新预检；没有因此触发生产回退。

最终 guard PID 31740，Task Scheduler 父进程 2644，交互 Session 1、Medium、Default、JobFlags 0。nonce `950f2f160eb24b1a9884cb09cc33098b`；23:07:26.597 ready；23:07:54.771 同一 guard 确认 armed，之后才于 23:07:55.301 停止精确核对的旧实例。窗口关闭隐藏到托盘后，工具终止该已验证旧进程，不借陈旧启动器记录选择停止目标。

正式候选通过既有独立启动器启动：

| 项目 | 本轮值 |
|---|---|
| 新 Receiver PID | 30360 |
| 创建时间 | 2026-11-03 23:08:05.0231386 +08:00 |
| Runtime RunId | 1 |
| Receiver DLL SHA-256 | `EA8F079779F4494A9C675CA93EF3B18185FD13C00C6B9228B3143E8134B83D87` |
| 原生桥接 SHA-256 | `21A5556AF12F0FBF17085E23E59A4F97EBCBA883D6FFD8E5851EF5DD88E0F721`，前后相同 |
| settings SHA-256 | `C9A8C991E8E0353CA668813F437562E7754D9F6F7D041C8AA347136F716E076E`，前后相同 |
| 自然接纳 Sender | `D9D2A85B962AD219` |
| 运行日志 | `C:\rightpad-artifacts\receiver-runtime\20261103-230804-968-6216\receiver.log` |

部署程序 60 秒/19 次健康观察通过，23:09:08.240 发布。随后独立 guard 连续观察 **61.625814 秒/26 次**，23:10:11.352 放行，`actualRollback=false`。23:10:12.622 完成临时 guard 任务注销，正式 `Rightpad Receiver Dev` 仍 Running，新 Receiver 未停止。实际记录见 evidence 的 `guard-health.json`、`guard-released.json`、`cleanup.json`。未执行故意回退演练。

恢复包：`C:\rightpad-artifacts\test-results\20261103-io-abort-diagnostics-r2\recovery\backup-20261103-191352-192`。旧包 hold 和本地日志保留，不清理历史资料。

## 结果边界

这是为了下次异常提供足够证据的取证版本。原始故障根因仍未确认，不因 Connected 或观察通过而声称问题已修复。用户正常使用验收；代理没有操作游戏、注入生产输入、更新 Android、改手机布局、驱动、许可或执行 Git 写入。
