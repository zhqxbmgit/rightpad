# Mode Contact v1 / Touch v2 type8

当前格式：2026-10-17。绑定与保持状态只由 Windows Receiver 解释。Android 上报
Mode 接触事实，不携带键盘键、手柄键、按压行为或“切换开启”的业务状态。

复用 Android Sender worker/socket → UDP 50000，以及 Windows 既有 reverse worker
→ Android UDP 50002 listener。不新增应用传输端口、socket 或工作线程，不复用保留位。
Touch types1–4、GAMEPAD_STATE type5、RPCT/type6、legacy type7、RPLY/RPHF/RPST
保持各自格式及序号域。Mode 消息不能建立或续期 Touch presence，也不改变 Touch
序号或 Motion 的时间线。只有当前已 Connected 的 Sender run/source 才参与处理。

## Android → Windows：恰好 32 字节

所有多字节整数 little endian。

| Offset | Bytes | 字段 |
|---:|---:|---|
| 0 | 1 | Touch 协议版本 2 |
| 1 | 1 | packetType 8，MODE_CONTACT |
| 2 | 8 | senderRunId |
| 10 | 8 | generation，Receiver 发放的非零失效代次 |
| 18 | 4 | sequence，独立有序转换序号 |
| 22 | 4 | contact，独立接触编号 |
| 26 | 1 | phase：0 Idle、1 DOWN、2 正常 UP、3 CANCEL |
| 27 | 5 | reserved，全部为 0 |

Idle 要求 sequence=0、contact=0；非 Idle 要求 sequence 非零。字段、精确长度、phase
及 reserved 校验完成后才进入状态机。正常 DOWN/UP 还必须满足下面的运行语义。

## Windows → Android：RPMD，恰好 32 字节

| Offset | Bytes | 字段 |
|---:|---:|---|
| 0 | 4 | ASCII RPMD |
| 4 | 1 | version 1 |
| 5 | 1 | message 1，当前代次/确认快照 |
| 6 | 2 | reserved=0 |
| 8 | 8 | destinationSenderRunId |
| 16 | 8 | generation，非零 |
| 24 | 4 | acknowledgedSequence |
| 28 | 1 | ready：0 或 1；Receiver 是否已接收本代次 Idle |
| 29 | 3 | reserved=0 |

这不是触觉消息。HapticFeedbackListener 按独立 magic 分流；先校验精确格式，再在
UI 回调时重新核对当前 source 地址、Sender run 和 listener 身份，最后交给 Sender。
不调用任何触觉 API。

## 新代次与接触

1. Receiver 在 presence 或配置安全边界建立新 generation，序号/接触编号归零，释放。
   当前实现使用进程启动时 UTC ticks 为种子、进程内单调递增的 64 位计数；它不是
   密钥或认证 token。
2. Android 接受更大的 generation 后丢弃旧转换队列、旧接触和可发送状态，先发 Idle。
   较小的 generation 被忽略；同代次确认不得倒退或超过本地已创建的序号。
3. Receiver 接受正确 run/source/generation 的 Idle，置 ready 并回复；Android 看到
   ready 后才允许下一次新的 DOWN。同步前已经发生的接触不自动重放。
4. 首次 DOWN 为 sequence=1、contact=1；每次转换 sequence 加一，每次新 DOWN
   contact 加一。Receiver 只按 acknowledgedSequence+1 应用正常转换。
5. DOWN 要求当前未 touching，contact 恰好是前一次加一。UP 要求当前 touching 且
   contact 与本次相同。错误顺序不会先行解释，等待发送端重传缺失转换。

Receiver 只记录当前代次的最后确认转换、当前 contact、touching 和自身输出状态，
不积累每次接触的历史。完全相同的最后确认报文可续期 Mode lease，不再次输出。
更早的序号、同接触的新 DOWN 序号、错误 contact 不得重新切换。

Android 保留最多 32 个未确认转换，按头部顺序发送。收到确认后移除已确认前缀，
保留最后确认状态作为刷新状态。序号/接触编号接近 uint32 上限或队列满时，转为
CANCEL，等待新代次，不进行回绕重放。

## 重传、活性与失效

- 新事实立即唤醒既有 Sender。未确认队头固定每 20 ms 重试；队列为空时，每 100 ms
  刷新最后确认的 Idle、DOWN 或 UP。正常确认推进可立即调度下一个队头。
- 相同确认不强迫立即再次发送，避免 ACK/刷新忙循环。
- Receiver 在转换、Idle 和必要的乱序响应时发确认，并每 100 ms 提供当前快照。
  复用既有 reverse 有界队列；队列丢弃或网络丢失由后续周期快照和重传恢复。
- Mode lease 固定 600 ms，独立于 Touch presence 2000 ms 和 B/X gamepad lease
  300 ms。相同有效 DOWN 刷新允许静止长按；正常 UP 刷新允许切换保持持续存在。
- Receiver 的现有收包循环约每 10 ms 及每次收包检查 Mode deadline。600 ms 没有
  有效 Mode 活性则释放、清除保持并更换 generation。保留原有鼠标 MotionClock。
- Android 连续 600 ms 没有有效当前确认时停止承认新 DOWN，提交 CANCEL。
  恢复后仍等待新代次同步和新的明确接触。

无法保证任意长断网期间的每个短按都被执行：超时以安全释放、丢弃旧工作为准，
不在恢复后补做游戏动作。

CANCEL 是安全转换：Android 丢弃未确认普通队列，优先发送并重试 CANCEL；
Receiver 在当前 run/source/generation 有效时允许其跨越普通转换序号间隙，直接
释放并更换 generation。因此丢失的旧 DOWN/UP 不阻挡取消，迟到的旧 DOWN 也不能
在取消或超时后复活。旧代次 CANCEL 同样不能取消新代次状态。

Android pause/close/目标切换在既有 worker 上尽力发送最终 CANCEL；即使它丢失，
Mode lease 或 Receiver presence 变化仍负责释放。目标切换创建新的 ModeContactStream
并更换 Sender run；旧路由只允许最终安全消息。Receiver Stop/Dispose 在本地直接释放。

## 绑定变更与输出确认

Mode 配置不加入 RPCT，也不要求 Android 缓存绑定。Receiver 成功 Save 后，只有完整
ModeBinding 改变才释放旧输出并建立新 generation；相同配置及无关 Save 不触发状态变化。
这同时阻止旧绑定下的未确认接触按新绑定解释。具体保存与键盘/手柄归属见
[MODE_CONTROL.md](MODE_CONTROL.md)。

业务转换在后端处理后才正常推进确认。后端失败时释放/关闭故障输出、清除保持并保留
真实错误，用错误门闩和失效代次阻止旧工作复活。正常转换中的输出失败立即切换
generation；维护时延后释放的失败也关闭故障设备并阻止后续按下，租约随后使代次失效。
不把 ACK 当作游戏已处理输入的证明。RPMD ready 只表示
接触流同步完成，不是驱动健康或按键成功标志。界面使用本地 ModeSession 的实际
成功提交状态及错误，不依据网络请求或 ready 宣称按下成功。

## 兼容性

旧 Android 不发送 type8，新 Receiver 的 Mode 留在同步等待且无输出，原鼠标/B/X
照常工作。全新启动的新 Android 遇到旧 Receiver 时得不到 RPMD，不建立 Mode 接触流，
原鼠标/B/X 不依赖此功能。若 Android 已连接过新 Receiver，而只把 Windows 单端换回
旧版，旧代次刷新/CANCEL 可能被旧 Receiver 计为未知类型；它们不会解释成 Touch。
生产部署及本轮恢复包同时配套更新/恢复 Receiver、DLL 与 APK，未验证所有混搭版本。

跨 Receiver 进程的 generation 以主机 UTC ticks 排序。若主机时钟被显著回拨后再重启
Receiver，Android 可能保守拒绝较小的新代次并保持 Mode 无输出；此特殊时钟变更场景
没有做实测。本轮没有引入持久代次、时钟同步或认证系统。

## 验证

Windows ModeBindingTests 覆盖顺序、重复、失效代次、活性、保存、后端失败及合并；
Java ModeContactTests 覆盖队列、ACK、丢包重试、取消、边界和格式；真机测试复用实际
View/Sender 和当前正式矩形。结果与明确未覆盖项见
[MODE_CONTROL_DELIVERY_20261017.md](MODE_CONTROL_DELIVERY_20261017.md)。
