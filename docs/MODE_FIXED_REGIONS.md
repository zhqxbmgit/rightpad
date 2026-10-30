# Mode 向右固定三区域手势绑定

契约日期：2026-10-29；本地档位反馈修正：2026-10-30。独立第三用途，不自动启用，三个入口默认未绑定。原单键普通／Toggle、电脑快捷菜单 Ring 六槽、静态 M、stable ID `rightpad.input.mode` 和布局持久化格式保留。

## 使用与固定几何

在 Receiver 的 Controls → Mode · 用途选择“手势绑定”，打开“配置手势绑定 · 固定三区域预览”，配置三个入口和两个独立宽度。必要时返回 Controls，通过现有大布局编辑器调整 Mode，最后点击主窗口底部统一 Save。完成配置只返回，不独立保存。全部选择和宽度编辑可用左键完成。

三个入口均支持未绑定、目录中的一个键盘键、一个 Xbox360 按钮、鼠标左／右／中键。只有二档额外支持“打开／关闭电脑快捷菜单”；直接复用现有 Ring 和六槽设置，提供原六槽编辑器入口。没有 Toggle、保持、连点、宏、组合键、长按计时动作或自动升档。

设实际 Mode 矩形为 `(L,T,W,H)`，density 为 `d`，两个独立宽度为 `a,b` dp：

| 区域 | 水平范围 | 垂直范围 |
|---|---|---|
| 原地 | `[L,L+W)` | `[T,T+H)` |
| 一档 | `[L+W,L+W+a*d)` | `[T,T+H)` |
| 二档 | `[L+W+a*d,L+W+a*d+b*d)` | `[T,T+H)` |

统一左／上包含、右／下不包含。两条内边界分别归一档、二档，没有重叠或缝隙。同一坐标不随进入方向改变分类；不使用相对 DOWN 位移、角度、轨迹投影、回差、边界余量、吸附或延迟确认。原地不同落指位置到同一档位的所需距离允许不同。

初始开发值为 **8 / 10 dp**，各自 **1..50 dp**，步长 **0.5 dp**；必须有限、正数、在范围内且是半 dp 步长。二档不要求比一档宽。大号减／加按钮只编辑草稿。初始值和新反馈未经真人验收，也不是写入正式设置的授权。

移动 Mode 使三个区域整体跟随；改变宽度使一档从新的右边缘开始，二档紧接一档；改变高度使三个区域同步改变高度。档位宽度只按 density 换算，不随 Mode 比例缩放。不自动移动 Mode、裁切区域或改 B/X、触控板及原布局。

## 接触、回撤与输出

只有新的 DOWN 位于原 Mode 矩形，并且配置、实际布局和身份完整同步，才接纳可能产生动作的接触。延伸区域不是按钮：直接在那里的触控板或 B/X 落指仍走原路由。Mode 接纳后持续占有接触，滑入其他控件不转交，不产生 Touch 移动／点击／拖动或 B/X 动作。

Android 只识别区域和预选，Receiver 拥有绑定和输出。DOWN、MOVE、停住或 400 ms 均不发键。只有正常 UP 提交一个最终动作：

- 从未进入档位、一直处于原地：提交原地。
- 一档／二档之间预选当前档位；二档退回一档，UP 提交一档，不锁最高档。
- 进入过档位后回原地：准备取消，UP 不补发原地；尚未 UP 可再次进入档位。
- 向左、上／下或越过二档最右侧：永久失去本次选择资格，返回区域也不恢复。
- MOVE 和 UP 历史样本按原顺序处理，UP 最终坐标做最后判定。没有 MOVE 也能由 UP 选档。`entered` 和永久 `invalid` 事实不被末点掩盖。
- CANCEL、pointer 异常、前后台、编辑器、连接、布局、用途和其他安全边界取消，不执行选择。

一个接触最多一次提交。未绑定、无效、取消、输出失败不回退到其他入口；重传、ACK、重同步和输出完成都不提交动作。复用原 Mode 输出与固定 25 ms 最小可见脉冲；调度允许正向超时。前一个释放未完成时，后一个 DOWN 不被确认覆盖，通过原有有界转换重传等待，不建立动作队列。安全释放绕过普通保持时间。

键盘仍是原独立 HID 键盘；鼠标仍由 MouseButtonMerge 合并 Gesture／Mode；手柄仍由原路径合并 Screen Controls／Mode（按钮 OR、扳机 max）。Mode 只撤销自己的贡献，不为制造 click 临时释放其他来源的同键。错误进入原门闩和清理，不重放、不无限重试、不更换后端。

二档菜单动作只在正常 UP 打开原 Ring，六槽来自本接触完整旧绑定快照。Mode 接触和迟到消息不进入 Touch，不能成为选槽点击。已打开菜单时，新 Mode DOWN 仍手动关闭；关闭接触的 UP 完整消费，不补发原地或档位。Ring 原 Motion 路径、系统光标、模态消费、槽位脉冲、确认、自动关闭和安全规则保留。

## Save、一致性和布局边界

用途、三个绑定、宽度和六槽只编辑草稿。统一 Save 验证完整配置及当前手机几何，原子写盘成功后才 Publish。失败不发布、不推进可用 Mode revision、不下发新可用行为；异步写盘期间几何失效则恢复原正式磁盘设置并不 Publish，恢复失败显示真实错误。

同一手势用途中正常接触的成功 Save：Android 保留 DOWN 时的区域；Receiver 保留已接纳接触的不可变 ModeBinding/revision，包括菜单所用六槽；UP 必须携带同一 revision。下一 DOWN 取最新完整配置。仅保留当前配置和一个已接纳接触快照，不无限保存历史；尚未接纳的过时 DOWN 安全失效，不在恢复后补做。

用途切换、已打开 Ring 的配置改变及安全生命周期仍释放、关闭并换 generation。Mode-only Save 不推进 RPCT 的 B/X epoch/revision，不改变在途 B/X DOWN 快照。

生产几何来自 TouchCaptureView 的实际 `getWidth()/getHeight()`、density、同一 `controls.rects()` Mode 矩形和 display-cutout 安全边界。应用为 edge-to-edge，瞬态系统栏覆盖 View，不把截图、系统外框或 dp 外框当作 MotionEvent 坐标。Receiver 使用该 View 宽高比等比例预览三色区域和两条内边界，显示尺寸、density、Mode 数值和布局身份。

完整区域必须位于 `[safeLeft,safeRight) × [safeTop,safeBottom)`；错误列出超出的左／上／右／下侧。不缩小区域、不平移 Mode、不改用户布局。手机几何未知时明确不可用，禁止启用新用途的 Save。未启用时，新区域放不下不影响旧用途。

现有远程编辑器在试用前验证已提交宽度和草稿 Mode；Android 实际应用前再次校验，等待试用期间配置变化也重新校验。手机原有本地编辑器 Save 同样拒绝无效几何。原 RPLY 和布局持久化格式不变。

View 大小／insets、Mode 移动／缩放、布局试用、保留、取消／恢复均取消在途 Mode，推进单调布局 identity，通过 generation/Idle 同步等待新的 DOWN。同一接触中不移动边界，旧 UP 不产生新动作。

## 本地反馈

接纳新 DOWN 后一次 PRESS 保留 ScreenControlFeedback／B STRONG_ONE_SHOT：API 26+ 10 ms / 255，旧分支 legacy 10 ms，生产 minSdk 34 不变。

档位也复用该策略：每个 MOVE 开始时记录有效区域，仍按原顺序处理全部历史样本与最终坐标，再比较最终区域。最终为与事件前不同的一档／二档时，尽力请求一次短振。原地→一档、原地→二档、一档→二档、二档→一档均提示，后续再次切换和回原地后的重入也可提示。同档移动不重复，档位→原地不提示；不再使用整次接触每档一次的资格标记。

一个 MOVE 最多一次请求：原地经过一档最终二档只提示一次；一档经二档最终仍一档不提示；最终原地不提示。任何历史或最终样本导致永久无效，整个事件不提示，返回也不恢复。反馈合并不跳过样本，不丢失 entered／permanent-invalid 事实，不建立历史振动队列。UP 历史和最终坐标仍判定动作，但始终不触发或补排档位振动；没有 MOVE 的 UP 同样不新增档位反馈。

取消、无效、生命周期、重传、ACK、同步和输出完成不新增提示。后端缺失／异常由原策略隔离，无重试、补振、声音或新设置；不影响识别、传输、输出及释放。反馈仅表示本地接纳／预选，不证明 Receiver 或游戏成功，也不要求数振动次数。B/X、Touchpad CONFIRM、Ring RPHF 和电脑声音移除保持。

## Mode v2 精确协议

原 Sender worker/socket → UDP 50000、原反向 worker/listener → UDP 50002。没有新 socket、网络线程、服务、端口或通用可靠传输框架。type8/RPMD v1 的长度、reserved 和意义不变；新用途拒绝 type8 执行动作。Touch 1..4、GAMEPAD_STATE、RPHF、RPCT B/X 和 RPLY 及各自序号域不改变。本轮之前 type10 未分配。

所有多字节字段 little endian。Android → Receiver **恰好 96 字节**：

| Offset | Bytes | 字段 |
|---:|---:|---|
| 0 / 1 | 各 1 | 外层 version=2 / type=10 |
| 2 / 10 | 各 8 | Sender run / 非零 generation |
| 18 / 22 | 各 4 | 独立 uint32 sequence / contact |
| 26 / 27 | 各 1 | phase 0 Idle、1 DOWN、2 UP、3 CANCEL / 消息 version=2 |
| 28 | 1 | 0 无动作、1 原地、2 一档、3 二档；仅 UP 可非零 |
| 29 | 3 | reserved=0 |
| 32 / 40 | 各 8 | 非零已提交 Mode revision / 非零 Android 布局 identity |
| 48 / 52 | 各 4 | int32 View width / height |
| 56 | 4 | IEEE float32 density |
| 60 / 64 / 68 / 72 | 各 4 | int32 Mode X / Y / width / height，View px |
| 76 | 4 | reserved=0 |
| 80 / 84 / 88 / 92 | 各 4 | int32 safeLeft / safeTop / safeRight / safeBottom |

校验精确长度、版本、phase、选择、reserved、revision、geometry identity、View 20..32768、有限 density `(0,16]`、Mode 最小 20 px 且在 View 内、安全边界在 View 内且面积正。Idle sequence/contact=0，其他 phase sequence 非零；DOWN/UP 的 contact 和严格顺序沿 v1。选择没有 Windows 键值。

Receiver → Android **RPMD v2，恰好 56 字节**：

| Offset | Bytes | 字段 |
|---:|---:|---|
| 0 | 4 | ASCII RPMD |
| 4 / 5 | 各 1 | version=2 / message=1 |
| 6 | 2 | reserved=0 |
| 8 / 16 | 各 8 | destination run / 非零 generation |
| 24 | 4 | acknowledgedSequence |
| 28 / 29 | 各 1 | ready 0/1 / purpose 0 单键、1 Ring、2 手势 |
| 30 | 2 | reserved=0 |
| 32 / 34 | 各 2 | u16 一档／二档宽度，半 dp，2..100 |
| 36 | 4 | reserved=0 |
| 40 / 48 | 各 8 | 非零 Mode revision / Receiver 已观测布局 identity，未知=0 |

ready 表示本代次 Idle 接纳，不证明驱动成功。Android 还要求 geometry identity 与当前布局匹配、合法完整配置、几何可用、当前 discovery source/run/listener 和 600 ms ACK 活性，才允许新 DOWN。

新 Receiver 识别 type10 前同时提供独立 v1/v2 回复；v1 只对旧用途提供可用 ready。旧 Android 严格忽略 56 字节回复，继续原 32 字节用途。新 Android 接受 v2 后，在该 Sender run 内拒绝迟到 v1 降级；先报告几何和 Idle，再等身份匹配。若升级发生于未完成 v1 接触，安全 CANCEL 后同步，不把旧队列改装成新动作。

新 Android 全新启动连接旧 Receiver 可继续旧用途；已经使用 v2 后仅把 Windows 降回旧 Receiver，Mode 保守无输出，恢复匹配双端并重启 Android 后恢复。普通 Touch/B/X 不依赖 Mode 升级。生产更新先 Receiver 后 Android；完整恢复包按匹配旧双端恢复。

原 32 项有界转换队列、20 ms 队头重试、100 ms 刷新、600 ms ACK／lease、priority CANCEL、run/contact/generation、去重和 presence 规则保留。几何身份在 Sender run 内单调增加，旧几何不能回退；旧 revision、未同步和恢复前接触不补执行。

实施与实测见 [本轮交付报告](MODE_FIXED_REGIONS_DELIVERY_20261029.md)。自动化不代表真人盲操作、振动区分或游戏体验验收。
