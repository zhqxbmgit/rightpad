# rightpad Project State

Updated: 2026-09-13

## 1. Current Phase

当前阶段：

Normal-click phone haptics implemented; automated device acceptance passed
(2026-09-13). Windows remains the sole gesture authority. Independent
[Haptic Feedback v1](HAPTIC_FEEDBACK_PROTOCOL.md), Windows → Android UDP 50002,
echoes the qualifying UP run/session/sequence only after normal LEFT DOWN succeeds
and release is scheduled. A bounded asynchronous sender cannot block mouse output.
Android validates the active Receiver/run and recent raw UP identity, consumes it
once, and calls `performHapticFeedback(CONFIRM)` on the UI thread. No gesture
recognition, VIBRATE permission, user haptic setting or motion/timing changes.

Validation: Windows Debug and Release each **187/187 tests**, both builds with
0 warnings/errors. Android: 16 encoder checks, 4 Sender groups, 36 UI checks,
166 discovery checks, 85 new haptic codec/gate/raw-Sender checks and an additional
real UDP listener lifecycle suite passed. `assembleDebug` and `lintDebug` passed;
lint remains 0 errors / 6 existing warnings. Tests cover malformed packets,
unsigned identity, duplicate/reordered/stale feedback, normal versus drag paths,
queue cancellation, failed DOWN, stalled/full/error sender isolation, thread and
socket cleanup. All previous assertions retained.

Runtime recovery: old Receiver PID 16228 stopped and UDP 50000 release verified;
final Release independently launched as PID **20648**, RuntimeRunId **1**, current
user `Z88888888\zhqqq`, explorer/interactive Session **2**, Medium, Default desktop.
UDP 50000/50001 belong to this PID and backend remains **libvirtualhid**. Existing
Android heartbeat reconnected after Windows restart. APK overwrite/restart then
changed sender run from `15FC36861E8991B5` to `6C81FB2DE1AD6748`, admitted by that
same Receiver; first new DOWN established sequence **0**. Disconnected/Connected,
new input and preserved cumulative stats were verified with no Receiver restart.
Android PID 30720 is resumed; OFFER source `192.168.1.11` selected automatically;
50002 listener active on phone `192.168.1.9` (observed LAN addresses, not defaults).
WPF header reads Connected. Settings file SHA-256 stayed unchanged.

Eleven automated inert-window Android → Virtual HID Raw Input scenarios passed:
single click = 1 haptic; three separated normal clicks = 3; MOVE and long hold = 0;
NoMoveDrag, moving drag, >3 s stationary drag, expired second-contact negative,
rearm, three-drag chain and expired-rearm negative each = only the first ordinary
click's 1 haptic. Exact button order, applicable sensitivity-scaled relative motion,
held-state semantics and neutral release passed. These 11 scenarios produced
11 normal-click haptics total. Actual phone Logcat correlated each feedback and
reported `performed=true`; Android vibration-service history recorded completed
CONFIRM requests (device-mapped CLICK effect), without claiming subjective feel
or a latency benchmark.

Real pause/resume released 50002 while background, disconnected/reconnected the
unchanged Receiver, retained senderRunId after a fresh OFFER, and rejected a
deliberately injected stale feedback without vibration. A fresh resumed click
again produced one native DOWN/UP and one haptic. No Android sender/listener error
or queue overflow; one intentional stale-packet rejection is expected. Final
Receiver snapshot: Runtime Running/Connected, LastError null, mouse failures 0,
gap/old/invalid 0; duplicate Touch copies filtered as expected. Temporary witness
task removed. Evidence remains ignored under `windows/test-results/haptic/`,
`haptic-*.log`, and `receiver-runtime/20260913-194654-684-32836/`.

Remaining acceptance for this feature: the user's real-finger assessment of
CONFIRM strength, brevity and click correspondence. Automated tests do not claim
subjective haptic quality. Broader existing project pending work below is unchanged.

Double Tap Drag is implemented and accepted (2026-09-12). Single Tap remains
immediate; a valid first UP arms one second DOWN within 130 ms, using Android
event timestamps. Each matching Drag UP now rearms from its own Android
`TouchSample.TimestampNs`, matching Moonlight `TrackpadContext` and allowing a
rapid direct DOWN to continue a drag chain without another Single Tap. Settings
range is 50–1000 ms. No Android or RAW motion change. Debug and Release builds:
0 warnings / 0 errors; full Windows tests: 182/182 in both configurations.
The scoped Discovery receive-loop fix recovers only non-cancelled ConnectionReset;
real dead-requester recovery and non-reset Shutdown propagation both pass.

Drag-UP rearm production verification passed on the fresh Receiver. The native
automatic Android-to-Virtual-HID run observed two- and three-drag chains with
76 ms and 69/75 ms rearm gaps, exact held motion/button order, a 306 ms negative,
and final neutral. Human verification then recorded 282 contacts, 95 Drag Starts
and 95 Drag Ends. Of 95 post-drag recontact opportunities, 35 directly re-entered
Drag at 25.760–126.367 ms (median 66.834 ms), including a longest chain of nine;
60 missed at 134.517 ms or later. Native Raw Input ended at 138 DOWN / 138 UP,
`RawHeld=false` and `LEFT=false`. The user reported no perceptible difference in
feel, so rearm is not currently shown to explain Moonlight's subjective advantage.
No 130 ms tuning, Android change or unbuffered-dispatch work was added.

Five automated Android → UDP → production Virtual HID checks passed: single tap,
double tap, drag, >3-second stationary drag and expired-window negative. Native
Raw Input verified button order, movement while held, exact sensitivity-scaled
relative movement and neutral at every end. Human A–E completed with normal feel:
C matched 127.069 ms and held LEFT through 276 moves; D matched 68.710 ms and held
for about 5.34 s; E at 269.900 ms produced 273 moves with LEFT released. B's
184.994 ms interval produced two ordinary clicks (full Windows double-click sequence),
not a drag-window match. Some human attempts exceeded 130 ms; the default remains
130 pending more evidence, with no automatic tuning. Human stationary touch had
small sample changes; strict >3-second no-MOVE behavior is proven separately by
controlled deterministic, real-loopback and native automatic tests.

Latest verified Release was promoted with matching artifact hashes. Old PID 28696
exited and UDP 50000/50001 were free before the first fresh launch. During the later
human flow that task-owned process exited and an Explorer-launched Receiver PID 19828
appeared; final recovery explicitly closed that verified same-path process, rechecked
both ports free, and started independent-task PID 18964, which owns both ports. It is
the current user, Session 1, Medium integrity, Default desktop and libvirtualhid.
Android was not built, installed or restarted by Codex; its PID changed externally
from initial 8156 to final 8764, remained foreground and naturally connected. Existing
user sensitivity 6/6 survived; Tap settings remain 300/8/25
and Double Tap Interval 130. Final recorded diagnostics: invalid/gap/old/output
failures all zero; no LastError. Duplicate control packets are still rejected by
the unchanged gate. Temporary logs and test tools remain ignored, outside Git.

Receiver Discovery v1 implements zero-operation trusted-LAN target selection for
one Android phone used with two Windows PCs in separate locations. UDP 50001 is
independent of unchanged Touch v2 UDP 50000; OFFER source IPv4 is authoritative,
with no fixed IP, host chooser or first-interface heuristic. Target changes clear
capture/queue and rotate senderRunId with Touch sequence zero. Specification and
validation contract: [DISCOVERY_PROTOCOL.md](DISCOVERY_PROTOCOL.md).
Single-site acceptance passed: Debug/Release each 145 Windows tests; Android
encoder/sender/UI plus 166 discovery checks, build and lint (0 errors). Automatic
source-IP discovery, pause/resume, Power reopen, Wi-Fi off/on and four final RAW/
Single Tap smoke rounds passed on one unchanged Receiver instance. IPv6-only
link updates no longer reset the IPv4 target. Remaining discovery acceptance:
the second physical PC at the other location.

Protocol v2 — senderRunId, foreground heartbeat and explicit connection state.
WPF Receiver UI v1 is the committed/accepted foundation (`358a2729`).
Protocol v2 connection behavior and RAW/Single Tap regression have completed
human acceptance.

Virtual HID Mouse POC passed with libvirtualhid commit
`6fdb8bd4de3b68d96c30e5303ac2ebb333c09746`, driver `2026.905.2300.20` and an
active lifetime license. A Medium-integrity Receiver produced Move and Single Tap
in a High-integrity foreground through a Raw Input-visible rightpad-owned mouse,
without SendInput fallback or an obvious subjective regression. Production now
uses libvirtualhid Virtual HID Mouse, from the single `MouseBackendDefaults.Production`
definition. Human A/B found no obvious feel degradation; Raw Input visibility,
Medium Receiver → High foreground success, the measured SendInput limitation there,
and POC/build/runtime plus Driver/Broker/Lifetime license validation support adoption.
SendInput is retained only as an explicit development/diagnostic compatibility
override, never an automatic fallback. No objective latency benchmark is complete;
no lower latency or higher polling rate is claimed.

Start with Windows is implemented in the WPF Overview Receiver card. It uses the
current-user `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value
`rightpad Receiver`, with the quoted current executable path and Registry as the
UI source of truth. A GUI-only named Mutex prevents a login-started Receiver and
a later manual launch from competing for UDP 50000. Human Windows reboot and
sign-in acceptance passed.

The WPF Receiver now has the frozen minimal tray behavior: the title-bar X hides
only MainWindow, leaving the ReceiverRuntime, UDP 50000, Protocol v2 presence,
RAW/Single Tap and the GUI mutex alive. Tray Open/double-click restores and
activates the window; tray Exit is the sole explicit application exit path and
performs runtime cleanup, final settings flush, tray disposal and shutdown.
Minimize remains the standard taskbar minimize, and login startup still shows
the window normally.

Intermittent Connected-but-input-inactive root cause remains unconfirmed. A
diagnostic-only Flight Recorder now keeps low-frequency, size-bounded history of
the Receiver's UDP → acceptance → session → motion → SendInput chain. It does not
reset, retry, reconnect, restart or otherwise change input behavior. After a
future incident, the user may recover control minutes later and report the
approximate wall-clock time; Codex must first freeze the rolling evidence with
`windows/tools/FreezeRightpadFlightRecorder.ps1` before considering active probes.

Real CASE 5 was captured on 2026-09-10 (user-reported onset approximately
11:38–11:39, exact onset uncertain): Touch/Accepted/Motion/SendInput continued,
SendInputFailures stayed zero, and the user confirmed a visible, immobile cursor.
Existing Windows history did not identify a root cause. The next diagnostic step
adds read-only cursor, clip rectangle, virtual screen, cached foreground identity,
input desktop and console session observations on the existing 1 Hz snapshot task,
plus four intended relative movement counters at the SendInput boundary. No bug
fix attempted. Root cause remains unconfirmed. No changes to RAW values, protocol,
gesture behavior or Android. 1 Hz cannot rule out subsecond cursor repositioning;
sampled movement cannot establish its source. See windows/README.md for fields and
interpretation limits. Release build passed with 0 warnings/errors and all 133
Windows tests passed. Section 19 replacement on 2026-09-10 verified old PID 12504
exited and UDP 50000 was released, then launched fresh PID 17536 through the
independent task as the current user, Session 2, Medium, Default. The unchanged
Android Sender naturally restored Connected. Controlled RAW/tap smoke observed
actual cursor movement and exactly one LEFT DOWN/UP; the user also reported normal
RAW feel with no perceived regression. Baseline evidence is retained under ignored
windows/test-results/cursor-witness-20260910/.
The 4m46s baseline contained 287 continuous snapshots, successful cursor/clip/desktop
reads, Default input desktop, matching console/Receiver Session 2, full-screen clip
bounds and zero SendInput failures. Over 192s, whole-Receiver CPU averaged 0.138% of
one logical core; Flight Recorder growth was approximately 1.66 KB/s. This is a
normal-use baseline, not a throughput ceiling or a root-cause finding.

Start with Windows automated verification (2026-09-09): Release build passed with
0 warnings/errors and all 110 Windows tests passed. Real WPF Enable/Disable/Enable
confirmed the exact HKCU command, deletion and final On state. The final fresh
Receiver PID 9088 runs as the current user in explorer Session 1, Medium integrity,
Default desktop and owns `0.0.0.0:50000`; foreground Android heartbeat restored
Connected without restarting Android. GUI Stop/Start, 200% DPI minimum layout,
bounded second-instance exit, RAW movement and Single Tap native smoke passed.
Windows login startup then passed three real reboot validations. During the first,
one transient input-loss episode was observed while WPF still showed Connected;
input later recovered without restarting Receiver, ReceiverRuntime or Android.
Two subsequent reboot validations completed normally. The root cause remains
unconfirmed, so no speculative workaround or production-code change was added;
re-investigate only if the issue recurs with a preservable failure scene.

Protocol v2 automated/device verification (2026-09-09):

- Windows Release build: 0 warnings / 0 errors; 100/100 tests passed.
- Android assembleDebug/lintDebug passed: lint 0 errors, 7 existing-scope warnings;
  testDebugUnitTest has NO-SOURCE. Explicit JDK tests passed 16 encoder checks and
  4 Sender/schedule/socket groups, including high-bit runId and >300 MOVE packets
  with timely heartbeat. No extra production thread/socket/setting/permission.
- Foreground idle: 605.6 seconds, 577 UI observations, all Connected. Heartbeats
  58 → 1265 (+1207), Touch Samples 0 Hz, Gap/Old/Invalid 0 throughout; no new
  Presence Timeout (1 → 1). The earlier timeout was initial device screen-off
  before the controlled foreground interval; waking resumed the same Sender.
- Home/background → Disconnected; resume → Connected using the same Sender
  `9A5068F45BF0D03C`, Touch sequence continued from 27 to 28.
- Force-stop/reopen → new Sender `438C2DBE6A585FC6`; first DOWN sequence 0 accepted.
  WPF PID 26036 and Receiver Runtime RunId 1 stayed unchanged across all checks.
- Active DOWN then background: session became None on presence expiry; resume
  and old Android MOVE/UP produced no new Touch end. New DOWN and native input
  worked. Receiver-side orphan MOVE/UP/residual/gesture cleanup is also tested
  deterministically, independently of Android capture cleanup.
- Four GUI/real-device smoke rounds observed native movement and exactly one
  injected LEFT DOWN/LEFT UP per tap. Each swipe produced +70 RAW X counts;
  cumulative clicks 4. Two swipes injected during Android Activity entry had
  nonconstant raw Y in Android logs, producing +45/+36 Y counts respectively;
  these were raw source coordinates, not an inherited Receiver position jump.
- Overview/Diagnostics inspected at minimum 860×600 DIP, 200% DPI; Last Seen and
  added counters fit. Runtime settings remain RAW 7/7, Single Tap 300/8/25.
- Independent launcher identity: current user, explorer SessionId 1, Medium
  integrity, Default desktop; UDP 50000 owned by the same PID. Android remains
  resumed/awake with no Sender error or queue overflow in the v2 run logs.
- Evidence is local under ignored windows/test-results/: gui/v2-idle.csv,
  gui/v2-lifecycle.log, v2-windows-tests.log and receiver-runtime/20260909-134949-940-25320.
  No commit/push at verification time. Human acceptance subsequently confirmed:
  Connected/Disconnected lamp, foreground/background transition, Sender restart
  recovery without Windows restart, RAW feel, Single Tap, and no recovery jump.
  Original v1 text verified unchanged.

目前完整链路已经实现：

```text
Android real touch
→ MotionEvent historical/current samples
→ Protocol v2 UDP (run identity + presence)
→ Windows Receiver
→ Touch session processing
→ RAW relative delta
→ fixed sensitivity
→ fractional accumulator
→ libvirtualhid Virtual HID Mouse
```

自动链路已经通过。

当前最新阶段已经包含：单进程 WPF Receiver UI v1 + RAW Mouse + Single Tap。

WPF v1：Release build / 79 项自动测试通过；真实 GUI 参数持久化、运行中倍率更新、GUI Start/Stop、Android swipe/tap 与 SendInput 已验证。第一版正式 UI 的视觉与输入体验验收已通过。详见 RECEIVER_UI.md。

RAW Mouse 与 Single Tap 的真人验证均已通过。

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

- Game-first / necessary-complexity / monetary-cost-not-primary engineering
  principle frozen in AGENTS.md.
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
- Protocol v2 encoder, runtime senderRunId, foreground heartbeat
- UDP Sender

Foreground keep-screen-on: Implemented。

Android minSdk：34。

原因：正式输入使用 `MotionEvent.getEventTimeNanos()` 和 `getHistoricalEventTimeNanos()`，不维护 eventTimeMs 兼容路径。

Xiaomi 14 / Android 16 已完成真机验证。

Android UI v1 completed：immersive fullscreen、真实电量显示和 Power
clean-exit control 已在 Xiaomi 14 验证；除 Power 保留圆形区域外，整个界面仍是
touch surface，Settings 仍仅为视觉 affordance。Android 边缘手势仍可临时显示
transient system bars；Android → Windows E2E 已通过。

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

## 7. Protocol v2 — Current; v1 retained as history

- Transport: UDP
- Port: 50000
- Version: 2 only; production rejects v1
- Events: DOWN = 1, MOVE = 2, UP = 3, HEARTBEAT = 4
- Byte order: Little Endian
- Touch header: `uint8 version`, `uint8 packetType`, `uint64 senderRunId`, `uint16 sampleCount`, `uint32 sessionId`, `uint32 sequence`
- Touch header size: 20 bytes; run/count/session/sequence offsets 2/10/12/16
- Sample: `uint64 timestampNs`, `float32 x`, `float32 y`
- Sample size: 16 bytes
- Touch packet size: `20 + sampleCount * 16`
- HEARTBEAT: exactly 10 bytes, version/type/runId only; one copy every 500 ms foreground
- Presence: valid current heartbeat or accepted Touch; local monotonic timeout 2000 ms
- New Sender creation gets random uint64 runId and sequence 0; pause/resume retains both
- New-run HEARTBEAT/DOWN resets input baseline, preserving WPF/Runtime/socket/settings/counters
- Runtime-local retired ID set prevents old runs from switching back
- DOWN: 1 sample
- MOVE: 1 or more samples
- UP: 1 sample

Protocol currently has no CANCEL event.

DOWN / UP are transmitted three times using the same logical sequence and identical datagram. MOVE is sent once.

No ACK / reliable UDP / FEC / retransmission protocol.

---

## 8. Network Status — Simple transport retained

Transport architecture remains intentionally simple; Protocol v2 presence/run
identity added for explicit connection state and sender-restart recovery.
This does not reopen network optimization. v1 measurements below remain historical
evidence; the full v1 layout is preserved in INPUT_PROTOCOL.md.

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

因此：**TRANSPORT OPTIMIZATION REMAINS OUT OF SCOPE.**

除非未来实测出现具体问题，不继续优化网络层。

---

## 9. Windows Receiver Status

技术栈：C#、.NET 8、`net8.0-windows`。

已实现：

- UDP receive
- Protocol v2 decode, sender-run admission and presence cleanup
- malformed packet rejection
- sequence statistics
- raw sample diagnostics
- TouchSessionProcessor
- RawMotionProcessor
- Fractional accumulator
- libvirtualhid Virtual HID relative mouse backend; explicit SendInput dev override
- Single Tap recognition and locally timed left click

当前 production/default backend：libvirtualhid Virtual HID Mouse。
Program 显式传 backend 给 ReceiverRuntime；Runtime 没有隐式默认值。
开发 launcher 无 override 时不传 backend 参数；HKCU startup 仍只有 quoted EXE。
初始化失败进入 Runtime Error / LastError / diagnostics，无自动 SendInput fallback。

---

## 10. RAW Mouse Baseline

RAW pipeline：

```text
Accepted sample
→ absolute position difference
→ fixed sensitivity
→ fractional accumulator
→ libvirtualhid Virtual HID Mouse
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

WPF Motion / Tap 页面支持实时调参，并自动保存至 %LocalAppData%\rightpad\settings.json。详见 RECEIVER_UI.md。

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

### Independent Receiver Development Runtime

Receiver development runtime: Task Scheduler interactive user launch。

Status: Independent launcher adopted。

正式开发入口为 `windows/tools/RightpadReceiverTask.ps1`，任务名为
`Rightpad Receiver Dev`。使用当前用户 InteractiveToken、Limited / LUA、与
explorer 相同的交互 Session、Medium integrity 和 Default desktop；无触发器、
无自动重启。运行 Release WPF GUI，传入明确的 `--dev-log-dir`；日志保留在 ignored
`windows/test-results/receiver-runtime/`。持久 Receiver 不再作为 Codex shell 子进程启动。

该脚本是供 Codex 自动测试和开发阶段使用的 development-only launcher，不是最终产品
UI，也不是 Windows Service。最终用户 GUI Receiver 完成后，日常使用不依赖此
Task Scheduler launcher。

Reason: Codex Start-Process runtime was experimentally confirmed to inherit a
KILL_ON_JOB_CLOSE Job。

- High confidence: old launch method had Codex Job lifetime dependency。
- Medium confidence: this dependency caused the previously observed Receiver
  disappearance。缺少旧 PID 的精确退出时间、exit code 和直接终止证据，不能写成
  历史故障已 100% 证明；当前也没有证据证明虚拟网卡是根因。

上述独立 launcher 修复发生在 v1 阶段，当时没有修改 Protocol、Motion、Gesture 和
diagnostic-only timeout。当前 v2 继续保留相同独立进程启动规则。

产品的登录启动与此开发入口分离：WPF 的 Start with Windows toggle 只管理当前用户
HKCU Run value，不调用 PowerShell、不创建或修改 `Rightpad Receiver Dev` task。

### Android Redeploy Lifecycle

当前已验证的开发环境恢复流程：

```text
Android APK reinstall / app restart
→ keep current v2 WPF Receiver PID / Runtime RunId
→ observe Disconnected / Connected and new senderRunId / sequence 0 acceptance
→ verify UDP 50000 and end-to-end input
```

Android Sender 的运行时状态随 App 进程重建；新 senderRunId 使现有 Receiver 自动
清理旧输入并重建 sequence baseline。不能重启 WPF 来掩盖 Sender restart。
onPause/onResume 保持 Sender 和 sequence，仅停/启 heartbeat。仅 Windows 本身需
启动或更新时使用独立交互 launcher，持续进程不得依赖 Codex shell Job 生命周期。

---

## 14. Timeout Status

当前 Receiver：`input_timeout = 2 seconds`。

当前行为：diagnostic only。

它不会清除：

- active TouchSession
- previous position
- fractional residual

原因：正常手指静止按住超过 2 秒可能没有任何 MotionEvent。

Touch silence 与 presence timeout 独立。前台无 Touch 时 heartbeat 继续证明在线；
当前 run 连续 2000 ms 无 heartbeat 或 accepted Touch 才一次性 Disconnected。
这时清 session/previous/residual/gesture/pending click/held button，保留 current run 和
sequence。恢复后的旧 MOVE/UP 不输出；新 DOWN 正常。Single Tap 仍由本地 timer
释放；CancelPendingAndRelease 处理 run change/断线清理与 timer 竞态，失败为 Error。

---

## 15. Gesture Status

Single Tap: Implemented — Human validation passed。

Double Tap Drag: Implemented — automated and human A–E validation passed (2026-09-12)。

第一次有效 Tap 仍在 UP 立即 Click，不增加 130 ms 等待；只记录一次 Double Tap
资格。第二次 DOWN 依据 Android TouchSample.TimestampNs 判断非负且 <= 130 ms
的间隔，立即 held LEFT。两次落点距离不受限制；拖拽 contact 不受 300 ms/8 px
约束，RAW motion 未改动。Drag UP release 后以该 UP 的 Android TimestampNs
重新 arm；130 ms 内直接重新落指可连续 Drag，无 MOVE 的 Drag 也相同。过期的
second contact 仍可成为普通 Single Tap。Reset、sender change、presence timeout、Stop、Dispose
和 output failure 清资格并配合原有按钮清理；普通 input timeout 不释放静止拖拽。

附带范围例外：Discovery receive loop 仅恢复未停止时的 SocketError.ConnectionReset
(Windows UDP 10054，向已关闭回复端口发送 OFFER 后的 ICMP)。每次运行最多记录
一次 diagnostic，原 socket 继续服务后续客户端；其他 socket 错误仍传播。协议、
Android、端口、广播和选择逻辑未改动。

当前 GestureProcessor 与 RAW Motion 独立消费 accepted raw packets。每个 MOVE
sample 都检查相对 DOWN 的 X/Y 独立阈值（各自 <= 8 px）；任一 sample 越界后永久
取消本次 Tap candidate。匹配 UP 还需在阈值内且 eventTimeNs 时长 <= 300 ms。
微小 RAW 位移照常输出。LEFT DOWN 后由一次性 .NET timer 在约 25 ms 后 LEFT UP，
不阻塞 UDP；重叠点击依次完成各自 hold。Mouse output 失败会记录并停止 Receiver，
清理时 best-effort LEFT UP；强制终止或持续注入失败无法保证释放。

已确定需求及状态：

- Single Tap → Left Click：已实现
- Double Tap Drag → second DOWN immediately holds left mouse button → normal Motion Engine movement → UP releases button：已实现

Windows Receiver 参数：

```text
tapMaxDurationMs = 300
tapMovementThresholdPx = 8
doubleTapIntervalMs = 130
clickHoldMs = 25
```

上述默认值已从 C:\zhq 的 PreferenceConfiguration.java / TrackpadContext.java
只读核实；movement 使用 X/Y 独立判断，不是 Euclidean distance。
`doubleTapIntervalMs = 130` 已实现，整数范围 50–1000 ms。Tap 页面可热更新；旧 settings.json 缺少字段时静默默认 130，下次保存保留已有设置并写入新字段。
当前 CLI：`--tap-max-duration-ms`、`--tap-movement-threshold-px`、`--click-hold-ms`、`--double-tap-interval-ms`。旧 `--double-tap-interval` 仍拒绝。

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
- WPF Receiver UI v1 implementation — single-process GUI, automatic Start, runtime settings, persistence and diagnostics; accepted as the first formal UI baseline
- Start with Windows implementation — current-user HKCU Run toggle with Registry source-of-truth state and GUI single-instance guard; real Windows reboot/login acceptance passed
- Minimal Receiver tray lifecycle — X hides without stopping input, Open restores, and tray Exit performs the existing orderly shutdown
- Virtual HID Mouse POC — libvirtualhid backend, Raw Input identity, lifecycle,
  Android E2E and Medium Receiver → High foreground human acceptance passed

---

## 19. Current Pending Work

Immediate:

1. Validate automatic discovery on the second physical PC at its separate location
2. Run the formal SendInput vs Virtual HID A/B measurement when separately requested
3. Run the diagnostic-only Flight Recorder with cursor/input-environment witnesses during normal use; freeze the next real CASE 5 before probing
4. Compare RAW directly against Moonlight Noir if further motion evaluation is needed
5. Continue monitoring Single Tap feel and accidental clicks during normal use

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
- Double Tap Drag: completed; see current validation above
- game profiles
- network optimization
- generic reconnect / handshake frameworks
- simultaneous multi-PC selection UI (separate-location automatic discovery implemented)
- virtual gamepad buttons

These are deferred, not forgotten.

---

## 21. Next Decision Point

The production mouse backend decision is now libvirtualhid Virtual HID Mouse.
Further controlled SendInput vs Virtual HID measurements of broad game compatibility,
latency, jitter and stability remain separate work when requested; they are not
claims established by this adoption. Keep the explicit development override for
those comparisons and diagnostics, with no automatic fallback.

Motion-filter selection remains separate and evidence-driven. Do not implement a
complicated filter before RAW measurements demonstrate a specific need.

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
