# 普通 Touch 明确取消（2026-10-23）

本轮扩展只处理 Android 明确发生的普通鼠标接触取消，不把正常 UP 全部副本丢失
宣称为已解决。普通 DOWN/MOVE/UP、heartbeat、RPHF、Mode、B/X 的原有字节不变。
取消不是 UP，不生成 tap、菜单动作或 RPHF。没有 MOVE 的静止按住仍合法。

## 消息与身份

Android → Receiver，原 UDP 50000，恰好 22 字节，小端：

| 偏移 | 类型 | 含义 |
|---:|---|---|
| 0 | u8 | version=2 |
| 1 | u8 | type=9 |
| 2 | u64 | senderRunId |
| 10 | u32 | 被取消 sessionId |
| 14 | u32 | 该接触原 DOWN sequence |
| 18 | u32 | 取消边界 sequence，必须大于 DOWN sequence |

边界从原 Touch sequence 域取下一值；不增加正常 Touch 的字段或改变副本规则。
uint32 不环绕，沿用原 run 内递增约束。Receiver 在原串行接收路径处理，只有当前
presence 的 run 与来源 IPv4 相符才接受；该消息不能建立 run 或续期 presence。
这属于关联与防旧输入要求，不增加认证或正常 Touch 来源锁。

Receiver → Android，原共享 UDP 50002，恰好 28 字节：0..3 ASCII `RPTC`，4=1，
5=1（ACK），6..7=0；8=u64 run，16=u32 session，20=u32 DOWN sequence，24=u32
取消 sequence，均小端。Android 同时验证当前监听身份、来源和四项完整待确认身份。
RPHF、RPCT、RPST、RPLY、RPMD 均不能确认取消。

## Receiver 围栏

Receiver 记录最近已接纳 DOWN 的 session、sequence、来源。取消边界若已经被后来的
已接纳包越过，只回复 ACK，绝不释放新接触。未越过边界时，旧 DOWN 身份或同 DOWN
sequence 的错误 session/来源不能清理当前接触；较新的未见 DOWN 可以建立取消围栏，
涵盖 DOWN 本身丢失或取消先到的情形。先推进 sequence 围栏，再清理 Motion、Gesture、
双击资格、排队点击与已保持左键。菜单只清理本次 Touch 的资格，保留打开状态和 Mode
接触。取消不调用普通 UP，不执行槽位、不发送点击确认。重复或延迟取消只确认已完成
或已被新输入越过的边界。后续旧 DOWN/MOVE/UP 不能重新建立已取消接触。

## Android 入口、重试和失败收尾

`stopCapture` 在仍有普通鼠标 pointer 时调用 `cancelTouch`；ACTION_CANCEL、多指／
pointer 不匹配、视图分离、输入模态切换和本地 stopCapture 共享该入口。Sender 的
setForeground(false)、setTarget 和 close 也独立收尾，正确性不依赖 onPause 调用顺序。
正常 UP 后 pointer 已清除，不会触发普通取消；Mode CANCEL 保留独立协议。

当前路由最多一项取消。开始后立即清空旧 Touch 队列并围栏 capture；已经出队的旧包
还会核对 cancelledThrough。原 Sender worker 最多每 100 ms 尝试一次，窗口 1500 ms，
无追赶发送；收到精确 ACK 后解围。等待期间原 heartbeat、B/X safety Neutral 和 Mode
收尾仍可工作。ACK 丢失只重传相同取消，不重放动作。

到期未确认则停止所有旧路由流量，向 UI 提交有身份检查的重同步。UI 在同一目标上换新
sender run、重置队列／顺序并清理反馈、配置、布局事务缓存，然后发送新 run heartbeat。
既有 Receiver run 切换立即清理旧输入；如果 UI 或通信不能恢复，没有旧流量续命，既有
2000 ms presence 超时完成安全清理。暂停、关闭和目标变化不把取消移入新路由；旧路由
至多保留一个最终取消尝试，停止其 presence 流量仍是丢包兜底。已发出的有限 UDP 在网络
中的任意长延迟不构成必达保证；换 run 后旧 run 会被原有退休集合拒绝。

## 首次与周期 heartbeat 失败

首次 heartbeat 未成功前保持原先接入约束，不发送 Touch。若已经出队的包遇到这一条件，
显式记录限频的 admission abort、清理本地待发送队列，并对仍活跃接触建立取消围栏；
不把旧 DOWN/UP 放回队尾或等网络恢复后重放。首次失败的空闲等待服从 heartbeat 的
500 ms 截止时间，取消重试单独有界，避免配置请求的零截止时间造成忙循环。

至少一次 heartbeat 已成功后，周期性 heartbeat 的 IOException 只按限频策略记录；
当前已出队且路由／前台／取消围栏均有效的 Touch 仍执行原 3/1/3 次尝试。Touch 自己
的发送失败仍是 best-effort UDP，不加入无界重试或过期包积压。

## 版本交错

先部署 Receiver，再更新 Android：旧 Android 与新 Receiver 保持原行为。反向组合中，
旧 Receiver 拒绝未知 type9，绝不会当作 UP；新 Android 无 ACK 时使用受限失败重同步，
既有新 run 接纳负责清理，不假设旧端支持 ACK。本轮实际部署采用前一种顺序。
