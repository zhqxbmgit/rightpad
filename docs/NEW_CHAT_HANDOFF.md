# rightpad 新对话完整交接：用户操作条件、开发习惯与当前项目状态

## 2026-10-30 交接补充与本轮仓库交付

本节及下方 2026-10-30 反馈修正说明优先于后续历史时点。用户本轮明确要求更新交接文档，并将当前项目提交、普通推送到既有 `origin/main`；授权限于本次源码／测试／文档／配套工具交付，不授权重新构建、部署、重启或修复运行问题。范围和验证边界见 [Git 交付记录](GIT_DELIVERY_20261030.md)。各开发报告中的“未执行 Git”保留为当轮历史事实，不与本次单独授权冲突。

**当前产品基线是固定三区域，禁止退回先前的相对 DOWN 设计。** 原地是原 Mode 矩形，一档紧接其右侧，二档紧接一档；两个设置值分别是独立区域宽度，不是相对落指的累计阈值，不要求二档宽于一档。没有斜向识别、回差、延迟确认或计时升档。旧对话中的相对距离演示仅是历史讨论，不能作为当前实现规范。只有原 Mode 内 DOWN 接纳；延伸区直接落指仍走原路由，Mode 接纳后保持所有权。

**当前反馈按每个 MOVE 的起点／最终区域变化提示。** 二档退回一档、后续反复跨档、回原地后重入均可再提示；同档移动、历史往返后最终同档、最终原地、任何永久无效及 UP 静默。全部历史样本仍顺序处理，不能为了合并反馈丢掉 entered／永久 invalid 事实。正常 UP 执行一次最终动作；入档后回原地 UP 取消，不补发原地，完整范围外永久取消。反馈只代表本地接纳／预选，不代表电脑成功。

**最新只读复核时点为 2026-10-30 17:54:34 +08:00。** Receiver PID 20292／创建 11:23:35.456177 +08:00／Runtime RunId 1 仍为原实例，Running／Connected，Sender `55A60B1A1466E087`，LastError 为空，鼠标／手柄失败均为 0。正式 Receiver DLL 为 `8D5C9F4D8362D5E28E012EABA70DC80363417E35C8285D7BA5FB5C5E730DD4E5`；Android 新 APK 为 `EEC4D19C1B7ED24BCAAFBA36A7EAD52C5EA24E6508E5EDFA9CEEB935DFD33A2C`。这些身份和健康数据仅是该时点，不能套用于未来现场。

最近读取的正式配置已由用户启用手势用途，原地 **Q**／一档 **W**／二档 **E**，宽度 **22.5／20 dp**；settings SHA-256 为 `665E494B2229CCAD3C0F2A2820BCDBB3C355BA8BB2A633F91242C28F063F5F6F`。不要按旧报告改回单键、Toggle 或 8／10 dp。713 字节布局保留证据在反馈交付轮次；本讨论助手的复核没有重新访问手机。两份 APK 的 ZIP 条目共 6 项，独立解压哈希比较仅 classes2.dex 改变，其余内容相同；包体尺寸下降不是删去资源或重建依赖的依据。

两轮工程交付已只读复核可接受：Windows 808/808 来自固定区域交付；Android 最新生产核心 346／生产 View 夹具 508 来自反馈修正。反馈轮次仅更新 Android，保留 Receiver 实例。固定区域首轮发生过保护工具误判并实际自动回退，独立审计恢复后才重新交付；反馈轮次没有实际回退。不能把两轮记录混写，也不能把自动化、Connected 或振动请求视为真人盲操／游戏验收。

**I/O 中止问题继续搁置，等待用户重新明确要求。** 已有两次旧 Receiver 实例的同类错误：2026-10-26 可定位 runtime_error 于 00:27:42.875；2026-10-29 仅可从快照推算最后输入约 19:07:25.697，保留日志从 19:48:07 已为 Error，不能编造精确故障时刻。两次底层根因均未确认，不能认定 Wi-Fi、驱动或某种取消竞态；后续 Connected 不证明已修复。原始证据分别保留于 `C:\rightpad-artifacts\test-results\20261026-receiver-disconnect-readonly-r1\evidence` 和 `C:\rightpad-artifacts\test-results\20261029-receiver-disconnect-readonly-r1\evidence`。用户说“先不管，等我完成任务”后，本讨论助手没有执行恢复／重启／修复；本轮推送也不恢复该项工作。

外部 APK、正式设置、手机布局、日志、完整恢复包和运行目录继续留在本地产物根目录；源码仓库包含契约、交付记录、测试和专用恢复工具源码。无关 `pelican-bicycle.html` 原样保持本地未跟踪，不提交。本轮 Git 交付不产生新的真人验收结论。

**2026-10-30 Mode 档位反馈修正（当前状态优先）**：任务 `20261030-mode-region-feedback-r1`，Android 生产 `ModeFixedRegions` 已从整次接触每档一次，改为每个 MOVE 完整处理历史后比较事件前／最终区域；二档退一档、后续反复切换、回原地后重入均可请求短振。同档／历史往返最终同档、最终原地、永久无效、UP 静默。原 PRESS、固定几何、entered／invalid、Save 快照、所有权、协议和 Receiver 输出不变。完整 Android 主机入口通过，固定区域 **346**、生产 View 分派夹具 **508** 检查；assembleDebug/lintDebug 通过（0 错误／14 既有警告）。详情及正式更新证据见 [本轮交付报告](MODE_REGION_FEEDBACK_DELIVERY_20261030.md)。

本轮仅更新 Android，不构建／部署／重启 Receiver。现场 Receiver 为 PID **20292**／创建 **2026-10-30 11:23:35.456177 +08:00**／Runtime RunId **1**；启动器旧记录已陈旧，按真实进程、端口、上下文和快照核对，没有修改记录来掩盖现场。实际正式设置已由用户改为手势用途，宽度 **22.5／20 dp**，原地 Q／一档 W／二档 E；本轮原样保留。手机布局仍 **713 字节**、SHA-256 `C82C99AF…`，前后逐字节核对相同。新 APK `EEC4D19C…`；恢复包及独立 guard 在 `C:\rightpad-artifacts\test-results\20261030-mode-region-feedback-r1`。**已正式更新并自然重连**：Android PID **8119**、Sender **55A60B1A1466E087**；独立 guard **8788** 在完整真实预检／同一 armed 确认后保护安装，**60.684 秒／22 次**连续健康观察后自行退出，任务结果 0，临时任务已注销，无实际回退。没有 Git 写入、ADB 触摸或生产输入注入。实际短振、边界附近是否频繁以及游戏体验仍由用户正常使用确认；I/O 中止问题继续搁置。

**2026-10-29 固定区域实施轮次**：用户明确授权本机实现、隔离验证及独立保护下双端正式部署；这是本轮独立授权，不扩大其他历史权限。新的 Mode 第三用途为固定原矩形＋右侧两个独立 dp 宽度，UP-only，保留旧用途、正式值和布局，不自动启用。撤销的相对 DOWN 方案不恢复。契约见 [MODE_FIXED_REGIONS.md](MODE_FIXED_REGIONS.md)，本轮状态、精确验证和部署证据以 [交付报告](MODE_FIXED_REGIONS_DELIVERY_20261029.md) 为准。

本轮已正式更新两端：Receiver PID **19780**／Runtime RunId **1**／创建 **23:36:31.1978982 +08:00**；Android PID **18924**、Sender **E82B8B25C291BE40** 自然接纳。正式 DLL `8D5C9F4D…`、APK `D49E04B3…`，完整哈希见交付报告。Windows **808/808**、Android 新区域 **294**／生产 View **109** 检查、APK/lint 通过（14 既有警告）。第二个独立 guard PID **29916** 完整预检、同一 armed 确认、**61.986 秒／22 次**健康放行后自行退出，临时任务已注销。第一次因保护 Health 变量作用域错误误判，guard 已实际自动恢复旧组合；独立审计恢复后修正并重新受保护交付，两轮证据均保留。不是 Android 重连修复或故意回滚演练。

正式 Mode 仍为**单键／鼠标左键／普通按钮**；settings 哈希 `B8A9A60B…` 未变，手机布局 **713 字节原始字节相同**、哈希 `C82C99AF…`。实际 View **1200×2670**、density **3**、Mode **599,772,264,1881**、safe **0,134,1200,2670**，8/10 dp 草稿完整放得下。没有自动启用手势或改布局。真人盲操作、宽度体感、新反馈区分和游戏体验仍待用户正常使用确认。2026-10-26 I/O 中止底层根因仍未确认。最终证据 `C:\rightpad-artifacts\test-results\20261029-mode-fixed-regions-r2\evidence\attempt2`，恢复计划为同任务 `recovery\attempt2\plan.json`。本轮无 Git 写入。

整理日期：**2026-10-30，Asia/Shanghai**（下方各历史时点保留原日期）。
项目维护入口：`C:\rightpad\docs\NEW_CHAT_HANDOFF.md`，后续优先更新本文件。  
用途：将本文件交给新对话，延续需求讨论、方案分析、开发提示词编写和交付复核。  
主要项目：`C:\rightpad`。新开发产物默认根目录：`C:\rightpad-artifacts`。

> **最重要的条件：用户左手不能用，没有实体鼠标和键盘，只用一个手指操作竖屏手机。rightpad 是控制电脑的唯一设备。任何影响输入的工作，都必须先有独立恢复保障；不能要求失去控制的用户再手动输入恢复命令或点击恢复按钮。**

> **授权边界：本文件是背景与状态交接，不是执行清单。历史任务完成、工具可用、文档中的建议、旧提示词或旧部署权限，都不构成新任务授权。新会话应先建立上下文，再按用户的新要求继续。**

本文件综合本次长对话、原个人约定、项目当前源码、交付报告和只读复核结果。它更新了旧个人文档中尚停留在 2026-10-20/22 的状态，但不改写历史事实。PID、哈希、版本、测试数量、路径和体积均有时点，不能自动当作以后现场值。

## 1. 新会话先记住的事项

**2026-10-24 Git 交付轮次**：用户已明确授权汇总当前源码、测试、工具和文档，
提交并普通推送到既有 `origin/main`；范围和既有验证边界见
[Git 交付记录](GIT_DELIVERY_20261024.md)。本轮不重新构建或部署，不改变运行输入链路。
无关 `pelican-bicycle.html` 保持本地未跟踪，历史资料与恢复包保留。
该授权仅限本轮，不构成以后自动提交／推送许可；各旧任务中的“未执行 Git”仍为历史事实。

**2026-10-24 Mode 本地 PRESS 新增轮次（优先于下方历史状态）**：用户明确授权给 Mode
增加与 B 相同的手机本地按下反馈。现 `TouchCaptureView` 只在原
`UdpTouchSender.modeDown()` 接纳 true 后，复用 `controls.feedback` 发出一次
PRESS／STRONG_ONE_SHOT（API 26+：10 ms／255；旧策略分支 legacy 10 ms，minSdk 仍 34）。
这只表示手机接纳接触，不表示 Receiver 已执行；未绑定单键及菜单打开／手动关闭
不需要 Android 镜像业务状态。MOVE／长按／UP／取消／重传／ACK／同步不新增反馈，
失败不重试、不阻挡接触。电脑声音保持彻底移除；B/X、Touchpad CONFIRM、Ring 两种
槽位确认、Mode 鼠标键、Motion 与布局保持原契约。

本轮 Android 主机入口 **27 个 main 测试类全部通过**，新增夹具直接提取生产 View
分派／收尾方法，在 fake Android 外壳中执行真实 router／ModeContactStream／反馈策略，
**94 项检查通过**。首次旧回归误定位新增 DOWN 分支，修正定位后通过，旧断言未删除。
assembleDebug／lintDebug 成功，lint 0 错误／14 项既有警告；未运行手机 instrumentation
或生产输入注入，未构建／部署 Windows 或原生组件。

Android 已保留数据覆盖安装，实际包哈希 `3DB4AC5581C2026147F55F9608626762F869D4BA3339178B089685C5418D8FE3`，
版本仍 0.1／versionCode 1；旧完整 base APK `493808D6852CC55EA624EDFB80095222B9C9085CE74D51FA29433CB916F4A898`，
现场无 splits，新旧签名一致。手机 PID 12662（时点值）；新 Sender `26649425012F37B3`
已自然 Connected；Receiver 仍 PID 31572、原创建时间、Runtime RunId 1，正式文件与设置
哈希保持。布局前后原始字节完全一致：696 字节，SHA-256
`0560DEA68780DDFF60249429A1EAB669CAF685E0719F2E4F61C38AB724F3A0CC`。

本轮独立 guard PID 31988，在真实 Task Scheduler／Limited／Session 1／Default 环境
完成非破坏预检；短启动进程退出后仍存活。已依次核对新 nonce、ready、armed 和同一
guard 确认后才安装。保护只恢复 Android，绝不重启 Receiver。已连续观察 62.29 秒／
25 次健康检查后放行，自行退出且 LastTaskResult=0，23:14:23 +08:00 注销本轮临时任务。
最终健康放行及收尾记录见 [本轮报告](MODE_LOCAL_PRESS_DELIVERY_20261024.md) 和
`C:\rightpad-artifacts\test-results\20261024-mode-local-press-r1\evidence` 为准。
完整旧 APK、原布局、候选与恢复脚本均保留在该任务目录。没有执行真实回滚；
Mode 振动体感、正常操作及真人游戏仍由用户验收，不把调用日志当作与 B 体感一致证明。

**2026-10-24 22:43 后的最新状态（优先于下方历史时点）**：用户实际体验后认为
“声音反馈不实用”，明确决定放弃并彻底移除。当前源码和正式运行包已删除电脑声音
设施、接线、配置类型、界面、音效与专属测试；不是关闭开关或默认静音，没有替代反馈。
不要再把声音列为待接入／待优化／默认开启功能。

新 Receiver PID **31572**，创建 `2026-10-24T14:42:13.9714541Z`（22:42:13 +08:00），
正式 DLL SHA-256 `CB6CD7423F9459E913200B2AC3DECE8BD9CC9D0D93AD32BF0947650345E26F81`。
这些是本轮时点记录，未来操作仍重新核对。新任务
`C:\rightpad-artifacts\test-results\20261024-mode-sound-removal`：Release 0 警告／0 错误，
完整 Windows 回归 **796/796**；专属声音测试组和入口已删除，原输入回归保留。

本轮独立 guard 20180 在真实 Limited 交互环境预检，启动进程退出后仍持续心跳；
新 nonce／PID／创建时间／ready 核对后 armed，同一 guard 确认后才停旧实例。完整候选
目录替换正式包，Android 自然重连，60 秒／19 次健康检查后独立放行，临时保护任务已
注销，正式 `Rightpad Receiver Dev` 保留运行。当前 Controls 没有声音面板或空面板。

正式 settings 原本没有 `modeSound`，实际未改写，SHA-256 仍为
`B02A5414C2FB2C8453DB77102821D9D9EEE9C8BEBD4C2058786BC03196B088D5`；保留鼠标左键／
Toggle、Ring 顶部 Esc、Sensitivity 12/12、C Amax 160000／Tau 30 及所有正式值。
用户已明确确认此前鼠标单键绑定通过；本轮保留这些接受行为，不重做输入功能。
未操作 Android／手机布局、驱动、Broker、许可及 Git 写入，既有未提交成果保留。

当前证据见 [移除交付报告](MODE_SOUND_REMOVAL_DELIVERY_20261024.md)。
[原声音交付报告](MODE_SOUND_DELIVERY_20261024.md) 保留当时成功事实，现仅为历史资料；
旧声音脚本、候选、恢复包、hold 和个人试听样例保留，不作为当前自动执行入口。
未执行真实回滚，未做真人游戏验收；本轮部署后的实际操作体验仍由用户确认。
本轮授权不构成以后部署许可。

1. 本讨论会话主要做需求讨论、方案分析、写提示词和只读复核；实施、构建、测试和部署交给另行授权的 Codex 或 DeepSeek 开发会话。
2. 用户说“写提示词”时，交付可复制提示词；**当前讨论助手不要自行实施**。
3. **每次写开发提示词，同时告诉用户建议使用什么执行者、模式、推理强度和速度。** 模式建议是针对任务的判断，不是永久固定偏好，也不自动扩大权限。
4. 用户自己做实际使用和游戏验收。不要擅自启动、操作或截图游戏，不要要求用户提前开游戏供代理操作。
5. 保留正式设置、手机布局、已接受手感、既有未提交/未跟踪工作、历史证据和恢复资料。
6. 用户最新明确取舍是：**只修明显影响使用的问题**。不要把 AI 审计报告整体变成整改任务。
7. 最新三项输入可靠性修复已经实现、构建、回归并部署到两端，本会话只读复核认为工程交付可以接受。**不要把它们重新列成未开发任务。**
8. 最新三项修复仍待真人双击拖动、实际取消释放及游戏体验验收；不能把自动化或被动日志观察写成全面真人通过。
9. 新产物默认外置已经完成；异常 JSON 已处理；五批历史归档已经完成。没有新的清理或迁移授权。
10. 最近讨论到换电脑、重装系统和换手机。确认需要迁移准备，但**尚未授权制作迁移包、安装器、改签名、迁移设备或重装系统**。
11. 2026-10-23 Mode 单键鼠标键轮次：P1 完成源码/构建/回归/候选/旧版备份/独立保护
    预检与演练；P2 修正保护缺陷后**已正式部署**（新实例 PID 10808，Receiver DLL
    `ADF44E…`，原生桥接 `21A555…`）。用户现已明确确认鼠标单键绑定通过；
    不要重复开发。§7A 的当时未验收状态仅为历史时点，不扩大为所有游戏验收。

## 2. 用户的长期操作条件与产品要求

### 2.1 实际操作方式

- 左手不能使用；不能把双手协作当作前提。
- 没有实体鼠标、键盘或另一套手柄用于接管。
- 只用一个手指，在竖屏手机很小的舒适范围内操作。
- 通常看电脑，不看手机；需要适合盲操作的稳定位置与动作含义。
- rightpad 同时承担日常桌面控制和游戏输入，主要用于支持手柄的 Steam 游戏。
- 用户关心小幅移动控制视角、微调、转向、停止、长时间舒适度，以及单指条件下完成不同动作。
- “一边前进一边移动视角”曾被提出为需求，但不等于授权新增摇杆、保持机制、宏或多指方案。

### 2.2 配置界面

- 重要设置尽量放在 Windows Receiver。
- 提供大按钮、清楚的选中状态、草稿值、正式值和运行状态。
- 通过左键完成，不依赖右键、滚轮、实体键盘快捷键或 Enter。
- 绑定选择提供可点击目录，不能要求手输扫描码、内部枚举或手改 JSON。
- 不要求用户自己在失去输入时运行 PowerShell、ADB 或恢复命令。
- 手机布局编辑采用电脑端完整、清晰、等比例的竖屏预览；用户接受独立大布局编辑窗口。
- 用户不需要额外的局部放大和顶部/中部/底部定位功能。
- 不自行增加多指、陀螺仪、加速度计、自动游戏识别、账号或云同步。

### 2.3 手感与算法边界

- 普通鼠标保留相对位移语义，不擅自变成摇杆速度控制。
- 不按速度、噪声、网络、FPS、游戏或负载动态调整灵敏度、增益、平滑、延迟或模式。
- 不未经批准增加预测、人工惯性、自动居中或新滑行。
- 固定重建完成已获得的真实位移，与额外制造距离的惯性不同。
- 已明确批准的例外要保留，尤其 C-Z1 的 true glide；不能用概括禁令删除已接受功能。
- 不能靠调 Motion 参数掩盖日志、资源争用、线程或协议问题。
- 用户重视稳定、可控、微调与长时间舒适度；不能只按最低延迟数字评判方案。
- 不因代码量、开发难度或价格直接否定有明确收益的方案；金钱成本、供应商许可、离线可用性、驱动维护和恢复依赖应分别评估。

## 3. 协作、授权与提示词习惯

| 用户表达 | 应交付的内容 | 不自动包含的动作 |
|---|---|---|
| “先讨论”“不要执行” | 讨论与方案；只读调查也需符合当轮范围 | 文件修改、设备操作、部署 |
| “看看源码/项目/报告” | 相关只读调查与证据判断 | 顺手修复、重启、改参数 |
| “写提示词” | 可复制的完整任务提示词，并说明建议模式 | 本讨论助手自己执行提示词 |
| “总结成文档” | 读取必要资料并创建文档 | 改程序、运行工程测试、部署 |
| 交给开发会话实施 | 在明确范围内实现、验证与交付 | 无关重构、清理或架构扩张 |
| 明确授权部署 | 备份、保护、受控更新与控制恢复 | 顺便更新驱动、系统或其他设备 |
| 询问目录体积/能否外置 | 体积分析或存放方案 | 删除、迁移、压缩历史证据 |
| Git 提交/推送 | 只按本轮明确仓库与范围操作 | 此后永久拥有 Git 写权限 |

交流要求：先给直接结论，再解释实际影响和证据；区分事实、推断、建议和未知项。长任务提供简短实质更新。不要反复确认已经明确授权的常规步骤，也不要将可自动完成的操作推给用户。

### 3.1 开发执行者和模式

- 用户使用过 DeepSeek Harness 和基于 `dsh-orb-cordis` 改造的电脑操作插件。插件第一版已开发完成，曾用于 rightpad 任务；这不等于当前会话可以修改 Harness 或插件。
- 用户也把范围明确的修复交给 Codex。
- 每次写提示词，应说明执行者、工作目录、执行模式、推理强度和速度建议，并解释必要取舍。
- 最近三项可靠性任务的建议是：**Codex / Local / 默认执行模式（非仅 Plan）/ High / 标准速度 / `C:\rightpad`**。
- 上一条是任务建议，不是永久锁定模型，也不是自动授予 Full Access 或切换权限的指令。
- DeepSeek 的具体模式应结合其实际界面与任务判断；不要凭空写一个未经核实的模式名。
- 不承诺某个额度必然能完成几小时任务。

### 3.2 实际测试由用户完成

用户已明确“实际测试我自己来”。最近任务没有授权代理操作游戏、ADB 触摸注入或合成生产鼠标/键盘/手柄输入。

优先用 fake 输出、隔离端口和回环通信验证故障。用户自然使用产生的日志可以在授权范围内只读分析，但不能冒充代理完成的真人验收。不要要求用户用第二根手指或故意让唯一控制设备失控来复现缺陷。

## 4. 唯一输入通道的恢复要求

### 4.1 影响输入前先准备

停止 Receiver、替换正式程序、更新 Android 或改变重要配置前，应按任务准备：

- 当前现场程序、依赖、APK、设置和布局的可用备份。
- 核对后的恢复路径、运行环境、脚本及依赖。
- 能独立于 Receiver、Android App、当前终端和 agent 会话继续工作的保护。
- 在真实独立环境中验证保护能启动并找到正确备份，之后才进入可能断开输入的窗口。

不能用“脚本写好了”“任务注册成功”代替独立环境验证。不能要求已经失去控制的用户再启动保护。

### 4.2 证据强度要分开

| 实际完成的事情 | 可以声称的结论 |
|---|---|
| 语法、路径和哈希检查 | 离线预检满足条件 |
| 独立计划任务执行预检 | 该独立环境能启动并完成预检 |
| 用模拟条件测试恢复决策 | 决策分支符合设计 |
| 部署后独立检查健康并退出 | 健康放行分支成功 |
| 真正替换旧版、启动并恢复操作 | 对应场景的真实恢复成功 |

不要求故意破坏唯一输入设备补证据。没有执行真实回滚，就保留限制，不能在后来的报告中倒写成已验证。

### 4.3 运行实例与控制恢复

- 持久 Receiver 应通过批准的独立启动方式运行，避免继承 agent shell 的终止生命周期。
- 核对实际 PID、创建时间、EXE 路径、DLL 哈希、当前用户、与 explorer 对应的交互 Session、Medium 权限和 Default 桌面。
- 停止旧实例后核对退出及 UDP 50000 释放；不能只看启动命令成功。
- EXE apphost 哈希可能不随 C# 代码改变，必须核对 Receiver DLL。
- Connected、进程存在、窗口响应、零输出失败均不能单独证明用户已恢复实际控制。
- 涉及的正式设置、布局及临时绑定应恢复；临时观察器和保护任务按本轮范围收尾，恢复包保留。
- 如果真人操作由用户验收，明确写待验收，不把只读快照写成全面通过。

## 5. 主要路径与运行依赖

| 类别 | 位置或说明 |
|---|---|
| 主项目源码 | `C:\rightpad` |
| 当前推荐开发入口 | `C:\rightpad\tools\Invoke-Development.ps1` |
| 外部产物根配置 | `C:\rightpad\artifact-root.txt`，当前默认 `C:\rightpad-artifacts` |
| 正式 Windows 程序 | `C:\rightpad\windows\Rightpad.Receiver\bin\Release\net8.0-windows` |
| 独立启动器 | `C:\rightpad\windows\tools\RightpadReceiverTask.ps1` |
| 正式 Windows 设置 | `%LOCALAPPDATA%\rightpad\settings.json`；当前用户为 `C:\Users\zhq\AppData\Local\rightpad\settings.json` |
| Receiver 发现身份 | `%LOCALAPPDATA%\rightpad\receiver-id`，不是驱动许可 |
| 手机包名 | `com.rightpad.capture` |
| 手机主界面 | `com.rightpad.capture/.MainActivity` |
| 手机布局 | App 私有 `files/screen-controls.properties` |
| 本地对照项目 | `C:\leftpad`，仅作参考，不自动修改 |
| leftpad GitHub 参考 | <https://github.com/zhqxbmgit/leftpad.git> |

leftpad 的本地具体 commit/版本没有在本交接整理中重新核对，不要编造版本。用户指出 DeepSeek 不方便访问 GitHub，因此对照任务优先直接看 `C:\leftpad`。

Windows 日常运行需要匹配的 **.NET 8 Desktop Runtime**、兼容的 libvirtualhid 驱动/Broker 和有效机器许可。运行已编译版本不需要 Codex、开发 SDK、JDK 或 CMake。

当前工程以 Windows 11 x64 为目标。生产鼠标后端是 libvirtualhid Virtual HID Mouse；SendInput 仅为显式开发/诊断覆盖，不是生产自动降级。不能擅自升级驱动、Broker、许可组件或改变后端。

项目记载的已验证驱动/Broker 为 `2026.914.1218.10`，libvirtualhid 固定依赖为 `53e1a949fc0784af716b782ddfa6c647cafd1f05`。未来迁移或升级时重新做兼容性核对，不把此快照解释为“以后只要装最新版本即可”。

## 6. 当前产品契约

### 6.1 Mouse 与 M/C

- Android 采集原始触摸、历史样本和时间戳并传输。
- Receiver 拥有普通鼠标 Gesture、Motion、设置和输出。
- 正常 GUI 默认 M-R1：固定 M8 重建、直接重建位置、Q0-C、1 ms 输出机会、重建完成释放，无 glide。1 ms 是输出机会，不是任意负载下硬实时 1000 Hz 保证。
- C-Z1 是批准的独立选择，保留固定 C12/既有 servo 与 true glide。
- M-R1 的位置整形不使用保存的 Tau/Support；正式灵敏度按既有规则生效。
- Receiver 本地 M/C 选择与手机 Mode 按钮用途独立。
- 当前是可信局域网个人设备系统，不主动扩展认证、配对、PIN、账号、HMAC 等体系；现有报文格式、来源/run 关联与防卡住措施仍应保留。
- 触摸期间静止按住合法，不能用无 MOVE 超时自动释放。
- 普通 Touch DOWN/UP 原规则为三份，MOVE 一份；网络 UDP 不是必达传输。

### 6.2 B/X 与设置

- B：基础 B，上滑 Y，下滑 A。
- X：基础 X，左/右/上滑对应方向键。
- 已接受的阈值、反馈和现场个人参数保留；不拿文档默认值覆盖用户当前值。
- 普通 tap 的最小保持机制与真正取消时的安全释放是不同语义。
- 统一 Save 采用磁盘成功后再发布正式设置；失败保留原正式值并提示。
- 布局试用是电脑草稿 → 明确试用 → 手机 60 秒期限 → 明确保留；否则回原现场布局。
- B3 的 3 秒未确认请求寿命不是将手机试用期改成 3 秒。

### 6.3 Mode 单键与 Ring 菜单

- Mode 原单键用途保留键盘/手柄绑定、普通按钮和切换保持；手机静态 M 标签不代表实际用途或 Receiver M/C。
- 菜单只保留固定完整 Ring，六向顺序为上、右上、右下、下、左下、左上。
- 六向独立大按钮/Islands 已删除，旧外观值兼容为 Ring。
- 有效 Mode DOWN 打开，再次有效 Mode DOWN 手动关闭。
- 使用真实 Windows 系统光标；打开不跳光标，打开位置是固定锚点。
- 包含边界的中央 12 DIP 为中性区，无外圈上限，装饰间隙不阻断方向选择。
- 菜单打开时，已接纳移动只进入原 Motion → 量化 → Virtual HID 路径一次。**用户接受游戏收到导航位移。**
- 菜单点按在普通鼠标按键事件产生前分流，不能向游戏泄漏普通左键或双击拖动。
- 已绑定方向有效点按：一次按下，25 ms 脉冲完成释放后，通过原 Haptic Feedback v1 提交一次普通点击确认，然后关闭菜单。
- 未绑定方向有效点按：不输出键盘/手柄/普通左键，提交同样确认后立即关闭；不伪造按下、不等 25 ms。
- 中央区域：无动作、无确认、不关闭。
- 一次打开最多接纳一个有效槽位选择，已绑定和未绑定共用限制；没有原来的串行动作队列。
- 反馈为尽力而为，不等手机确认执行才关闭，不因失败重放按键。
- Mode 按钮接触本身没有新增点击确认；槽位动作后的确认是已批准例外，不能与按钮接触混为一谈。
- 隐藏或受 ClipCursor 约束的系统光标不是菜单可用性否决条件；实际读取失败、显示区域/DPI 改变、租约及原生命周期故障仍会安全关闭并隔离旧输入。
- 不调用 SetCursorPos/ShowCursor/ClipCursor 制造私有导航，不加入游戏 Hook、注入或强制解除游戏约束。
- 菜单显示真实光标位置的白色主体、黑色轮廓箭头；箭头只读显示，不是另一套位置状态。
- 当前只有菜单窗口使用 SoftwareOnly，保留实际绘制机会跟随与已有时间统计，不改变全局绘制或 Motion。

### 6.4 已有游戏反馈

用户曾在以下无边框游戏报告菜单打不开：瑞奇与叮当：时空跳转、地平线：西之绝境、毁灭战士：黑暗时代。

后续修复后用户报告“桌面正常，游戏可以点开，但看不见鼠标难选”；高亮能够变化。可见箭头随后交付。呈现节奏修正后《地平线：西之绝境》仍卡；菜单窗口 SoftwareOnly 版本部署后，用户明确反馈 **“明显改善”**。

这是具体版本、具体游戏的真人反馈，不等于所有游戏、独占全屏、反作弊或所有 DPI/多显示器都已验收。不要沿用“箭头问题仍完全未解决”的旧状态，也不能说全部游戏已经流畅。

## 7. 最新完成：2026-10-23 三项输入可靠性修复

状态：**实现、隔离构建、适用回归、独立恢复保护下的两端部署完成；本讨论会话已只读复核，工程交付可接受。真人操作体验仍待用户日常确认。**

### 7.1 普通接触明确取消

原缺口：ACTION_CANCEL 或 pointer 异常在手机本地结束接触，没有普通 Touch 取消线事件；Receiver 静默超时只诊断，heartbeat 可能维持旧拖动。

当前实现：

- `stopCapture`、ACTION_CANCEL、多指/pointer 异常、暂停、目标变化和关闭接入取消收尾。
- 新增 UDP 50000 上 22 字节 v2/type9；身份包含 sender run、session、原 DOWN sequence、取消 sequence 边界。
- type9/RPTC 是这轮明确授权并已部署的必要扩展；旧文档“普通 Touch 无取消线事件”的历史描述不构成回退新实现的理由。
- 取消边界取自原 Touch 顺序域；既有普通 Touch 与 heartbeat 的字节含义未改变。
- UDP 50002 新增独立 28 字节 `RPTC` ACK，精确回显取消身份；没有借用普通 UP 或 RPHF。
- Receiver 先推进顺序围栏，再清理对应 Motion、Gesture、双击资格、排队点击和已保持左键。
- 取消不生成普通 click、菜单槽位动作或点击确认；type9 对菜单只清该 Touch 资格，不自行执行槽位。Mode 本身的取消仍是独立生命周期。
- 重复取消幂等；旧 run、错误来源、旧接触、迟到取消不释放新接触；取消先于 DOWN 到达也能防止旧接触复活。
- Android 最多一项待确认取消，等待时禁止新 capture，每 100 ms 最多尝试一次，窗口 1500 ms。
- ACK 耗尽后停止旧路由流量，安排同目标新 sender run 并重置相关事务缓存；若 UI 或通信不能恢复，不再以旧 heartbeat 续命，由既有 2000 ms presence 超时收尾。
- 上述时间是机制配置，不是任意网络/系统调度下的硬实时保证。

**仍未解决：正常 UP 所有 UDP 副本在网络中丢失，而 Android 没有明确取消的情况。不能宣称本轮解决所有丢包或任意故障下立即释放。**

### 7.2 共享反馈监听恢复

- 原监听线程在同目标、同 sender run 下重建 socket。
- 退避为 100/200/400/800/1600/2000 ms，封顶，不启用 SO_REUSEADDR。
- identity 和 socket 接收代次都校验；失败 socket 的旧 UI 工作作废。
- socket 重建不重配 HapticFeedbackGate，保留已消费点击去重与期望 UP 身份。
- 覆盖原 RPHF、Controls 配置、健康状态、布局、Mode 回复及新取消 ACK。

### 7.3 heartbeat 失败后的 Touch

- 首次 heartbeat 尚未成功：保持原接入门槛，显式安全放弃旧队列，必要时建立取消围栏，不等网络恢复后补发旧动作。
- 至少成功一次后的周期性 heartbeat IOException：当前有效 Touch 仍尝试发送，保持 DOWN/UP 三份、MOVE 一份。
- 没有无限 Touch 缓存或新 Touch 重传队列；Touch 自己的发送失败仍为 best-effort。

### 7.4 验证与部署

| 项目 | 实际记录 |
|---|---|
| Windows 最终 Release | 0 警告、0 错误 |
| Windows 全量回归 | 785/785，通过数是该版本结果 |
| Android 主机 | 26 个 main 测试类全部成功；新增可靠性故障组 3/3 |
| Android 构建/lint | assembleDebug、lintDebug 成功，0 error、14 warning |
| 故障验证 | fake 输出、隔离动态端口和 loopback，非生产故障注入 |
| 更新顺序 | 先 Receiver，确认旧 Android 自然重连，再更新 Android |
| 独立保护 | Task Scheduler 真实环境预检、布局临时夹具字节写回、五种决策演练、独立健康放行 |
| 实际双端回滚 | **未执行** |
| 控制观察 | 被动观察自然移动和点击确认；有效只读按键/XInput 快照无保持 |
| 真人验收 | 双击拖动、实际取消释放、反馈体感和游戏仍待确认 |

部署前旧 PID 19128 没有可用可见主窗口。执行者按进程身份精确核对后终止残留，再更新；报告没有声称优雅退出。正式旧目录保留为 hold。

保护 ready 早于 armed 和部署；三个本轮临时预检/决策/Guard 任务已撤销，正式 Receiver 独立任务继续运行。没有真实回滚，不把预检、夹具或健康放行冒充为真实恢复。

## 7A. 2026-10-23 Mode 单键鼠标键：**已部署（P2）**

状态：**实现、Release 构建、全量回归、原生隔离构建、旧版备份、独立保护预检/决策、
部署演练与正式部署均已完成；真人验收尚未进行。** P2 已停止旧实例（PID 24940）并
由批准启动器启动新实例（PID 10808，Receiver DLL `ADF44E…`、原生桥接 `21A555…`）。
正式 `settings.json` 与手机布局未被改动。

范围：只给 Mode“单键绑定”增加鼠标左键、右键、中键（滚轮按下），普通按钮与切换
按住；Ring 六个槽位继续不接受鼠标；无协议/字段/端口/Android 改动。

关键实现：

- `ModeBindingKind.Mouse = 3`（只追加），稳定标识 `left`/`right`/`middle`；
  非法或含鼠标槽位的配置整体回退为未绑定，不误输出其他按钮。
- 新增 `MouseButtonMerge`：触摸板 Gesture 与 Mode 各自持有按键贡献，同一个生产
  Virtual HID Mouse 输出 OR 后的有效状态；只有有效状态变化才提交边沿；一方释放
  不解除另一方仍需要的键。左/右/中可同时保持；移动报告保留按键状态。
- 原生桥接增量 `rightpad_vhid_mouse_api_version=1` 与
  `rightpad_vhid_button(handle, button, down)`（0 左/1 右/2 中），旧左键与
  mouse/gamepad ABI2 入口不变；桥接缺少扩展时右键/中键明确报错，左键仍可用。
- 普通 Touch 取消只撤销触摸板贡献，不清除有效 Mode 切换保持；Mode 自身
  CANCEL/租约/presence/run 变化才清除。

验证数字（时点）：Windows Release 0 警告 0 错误；全量回归 **796/796**（新增 11 项）；
原生 `bridge_tests` **773** 项通过。P2 部署：guard 由部署脚本异步启动并 ready，
armed→停旧→完整包替换→批准启动器启动→自然重连→60 s soak（19 次复核）→release，
guard 健康放行后注销本轮 guard 任务；未触发回滚。真实桌面鼠标右键/中键输出、游戏
手感、真人盲操作与真实回滚仍未验证。

交付与证据入口：

- [鼠标键交付报告](C:/rightpad/docs/MODE_MOUSE_BUTTONS_DELIVERY_20261023.md)（含 §7A 修正与 §7B 实际部署）
- 任务目录：`C:\rightpad-artifacts\test-results\20261023-mode-mouse-buttons`
- 旧版完整备份（保留）：`…\recovery\backup-20261023-194403-146`
- 旧版运行包 hold：`C:\rightpad\windows\Rightpad.Receiver\bin\Release\.hold-mode-mouse-buttons-20261023200532643`
- 部署证据：`…\evidence\deployment-armed.json`、`deployment-release.json`、
  `guard-released.json`、`cleanup.json`、`post-deploy-verification.json`
- 部署脚本：`…\recovery\DeployRound.ps1`（默认 DryRun；`-StartGuard` 自启保护）
- 本轮 guard 任务已注销；`Rightpad Receiver Dev` 仍在运行。

真人验收仍由用户完成：`build/test 通过`、`健康放行` 都不等于用户已恢复实际控制。

## 8. 最新已知运行版本快照

**以下为 2026-10-23 P2 部署后的时点信息。新会话如获准调查现场，应重新核对，不按旧 PID 操作。**

| 项目 | 最新已知值 |
|---|---|
| Receiver PID | **10808** |
| 创建时间 | 2026-10-23 20:05:42.048854（Asia/Shanghai；UTC 12:05:42.0488541Z） |
| Session / 权限 / 桌面 | Session 1 / Medium / Default |
| 监听 | `0.0.0.0:50000`；部署后复核归属 PID 10808，唯一正式实例 |
| 运行状态 | Running / Connected |
| 部署后健康快照 | 2026-10-23 20:07:12 +08:00；MouseOutputFailures=0、GamepadFailures=0、LastError=null |
| Android sender run | `6547286BB9DA19ED`，时点身份，不是固定配置；停止前 Connected，新实例自然重连 |
| Runtime RunId | 1，仅该进程内身份 |
| Receiver 日志 | `C:\rightpad-artifacts\receiver-runtime\20261023-200542-001-32448\receiver.log` |

上一实例 PID 24940（DLL `F844594D…`、原生 `56829F9E…`）已于 2026-10-23 20:05:32
（UTC 12:05:32）正常关闭优先后按身份核对终止；其完整运行包保留为 hold，见 §7A。

版本 SHA-256：

| 文件/数据 | SHA-256 |
|---|---|
| 当前 Receiver DLL（P2 部署） | `ADF44E359068C9B916408DC6EF7A224605AD01F658DE96C5C7717B468E7E180C` |
| 当前 Receiver EXE | `B444947422873175F8B45A9206A9D870FC2853696FBB10496EE4417C546E33CE` |
| 当前 VirtualHid DLL（P2 部署） | `21A5556AF12F0FBF17085E23E59A4F97EBCBA883D6FFD8E5851EF5DD88E0F721` |
| 当前 Android APK | `493808D6852CC55EA624EDFB80095222B9C9085CE74D51FA29433CB916F4A898` |
| 正式 settings | `C3E4B9CBE21246FD004BC55DDEA360C32B88D5D13CA9F5AE20D36B7F69B4C7F1` |
| 手机正式布局 | `0560DEA68780DDFF60249429A1EAB669CAF685E0719F2E4F61C38AB724F3A0CC` |
| 上一运行版 Receiver DLL（PID 24940，保留为 hold） | `F844594D8C9D12F15178C096804222F1FF16BEF6F261B35F4FB121C9A779D521` |
| 上一运行版 VirtualHid DLL（保留为备份/hold） | `56829F9EC344C1C7F0C5C35C8F8BBE59AF4936ACB6ADC2B32D8931D77343BE5E` |
| 本轮备份旧 APK | `F47BC9E45680D0DF4E21AAC99FA2722C30C0CE1D5F010BB5E22133CFC8A277BA` |
| 可靠性轮次备份旧 Receiver DLL | `C3ACC098EBB5A6DE92684E1685505E9C6EDA627F851FB5579B4EC13917F0A88C` |

本会话独立读取验证（P1）：正式、候选、隔离构建与测试目录中的 Receiver DLL 哈希相同；
恢复清单 29 个文件长度及 SHA-256 全部匹配；正式 settings 哈希匹配。手机安装哈希、布局
及前台状态依据交付证据核对，没有重新运行 ADB。

**2026-10-23 P2 部署补充**：上表 PID 10808 / DLL `ADF44E…` / 原生 `21A555…` 是
**当前正在运行**的版本（2026-10-23 20:05:42 +08:00 启动）。P2 部署由 `DeployRound.ps1
-Mode Deploy -StartGuard` 在同一个无交互脚本内完成：guard ready → armed → 停旧
（PID 24940）→ 完整包替换 → 批准启动器启动 → Android 自然重连 → 60 s soak（19 次）
→ release → guard 健康放行 → 注销本轮 guard 任务。旧版完整包保留为
`C:\rightpad\windows\Rightpad.Receiver\bin\Release\.hold-mode-mouse-buttons-20261023200532643`，
备份 `recovery\backup-20261023-194403-146` 保留；未触发回滚，未做真人验收。

较早报告中的 PID 31900、34628、31232、22204、19128 是历史实例，不能当作当前要停止的进程。旧运行根的 current.json 曾与现场不符；不能为使记录“看起来正确”擅自重启、改写或清理。

### 8.1 最新交付入口

- [三项输入可靠性交付报告](C:/rightpad/docs/INPUT_RELIABILITY_DELIVERY_20261023.md)
- [Mode 鼠标键交付与部署报告](C:/rightpad/docs/MODE_MOUSE_BUTTONS_DELIVERY_20261023.md)（§7B 实际部署）
- [鼠标键部署后复核](C:/rightpad-artifacts/test-results/20261023-mode-mouse-buttons/evidence/post-deploy-verification.json)
- [鼠标键 guard 健康放行](C:/rightpad-artifacts/test-results/20261023-mode-mouse-buttons/evidence/guard-released.json)
- [鼠标键收尾记录](C:/rightpad-artifacts/test-results/20261023-mode-mouse-buttons/evidence/cleanup.json)
- [普通 Touch 取消协议](C:/rightpad/docs/TOUCH_CANCELLATION_PROTOCOL.md)
- [完整证据索引](C:/rightpad-artifacts/test-results/20261023-input-reliability-r1/EVIDENCE_INDEX.md)
- [恢复目录](C:/rightpad-artifacts/test-results/20261023-input-reliability-r1/recovery)
- [原 Windows 程序与设置备份](C:/rightpad-artifacts/test-results/20261023-input-reliability-r1/recovery/backup-20261023-175721-867)
- [本轮最新 APK 候选](C:/rightpad-artifacts/test-results/20261023-input-reliability-r1/candidate/android/app-debug.apk)
- [保护实际健康放行记录](C:/rightpad-artifacts/test-results/20261023-input-reliability-r1/evidence/guard-released.json)

注意：恢复目录中的 `android/installed-base.apk` 是更新前的旧 APK，不是当前最新版。专用保护脚本包含本机用户、路径和设备地址，不能当成跨机器通用安装器直接运行。

## 9. Android 详细录制问题：已修复默认开启，未证明改善手感

2026-10-22 已交付 Android 诊断录制改造，之后本轮可靠性 APK 继续保留它：

- 普通启动不创建 CSV、诊断队列或诊断 worker，不输出逐样本及逐包成功详细日志，也不先构造这些字符串。
- 生命周期、连接、错误和低频日志仍存在；不能说整个项目不记录日志。
- 开发者通过一次性 intent 参数 `rightpad.diagnostic.touch=true` 显式开启。
- 每次最多 60 秒或 10,000 条诊断尝试，样本和发送成功诊断共同计数；256 项有界队列。
- 真实 Touch 先按原路径提交；诊断文件和详细日志由独立 worker 处理，满时仅丢诊断。
- 暂停、目标变化或关闭终止本次诊断，Resume/Activity 重建不自动续录。
- 旧 82 个 CSV 及手机私有资料保留，没有因此获得清理授权。
- 没有端到端延迟/CPU 前后基准；用户当时反馈 **“感觉不到”**，不能写成用户已感到明显改善或卡顿根因已修复。

Windows 的 receiver.log、FlightRecorder、手动 Hitch 导出与 Android 详细触摸 CSV 是不同机制。用户问“什么时候录制”时应分别说明。

证据：[Android 诊断交付](C:/rightpad/docs/ANDROID_TOUCH_DIAGNOSTICS_DELIVERY_20261022.md)。旧任务证据仍在 `C:\rightpad\windows\test-results\android-touch-diagnostics-20261022`，属于保留恢复资料。

## 10. 开发产物外置与历史归档：已经完成，不重新清理

### 10.1 未来产物的默认位置

2026-10-22 已接入统一根目录。源码仍在 `C:\rightpad`：

| 类别 | `C:\rightpad-artifacts` 下的默认位置 |
|---|---|
| Windows 构建 | `builds\windows\<任务或local>\<项目>\bin\<配置>\…` |
| .NET 中间文件 | `builds\windows\<任务或local>\<项目>\obj\…` |
| 原生构建 | `builds\native\<任务或local>\<配置>\bin`、`obj` |
| Android 构建 | `builds\android\<任务或local>\app\build` |
| Android 项目缓存 | `builds\android\gradle-cache` |
| Android JVM 测试 | `builds\android\<任务或local>\jvm-tests` |
| 每轮证据/候选/恢复 | `test-results\<任务>\evidence`、`candidate`、`recovery` |
| 新启动实例日志 | `receiver-runtime\<时间-PID>` |
| 新手工导出约定 | `exports` 或本轮 evidence |

当前 .NET 实际出现 `obj\Release\Release\net8.0-windows`；这是已核实的冗余路径/文档准确性问题，没有证据显示它影响正常运行。本轮没有授权修它。

正式运行目录、设置和旧资料不因默认外置自动移动。构建生成候选不等于部署；Start 启动正式目录也是既定流程，不是应当偷偷自动部署的缺陷。

HitchTraceExport 的手动导出仍可能写 `C:\rightpad\windows\test-results\hitch`。这是调查和归档中明确保留的当前可写路径，不是外置任务承诺已覆盖却遗漏的项目。不要因此重新判定交付失败或迁走整个目录。

文档：[开发产物流程](C:/rightpad/docs/DEVELOPMENT_ARTIFACTS.md)、[外置交付](C:/rightpad/docs/DEVELOPMENT_ARTIFACTS_DELIVERY_20261022.md)。

### 10.2 异常 JSON 已处理

原问题是 PowerShell Get-Content 附加的 PSDrive/Provider 等元数据被递归序列化，产生数百 MiB 的 JSON。

本会话已按授权处理六份异常 JSON：约 917.5 MiB 精简为约 23.3 KiB；原始字节以 gzip 保存约 56 MiB，并保留哈希、说明和映射。对应 phone-verify.ps1 改用纯文本行读取及体积保护，离线检查通过。

这不是删除原始证据，后续没有重复压缩/解压/去重。维护资料随完整 `mode-ring-only-20261020` 历史任务归档，位置为：

`C:\rightpad-artifacts\archive\rightpad-history\windows\test-results\mode-ring-only-20261020\json-maintenance-20261022`

未来证据只序列化显式字符串、数值、布尔、数组和路径，生成后检查大小与解析性；不要把进程、文件、Provider 等扩展对象直接深度序列化。

### 10.3 五批历史归档

每批均先建立源基线，复制到归档并验证，再同卷移动原件到 holds；旧路径实际移出。归档和 holds 各保留一套，不建立链接，不删除原件或失败证据。

| 批次 | 完成范围 | 文件数 | 逻辑字节 |
|---|---|---:|---:|
| `20261022-232846-history-archive-batch1` | 5 项，含两份源码导出等 | 77 | 12,836,179 |
| `20261022-235043-history-archive-batch2` | 6 项旧构建目录 | 324 | 81,970,403 |
| `20261023-001243-history-archive-batch3` | 28 个旧 Receiver run；1 个关联依赖项当时保留 | 104 | 111,831,622 |
| `20261023-003207-history-archive-batch4` | 6 个历史任务/预览目录 | 724 | 88,627,492 |
| `20261023-122435-history-archive-batch5-consolidated` | 21 个完整历史任务与关联旧 run，共 22 根 | 5,292 | 653,583,811 |

归档根：`C:\rightpad-artifacts\archive\rightpad-history`。  
原件 holds：`C:\rightpad-artifacts\migration-holds\<批次>`。  
两者均保留原项目相对层级。

第五批结束时，项目逻辑体积从 751.41 MiB 降至 **128.11 MiB**，减少约 82.95%。这是历史时点，不是最新可靠性部署后的实时体积；后来有新源码及正式旧版 hold。不能用这个数宣称当前目录一定仍为 128.11 MiB。

**同一 C 盘保留 archive 和 holds 两套资料，没有释放 C 盘总空间。** 用户原目标是项目目录整理，不能偷偷将其扩展为删除 holds 或全盘去重。

第五批仍原位保护 Hitch、旧运行记录、当前软件绘制版本恢复资料、Android 诊断恢复资料、固定原生依赖、measurements 等。Receiver 后来换成新实例，不自动解除这些文件的保留要求。

历史报告、脚本和 JSON 内的旧绝对路径保持原字节；追溯使用索引映射，不保证旧路径可直接重放。尤其恢复脚本可能存在包外依赖，不能只搬一个 recovery 子目录。

- [统一归档索引，包含前四批入口](C:/rightpad-artifacts/test-results/20261023-122435-history-archive-batch5-consolidated/archive-index.md)
- [第五批完整报告](C:/rightpad-artifacts/test-results/20261023-122435-history-archive-batch5-consolidated/README.md)
- [初始迁移调查](C:/rightpad-artifacts/test-results/20261022-history-migration-survey/README.md)

## 11. AI 审计的筛选结果与尚未授权事项

用户陆续提供过多份 AI 审计。不能把“多人都这么说”当作正确证据；需要对照当前源码、固定依赖及当前契约。

### 11.1 已修复，不要重复开发

- 普通取消缺少 Windows 收尾。
- 同身份反馈监听故障不恢复。
- 心跳失败主动吞掉已出队 Touch。

它们对应第 7 节的最新交付，而不是当前未完成清单。

### 11.2 有依据、但没有新实施授权的候选事项

| 候选 | 已知边界 |
|---|---|
| Mode 手柄暂未就绪被当作输出失败锁存 | 在恢复窗口操作手柄绑定可能阻断后续 Mode；应区分未尝试输出的暂不可用与结果不明的真实输出失败，不能全局清掉安全锁存 |
| 首次 Mode 键盘设备同步创建 | 在输入接收/Mode 锁路径内，有服务异常时阻塞风险；实际 5 秒/10 秒停顿未测，不是已确认正常卡顿根因 |
| Stop Receiver 界面 | 停止会切断唯一输入，需要单独讨论受保护重启或维护语义；不擅自改按钮含义 |
| 启动器 CloseMainWindow 与托盘隐藏 | 不能保证走正常退出清理；不等于已证明驱动残留按键；本轮部署已如实处理残留终止 |
| 日志异常隔离 | 同步日志及错误处理再次写日志可能妨碍故障收尾；不等于必须进行整套异步日志重构或已证明卡顿根因 |
| 后台 Mode CANCEL 持续重发 | 旧路径存在后台收尾问题；不保证系统任何后台状态都精确 50 Hz；不是本轮专门整改项目 |
| 普通 DOWN 多余 gamepad 安全中性 | 调用路径存在；不能断言每次必定三个包抢在 DOWN 前，更不能全局省略真正安全释放 |
| Type8 入口超时/收包统计遗漏 | 统计与短暂租约边界问题存在，非无限绕过超时；用户此前选择暂不纳入 |
| 布局合并 UI 投递竞态 | 有丢唤醒窗口，后续包可能重新唤醒；非所有情况永久失效；本轮按范围未改 |
| 序列上限、构建路径、签名/打包、文档漂移 | 属于边界、迁移或维护事项，不能自动扩展为整改 |

首次手柄 Neutral 提交失败后安装不可用对象的代码事实存在，但现有启动自动重试只允许一个已识别的创建错误。不能把实际输出失败的 fail-stop 策略一概判为错误，更不能无分类自动重试、重放旧按键。

### 11.3 不应照单实施的审计结论

- **“Runtime::create 驱动失败返回 nullptr，键盘必然 Native AV”不成立。** 固定版本工厂使用 make_unique，返回对象或抛异常，桥接有异常边界。可讨论防御检查，不能据此宣称已确认崩溃。
- **“clock() 与 now 是不同生产时钟域”不成立。** 都以 Stopwatch 为基准；成功按下后再取时钟可避免输出调用耗时吃掉最小脉冲，不应统一改成更早的 now。
- **“布局前设 State 就一定首帧箭头错位”证据不足。** 现有 OnRenderSizeChanged 会重算；中间数值不等于已显示错误帧。
- **“Hitch 写旧目录违反已承诺外置范围”不成立。** 它已明确保留。
- **“未跟踪源码证明全新 clone 必然引用缺失新文件而构建失败”推导过头。** Git HEAD 不包含所有工作树新引用。正确结论是最新工作尚未全部通过 Git 交付。
- **“Start 不直接启动新 candidate 是缺陷”不成立。** 构建和部署刻意分开。
- 无测量依据不能将 GetDpiForMonitor、GC 分配、锁竞争或日志写入直接定为当前游戏卡顿根因。

用户目前没有授权以上候选的新修复。优先进入日常真人验收，只有用户提出具体下一项时再写提示词或调查。

## 12. 最近讨论：换电脑、重装 Windows、换手机

**状态：只完成只读分析与建议，未制作迁移包，未实施迁移或安装。**

结论：可以迁移后继续使用，但当前不是复制项目目录就能在干净环境即用的通用便携包。

### 12.1 换电脑/重装系统

- 需要完整当前 Windows 运行包、匹配 .NET 8 Desktop Runtime、兼容驱动/Broker 及有效机器许可。
- 外部驱动和激活不包含在 rightpad 源码或发布目录内；复制 C:\rightpad 不能代替安装与激活。
- 现有许可是否可转移、重装后如何恢复或是否需解除旧激活，要核对账户与厂商规则；本会话没有保证，也没有读取/导出许可密钥。
- `%LOCALAPPDATA%\rightpad\settings.json` 在项目目录外，要单独备份。
- Start with Windows 在当前用户 HKCU Run 注册表中，不在 settings.json；迁移后需重新登记。
- 手机和电脑需处于可互通局域网，恢复私有网络下 UDP 50000/50001 入站规则及 50002 反馈可达条件。
- 自动发现通常无需固定原电脑 IP。新旧 Receiver 同一局域网同时运行时可能选到其中一个，当前没有手动主机选择器。
- 开发脚本、local.properties 和本轮恢复脚本存在本机路径、用户、会话、设备地址假设，不能原样当跨机安装器。
- 继续开发还需恢复 SDK/JDK/编译工具及相关路径；只运行现成版不需要完整开发环境。

**重装前必须先安排尚未安装 Receiver/驱动时的控制与恢复方式。不能假定 rightpad 在 BIOS、系统安装或新环境尚未配置阶段已经可用。**

### 12.2 换手机

- 当前仅 Android 客户端，没有 iPhone 版本。
- 当前 `minSdk=34`，即 Android 14；`targetSdk=36`。最低版本满足不代表所有机型、系统限制和触觉体验都已验证。
- Manifest 设置 allowBackup=false，不能指望系统换机自动恢复手机布局；应单独导出当前私有布局。
- 布局按宽高比例保存，可在新尺寸重算，但位置、大小与盲操作舒适度需确认。
- 触摸采样、密度和振动硬件可能不同，相同参数不保证完全相同体感；不因此自动改 Motion 或加补偿。

### 12.3 容易遗漏的源码与签名

- 当前源码有大量未提交及未跟踪工作，不能只依赖 GitHub 恢复最新版。
- 当前 APK 是 debug 构建，项目没有专门的 signingConfig。换开发电脑/重装后可能生成不同 debug 签名，不能直接覆盖安装到旧手机。
- 应确认并妥善保存实际使用的签名 keystore；通常在开发用户 `.android\debug.keystore`，实际位置仍需核对。
- 本对话没有完成签名密钥盘点或备份，不能声称它已包含在恢复包中。证书指纹也不是签名私钥备份。
- 当前普通用户版本仍为 `0.1 / versionCode 1`，不能只按版本号区分 APK，应使用哈希与签名。

建议过的未来任务是生成“当前可用版本迁移包”：两端匹配程序、正式设置、布局、签名备份清单、依赖版本及迁移预检。**这只是建议，不是已获授权的下一步。** 不需要为日常运行搬走全部历史证据，也没有授权删除原资料。

相关外部核对入口：

- [Android APK 签名说明](https://developer.android.com/studio/publish/app-signing.html)
- [Android API 等级对应](https://developer.android.com/guide/topics/manifest/uses-sdk-element)
- [上游 Windows 驱动、平台及机器许可说明](https://github.com/LizardByte/libvirtualhid/blob/master/docs/windows-driver.md)

## 13. 证据、测试、Git 与工具约定

- 源码分析、构建、回归、部署、实际加载版本、生产链路、真人游戏和真实回滚分别报告。
- 765/765、769/769、后续 777 等只属于各历史版本，不能累计或当成当前版本结果；当前最新记录为 785/785。
- 不因用户问状态或文档整理重新全量构建、测试和部署。
- 故障回归优先 fake、替换接口、回环和独立端口，避免真实输入。
- 失败尝试与修正后重测都保留，不能只报成功摘要。
- 用户观察过构建/测试负载期间输入更容易卡，不等于正常游戏卡顿根因已经确认；避免无意义并行重负载，不擅自改进程优先级。
- PowerShell 5.1 与 7 的脚本、JSON 日期、编码及 stderr 行为有差别；按实际独立环境核对。
- 进程、端口、CIM 或计划任务只读查询若受限，先找安全替代并说明限制，不因补证据提权或改 ACL。
- 每轮 Git commit/push 单独授权；不要 reset、checkout 回退、改历史或 git clean，不为了“干净工作树”清掉真实工作。
- 不自动压缩、去重、迁移、删除归档、holds、失败记录、源码导出或恢复包。
- 没有新授权，不修改 DeepSeek Harness、电脑操作插件或 C:\leftpad。

## 14. 新会话的推荐阅读顺序

1. 本文件的第 1～4 节，确认操作条件与授权边界。
2. 第 7～8 节及最新交付报告，避免重做三项已经完成的修复。
3. 根据用户当轮要求读取当前 AGENTS、架构与相关契约，不为补上下文扫描全部历史大文件。
4. 涉及旧证据时先查归档索引，找新路径，不推断旧目录丢失就是证据被删除。
5. 用户没有给出具体新实施任务时，停留在讨论、分析或提示词职责内。

核心资料：

| 文件 | 用途 |
|---|---|
| [原用户约定](C:/rightpad/docs/USER_PREFERENCES_AND_WORKFLOW.md) | 长期条件与完整历史；个别状态滞后，结合本文件更新 |
| [AGENTS](C:/rightpad/AGENTS.md) | 当前工程约束；顶部覆盖说明优先于被替代旧段 |
| [架构](C:/rightpad/docs/ARCHITECTURE.md) | 模块职责及历史演进 |
| [Motion](C:/rightpad/docs/MOTION_ENGINE.md) | M-R1/C-Z1 与固定算法 |
| [Receiver 本地 M/C](C:/rightpad/docs/RECEIVER_TOUCH_MODE.md) | 模式选择权威 |
| [Ring 菜单](C:/rightpad/docs/MODE_SHORTCUT_MENU.md) | 当前菜单交互 |
| [Mode 接触协议](C:/rightpad/docs/MODE_CONTACT_PROTOCOL.md) | 与普通 Touch 取消分开的 Mode 生命周期 |
| [普通 Touch 取消](C:/rightpad/docs/TOUCH_CANCELLATION_PROTOCOL.md) | 最新 type9/RPTC |
| [触觉反馈](C:/rightpad/docs/HAPTIC_FEEDBACK_PROTOCOL.md) | 普通点击确认与去重 |
| [B/X](C:/rightpad/docs/SCREEN_CONTROLS.md) | 已接受的控件行为 |
| [远程布局协议](C:/rightpad/docs/REMOTE_LAYOUT_PROTOCOL.md) | 草稿/试用/保留/恢复 |
| [软件绘制真人反馈](C:/rightpad/docs/MODE_MENU_CURSOR_SOFTWARE_FOLLOWUP_20261022.md) | 用户“明显改善”及证据边界 |
| [最新可靠性交付](C:/rightpad/docs/INPUT_RELIABILITY_DELIVERY_20261023.md) | 本轮工程状态、版本和恢复证明 |
| [Mode 鼠标键交付（已部署 P2）](C:/rightpad/docs/MODE_MOUSE_BUTTONS_DELIVERY_20261023.md) | 鼠标键契约、验证数字、候选/备份、保护与部署（§7B） |
| [开发产物流程](C:/rightpad/docs/DEVELOPMENT_ARTIFACTS.md) | 当前构建/测试/备份入口 |
| [归档统一索引](C:/rightpad-artifacts/test-results/20261023-122435-history-archive-batch5-consolidated/archive-index.md) | 五批迁移及新旧路径映射 |

## 15. 可直接贴给新对话的开场文字

> 请先阅读我提供的《rightpad 新对话完整交接》。它记录我的真实操作条件、协作方式和截至 2026-10-23 的项目状态，不是实施所有建议的授权。我左手不能用，没有实体鼠标键盘，只用一个手指操作手机；rightpad 是唯一控制设备。本会话继续做需求讨论、方案分析、写开发提示词与只读复核。说“写提示词”时只写提示词，每次同时告诉我建议用什么执行模式和推理强度，实际开发交给我另行授权的会话。三项输入可靠性修复已完成两端部署并只读复核，真人操作和游戏验收仍待我自己确认；不要重复开发。新产物默认外置、异常 JSON 处理和五批归档均已完成，没有新的清理授权。换电脑/重装/换手机目前只讨论到迁移准备，尚未授权制作迁移包。现在先建立上下文，不修改程序、配置或运行环境，之后按我的具体要求继续。

## 16. 后续维护约定

- 本文件是项目内固定的交接入口；后续在用户授权的文档维护或任务交付范围内更新，不必为每轮重新建立一个主入口文件。
- 更新整理日期；长期偏好、当前实现、历史记录、待验收和建议分开维护。
- 完成的新任务以实际交付、源码或证据为依据同步；未实施的建议不能升级成已完成事实或自动授权。
- 运行快照必须注明核对时间，及时替换当前版本与证据入口；历史 PID、哈希和测试数不充当下一轮默认值。
- 用户的真人反馈按具体版本和场景记录；不能拿自动化通过覆盖未验收项。
- 已完成任务从待办中移出，避免新对话重复开发；尚未授权的候选仍保留授权边界。
- 历史交付报告、失败证据和归档索引保留原意；本文件只汇总当前理解，不倒改历史成功与恢复证明。
- 项目工程规则以当前 AGENTS 和相关任务契约为准，用户最新明确要求优先；本交接文件不自行扩大实施范围。
- 原用户约定文档保留长期背景并链接到这里；当前工作区日期版是 2026-10-23 的快照，后续以项目内本文件为持续维护版本。

---

整理说明：本文件已按用户要求放入项目 docs，作为可持续更新的交接文档；原用户约定文档仅新增入口，历史正文保留。此次放置仅涉及这两份 Markdown 文档，未改动程序、配置、设备、运行实例或 Git。后续使用时优先遵守用户新的明确要求。
