# Mode 电脑声音反馈交付（2026-10-24，Asia/Shanghai）

> **后续状态：已移除。** 用户实际体验后明确认为“声音反馈不实用”，决定放弃并彻底
> 移除。本文保留当时实现、测试及部署成功的历史事实，不再作为当前产品说明。
> 当前状态及新版本证据见 [撤除交付报告](MODE_SOUND_REMOVAL_DELIVERY_20261024.md)。

本轮完成实现、构建、fake／loopback 全量验证、独立恢复保护、正式部署及实际 UI
试听冒烟。用户本轮明确授权满足保护条件后直接部署；没有再次索要常规部署确认。
当前工程健康检查通过；**用户是否实际听到、音量与游戏体验尚未验收**。

## 使用与触发

Controls → Mode · 电脑声音反馈：默认开启、30%，0% 静音。使用大号减小／增大按钮
以 5% 步进调节，或直接点两枚试听按钮。开关与音量只改草稿，底部统一 Save 写盘
成功后才生效。试听使用草稿音量，忽略正式开关，不产生输入、不打开菜单、不发布设置。
关闭声音成功生效时使音频失效，输入状态不变。

Toggle 成功保持／撤销分别响一次；正常 UP 无声。普通按钮成功按下响一次，正常
释放无声；实际保持因既有安全生命周期成功撤销时响一次释放音。菜单成功打开响
一次打开音，实际关闭响一次关闭音；槽位不叠加成功音。未绑定单键、重复／重传／
迟到、维护、失败输出、中央、导航／高亮、重复关闭均不发对应成功音。
声音只表示 Mode 自己的状态；另一鼠标来源仍可保持同一个键。完整契约、音频故障
边界及素材说明见 [MODE_SOUND.md](MODE_SOUND.md)。

## 素材、发布与依赖

资源绝对目录：`C:\rightpad\windows\Rightpad.Receiver\Resources\ModeSound`。

| 文件 | 字节 | 格式／时长 | SHA-256 |
|---|---:|---|---|
| mode-on-complete135.wav | 25964 | PCM 48 kHz／16-bit／stereo，135 ms | DA1408D51C320D7D62E8E1FEA4038B67CF532DE37DFB9DF9EF543B29A7A01BD3 |
| mode-off-minus7-complete170.wav | 32684 | PCM 48 kHz／16-bit／stereo，170 ms | 3890DF8F1B035F8DB9E032938D8DA03FEE4CFDBCFE218F7EEB02D12003F361DA |

从用户指定试听目录逐字节复制，未重生成或调音，未使用双击节奏。释放音为同一
Cherry MX 青轴录音降低七个半音后的派生声音。原 MIT／Hai Nguyen 许可保留在
`LICENSE-source.txt`，来源及派生声明在 `NOTICE.md`，处理说明在
`complete-shorter-manifest.json`。WAV 嵌入 DLL，三份文本／JSON 在正式运行包
`Resources\ModeSound` 下随程序发布，运行不联网、不依赖个人试听目录。

音频由独立后台线程调用 Windows 自带 WinMM，无新增第三方包或系统安装。
默认设备每次提示重新打开，音量仅缩放本提示 PCM，不修改系统音量。
线程事件唤醒，容量一项最新请求；工作线程不持生产者短锁执行音频操作。
新状态中断旧状态，250 ms 以上未开始提示丢弃；运行／配置／停止边界使旧工作失效。
实际提示后 500 ms 试听让位，设备错误最多每 30 秒记录一次、消息有界，至少 5 秒且
有新提示才重试。Dispose 不等待设备阻塞；系统调用若阻塞，音频取消在返回后完成。
输入正确性与清理从不等待音频，声音错误不进入输入失败门闩、不重放按键。

## 验证与保留记录

最终任务根：`C:\rightpad-artifacts\test-results\20261024-mode-sound-deploy`。
后文相对证据均以该根为基准。

| 项目 | 结果／证据 |
|---|---|
| Windows Release | 0 warnings／0 errors；`evidence\windows-build.log` |
| 完整 Windows 回归 | **813/813**；`evidence\windows-tests.log` |
| 新声音专项 | 17 项，包含于全量：资源哈希／格式、配置缺省／边界／非法值／未知字段、Save 草稿／失败／不重置输入、三类绑定、去重、失败输出、安全生命周期、鼠标来源、菜单各关闭、快速切换／过期／禁用／Dispose、初始化／设备失败、阻塞隔离、试听、真实 WPF 按钮 |
| UI 离屏检查 | 独立进程加载主题，检查大按钮、草稿及 0% 提示；`evidence\mode-sound-panel.png`，已目视检查布局 |
| 原有回归 | Mode 键盘／手柄／鼠标，Ring，普通点击／双击拖动，来源合并，M-R1／C-Z1 等全部通过 |
| 音频独立冒烟 | 前一生产代码候选通过默认输出按 30% 各提交一次，`C:\rightpad-artifacts\test-results\20261024-mode-sound-release\evidence\audio-smoke.log`；最终正式版本另做 UI 试听 |
| 正式 UI 冒烟 | 两个试听请求均接收，显示“最近播放请求已提交（不代表已经听到）”；30→25→30、开关草稿切换并恢复，最终 Save disabled；未 Save、未触发真实 Mode 绑定／Ring |
| 工程检查 | `git diff --check` exit 0；仅有既有 LF/CRLF 提示；没有 Git 写操作 |
| 原生／Android | 未修改或重建；显式 `-ReuseDeployedNative`，`evidence\native-reuse.json` 保留原生 DLL／许可复制哈希 |

早期失败完整保留，没有用最终成功覆盖：

- `20261024-mode-sound`：测试 fake 鼠标缺接口、TouchHeader 名称错误两次编译记录
  为 `windows-build-attempt1.log`／`attempt2.log`；修正后声音 16 项通过。
- 同一早期任务全量为 810/812，两项是设置字段总数仍断言 12；新增独立 modeSound
  后应为 13，已更新旧断言。没有把这两项失败归咎于生产输入。
- `20261024-mode-sound-final`：新增 WPF 夹具缺 PageTitle 资源；改为独立进程先加载
  主题。初版图因被渲染子节点位移为空白，改为 VisualBrush 后目视检查。
- `20261024-mode-sound-release`：813/813。最后将 0% 试听文字明确为“上次试听”后，
  新建最终 deploy 身份再构建并完整跑过 813/813。各候选和日志均保留。
- 保护演练首次安全拒绝复用前一候选的任务名：
  `evidence\dryrun-attempt1-task-identity.json`；改用最终身份专用任务名后通过。
- 独立存活的外层核验曾因 PowerShell 7 自动把 JSON ISO 字符串读成 DateTime，
  将同一创建时间误报不等；规范化 UTC 后通过。实际部署脚本使用 Windows
  PowerShell 5.1，始终核对同一真实 PID／创建时间。该误报没有停止 Receiver。

## 部署前现场与备份

本轮重新调查的旧实例为 **PID 19728**，创建 `2026-10-24T03:38:14.6407065Z`
（11:38:14 +08:00），正式路径、用户 `BAODINE77777777\zhq`、Session 1、Medium、
Default 桌面，唯一占有 `0.0.0.0:50000`。旧实例来自普通桌面启动，旧 launcher
记录已失效；未套用交接中的 PID，也未让旧记录驱动停止。旧进程没有 dev-log-dir，
使用当前 flight-recorder 快照确认 Running／Connected、输出失败 0。

正式配置为鼠标左键／Toggle、purpose=单键；Ring 顶部 Esc 及其余槽位保留。
Sensitivity 12/12，用户保存 C Amax 160000／Tau 30 及其他参数原样保留。
settings 前后 SHA-256：`B02A5414C2FB2C8453DB77102821D9D9EEE9C8BEBD4C2058786BC03196B088D5`。
没有为声音缺省字段改写 settings。手机未访问、未改动或重启，未修改布局，未作本轮
手机文件字节快照；不将未访问的文件声称为“重新哈希验证”。

备份保存旧完整运行目录、settings、Recovery.ps1，以及 launcher／ArtifactPaths／
artifact-root／计划任务 XML 支持材料。恢复包 manifest 记录 10 个文件的长度和 SHA-256；
支持材料另有 support-manifest。候选递归清单 11 文件，WAV 在 DLL 内独立通过资源测试。

## 独立保护及真实时间线

计划／脚本：`recovery\guard-plan.json`、`ProtectionGuard.ps1`、`RoundSafety.ps1`、
`RunGuardTask.ps1`、`StartProtection.ps1`、`DeployRound.ps1`。源码版本留在
`C:\rightpad\tools\ModeSoundDeployment`，按当前现场重新生成计划，不原样运行旧轮次脚本。

保护任务名 `Rightpad Mode Sound Guard 20261024 Deploy`，InteractiveToken／Limited，
无触发器、Hidden、IgnoreNew；任务最多 1 小时，本轮 guard 窗口 900 s，armed 恢复期限
300 s。恢复与部署共用 `Global\RightpadModeSound20261024`。真正的独立任务中实测
创建、获取、释放均成功；父进程为 Schedule 服务 PID 2484，jobFlags=0，没有终端
KILL_ON_JOB_CLOSE。完整备份、候选、现场、依赖和 6 例恢复决策在该环境验证。

启动保护的短进程 PID 32172 已退出后，guard PID **20648** 仍独立存活并继续心跳；
`independence-continuity.json` 保存前后心跳、nonce、创建时间及 Schedule 父身份。
另用已停止演练保护留下的 ready 实测拒绝，见 `stale-ready-rejected.json`：
不存在对应存活 PID 时，在 armed／stop 之前失败。

以下均为 **2026-10-24，UTC+08:00**，证据原文件为 UTC ISO：

| 事件 | 时间 |
|---|---|
| 当前 guard 创建 | 19:45:59.4925201 |
| 当前 guard ready 已真实写出 | 19:46:09.7560144 |
| 部署 armed（含 guard PID／创建时间／nonce） | 19:47:22.8830216 |
| 独立 guard 确认 armed | 19:47:24.2571848 |
| 旧进程退出且 UDP 50000 释放 | 19:47:25.0844556 |
| 新 Receiver 创建 | 19:47:35.0681281 |
| 自然重连及 60 s soak 后发布，19 次检查 | 19:48:39.4633309 |
| 独立 guard 确认健康并放行 | 19:48:41.2654876 |
| 部署 guard 已注销，正式任务保留运行 | 19:48:41.6424436 |
| 早期预检临时任务也已核对并注销 | 19:51:11.1625040 |

ready、armed、guard-armed、old-stopped、release、guard-released、cleanup 等 JSON 和
guard／deploy 日志全部保留。只有 ready 对应当前启动请求的 nonce、真实存活进程、
精确创建时间、当前路径／用户／Session／Medium／Default 和新鲜期限时才能 armed。
armed 后还必须等同一 guard 确认才停止旧实例；本次不存在先 armed 再补 ready 的空窗。

旧实例隐藏且无可关闭主窗口句柄，正常窗口关闭路径不可用，本轮对精确匹配的旧进程
受控终止，`graceful=false`，没有声称正常退出。新实例通过原批准的独立 launcher
Task Scheduler 启动，不继承 agent shell 终止生命周期。

保护到期而未健康发布会恢复旧完整包。恢复前重新校验备份／外部依赖；以互斥避免与
部署争抢目录。废弃互斥按已获所有权处理；若部署进程超过恢复期限且仍占锁，额外
45 s 后仅可按 armed 内 PID／创建时间／本轮脚本命令核对并终止本轮过期部署进程，
再获取锁恢复，不终止无关终端／实例。新 Receiver 只接受 launcher 当前精确身份。
这些恢复分支有预检／决策证据，**没有现场故障注入或真实回滚执行证据**。

## 实际加载版本和保留路径

新实例 **PID 9800**，创建 `2026-10-24T11:47:35.0681281Z`，用户／Session／权限／
桌面验证同上；独立 wrapper PID 28120，父为 Schedule 服务，jobFlags=0。
实际加载模块已在试听后检查：

| 模块 | SHA-256 |
|---|---|
| Rightpad.Receiver.dll | 9EECD70D6AC8F04127AC1C0EEA48642C80B58590EB04E8376CFB32699E1A6F08 |
| Rightpad.Receiver.exe | B444947422873175F8B45A9206A9D870FC2853696FBB10496EE4417C546E33CE |
| Rightpad.VirtualHid.dll（原样复用） | 21A5556AF12F0FBF17085E23E59A4F97EBCBA883D6FFD8E5851EF5DD88E0F721 |
| Windows WINMM.dll（只读核验） | ED76D6396A53C168F91AEC28B69EC50DA47BFEA1C3966F28A107A6D93E97A2BD |

`post-deployment.json` 记录 19:51:10 的 Running／Connected、mouseOutputFailures=0、
gamepadFailures=0、LastError=null，唯一进程、端口、模块与 settings 未改证明。
正式 UI 两种试听成功入音频请求，日志记录 `mode_audio: playback_submitted`，无音频
失败记录；这是工程播放证据，不是用户确实听到的证明。

| 类别 | 绝对路径 |
|---|---|
| 最终候选 | `C:\rightpad-artifacts\test-results\20261024-mode-sound-deploy\candidate\Release` |
| 正式运行包 | `C:\rightpad\windows\Rightpad.Receiver\bin\Release\net8.0-windows` |
| 完整恢复包 | `C:\rightpad-artifacts\test-results\20261024-mode-sound-deploy\recovery\backup-20261024-194323-452` |
| 旧版 hold | `C:\rightpad\windows\Rightpad.Receiver\bin\Release\.hold-mode-sound-20261024194725273` |
| 当前日志 | `C:\rightpad-artifacts\receiver-runtime\20261024-194735-019-28120` |
| 全部本轮最终证据 | `C:\rightpad-artifacts\test-results\20261024-mode-sound-deploy\evidence` |
| 计划／支持材料 | `C:\rightpad-artifacts\test-results\20261024-mode-sound-deploy\recovery` |

正式 `Rightpad Receiver Dev` 任务仍为 Running，所有本轮临时保护任务已退出并注销。
未保留活动观察器；备份、hold、日志、候选、失败记录及历史资料均保留。
没有 Android／协议／布局／触觉／Motion／来源合并语义修改，没有驱动／Broker／
许可／供应商升级，没有 Git commit／push／reset／clean，也没有覆盖既有未提交工作。

**未执行真实回滚。待用户确认**：实际 30% 音量、两种状态的盲辨识、游戏中可听性、
输入与游戏体验。用户已确认的是原声音素材及前轮鼠标绑定，不能把这些确认扩大为
本轮端到端听觉或游戏全面验收。
