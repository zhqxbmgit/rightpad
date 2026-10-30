# Mode 向右固定三区域：实施、验证与双端交付

日期：2026-10-29，Asia/Shanghai。任务身份：`20261029-mode-fixed-regions-r2`。

**最终状态：已实现、完成适用验证，并在独立保护下正式更新 Windows Receiver 与 Android。没有自动启用手势用途。** 最终 Receiver PID **19780**／Runtime RunId **1**，创建时间 **23:36:31.1978982 +08:00**；Android PID **18924**，Sender **E82B8B25C291BE40** 已自然接纳。第二个独立 guard 连续观察 **61.986 秒／22 次检查**后放行，自行退出，任务结果 0；23:38:57 注销本轮第二个临时保护任务，正式启动器任务保留运行。

本轮发生过一次实际自动回退：第一次更新的输入链已经自然连接，但保护工具健康检查有变量作用域错误，误判交互上下文。guard 按失败规则恢复旧双端；随后独立审计确认旧完整组合、设置、布局和控制链健康，再修正工具并用新的 guard/nonce 完成最终更新。不是 Android 重连修复，不是故意破坏唯一输入的回滚演练。下文分别保存两次证据。

## 1. 开始现场与授权边界

读取项目 AGENTS、架构、handoff、产物流程、Mode／Ring／Controls／行为配置／反馈协议，以及撤销报告和 2026-10-26 I/O 事件资料，并沿生产入口核查。

开始 Git 状态仅有未跟踪 `pelican-bicycle.html`。没有恢复撤销的相对 DOWN 方案或沿用其任务身份。正式旧 Receiver 实测 PID 22492，创建 2026-10-29 11:32:39.907206 +08:00、Session 2、源树 Release 路径，持有 UDP 50000/50001；启动器记录当时陈旧，所以按真实进程、创建时间和端口核对，没有套用旧记录 PID。

在线手机为 `192.168.110.127:5555`、硬件序列 `c2e6a5e3`、型号 `23127PN0CC`。另一个 ADB 条目 offline，未操作。开始已安装 versionCode 1／versionName 0.1、更新时间 2026-10-24 23:12:59；实测只有完整 `base.apk`，没有 splits。正式原 APK 哈希见第 7 节。

未执行 Git 写操作、游戏启动／操作／截图、ADB tap/swipe、instrumentation 输入或生产键鼠／手柄注入。测试只用生产逻辑、fake 输出、隔离设置、隔离端口、loopback 和离屏 WPF。原生 ABI、驱动、Broker、许可、依赖版本均未改变。

## 2. 固定几何、边界、回撤和取消

新用途：Controls → Mode · 用途 → **手势绑定**。三个入口分别配置，不写死键位。正常 UP 才提交一个最终脉冲；DOWN、MOVE、停住和 400 ms 不输出。旧单键普通／Toggle、旧电脑快捷菜单及全部设置并存。

Android 使用实际 View 坐标，原地为现有 Mode 矩形；一档紧接 Mode 右侧，二档紧接一档，三者同 top/height。两个宽度是独立 dp，按 density 换算，不随 Mode 宽／高比例缩放。移动 Mode 整体跟随；改变宽度移动一档起点；改变高度同步改变三者高度。

所有区域左／上包含，右／下不包含；两条内边界向外归属。没有回差、余量、角度、吸附或延迟确认，不以每次 DOWN 为原点。

最终新程序自然上报：**View 1200×2670、density 3、Mode (599,772,264,1881)、safe (0,134,1200,2670)**。这来自生产 View 和切口 insets，不是截图推算。8/10 dp 草稿对应：

| 区域 | X 范围（View px） | Y 范围（View px） |
|---|---|---|
| 原地 | `[599,863)` | `[772,2653)` |
| 一档 | `[863,887)` | `[772,2653)` |
| 二档 | `[887,917)` | `[772,2653)` |

当前布局完整容纳初始草稿，无需自动移动或缩放任何控件。初始宽度只存在于新配置的内存默认／草稿，本轮没有写入正式 settings。

只有原 Mode 内的新 DOWN 才可能接纳；延伸区域不是独立按钮。直接在触控板或 B/X 落指保持原路由；Mode 接纳后锁定归属，不向其他路径泄漏。保留曾入档事实：二档退一档选择一档；入档后回原地准备取消，UP 不补原地；未 UP 可以再入档。任何越出完整有效矩形都永久失去本次资格，返回也不恢复。历史样本顺序和 UP 最终坐标均参与判定，无 MOVE 的 UP 同样有效。

CANCEL、pointer、前后台、编辑器、连接、用途和布局边界取消，不提交；一次接触最多一个动作，未绑定／失败无 fallback。完整契约、状态机和字段见 [MODE_FIXED_REGIONS.md](MODE_FIXED_REGIONS.md)。

## 3. 设置、预览、Save 和两端快照

三个入口支持未绑定、一个目录键盘键、一个手柄键、鼠标左／右／中键。只有二档可选打开／关闭原 Ring，直接进入既有六槽编辑器，不要求切换正式用途。新用途没有 Toggle、保持、连点、组合键、宏或长按动作；原单键 Toggle 没有删除。

一档 8 dp、二档 10 dp 是初始开发值，各自 1..50 dp、0.5 dp 步长，独立验证有限值／范围／步长。界面大号加减按钮、可点击目录、选中边框、绑定名称、草稿和已保存摘要全部可用左键完成。预览按手机实际 View 等比例绘制 Mode、两档和内边界，显示实际坐标／density／身份。未知几何明确不可用。

新用途 Save 与已启用新用途的布局试用都检查完整区域及安全输入范围，错误列出具体侧；Android 实际应用布局前和本地编辑器 Save 再次校验。不会悄悄裁切、移动 Mode、调整 B/X 或写坏原布局。旧用途不被未启用区域的溢出阻断。

统一 Save 先验证和原子写盘，成功后才 Publish。失败不生效、不推进可用 revision、不推新可用配置；保存期间几何失效则恢复原正式磁盘设置、不 Publish，恢复失败显示真实错误。

新手势 DOWN 保存实际几何、宽度、region 边界及 revision；Receiver 保存该接触不可变的完整旧绑定，包括 UP 菜单动作要使用的旧六槽。同用途接触期间成功 Save 保留本次旧快照，下一 DOWN 取新完整配置。只保留当前配置与一个已接纳接触的有界快照；未接纳的过时 DOWN 安全失效，不能旧识别配新绑定。用途切换、布局试用／保留／取消／恢复、坐标空间及生命周期边界取消旧接触并等新 DOWN。Mode-only Save 不推进或重定时 B/X。

## 4. 协议、输出和反馈

受限扩展为 **type10／96 字节 Mode v2** 和 **RPMD v2／56 字节**；旧 type8/RPMD v1 保留，reserved 不被占用。显式携带版本、最终选择、Mode revision、布局 identity、View／density／Mode／安全边界。复用原 worker／socket／端口，保留 run/contact/generation、ACK、600 ms lease、有界重传、去重和失效隔离。旧用途和旧设置不自动改用新用途；新用途遇旧客户端、不兼容或未同步时无输出，不误执行原单键。其他 Touch、GAMEPAD_STATE、RPHF、RPCT B/X 和布局持久化含义未改。

输出复用键盘、Xbox360 和鼠标来源合并。UP 脉冲最小可见 25 ms 后释放；新接触不能覆盖待释放动作。Mode 只撤销自己的贡献，不释放其他来源同键。失败不重放、不无限排队或改后端。Ring 仍是原六槽；UP 打开，本次 Mode 和迟到消息不成为槽位点击；菜单已开时关闭 Mode 接触完整消费，其 UP 不补动作。

本地 PRESS 保持原接纳后一次 10 ms／255 策略。档位同策略：首次一档最多一次，其后首次向外二档最多一次；直接二档只一次且消耗两档资格。回撤／重入／边界往返不再振；一个 MOVE 含历史最多一次；UP 不补振。取消、无效、生命周期、重传、ACK、同步和完成不新增提示。后端失败隔离、无重试，不影响输入。只表示本地接纳／预选，不能解释为电脑成功或真人盲辨通过。B/X、Touchpad CONFIRM、Ring 确认和电脑声音移除保留。

## 5. 适用验证与修正记录

最终 Windows Release 构建 **0 警告／0 错误**，完整回归 **808/808**，其中新增 **12 组** Mode 手势测试。最终 Android 主机入口全部通过，固定区域 **294 项检查**，直接提取生产 View 分派／收尾方法的夹具 **109 项检查**；APK `assembleDebug`、`lintDebug` 成功，lint **0 错误／14 项既有警告**。本轮不改原生桥接，使用批准的 `-ReuseDeployedNative`，逐项核对 DLL 和两份许可哈希，没有无故重建或升级。

验证直接执行生产 ModeFixedRegions、ModeContactStream、router、View 分派、ModeSession、Mode 编解码、Save、UdpReceiver 及原合并路径。覆盖固定最终坐标与不同 DOWN、Mode 平移／宽高／density、独立 dp、insets、精确边界、无回差、历史进入／越界事实、UP-only、回撤再选、永久无效、直接延伸区 DOWN、所有权隔离、未绑定／pointer／生命周期、版本与乱序重传、Save 旧／新快照（包括旧 Ring）、磁盘失败／保存期间几何竞态、未同步、旧设置／旧用途、超屏侧别、脉冲与失败门闩、来源合并、待释放门、Ring 手动关闭和反馈去重。旧 Mode／Toggle、B/X、Touchpad、Ring、M-R1/C-Z1 全量回归保留。

实际失败及处理均保留证据：

1. 首次 WPF 编译出现 Brush/Color 命名冲突，明确限定 WPF 类型后修正。
2. C#/Java 共享 golden 字符串抄写偏差修正；生产两端编码未通过猜测放行。
3. 旧 reserved-control 测试中“MOVE 不允许任何反馈”的历史断言被本轮明确授权替代；原 Mouse/B/X 隔离检查保留，新增实际 View 反馈去重验证。
4. 全量首轮 4 个历史 Motion UI 测试因其空 Settings 夹具触发新增构造接线，移到实际 MainWindow 入口，最终全量通过。
5. 并行构建时旧 Sender 高频测试一次 15 ms UDP 接收超时。未改生产网络／重试或测试时限，独立重跑完整 Android 主机入口通过；失败日志 `android-jvm-timeout.log` 保留。
6. 恢复准备阶段 Java PATH 和新版 apksigner 证书输出格式解析修正；失败准备副本均用新时间戳保留，没有停止生产或安装。
7. 首次部署后保护健康检查误判，自动回退与第二轮修正详见第 8 节。不能把这个工具问题归因于 Android 或历史 I/O 故障。

## 6. 正式值和布局保留

正式 settings 本轮前后 SHA-256 完全相同：

`B8A9A60BA338CF39E3CE43E44AE6AC389CBEA05FB387CBBA1292E47BC187FAAA`

仍为 **单键绑定／鼠标左键／普通按钮**，没有替用户启用手势。旧 Toggle 选项和保存字段保留。Ring 顶槽 Esc、其余原槽；Sensitivity 12/12、选中 M_R1、保存 Tau/Support 24/120、C-Z1 Tau 30／Amax 160000 等全部正式值保持。B 为 0.7/3/25/400；X 为 10/3/2/25/400，未用默认 12 覆盖用户的 X Left 10。

手机布局文件原始 **713 字节**，安装前后逐字节相同，SHA-256：

`C82C99AF3586141919D09757176FBC56BE93EBCE0BABADCFBB0329C03A65933C`

没有移动窄 X、加宽 B、重置布局或改 stable ID。`pelican-bicycle.html` 始终未跟踪，前后哈希相同：`10C33150C9383210A26806E9EFF50AF4EC0F169E7174AE40630F857C779D899F`。旧任务、撤销报告、候选、恢复包和 hold 资料全部保留。

## 7. 包、签名与恢复目标

任务根：`C:\rightpad-artifacts\test-results\20261029-mode-fixed-regions-r2`。

最终候选：`candidate\Release-final`。早期 `candidate\Release` 保留，未当作最终候选。最终正式目录：`C:\rightpad\windows\Rightpad.Receiver\bin\Release\net8.0-windows`。完整 8 文件清单、尺寸和 SHA-256 在 `evidence\candidate-final.json`、`evidence\attempt2\formal-final-hashes.json` 及保护计划中逐项对应。

| 对象 | SHA-256 |
|---|---|
| 最终 Receiver DLL | `8D5C9F4D8362D5E28E012EABA70DC80363417E35C8285D7BA5FB5C5E730DD4E5` |
| 最终 Receiver EXE | `E13FB39E5D3DE42DA59166A3722D23C6DE60FA9EBF4C6A2526D92FCA1F67A260` |
| 复用原生 DLL | `21A5556AF12F0FBF17085E23E59A4F97EBCBA883D6FFD8E5851EF5DD88E0F721` |
| 新 APK（451907 字节） | `D49E04B383F40DB258FE5C32FF40AF270CC63A9FA995C35F6A398B935BC8F573` |
| 旧 Receiver DLL | `CB6CD7423F9459E913200B2AC3DECE8BD9CC9D0D93AD32BF0947650345E26F81` |
| 旧实际 base APK（219107 字节） | `3DB4AC5581C2026147F55F9608626762F869D4BA3339178B089685C5418D8FE3` |
| 两端安装 APK 签名证书 | `978223C60C5473CFC61A1999596285C93D664595EAC71F9573E71FC781705F1D` |

最终 APK：`candidate\android-20261029-233505-768\rightpad-mode-fixed-regions.apk`。实际安装 versionCode 1／versionName 0.1，更新时间 **23:36:35**，未改签名、卸载或清数据。

最后保护计划：`recovery\attempt2\plan.json`，包括完整旧／新包、所有备份和依赖文件尺寸／哈希、恢复目标、实际设备、旧实例、新 guard 任务和互斥锁。Windows 完整旧包及 settings：`recovery\backup-20261029-233506-834`；旧 Android 完整集合：`recovery\attempt2\android-20261029-233505-768\base.apk`；原布局：`recovery\attempt2\screen-controls-20261029-233505-768.properties`。

恢复目标保持原正式目录、`%LOCALAPPDATA%\rightpad\settings.json`、设备 `c2e6a5e3` 的 `com.rightpad.capture` 完整安装集合及 `files/screen-controls.properties`。独立 Guard/Common/Deploy/StartGuard 副本随计划保存；不把仅恢复 Windows 的旧通用 Recovery.ps1 当作本轮双端保护。第一轮所有文件和错误证据仍保留，不覆盖。

## 8. 独立保护、自动回退与最终时间线

第一次 guard PID 30960，nonce `e986756c42e64f3694f2cd32f9209ca2`，23:30:17 ready；真实父进程为 Scheduler、Session 2／Medium／Default、jobFlags 0，短启动器退出后仍心跳。23:31:35 armed 并收到同一 guard 确认后才更新；新版临时实例 PID 15924，Android Sender B6C462CC80B733E9 已自然 Connected 并有正常输入。

保护工具 Health 参数 `$identity` 遮蔽了导入启动器 helper 所需的 Windows 身份变量，使 explorer Session 核对误报。23:32:54 部署不放行；23:32:56 guard 自动恢复完整旧 Windows＋APK＋必要 settings／布局，旧实例 PID 22304 于 23:33:08 启动。旧组合实际健康，但相同检查错误使第一 guard 最终结果为 1。修正后的独立只读审计完整验证旧组合恢复（`old-pair-restored-independent-audit.json`）；不是让用户手动恢复。

修正同时做了两件事：Health 参数改为 expectedReceiver，导入 helper 明确读取 script scope 的 Windows 身份；新的独立预检在 ready 前**实际调用同一条完整 Health 路径**，防止只做较弱手工预检。第一临时任务退出后单独核对并注销；第二轮使用新证据目录、新备份、新 task、新 nonce。

最终第二 guard：PID **29916**、创建 **23:35:18.6173235**、父进程 **2736=Scheduler**、用户 **BAODINE77777777\zhq**、Session **2** 与 explorer 一致、**Medium / Default / jobFlags 0**；nonce `41b530f6e4574cd8831accde2d9840e8`。短启动器 21376 已退出后仍有新鲜心跳。

| Asia/Shanghai 时间 | 实测事件 |
|---|---|
| 23:35:30.475 | 独立完整旧组合 Health 预检通过，guard ready |
| 23:36:18.153 | deployment armed，关联新 nonce／guard PID／创建时间／plan hash |
| 23:36:18.761 | **同一 guard 明确确认 armed** |
| 23:36:19.443 | 才开始 Windows 更新 |
| 23:36:21.610 | 旧实例退出，50000/50001 已释放 |
| 23:36:31.198 | 最终 Receiver PID 19780 创建，由批准启动器计划任务启动 |
| 23:36:33.532 | 用户／Session／Medium／Default／端口／候选文件身份核对 |
| 23:36:34.653 | 再核对设备、完整 APK 集合、签名、布局后开始 Android 更新 |
| 23:36:35.038 | 保留数据 `install -r` Success |
| 23:36:35.564 | `am start -S` 完成，MainActivity 前台 |
| 23:36:36.492 | 同一 PID／Runtime RunId 上自然接纳新 Sender，发布健康候选 |
| 23:37:39.665 | 61.986 秒、22 次连续健康检查完成，独立放行 |
| 23:38:56.919 | 最终包／签名／设置／布局原始字节／当前身份再审计通过 |
| 23:38:57.151 | guard 已自行退出且结果 0，仅注销临时保护任务 |

最终 Receiver 在 Android 更新、启动、自然重连和观察期间没有重启。第一次回退原因是保护工具误判，已明确区分；没有把反复重启包装成 Android 重连问题修复。正式 `Rightpad Receiver Dev` 继续运行，Status 的 PID 19780 与真实进程和 UDP 50000 一致。运行日志：`C:\rightpad-artifacts\receiver-runtime\20261029-233631-150-12856\receiver.log`。

最终被动审计 Running／Connected、lastError null、鼠标／手柄输出失败均 0，接受输入包 3673、heartbeat 285；这些是当时真实链路证据，不是全面真人体验验收。

## 9. 证据与未验证事项

主要证据在任务根 `evidence`，最终保护证据独立位于 `evidence\attempt2`：完整预检、ready、心跳、独立存活、armed／确认、停止／端口释放、启动身份、安装、前台、自然同步、61.986 秒放行、最后审计和收尾均有单独 JSON／日志。Windows 最终构建／808 回归、Android 主机／构建 lint、原生复用、离屏预览、失败记录和两轮恢复包均保存。

没有剩余的实现／构建／本轮正式部署步骤。仍需用户正常使用确认：真人盲操作误触率、8/10 dp 的实际舒适性、PRESS 与两档提示的区分、实际键位／游戏操作体验。没有自动替用户启用新用途、写测试绑定或改布局；没有以振动请求、fake 输出、Connected 或自然日志声称真人通过。

2026-10-26 Receiver I/O 中止事件的底层根因仍未确认，本轮没有修改网络／驱动或吞异常，当前 Connected 不代表该历史问题修复。

**实际使用：Receiver → Controls → Mode · 用途 → 手势绑定；配置三个入口及两个独立区域宽度，必要时通过现有布局编辑器调整 Mode，再点击底部统一 Save。正式部署不会替用户自动启用该用途。**
