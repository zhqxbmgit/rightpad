# rightpad Project State

Updated: 2026-09-08

## 1. Current Phase

当前阶段：

RAW Mouse Baseline — Human Feel Validation

目前完整链路已经实现：

```text
Android real touch
→ MotionEvent historical/current samples
→ Protocol v1 UDP
→ Windows Receiver
→ Touch session processing
→ RAW relative delta
→ fixed sensitivity
→ fractional accumulator
→ SendInput
```

自动链路已经通过。

当前正在等待/进行的核心验证是：真人手指实际控制 Windows 鼠标后的 RAW 手感评价。

不要把当前 RAW baseline 描述成最终 Motion Engine。

---

## 2. Product Constraints

当前硬约束：

- Android → Windows 11 only
- Single finger only
- Portrait phone
- Phone used blindly as dedicated touch surface
- Relative mouse semantics
- No right click
- No scroll
- No multi-touch
- No gyroscope
- No accelerometer
- Single tap required
- Double-tap drag required
- Future virtual gamepad buttons are possible but not current scope

输入目标：

- smooth
- stable
- predictable
- low latency
- no obvious inertia
- no dynamic feel changes

---

## 3. Engineering Constraints

已冻结原则：

- Avoid over-engineering
- Complexity must buy measurable value
- No speculative future architecture
- Measure before optimizing
- Android remains mainly a raw input sensor
- Windows Receiver owns tuning and input behavior
- User-adjustable parameters should live on Windows
- No hidden acceleration or adaptive behavior

---

## 4. Security Scope

Security is intentionally out of scope.

项目条件：

- personal use
- one user
- one Android phone
- one Windows 11 PC
- trusted LAN

明确不实现：

- authentication
- pairing
- PIN
- encryption
- TLS / DTLS
- tokens
- certificates
- signatures / HMAC
- anti-replay
- multi-user permissions
- security handshake

仍保留 malformed packet validation 和输入 fail-safe，因为它们属于 reliability，不属于 security。

---

## 5. Android Input Status

已完成：

- Java / native Android Activity + View prototype
- ACTION_DOWN / MOVE / UP / CANCEL local handling
- Historical MotionEvent samples
- Current MotionEvent samples
- Real eventTimeNs
- Touch session tracking
- Logcat diagnostics
- CSV Touch Dataset Recorder
- Protocol v1 encoder
- UDP Sender

Android minSdk：34。

原因：正式输入使用 `MotionEvent.getEventTimeNanos()` 和 `getHistoricalEventTimeNanos()`，不维护 eventTimeMs 兼容路径。

Xiaomi 14 / Android 16 已完成真机验证。

---

## 6. Real Touch Measurements

Xiaomi 14 实测：

- 典型正 sample interval：约 4.21 ms
- 对应基础节奏：约 237–238 samples/s
- 真实活跃移动有效 sample rate：约 203–236 Hz
- 多组测试合计约：222 Hz
- 典型 MOVE packet rate：约 100–115 Hz
- 典型：2 samples per MOVE packet

大部分 MOVE 包结构：historical sample + current sample。

Historical sample extraction 已证明是必要基础能力，不能删除。

---

## 7. Protocol v1 — Frozen

- Transport: UDP
- Port: 50000
- Version: 1
- Events: DOWN = 1, MOVE = 2, UP = 3
- Byte order: Little Endian
- Header: `uint8 version`, `uint8 eventType`, `uint16 sampleCount`, `uint32 sessionId`, `uint32 sequence`
- Header size: 12 bytes
- Sample: `uint64 timestampNs`, `float32 x`, `float32 y`
- Sample size: 16 bytes
- Packet size: `12 + sampleCount * 16`
- DOWN: 1 sample
- MOVE: 1 or more samples
- UP: 1 sample

Protocol currently has no CANCEL event.

DOWN / UP are transmitted three times using the same logical sequence and identical datagram. MOVE is sent once.

No ACK / reliable UDP / FEC / retransmission protocol.

---

## 8. Network Status — Frozen

真实 Android → 5 GHz Wi-Fi → Windows 测试结果：

- no sequence gaps
- no old packets
- no invalid packets
- no sender queue overflow
- no sender errors
- Android samples and Windows decoded samples matched exactly

真实测量结论：当前链路已经足够稳定。

当前没有证据支持：

- jitter buffer
- interpolation
- fixed-rate network scheduler
- network prediction
- FEC
- retransmission
- clock synchronization

因此：**NETWORK LAYER IS FROZEN.**

除非未来实测出现具体问题，不继续优化网络层。

---

## 9. Windows Receiver Status

技术栈：C#、.NET 8、`net8.0-windows`。

已实现：

- UDP receive
- Protocol v1 decode
- malformed packet rejection
- sequence statistics
- raw sample diagnostics
- TouchSessionProcessor
- RawMotionProcessor
- Fractional accumulator
- SendInput relative mouse backend

当前 Prototype backend：SendInput。

Virtual HID：not implemented。只有真实游戏兼容性证明需要时才考虑。

---

## 10. RAW Mouse Baseline

RAW pipeline：

```text
Accepted sample
→ absolute position difference
→ fixed sensitivity
→ fractional accumulator
→ SendInput
```

没有：

- FIR
- second-order smoothing
- acceleration
- prediction
- interpolation
- resampling
- fixed-rate output ticker

同一个 MOVE packet 中的多个 sample 按原始顺序逐 sample 立即输出，不会合并。

Fractional residual：

- X/Y independent
- truncate toward zero
- reset on new DOWN
- remaining sub-count residual discarded after session ends

---

## 11. Current Sensitivity

RAW default：

```text
sensitivityX = 7.0
sensitivityY = 7.0
```

来源：以前实际长期使用的 Moonlight Noir 默认有效线性倍率约为 7x，因此 7.0 / 7.0 是当前更合理的 RAW baseline。

仍可通过启动参数 `--sensitivity-x`、`--sensitivity-y` 覆盖。

当前没有 settings UI 或配置文件。

---

## 12. Windows Mouse Environment

当前真人 RAW baseline 测试环境：

- Windows mouse sensitivity: 10 / 20
- Enhance Pointer Precision / Windows mouse acceleration: OFF

不要由 rightpad 自动修改这些 Windows 设置。

---

## 13. SendInput Execution Constraint

重要开发环境事实：Codex 沙箱/受限上下文中直接运行 Receiver 时，SendInput 可能失败：

```text
inserted = 0
Win32 error = 5
Access Denied
```

这不是 rightpad 输入链错误。

真实 SendInput / 真人鼠标测试时，Receiver 必须运行在当前登录 Windows 用户的交互桌面 session 中。

已验证可工作的上下文：

- same Windows user as explorer.exe
- same active SessionId
- Medium integrity

Codex 可以自动构建、测试、管理进程，但最终 Receiver 必须从可注入当前交互桌面的上下文运行。

---

## 14. Timeout Status

当前 Receiver：`input_timeout = 2 seconds`。

当前行为：diagnostic only。

它不会清除：

- active TouchSession
- previous position
- fractional residual

原因：正常手指静止按住超过 2 秒可能没有任何 MotionEvent。

目前无法仅凭 packet absence 区分：

- normal stationary touch
- disconnected sender

当前不要增加 heartbeat。真正需要按钮 fail-safe 前再重新处理该问题。

---

## 15. Gesture Status

Gesture Engine 尚未实现。

未来已确定需求：

- Single Tap → Left Click
- Double Tap Drag → second DOWN immediately holds left mouse button → normal Motion Engine movement → UP releases button

Windows Receiver 参数：

```text
tapMaxDurationMs = 300
doubleTapIntervalMs = 130
clickHoldMs = 25
```

Advanced: `tapMovementThreshold`。

Gesture processing must not alter motion feel.

---

## 16. Moonlight Reference

旧 Moonlight Noir 只作为参考实现。

已知特点：

- fixed-rate output
- second-order critically damped follower
- approximately 35 ms time constant in old setup
- glide / inertia
- velocity and acceleration caps
- historical sample handling
- fractional motion preservation

值得保留的思想：

- historical samples
- fractional motion
- deterministic processing
- possible value of stable output timing

明确不直接继承：

- glide
- noticeable inertia
- large tracking lag
- velocity / acceleration caps
- unnecessary streaming architecture

最终 Motion Engine 尚未选择。

---

## 17. Motion Engine Candidate Status

当前正式 baseline：Mode 0 — RAW。

尚未实现、只作为候选：

- Mode 1 — Short FIR
- Mode 2 — Second-order critically damped follower without glide / velocity cap / acceleration cap

不要提前认定任何滤波器为最终方案。

算法选择必须基于：

- RAW 真人手感
- real touch datasets
- stop response
- reverse response
- speed stability
- micro movement
- subjective gaming feel

---

## 18. Completed Phases

Completed:

- Phase 0 — Project requirements / architecture / docs
- Phase 1 — Android Touch Capture Prototype
- Phase 1.5 — Touch Dataset Recorder
- Phase 2 — Windows Raw UDP Receiver
- Phase 3 — Android Protocol v1 UDP Sender + real LAN E2E
- Phase 3.5 — Real Finger Transport Characterization
- Phase 4 implementation — RAW Mouse Baseline

---

## 19. Current Pending Work

Immediate:

1. Verify actual cursor movement in interactive Windows desktop
2. Human RAW touch feel test at sensitivity 7 / 7
3. Compare RAW directly against Moonlight Noir

Evaluate:

- stationary jitter
- slow micro movement
- medium constant-speed stability
- fast movement responsiveness
- sudden stop
- sudden reverse
- packet batching / pulse feeling

Only after RAW human feedback: decide whether Motion Laboratory / filter implementation is required.

---

## 20. Explicitly Deferred

Do not implement yet:

- Motion filter
- Gesture Engine
- left-click / double-tap drag
- Virtual HID
- driver
- game profiles
- settings UI
- network optimization
- heartbeat
- device discovery
- virtual gamepad buttons

These are deferred, not forgotten.

---

## 21. Next Decision Point

The next architectural decision depends on RAW human testing.

Possible result A: RAW is already sufficiently good. Then only minimal conditioning should be considered.

Possible result B: RAW shows clear speed ripple, jitter, batching/pulse feeling, or instability. Then create a controlled Motion Laboratory and compare:

```text
RAW
vs
Short FIR
vs
modified second-order model
```

Do not implement a complicated filter before this evidence exists.

## Maintenance Rule

Update PROJECT_STATE.md only when:

- a development phase completes
- a major design decision is frozen
- a previously frozen decision changes
- a major measurement changes engineering direction
- the current next step changes

Do not update it for:

- small bug fixes
- minor refactors
- routine commits
- temporary experiments

AGENTS.md remains higher priority than PROJECT_STATE.md.

If PROJECT_STATE.md conflicts with a design document, do not silently resolve it. Report the inconsistency.
