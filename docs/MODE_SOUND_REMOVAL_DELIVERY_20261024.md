# Mode 电脑声音反馈移除交付（2026-10-24，Asia/Shanghai）

用户实际体验后明确认为“声音反馈不实用”，决定放弃并彻底移除。本轮在当前源码上
完成针对性删除、隔离构建、回归和独立恢复保护下的正式部署，恢复 Mode 无电脑声音
反馈的产品状态。不是开关关闭、默认音量 0 或整体回滚旧版本，没有替代反馈。

## 删除与保留范围

删除生产文件 `windows/Rightpad.Receiver/ModeSound.cs`、`WindowsModeAudioBackend.cs`：
声音通知接口、提示枚举、设置记录、播放器、WinMM P/Invoke／结构、后台线程／事件／
请求槽、优先级、过期／重试、资源读取、PCM 处理、日志与状态设施全部撤除。

从当前 Program、ReceiverRuntime、ModeSession、RuntimeSettings、SettingsFileStore、
MainViewModel、SettingsViewModel、ControlsView XAML／code-behind 中移除创建／传递／
失效／Dispose 接线、状态触发、声音专设参数、配置读取／默认／校验／序列化字段、
开关／百分比／加减／试听按钮及其绑定。没有空实现、隐藏开关、空面板或无效按钮。
ModeSession 仅去除声音语句和只为声音存在的 safetyRelease 参数，原按键提交、25 ms、
失败锁存、取消、租约、presence/run、代次与来源合并路径不重写。

删除项目专属 `Resources/ModeSound` 内两个 WAV、原许可、NOTICE 和处理 manifest，
并删除 csproj 中相应 EmbeddedResource／Content 项。这些声明只属于被删除音效；
仍发布的原生桥接两份许可均保留。个人 visualizations 试听目录未访问或修改。

删除 `ModeSoundTests.cs` 和测试宿主中的三个声音入口、测试注册；原 SettingsTests
两处精确结构断言由 13 恢复为实际 12，未放松其他断言。被删除声音测试中的输入检查
由仍存在的 ModeBindingTests、ModeMouseButtonTests、ModeMenuTests、TouchCancellation、
Settings 等回归覆盖，不删除有价值的原有输入测试。

保留键盘／手柄／鼠标左／右／中单键、普通按钮／Toggle、鼠标来源合并、Ring 真实光标
路由／选槽／关闭／手机确认、普通点击／双击拖动／Touch 取消／安全释放、统一 disk-first
Save／草稿／未知字段合并、Motion／M-R1／C-Z1／B/X 及用户已接受行为。

通用构建工具 `-ReuseDeployedNative`、递归候选清单仍被当前构建合法共用，故保留。
原 `tools/ModeSoundDeployment` 脚本保留作历史恢复材料并新增 README，明确不是当前入口。
没有 Git checkout/reset、没有使用旧包冒充新构建，也没有覆盖既有无关未提交工作。

## 验证结果与残留检查

独立任务根：`C:\rightpad-artifacts\test-results\20261024-mode-sound-removal`。
以下相对证据路径均以该根为基准。

| 检查 | 实际结果／证据 |
|---|---|
| Windows Release | **0 警告／0 错误**，`evidence/windows-build.log` |
| Windows 适用全量回归 | **796/796**，`evidence/windows-tests.log`，保留全部原输入回归 |
| 生产源码／项目／UI／测试引用 | 对 modeSound、ModeSound、ModeAudio、mode_audio、WinMM、两个 WAV 名、资源前缀和声音测试入口搜索，零命中；`source-removal-audit.txt` |
| 编译后候选 | 类型、ManifestResource、PInvoke 模块及全包文件审计通过；`candidate-removal-audit.json` |
| 正式运行包 | 同样的元数据／资源审计通过，只有候选匹配的 8 个运行文件，专属资源目录不存在；`formal-removal-audit.json` |
| 实际进程 | 加载 DLL 哈希匹配，未发现原 Mode audio 线程，当前运行日志没有声音初始化／播放记录；`post-deployment.json` |
| 实际设置页 | Computer Use 只导航 Controls：无声音面板／开关／音量／试听，原绑定摘要保留、Save disabled；未编辑草稿／Save／触发 Mode 或游戏输入 |
| 配置准备 | 12 个隔离 JSON 夹具及重复字段拒绝通过；大整数／指数词法、数组、未知字段、嵌套同名字段、BOM 均保留 |
| 配置原子应用 | Windows PowerShell 5.1 下，存在字段时原子替换、缺字段时不写盘均通过；`settings-transition-tests.log`／`settings-transition-fixtures-attempt3` |
| 原生／Android | 未修改、重建或部署；`native-reuse.json` 记录复用的正式桥接及许可哈希 |

进程仍加载 Windows 系统 WINMM.dll。本轮只移除声音功能专属的托管 P/Invoke 和设施，
没有删除系统库或更改既有依赖；“系统库已加载”本身不等于产品声音功能残留。
无声音类型／资源／调用接线、无原专属线程、无相关日志和实际 UI 已撤除共同构成证据。

本轮代码 Release／796 项回归一次通过。以下工具验证错误已修正并保留记录：

1. 缺字段夹具对空字符串调用 String.Replace，期望值构造抛错；改为只在字段存在时
   做该操作。`settings-fixture-attempt1-error.json` 与原夹具保留。
2. Windows PowerShell 5.1 将 File.Replace 的 `$null` 字符串参数绑定为空字符串，
   原子替换夹具报非法路径；改用 `Language.NullString.Value`。第二次错误记录／夹具／
   日志保留，第三次两个分支通过。两次均未触碰正式设置或运行实例。
3. 最终证据脚本直接比较 `PSObject.Properties.Count`，触发 PowerShell 成员枚举导致
   文件数误报；实际候选／正式均为相同 8 文件。改为先数组化再 Count 后，全包长度／
   哈希与恢复包检查通过；`final-verification-attempt1-error.json` 和成功记录均保留。

UI 检查首次点击遇到 `coordinate input geometry is unavailable`；重新取得窗口并
激活 Receiver、刷新截图后重试成功，没有操作误捕获的其他窗口。该错误和恢复不被
算成产品失败；实际完整 Controls 树已确认无声音控件。

本轮修改前的目标源码／资源快照保留于 `evidence/source-before`，长度／SHA 清单在
`source-before.json`；与当前文件的差异在 `targeted-source-diff.txt`。历史构建、测试、
旧声音报告及恢复材料没有批量删除或重写。

## 正式配置的精确处理

部署前读取并备份当前 `%LOCALAPPDATA%\rightpad\settings.json`。实际顶层
**不存在 modeSound**，本轮 **未改写文件**。部署前后 SHA-256 均为：

`B02A5414C2FB2C8453DB77102821D9D9EEE9C8BEBD4C2058786BC03196B088D5`

`settings-transition.json` 记录 `modeSoundExisted=false`、`formalFileRewritten=false`、
前后哈希一致。实际字段差异为零；不是用默认配置重写后再比较值。

单键用途仍为鼠标左键／Toggle；Ring 顶部 Esc、其余未绑定槽位、Sensitivity 12/12、
C Amax 160000／Tau 30 及其他所有原正式字段保留。手机布局未访问或修改，没有声称
重新核验了未读取的手机文件字节。没有 Android 操作、协议／触觉或输入语义改动。

一次性部署准备器支持“若字段存在，仅删除顶层字段”分支：先准备副本并保留其他
原始文本／数值词法，在保护确认且旧进程停止后才以同目录 File.Replace 原子应用；
失败恢复匹配的完整程序及原设置。该分支只在隔离夹具执行，本次正式文件走无字段
不写盘分支。Receiver 本身没有新配置迁移、退役字段黑名单或兼容空实现；原通用
未知字段合并规则保留。

## 本轮现场、完整备份与独立保护

重新核验的旧实例为 **PID 9800**、创建 `2026-10-24T11:47:35.0681281Z`，正式路径，
唯一占有 `0.0.0.0:50000`。用户 `BAODINE77777777\zhq`，Session 1、Medium、Default
桌面，当前 launcher 记录与实际实例一致。现场／原生／依赖／设置哈希与日志快照在
`field-survey.json`。恢复目标是本轮部署前仍能使用的完整声音版，不是更早鼠标版本。

完整备份含旧运行目录 11 文件、原 settings 与 Recovery.ps1，manifest 共 13 个文件
逐一记录长度和 SHA-256；另保存 launcher／ArtifactPaths／artifact-root 与计划任务 XML
支持材料。候选长度／SHA 清单为 8 文件。正式部署使用完整目录暂存／校验／swap，
将旧完整目录保留为 hold，避免覆盖复制留下声音资源。

本轮保护为 `Rightpad Mode Sound Removal Guard 20261024`，独立 Task Scheduler
InteractiveToken／Limited 任务、无触发器、Hidden、IgnoreNew，900 秒窗口；armed 后
300 秒恢复期限，恢复／部署共用 `Global\RightpadModeSoundRemoval20261024`。
真实独立环境预检：完整备份／候选／外部依赖、当前实例、用户／Session／Medium／
Default、互斥创建／获取／释放、6 例恢复决策均通过。父进程是 Schedule 服务 PID 2484，
jobFlags=0，不依赖 Receiver、当前 agent 或终端的进程树。

启动保护的短进程 **33080** 已退出后，正式 guard **20180** 仍持续心跳；证据在
`independence-continuity.json`。已停止演练 guard 25292 留下的 ready 被实际拒绝，
见 `stale-ready-rejected.json`；未因遗留 ready armed 或停止 Receiver。

以下为 **2026-10-24，UTC+08:00**；原始 JSON 以 UTC ISO 记录：

| 边界 | 实际时间 |
|---|---|
| 本次保护创建 | 22:41:02.1551419 |
| 本次 ready 真实写出 | 22:41:12.1558153 |
| armed，含 guard PID／创建时间／nonce | 22:41:59.6766644 |
| 同一独立 guard 确认 armed | 22:42:00.4369853 |
| 旧进程退出且 UDP 50000 释放 | 22:42:04.2212991 |
| 配置无字段，不写盘核验 | 22:42:04.4251878 |
| 新 Receiver 创建 | 22:42:13.9714541 |
| 自然重连及 60 秒／19 次健康观察后发布 | 22:43:17.4815799 |
| 独立保护健康放行 | 22:43:19.4077657 |
| 临时保护任务注销，正式任务继续运行 | 22:43:19.6287795 |

ready 必须匹配新启动请求 nonce、真实存活 PID、精确创建时间、路径／当前交互上下文
和新鲜期限，随后 armed，再等待同一 guard 确认才能停旧实例。本次全部顺序有独立
文件证据，未接受上一轮计划或 PID。

原进程窗口关闭是托盘隐藏行为；正常窗口关闭尝试后仍存活，本轮仅对核验一致的旧
实例受控终止，记录 `graceful=false`，不声称正常退出。新程序通过批准 launcher 的
独立计划任务启动，wrapper PID 9436、父为 Schedule 服务、jobFlags=0。

恢复与部署共用锁；超期未健康发布时恢复完整备份。废弃互斥按取得所有权处理；
持锁部署进程超过期限及额外 45 秒时，只可按 armed 内精确身份处理该本轮过期部署
进程，再获取锁恢复，避免目录竞态。不停无关实例、不让失去输入的用户执行恢复操作。
本轮验证了预检／决策／独立运行和正常放行，**没有故障注入或真实回滚**。

## 实际新版本与路径

新实例 **PID 31572**，创建 `2026-10-24T14:42:13.9714541Z`，正式路径
`C:\rightpad\windows\Rightpad.Receiver\bin\Release\net8.0-windows\Rightpad.Receiver.exe`。
用户／Session／Medium／Default、唯一实例和 UDP 所有权均已核验。

| 文件 | SHA-256 |
|---|---|
| Receiver DLL，新构建且实际加载 | CB6CD7423F9459E913200B2AC3DECE8BD9CC9D0D93AD32BF0947650345E26F81 |
| Receiver EXE apphost | B444947422873175F8B45A9206A9D870FC2853696FBB10496EE4417C546E33CE |
| 原生桥接，原样复用 | 21A5556AF12F0FBF17085E23E59A4F97EBCBA883D6FFD8E5851EF5DD88E0F721 |

`post-deployment.json` 记录 22:45:56 的 Running／Connected、mouseOutputFailures=0、
gamepadFailures=0、LastError=null，实际加载模块、线程、日志／资源检查与配置哈希。
这些是工程健康与被动观察，不是真人操作或游戏全面验收。

| 内容 | 绝对路径 |
|---|---|
| 本轮新构建候选 | `C:\rightpad-artifacts\test-results\20261024-mode-sound-removal\candidate\Release` |
| 当前正式完整包 | `C:\rightpad\windows\Rightpad.Receiver\bin\Release\net8.0-windows` |
| 当前轮次旧版恢复包 | `C:\rightpad-artifacts\test-results\20261024-mode-sound-removal\recovery\backup-20261024-223615-960` |
| 旧声音版 hold | `C:\rightpad\windows\Rightpad.Receiver\bin\Release\.hold-mode-sound-removal-20261024224204394` |
| 本轮证据 | `C:\rightpad-artifacts\test-results\20261024-mode-sound-removal\evidence` |
| 本轮恢复计划／脚本／支持材料 | `C:\rightpad-artifacts\test-results\20261024-mode-sound-removal\recovery` |
| 当前独立运行日志 | `C:\rightpad-artifacts\receiver-runtime\20261024-224213-925-9436` |

正式 `Rightpad Receiver Dev` 任务保留 Running；本轮临时保护任务已退出注销，无活动
观察器遗留。所有历史声音候选、报告、备份、hold、恢复脚本和个人试听文件保留。
未升级驱动／Broker／许可／供应商组件，未改其他项目，未 Git commit/push/reset/clean。

**真实回滚：未执行。仍待用户确认：本次移除后的日常操作和实际游戏体验。**
用户此前已验收的鼠标单键功能继续保留；没有再把被放弃的声音列成待优化任务。
