# Mode 固定三区域：回撤与再次入档的本地反馈修正

日期：2026-10-30，Asia/Shanghai。任务：`20261030-mode-region-feedback-r1`。

## 范围与实际变更

本轮按用户指令只修 Android 本地预选反馈，并完成 Android 专用独立保护更新。没有构建、部署、停止或重启 Windows Receiver，没有修改协议、区域宽度、布局、绑定、输出脉冲或消息去重。开始已有大量上一轮未提交／未跟踪成果，本轮在其上作限定增量；无 Git 写操作、reset、clean、清理、依赖升级、游戏操作、ADB tap/swipe、instrumentation 或生产输入注入。历史 2026-10-29 交付报告保持当时事实。

生产修改仅 `android/app/src/main/java/com/rightpad/capture/ModeFixedRegions.java`：移除 firstHint／secondHint 整次接触资格，以 beginEvent 保存事件前选择；所有历史／末点仍顺序 sample，eventHint 最后比较两端。最终是不同的一档或二档且未永久无效才请求一次原短振。View 原有 beginEvent→全部历史→末点→一次查询→ScreenControlFeedback 接线已满足要求，无需改生产 View 或反馈后端。

现在原地→一档、原地→二档、一档→二档、二档→一档均可提示，之后重复切换仍提示。回原地不提示，重入可提示；同档移动不重复。一个 MOVE 从原地经过一档最终二档只请求一次；从一档经过二档最终一档静默。最终原地或任一样本永久越界，整个事件静默；不跳过历史，不丢 entered／invalid。UP 仍参与最终选择但不触发／补放振动，无 MOVE 的 UP 也静默。

接纳新 DOWN 的 PRESS 保持一次，复用现有 ScreenControlFeedback／B STRONG_ONE_SHOT，API 26+ 10 ms／255，旧分支 legacy 10 ms，minSdk 不变。后端缺失、服务异常、振动调用异常不重试，不影响选择、传输、UP／CANCEL。没有波形、设置、声音或后台补振队列。反馈只表示 Android 本地接纳／预选。

新增／修改测试为 `android/tests/ModeFixedRegionsTests.java`、`android/tests/fixtures/ModePressViewFixture.java.txt`。核心直接执行生产类；View 夹具仍由现有 ModePressFeedbackTests 原样提取生产 onTouchEvent／stopCapture，运行真实 router、ModeContactStream、反馈策略，只有 Android 外壳和输出后端是假实现。并非复制另一份选择算法。

文档增量为 AGENTS 限定例外、MODE_FIXED_REGIONS 当前契约、SCREEN_CONTROLS、NEW_CHAT_HANDOFF 及本报告。新 `tools/ModeRegionFeedbackDeployment/{Prepare,Common,Guard,StartProtection,Deploy}.ps1` 复用 Android-only 保护，移植上一轮已修正的 script-scope 身份 helper，并在 ready 前真实执行与更新／恢复一致的完整 Health 路径；没有带入双端保护中的 Receiver Stop／Start 功能。

## 验证

`tools/Invoke-Development.ps1 -Action TestAndroidJvm -Task 20261030-mode-region-feedback-r1` 完整主机入口通过：固定区域 **346 项**，生产 View 分派 **508 项**，ModeContact **56 项**，ScreenControlFeedback **30 项**，其余 Sender、编码、布局、Save／接触快照、所有权、旧 Mode、B/X、Touchpad、RPHF、Controls、取消／同步／ACK／重传及故障回归全部通过。没有为本轮修改网络行为或调整测试时限。

新增覆盖反复一档→二档→一档→二档、回原地／重入、同档重复、多历史最终差异／同档往返、最终原地、历史越界后返回、UP 改选择、无 MOVE UP，以及 API 25／26／36 下正常、振动调用抛异常、硬件缺失、服务抛异常。异常时最终 UP 的真实编码选择保持，隔离路径和安全取消保留。

Windows Wrapper 使用同一新任务身份执行 `assembleDebug lintDebug`，成功；lint **0 错误／14 既有警告**。实际候选已复制冻结到本轮 candidate，保护计划记录尺寸／哈希。没有 Windows 构建或测试来改写正式二进制。

证据：任务 `evidence/android-jvm.log`、`android-build-lint.log`；lint 在 `builds/android/20261030-mode-region-feedback-r1/app/build/reports`。本轮自动化未出现失败用例；最初受限执行环境拒绝启动 ADB／访问 Scheduler 后，改用获准的真实环境只读核对，未安装或重启。java 不在 PATH，但项目 JAVA_HOME 已正确指向 Android Studio JBR，测试按现有入口使用它。没有把环境权限失败写成生产故障。所有本轮日志、候选、恢复文件保留。

## 现场、包及原始数据

实际设备 `192.168.110.127:5555`，硬件序列 `c2e6a5e3`，型号 `23127PN0CC`；另一个 offline 条目未操作。准备脚本每轮枚举唯一在线设备，再读取真实硬件序列／型号写计划，并持续交叉核对，没有盲用历史 IP／PID／包哈希。

当前完整安装集合实测只有 base.apk，无 splits；完整旧集合已 pull 并与设备 sha256sum 核对。保留数据 `install -r`，不卸载、不清数据、不改签名／版本／布局。

| 对象 | SHA-256 |
|---|---|
| 本轮新 APK，230439 字节 | `EEC4D19C1B7ED24BCAAFBA36A7EAD52C5EA24E6508E5EDFA9CEEB935DFD33A2C` |
| 实际旧 base APK，451907 字节 | `D49E04B383F40DB258FE5C32FF40AF270CC63A9FA995C35F6A398B935BC8F573` |
| 新旧 APK 签名证书 | `978223C60C5473CFC61A1999596285C93D664595EAC71F9573E71FC781705F1D` |
| 手机布局原始 713 字节 | `C82C99AF3586141919D09757176FBC56BE93EBCE0BABADCFBB0329C03A65933C` |

新旧签名均通过 apksigner 验证。APK 文件名清单完全一致，解压内容逐项 SHA-256 比较只有 classes2.dex 改变（222196→222184 字节）；其余 manifest／资源／DEX 内容相同，APK 总尺寸变化来自 ZIP 存储差异。证据 `apk-inventories.json`、`apk-entry-hashes.json`、`apk-content-differences.json`。不是依赖升级或资源移除。

正式 settings 前后 SHA-256 均为 `665E494B2229CCAD3C0F2A2820BCDBB3C355BA8BB2A633F91242C28F063F5F6F`，当前用户已启用手势用途，宽度 **22.5／20 dp**、原地 Q／一档 W／二档 E；未改成初始 8／10 或历史单键用途。正式布局在安装后再次 exec-out 原始字节，与备份逐字节比较相同，见 `layout-byte-comparison.json`、`layout-after.raw`；最终审计还独立读取 `layout-final.raw` 再次逐字节确认。签名、应用数据与用户控制布局保持。

任务根 `C:\rightpad-artifacts\test-results\20261030-mode-region-feedback-r1`。实际新候选 `candidate/mode-region-feedback.apk`；完整旧包 `recovery/android/base.apk`；原布局 `recovery/android/layout.raw`；匹配计划 `recovery/plan.json`，包含冻结脚本／ADB 依赖、候选与旧包的清单／尺寸／SHA-256、实际设备与原 Receiver 身份。guard 自动失败／超时路径只恢复完整旧 Android 包，必要时恢复布局，并启动 MainActivity／验证自然连接，绝不重启 Receiver。未故意触发实际回退演练。

## 独立保护与更新

本轮 guard PID **8788**，创建 **17:43:24.8194818 +08:00**，父进程 **2680=Scheduler**，用户 `BAODINE77777777\zhq`、Session **1**、Medium、Default、jobFlags **0**。短启动器退出后 guard 仍独立心跳。nonce `ac0b7c5cf22c4b0dba28cf275561e221`，计划哈希 `99838E0C9487845B88131F448FF296CC444392FE38018B8CAE64139C17B0C955`。

| 本地时间 | 实际事件 |
|---|---|
| 17:43:36.162 | 独立任务执行完整旧包 Health、备份／依赖／设备／布局／Receiver 上下文等预检通过，ready |
| 17:43:48.257 | 安装前读取到同一 nonce／PID／创建时间的新鲜心跳，短启动器已退出 |
| 17:43:51.351 | deployment armed，绑定 guard 和 plan hash，超时 17:47:51 自动恢复 |
| 17:43:52.285 | 同一 guard 明确确认 armed |
| 17:43:52.478 | 收到确认后才开始安装 |
| 17:43:52.981 | install -r Success |
| 17:43:53.364 | am start -S 完成，MainActivity 前台 |
| 17:43:54.255 | 原 Receiver 自然接纳新 Sender `55A60B1A1466E087`，完整健康检查通过并 published |

原 Receiver 实测 PID **20292**，创建 **11:23:35.456177 +08:00**，Runtime RunId **1**，正式路径为源树 Release，UDP 50000 仍由其持有。正式文件逐项哈希、settings 哈希、用户／explorer Session／Medium／Default、实时快照持续核对。旧启动器 current 记录指向历史实例，Status 显示空 ReceiverPid，但端口实际为 20292；本轮没有篡改记录或重启来掩盖，实际保护按唯一真实进程身份和最新快照进行。

**最终状态：反馈修正、适用回归、受保护 Android 正式更新及自然重连均已完成。** guard 从 **17:43:55.097** 开始连续观察，至 **17:44:55** 累计 **60.6837462 秒／22 次检查**后自行放行并退出。没有触发实际回退。最终 Android PID **8119**，Sender **55A60B1A1466E087**；Receiver 仍 PID **20292**／原创建时间／Runtime RunId **1**，Running／Connected，lastError null、mouseOutputFailures 0、gamepadFailures 0。完整正式文件清单与哈希再次匹配，未写入 Receiver。

最终审计通过后，核对 guard 已退出、计划任务 State=3／LastTaskResult=0，再仅注销 `Rightpad-Android-Region-Feedback-20261030-R1` 临时保护任务；正式 Receiver 启动器任务未动，恢复包保留。精确收尾 UTC 时间（本地 +08:00）与原始快照见 `evidence/final-audit.json`、`guard-task-completed.json`、`temporary-task-cleanup.json`。最终包／前台／Sender 日志见 `android-package-final.txt`、`android-activity-final.txt`、`android-logcat-final.txt`，保护全链路记录为 `independent-preflight.json`、`guard-ready.json`、`guard-heartbeat.json`、`deployment-armed.json`、`guard-armed.json`、`installation-started.json`、`installation-result.json`、`activity-start.json`、`deployment-published.json`、`guard-health.json`、`guard-released.json`。

## 验收边界

本轮未执行生产键鼠／手柄注入，也未要求用户刻意失去控制。振动请求次数、fake 输出、MainActivity 前台和 Connected 都不是真人盲操作或游戏验收。实际短振是否清楚、无回差的边界附近是否提示过频、正常使用舒适性仍待用户确认；本轮不自动改区域宽度／波形。2026-10-26 I/O 中止底层问题继续按用户要求搁置，未改其异常处理，当前健康不代表该历史故障已修复。
