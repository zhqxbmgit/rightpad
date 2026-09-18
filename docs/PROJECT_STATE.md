# rightpad Project State

Updated: 2026-09-18

## 1. Current Phase

当前阶段：

**Live Sensitivity and safe Runtime Restart implemented and verified (2026-09-18,
uncommitted).** Successful Save publishes an atomic X/Y pair used per real input
sample before target accumulation. Draft/failed Save and output ticks cannot
change gain; old targets, pending and history remain intact. Tau/Support remain
frozen per run. Motion displays actual Active and committed Saved values, with
a guarded async Restart Receiver action for a mismatch. Restart joins cleanup,
tries the saved target twice, then restores the previous active Motion config
with current committed sensitivity; recovery never rolls back persisted settings.

Debug and isolated Release: 448/448 tests passed, zero warnings/errors. Nine new
tests cover live gain, realized-history preservation, atomic pair publication,
Save failure, UDP input, lifecycle guards, retry and recovery. Existing 24/120,
Q0-C, Earned-Settle, 1000 Hz and 12 ms regressions remain passing.
Real device automation produced 90/30/90 output counts from repeated 10 px
Android swipes at sensitivity 9/3/9, within WPF PID 408 and Runtime run 1.
One Android tap per Restart applied 24/120 -> 18/90 -> 24/120, runs 1 -> 2 -> 3,
with the same WPF PID, natural heartbeat reconnection, movement, click and Android
haptic performed=true. Final settings are 9/9 and 24/120, Connected, libvirtualhid,
LastError null and mouseOutputFailures=0. This is automated real-device evidence,
not a new human game-feel assessment. Evidence: ignored
`windows/test-results/live-sensitivity-restart-20260918/`.

**Production Motion algorithm is fixed; Tau/Support are configurable per Receiver
run (2026-09-17, uncommitted).** Ordinary GUI and launcher startup construct the
1000 Hz / 1 ms finite-critical Earned-Settle Q0-C path with fixed 12 ms playout,
live committed sensitivity, libvirtualhid and MotionTrace off by default. Product
defaults remain the validated tau 24 ms / support 120 ms baseline. The Motion page
edits integer Tau 8..60 ms in 1 ms steps and Support 40..300 ms in 5 ms steps.
They are independent settings; there is no automatic ratio or `support=tau*5`
rule. Product cadence and playout are not configurable or persisted.

Each ordinary production Start snapshots the committed settings and constructs
one immutable run configuration. Saving new values does not hot-switch the active
kernel; Stop/Start applies them to the next run. The 1000 Hz tick path does not
read settings or resize/rebuild the kernel. MotionTrace metadata uses the same
frozen Tau/Support. Explicit `--dev-motion-mode` remains authoritative and keeps
each development mode's original fixed parameters, including explicit production-
named K24-r5 mode selection.

`RuntimeSettings` now has eight product fields, adding `smoothingTauMs` and
`smoothingSupportMs`. Older six-field JSON loads 24/120 without a migration file;
invalid Tau or Support falls back independently. `motionCadenceHz` remains absent.
An older settings file containing either 250 or 1000 is accepted without warning;
the unknown field cannot affect startup and is omitted by the next normal settings
save. Explicit `--dev-motion-mode` remains authoritative. Fixed 250 Hz / 4 ms and
500 Hz / 2 ms K24-r5 Earned-Settle modes remain for development, regression,
benchmarking and diagnostics, alongside explicit development 1000 Hz.

The former 250/1000 product-selector implementation and its live-switch evidence
remain historical experiment context under ignored test results; there is no
product cadence switch lifecycle now. General Receiver Stop/Start, Android
reconnection, Virtual HID cleanup and diagnostics lifecycles remain unchanged.
Trace-off Flight Recorder snapshots continue to expose the actual run mode and
Motion clock/missed-tick counters for bounded verification.

Motion research state (2026-09-15, uncommitted): K24/K35 now use canonical
exact-state Q0-C; B/F retain legacy Q0-I. The original K24 permanent endpoint
counterexample is PASS_EXACT with Q0-C. New numerical certification v3 and
independent K24/K35 native qualification passed for the instant-flush modes.
**Finite-critical Earned-Settle production algorithm selected**: the product path
uses `RESAMPLED_1000HZ_FINITE_CRITICAL_K24_R5_SETTLE` as its retained algorithm-
family identity, Q0-C, 1000Hz/1ms/12ms, live committed sensitivity, trace OFF by
default and libvirtualhid. Tau24/Support120 is the validated default baseline;
ordinary product runs may use other saved legal Tau/Support combinations.
Fixed 250Hz/4ms and 500Hz/2ms remain available only through explicit development
mode selection.
No mode switches frequency or parameters automatically. UP freezes the
earned target and the run-fixed kernel continues until its configured Support is
settled; a new DOWN during
settlement continues the same cumulative ledger without flushing old backlog.
Raw-UP click/drag/button timing stays independent of Motion settlement.

Cadence Debug/Release builds and all 423 tests passed. The same Android workload
ran after independent 250/500/1000 Receiver restarts. Constant-motion interiors
had 0 skipped/catch-up ticks in all three modes; the complete 1000Hz trace had
one correctly skipped opportunity among 12017 opportunities, with no replay.
Endpoints and gesture/button release checks passed. Final trace-OFF 1000Hz PID
31524 passed a separate minimal movement/click/drag smoke and is left for human
game testing, Running/Connected, LastError null. Automatic input is stopped.
Evidence: `windows/test-results/k24-settle-cadence-ab-20260915/`.

The earlier measured medium/fast delivery of about 125 nonzero reports/s at every
internal cadence used a background Raw Input observer. N60 control testing found
about 1000 Hz foreground dispatch, about 125 Hz background dispatch, and about
1000 Hz again after foreground restoration. The old result therefore cannot
represent foreground game-style Raw Input delivery and does not establish either
a global Windows 125 Hz limit or a specific responsible kernel component.

Formal foreground central 10-second windows measured Raw/nonzero and independent
dispatch rates of approximately 238.5/238.5 Hz at 250 mode, 466.2/466.2 Hz at
500 mode, and 883.5/882.5 Hz at 1000 mode. Corresponding actual MotionClock rates
were approximately 250.0/499.6/996.1 Hz. Q0-C zero-count logical ticks and very
few skipped opportunities account for the main remaining gap from nominal clock;
1000 mode is not claimed to emit 1000 nonzero events, eliminate latency, or be
universally best.

Matched replay used the same accepted real Android traces in both arms: 18/18
paired final integer dx/dy results were identical and all 36/36 Earned-Settle arms
completed. Formal cost windows measured higher active-motion CPU, allocation and
context-switch cost at 1000 Hz, with a very low missed-opportunity ratio, no
catch-up replay, no mouse-output failure, and no runtime error or disconnect.
These results remain objective historical evidence for selecting production
1000 Hz; they do not establish that 1000 Hz is universally best.

The preceding 250Hz lifecycle prototype passed Debug/Release and all 406 tests.
Its bounded Android-to-libvirtualhid
Raw Input control/settle comparison preserved 90/900/0-count endpoints for a
single short swipe, ten same-direction swipes and ten alternating swipes.
Single tap, double-tap drag and re-arm passed with released buttons. The final
independent trace-OFF Receiver was Running/Connected, LastError null, with final
movement/click/drag smoke passed and automatic input stopped. Evidence is under
`windows/test-results/k24-earned-settle-20260915/`. This is not a new numerical
certification or a claim of improved human game feel. The existing instant-flush
K24 remains a development control. The later production status above
supersedes this prototype-era classification.
See `docs/MOTION_ENGINE.md` sections 17–18 for settlement, button and cadence semantics, and
section 17 below for the preceding Q0-C qualification evidence.

The following completed-feature records retain their original validation dates,
test counts and runtime identities; they are not the current Motion qualification.

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

- continuous camera smoothness and low wobble
- stable velocity and natural, gimbal-like motion
- precise micro-control and long-session comfort
- fixed, deterministic and predictable feel
- measured fast-turn, reversal, stop/settling and latency behavior
- no obvious inertia
- no dynamic feel changes

Rightpad is game-first, but it is not esports-latency-first. Absolute minimum
latency is not the leading Motion objective. Fixed response cost may be accepted
when objective measurements and human game testing show a meaningful visual and
control-quality gain; no universal acceptable millisecond threshold is currently
established.

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

Historical RAW baseline sensitivity：

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

旧 Moonlight Noir 只作为 reference、known-smoother baseline 和 design evidence。
它不是 Rightpad 的 target、upper bound、gold standard、required architecture
或 required parameter set。当前主观比较中 Moonlight 比 B 更丝滑，但 Moonlight
自身也不是最终满意方案；这不能写成 Rightpad 已经超过 Moonlight。

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

这些特征证明更强的 trajectory shaping 可能改善视觉丝滑度，但不能推导出
second-order follower、约 35 ms time constant、caps 或 glide 必须进入 Rightpad。
Glide 仍违反当前 Relative Mouse / no artificial inertia 约束。

Rightpad 的目标不是简单复制或匹配 Moonlight，而是在自身 Fixed Feel、Relative
Mouse、视觉稳定、微操和自然控制要求下找到最佳 Motion 系统，并在可行时争取超过
当前 B 与当前 Moonlight reference。当前选定产品 Motion 是固定 1000 Hz K24-r5
Q0-C Earned-Settle；未来替代候选仍必须以证据评估。

---

## 17. Motion Engine Candidate Status

RAW → B 真人游戏 A/B 已完成，B 明显更好，因此已有真人验证的研究 baseline 是 B：
`RESAMPLED_250HZ`，固定 250 Hz / 4 ms 输出机会、固定 12 ms playout、
timestamp-aware reconstruction，后级保留 production Q0-I。
This is historical comparison context, not the current launch contract. Ordinary
GUI launch and the launcher without an explicit development Motion option always
use fixed 1000 Hz K24-r5 Earned-Settle. The product UI has no cadence selector.
Legacy `motionCadenceHz` is ignored and removed on the next settings save. Fixed
250/500 Hz and other research controls require explicit `--dev-motion-mode`, which
remains authoritative for development runs.

B 的已验证主观优点：

- smoother movement
- better micro-control
- better fast turn and reversal
- clean stop
- subjectively unnoticeable UP

B 的剩余问题是 sustained movement 仍有明显 wobble。在两台 Windows PC 上的
主观手感基本一致，因此该波浪不太像单一 PC 特有问题。当前 Moonlight reference
主观更丝滑，但同样不是最终满意方案。

F4/F8 已实现为固定 4/8 ms causal boxcar position-average research prototypes，
不是最终产品选择。K24-r5 / K35-r4 已实现为 fixed causal finite-support,
normalized critical-damping-shaped position convolution：

| Explicit research mode | tau | T |
|---|---:|---:|
| `RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5` | 24 ms | 120 ms |
| `RESAMPLED_250HZ_FINITE_CRITICAL_K35_R4` | 35 ms | 140 ms |

K 的共同配置为固定 250 Hz、4 ms output opportunity、12 ms playout；完整有限
支撑、DOWN 前 zero history、run 固定参数，no prediction / glide / adaptation。
2026-09-15：K family 已从 Q0-I prototype 升级为 Q0-C，权威状态为当前
binary64 cumulative P 与已成功提交的 Int64 cumulative I，逐轴 exact truncate(P-I)。
normal tick 与 UP 使用同一合同；B/F legacy Q0-I、kernel、tau/T、cadence、
playout 与 sensitivity mapping 未改变。GUI、startup、trace、diagnostics 明示
quantizer=Q0C/Q0I。当前产品状态仅包括 1000 Hz K24-r5 Earned-Settle；
250/500 Hz SETTLE 及其它 K/B/F 模式仍是显式 development/research controls。

新 `rightpad.k-q0c-oracle/3.0.0`：K24/K35 冻结 corpus 各 319 PASS_EXACT，
加本轮 micro/chatter、boundary 与原 native failure，共 **726 PASS_EXACT、
0 PASS_BOUNDARY_PAIRED、0 FAIL、0 INCONCLUSIVE**；Debug/Release rebuild
均 0 warning/error，tests 各 **391/391 PASS**。原 failure 的 normal tick 输出
缺失的 +1，held/UP 与追加 1000 个恒零机会保持 I=0，无 terminal special case。

B control → K24 → K35 依次独立 Stop/Start native 验证通过。K24/K35 首项
left_right gate 的 normal/held/UP 与 Raw Input endpoint 均为 (0,0)；完整
qualification 各 39 contacts，无 endpoint/post-fence 失败，含 6 个大积压
立即 UP 的 drag contacts，motion flush/button release 顺序正确、click haptic
确认通过。trace ON 两个 K 均无 missed/skipped/catch-up。当前留下 K35/Q0C
trace OFF 供真人测试；Android 原 MainActivity/PID 保持前台，未重启。
UP backlog 风险仍在：本轮运动 combined pending 的 p95/max 约为 K24
614/651、K35 814/856 counts，必须由真人游戏评估控制感；不代表 kernel 已改善。
详细研究、数值合同与 native 报告保留在 ignored
`windows/test-results/motion-k-q0c-20260915/` 与
`windows/test-results/motion-k-q0c-certification-20260915/`；未 commit/push。

### Historical Q0-I prototype qualification (2026-09-14)

Q0 Mathematical Oracle v2 对当时冻结 corpus 的认证结果：K24 与 K35 **各自
319/319 PASS_EXACT、0 FAIL、0 INCONCLUSIVE**。该认证只覆盖冻结输入，
不证明任意未来输入；不能替代 native qualification 或真人游戏验证。

当日真实 Android → UDP → Receiver → libvirtualhid → Raw Input 的 K24
`left_right` contact 中，连续有限核最终 P exact 回到 0，但 production Q0-I
的 incremental residual roundoff 使最后 `total = 0x1.fffffffffffffp-1`，
未输出最后 +1 count。managed 与 Raw Input 最终均为 **(-1,0)**，held 和 UP
后仍永久不汇合。same-P actual production replay 100% 复现（0 mismatch），
故该反例不是 HID 丢报。**K24 = FAIL_ENDPOINT**。

由于该反例暴露 shared production Q0-I endpoint issue，K35 后续 native
qualification 被阻断：**NOT_QUALIFIED / NOT_RUN_SHARED_Q0_GATE_BLOCKED**。
K35 未执行该轮 native qualification，不能描述为当时 K35 已实测失败或 ready。
自动 unit/regression tests Debug/Release 各 379/379 通过，均 0 failed；
但当时 K24 native qualification endpoint 失败，不能泛称所有 correctness gates 通过。

该反例促成本轮 K-only canonical exact-state quantization；当时 Q0-C 尚未
实现，K 真人游戏 A/B 被阻断。2026-09-15 的新资格结论见上，不覆盖历史失败证据。
原始认证与 native 反例分别保留在 ignored
`windows/test-results/motion-q0-gate-v2-20260914/` 和
`windows/test-results/motion-finite-kernel-native-20260914/`，本 checkpoint 不提交这些证据。

后续仍以动作标签明确的真人轨迹和固定候选比较评估平滑质量。Latency、reversal
和 stop 不作为最高排序优先级，但必须完整测量、报告并由真人游戏验证。

不要提前认定任何滤波器为最终方案。

算法选择必须基于：

- B 与固定候选的真人游戏体验
- real touch datasets
- continuous smoothness / wobble attenuation
- velocity stability
- micro-control and long-session comfort
- stop response
- reverse response
- path and UP behavior
- added fixed latency

Fixed Feel 完全保持：更强平滑只能来自一个明确选择的固定配置，不能根据速度、
噪声、采样率、网络、FPS、游戏或系统负载动态改变 tau、window、gain、delay 或模式。
固定 filter settling 可以完成已经积累的真实位移；glide 或旧速度产生的人工距离
仍然禁止。

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
4. Run human game A/B of qualified K24/K35 Q0-C against B, emphasizing continuous
   stability, micro-control, reversal/stop and immediate-UP backlog; decide on
   promotion/commit only after human results, preserving B/F legacy Q0-I
5. Continue monitoring Single Tap feel and accidental clicks during normal use

Evaluate:

- stationary jitter
- slow micro movement
- medium constant-speed stability
- fast movement responsiveness
- sudden stop
- sudden reverse
- packet batching / pulse feeling

Use offline evidence and controlled human game A/B before selecting or promoting
any new Motion algorithm. Do not choose by lowest latency or resemblance to
Moonlight alone.

---

## 20. Explicitly Deferred

Do not implement yet:

- Alternative Motion-filter research beyond the selected production 1000 Hz K24-r5 configuration
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

Future Motion changes remain evidence-driven. Do not replace the selected fixed
1000 Hz K24-r5 product configuration with a complicated filter without a measured
need and an explicit fixed candidate. Rank candidates by continuous smoothness,
wobble and velocity stability, micro-control, natural feel and comfort together
with measured path, UP, reversal, stop/settling and latency costs.

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
