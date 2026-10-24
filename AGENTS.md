# AGENTS.md

# rightpad Project Instructions

Approved Mode Android-local PRESS feedback exception (2026-10-24): only after
`UdpTouchSender.modeDown()` accepts a new Mode DOWN, Android immediately attempts
one local PRESS through the existing ScreenControlFeedback instance and B's
STRONG_ONE_SHOT policy (API 26+: 10 ms / 255; older policy branch: legacy 10 ms;
production minSdk unchanged). This means phone contact admission, never Receiver
execution success. It applies independently of Receiver binding/use/Toggle state,
including unbound single binding and menu open/manual close. No local feedback is
added for MOVE, hold, UP, CANCEL, pointer faults, lifecycle, retries, ACK or sync;
failure is isolated with no retry. This supersedes only historical statements
prohibiting Mode local haptics. Static M, stable ID/layout, type8/RPMD, Touch/RPHF,
B/X, Touchpad CONFIRM, Ring confirmations, Motion and output devices stay intact.
Computer sound remains removed. See docs/MODE_LOCAL_PRESS_DELIVERY_20261024.md.

Current development artifact paths (2026-10-22): source stays in `C:\rightpad`;
`artifact-root.txt` defines the default `C:\rightpad-artifacts`. Use
`tools/Invoke-Development.ps1` and [docs/DEVELOPMENT_ARTIFACTS.md](docs/DEVELOPMENT_ARTIFACTS.md)
for current build/test/task/candidate/recovery commands. .NET defaults also apply
to direct dotnet commands; Android Windows Wrapper redirects its project cache
and Gradle build directories. Use a fresh task identity for independent rounds.
Formal deployed Receiver stays in its existing source-tree Release directory;
build success is not deployment. New launcher runs use external `receiver-runtime`;
Status resolves a still-live legacy run from its original record/log directory.
Historical task scripts/reports under old test-results remain archives, not current
default entry points. Do not bulk rewrite, move, compress or delete them.
Output relocation never grants deployment, restart, device, cleanup or Git authority;
the user's current task limits override the general deployment rules below.

Approved Mode Ring availability and unbound-confirmation exception (2026-10-22,
fourth round): a valid tap on an **unbound** Ring short-menu slot now submits
exactly one confirmation identical to an ordinary touchpad click through the
existing Haptic Feedback v1 channel (same RPHF bytes, sender run, raw UP identity,
Android CONFIRM and dedupe; no new protocol, field, waveform, service or setting),
then closes the menu. It emits no keyboard key, gamepad button or ordinary mouse
click, does not fabricate a press and does not wait 25 ms; a feedback failure is
logged only and still closes without replay. One open cycle accepts at most one
valid slot selection, counting bound and unbound slots. This supersedes the
unbound part of "center and unbound directions emit nothing" and the third-round
sentence that a confirmation is never emitted for an unbound direction. The
center/neutral zone still emits nothing and stays open. Cursor visibility is no
longer an availability veto: a hidden/suppressed cursor still has a real position,
and a ClipCursor constraint does not make coordinates unreadable, so while the
real GetCursorPos read succeeds the menu must open and keep running. This
supersedes the availability part of "Hidden/restricted/failed cursor or display
changes exit". Real GetCursorPos/GetMonitorInfo/GetDpiForMonitor failures, changed
monitor/DPI, the 2 s presentation timeout, lease/presence/disconnect,
configuration changes, output failures and manual Mode close still close the menu
and fence old input. The read-only adapter uses GetCursorPos only, never calls
SetCursorPos/ShowCursor/ClipCursor/SetCapture and no longer P/Invokes
GetCursorInfo/GetClipCursor. See docs/MODE_SHORTCUT_MENU.md,
docs/HAPTIC_FEEDBACK_PROTOCOL.md and
docs/MODE_MENU_COMPAT_DELIVERY_20261022.md.

Approved Mode motion-route exception (2026-10-20, second round): while the
Receiver-owned Ring menu is open, accepted Touch packets use the existing
Motion/HID mouse movement path exactly once with the selected engine, committed
Sensitivity, reconstruction, quantization and output device. Ordinary
Gesture single-click/double-tap-drag output is withheld before mouse buttons are
produced, so a menu tap executes only its slot binding (one press and release);
center and unbound directions emit nothing. The menu observes
the actual Windows cursor for highlight and taps, keeps the opening anchor fixed,
and no longer submits SetCursorPos or menu-specific displacement
accumulation/compensation. This supersedes the 2026-10-19 sentence prohibiting
normal Motion/HID routing for menu navigation; the rest of that exception (12 DIP
neutral radius, six directions, Ring only, environment faults, output latches,
lifecycle, protocols/devices/Motion parameters/Save unchanged) continues. See
docs/MODE_SHORTCUT_MENU.md and docs/MODE_MOTION_ROUTE_DELIVERY_20261020.md.

Approved Mode slot-completion exception (2026-10-21, third round): a valid tap on
a bound Ring slot completes its one keyboard/gamepad press **and** release, then
submits exactly one phone confirmation through the existing Haptic Feedback v1
channel (same RPHF bytes, sender run, raw UP identity and Android CONFIRM/dedupe;
no new protocol, service, setting, gesture, waveform or Android receiver change)
and only afterwards closes the menu automatically. The confirmation is a
best-effort side effect sent only after both edges completed: it is never emitted
for center, unbound, navigation, Mode-button or manual-close events, it never
waits for or proves phone execution, and its failure cannot replay the action,
block the close or block ordinary input. One open cycle executes at most one slot
action, so repeated, duplicated or late taps cannot enqueue more work. The
previous serial pulse queue and the "slot tap keeps the menu open" rule are
removed; a second Mode DOWN still closes the menu manually. Older slot/UI wording
that says a successful slot action keeps the menu open is superseded. Manual
close, lease and generation rules, bindings, Save, layout, Motion/M/C, B/X,
devices and all wire formats stay unchanged. See docs/MODE_SHORTCUT_MENU.md,
docs/HAPTIC_FEEDBACK_PROTOCOL.md and
docs/MODE_MENU_ACTION_DELIVERY_20261021.md.

Approved Mode system-cursor direction-selection exception (2026-10-19): the
Receiver-owned shortcut menu uses the original Windows cursor via a separate
absolute-position adapter, with actual readback, a fixed opening anchor and
12 DIP neutral radius. The fixed complete Ring uses six equal directions to
select the same ordered slots, including gaps and beyond the decorative outer edge. Do not draw
or accumulate a private operating pointer, warp the cursor on open, or move the anchor to
fit the screen. The 2026-10-20 exception above supersedes only the menu-movement part of
the old normal-Motion/HID prohibition; ordinary Gesture button output stays withheld.
Clip edge decoration and keep a visible selection caption.
Hidden/restricted/failed cursor or display changes exit and fence old input;
explicit fresh Mode contact can retry environment faults after sync. Preserve
output failure latches, original single bindings, settings/layout, B/X and all
existing protocols/devices. No game injection, Hook, persistent cursor constraint,
Android/driver/Broker/license or Git delivery change is authorized. See
docs/MODE_SHORTCUT_MENU.md and docs/MODE_SYSTEM_CURSOR_DELIVERY_20261019.md.

Approved Mode shortcut-menu exception (2026-10-18; appearance scope 2026-10-20):
Receiver may select the existing single-binding use or one six-slot shortcut
menu using only the fixed complete Ring. Old `appearance` value 1 is read as Ring
and normalized in memory; it is not exposed in the settings UI or renderer.
Menu actions are independent
single pulses, never Toggle/hold/macro actions. Reuse type8/RPMD, the original
Android stable ID/static M/formal layout, existing output devices, ports and
workers. Receiver-local modal ownership consumes menu navigation and taps before
normal Motion/Gesture/HID mouse output, cancels old chains and fences exit work.
Keep the original single binding and behavior, default old files to that use and
new slots to unbound. Drafts publish only after common disk-first Save. No Android,
Motion, M/C, accepted B/X parameter, haptic, native ABI, driver/broker/license or
Git delivery change is authorized by this exception. See docs/MODE_SHORTCUT_MENU.md.

Approved Mode binding exception (2026-10-17): the existing `rightpad.input.mode`
screen region now supports one Receiver-owned keyboard/gamepad binding with
Momentary or Toggle behavior. Android reports contact facts through independent
type8/RPMD on the existing workers/ports; Receiver owns binding, held state,
acknowledgements, lease and lifecycle generations. Reuse the original stable ID
and saved layout, default to unbound/Momentary, use the common disk-first Save,
and emit no Mode haptic or mouse Touch input. The additive driver-backed keyboard
extension preserves mouse/gamepad ABI2 and the existing single Xbox360 device.
Receiver-local M/C remains independent; no Motion or accepted B/X parameter
change is authorized by this exception. See docs/MODE_CONTROL.md and
docs/MODE_CONTACT_PROTOCOL.md. This supersedes historical Mode M/C/no-op wording
below while preserving those stages and their evidence.

Current selected baseline (2026-10-03): Touchpad R&D is paused. Android uses
fixed unbuffered acquisition for each admitted single-finger Mouse DOWN and the
normal `rightpad.input.mode` M/C control. The selected deployed Receiver uses
the existing explicit `M_R1` identity: M8 causal reconstruction, direct P(t)=R(t),
Q0-C, 1 ms opportunities, reconstruction-completion release and no glide. Saved
Tau/Support remain inactive for M-R1 shaping; committed Sensitivity stays live.
C remains C-Z1 (C12, tau 35 ms, damping 1, Amax 80000, Vmax 15000, fixed 4 ms,
Java-compatible rounding and true glide). FIX-2 (2026-10-04) selects M-R1 for
normal GUI startup, including bare quoted-EXE HKCU startup and ordinary launcher
Start. Explicit development overrides remain authoritative. `ProductionMode`
retains the historical M-F1 identity for configuration and regression compatibility.
Historical stage restrictions below describe those stages; the user
separately authorized their later deployments and this final source audit,
commit and ordinary fast-forward push. Preserve historical regressions/evidence
and the user's saved settings and layout.

## 1. Project Identity

Project name:

```
rightpad
```

rightpad is a dedicated Android-to-Windows gaming input system.

The purpose is to turn an Android phone into a high-quality touch input device for Windows 11 gaming.

The project is focused on:

- Low latency
- Stable input feeling
- Predictable behavior
- Long-term maintainability

This is NOT:

- A remote desktop application
- A streaming application
- A general office mouse replacement
- A cloud service

---

# 2. Target Platform

## Supported

Android client:

- Touch input device

Windows receiver:

- Windows 11 only

## Not a current goal

Do not add support for:

- macOS
- Linux
- iOS
- Cloud platforms
- Mobile-to-mobile usage

unless explicitly requested.

---

# 3. Core User Requirements

## Touch Operation

The device is operated with:

- One finger only
- Portrait phone orientation
- Phone screen is not viewed during use
- Phone acts as a dedicated touch surface

Do not add:

- Multi-touch
- Two finger gestures
- Three finger gestures
- Pinch gestures
- Gyroscope input
- Accelerometer input

While the rightpad Android Activity is in the foreground, prevent automatic
screen timeout / screen-off. Do not keep the device awake after rightpad leaves
the foreground. Do not use WakeLock or background services unless explicitly
approved.

---

# 4. Input Behavior Requirements

## Mouse Movement

The primary input mode is:

Relative mouse movement.

Expected behavior:

```
Finger moves
        ↓
Mouse moves

Finger stops
        ↓
Mouse stops

Finger lifts
        ↓
Movement stops
```

The system must NOT behave like a joystick.

Do not implement:

- Cursor drifting
- Continuous movement after release
- Auto-centering
- Momentum movement

`Mouse stops` means that the Motion Engine does not create new artificial
movement, drift indefinitely or continue after release as a velocity-controlled
device. A selected fixed deterministic filter may have a short, measurable
settling period in which it finishes displacement already earned by the finger.
That settling must converge to the existing target, remain fixed for the selected
configuration and be validated in real games. It must not integrate old velocity
to invent additional distance.

---

# 5. Motion Feeling Requirements

The target feeling:

- Smooth
- Stable
- Accurate
- Predictable
- Suitable for games

The same physical finger movement should always create the same response.

Avoid hidden behavior changes.

## Motion Quality Priority / Not Esports-Latency-First

Rightpad is game-first, but it is not esports-latency-first. The leading Motion
objective is continuous camera quality: smooth motion, low wobble, stable velocity,
natural continuity and gimbal-like visual stability. Precise micro-control,
natural control feel and long-session comfort follow closely. Fixed deterministic
feel, predictability and muscle-memory consistency remain mandatory foundations.

Fast-turn responsiveness, reversal response, stop response and absolute minimum
latency still matter and must be measured, reported and validated with human game
testing. They are not the sole ranking criteria. Do not reject a candidate merely
because it adds a fixed 10, 20, 30 or other number of milliseconds relative to the
current baseline. A fixed, deterministic and predictable response cost may be
accepted when it produces a meaningful improvement in trajectory stability and
does not noticeably damage control in real games. No universal acceptable or
unacceptable millisecond threshold is established; objective evidence and human
game experience decide the tradeoff together.

Stronger smoothing does not authorize adaptive feel. It means selecting one
stronger fixed configuration whose gain, tau, damping, coefficients, window,
cadence, playout delay and quantization behavior remain fixed for the run.

## Settling Is Not Glide

A fixed filter or follower may finish displacement already accumulated from real
finger movement after the true target displacement becomes fixed. This is filter
settling or completion of previously accumulated real displacement. It is allowed
only when it is deterministic, measurable, converges to that existing target and
does not continue indefinitely.

Glide or artificial inertia instead integrates old velocity or otherwise creates
distance beyond the real target. It remains prohibited. A fixed filter may finish
already-earned displacement; it may not invent new displacement. This distinction
preserves Relative Mouse semantics and does not permit momentum or joystick
behavior.

## Fixed Feel / No Runtime Adaptation

Explicit Phase Z1 experiment exception (2026-10-02): manual C selects
"zhq dynamics-faithful, Rightpad gain/transport-normalized" for human M/C game
A/B, referencing zhq revision 2eff689a2f3a7b645c977900ecfcca7a5069f603.
Only C is authorized to use true velocity glide, with fixed DT=4 ms, tau=35 ms,
dampingRatio=1, vector Vmax=15000, vector Amax=80000, glideDeceleration=120000,
position threshold 0.5 and velocity threshold 2. Java-compatible rounding has
its own successful-submit ledger and natural-stop carryOver. The live
committed Sensitivity and C 12 ms timestamp reconstruction remain; no reference
x7 gain or view scaling is imported. UP enters glide at its mapped playout
marker, without Earned-Settle or endpoint correction. One existing MotionClock
performs at most one fixed step per valid C deadline, skipping missed deadlines
without catch-up. Touching, release-pending and glide defer Requested changes;
new DOWN interrupts C using only the last natural carry and fences old work.
Hard lifecycle aborts clear all C state without output. M remains production
EXACT: Finite-Critical, Q0-C, 1 ms, Earned-Settle and immediate libvirtualhid.
This is an explicit fixed experiment, not a new general product glide permission
or an automatic/adaptive mode. See docs/MOTION_ENGINE.md.

Explicit Phase Z2 experiment history (2026-10-02): the fixed C Amax 160000
single-variable hypothesis has completed human validation. The user preferred
Z1 Amax 80000 for stronger gimbal character; Z2 reduced that character. True glide
was not subjectively obvious and did not hinder operation. Phase M-F1 precisely
restores C source to Z1 Amax 80000 and its 12 ms reconstruction; Z2 evidence stays
preserved. This is baseline recovery, not a new C experiment.

Explicit Phase M-F1 experiment exception (2026-10-02): only the M fixed
reconstruction/playout offset changes from historical 12 ms to 8 ms. Active M
uses 8 ms, Active C-Z1 uses 12 ms. The idle chain boundary chooses the delay;
Requested changes cannot retime touching, M earned settlement, C release-pending
or glide. M joining DOWN keeps its chain; C interrupted DOWN retains C12 and
fences old work. Saved Tau/Support, live committed Sensitivity, Finite-Critical,
Q0-C, 1 ms opportunities, Earned-Settle, native submit and missed-wake semantics
remain unchanged. No prediction, extrapolation, adaptation or delay sweep.
M12 historical goldens remain test-only references; M-F1 has independent goldens.
This implementation/automated-validation task excludes deployment, Stop, Restart,
Android build/deploy and human game testing. Preserve the current live Receiver
process and installed binaries, irrespective of its existing experiment identity.

Explicit Phase M-R1 experiment exception (2026-10-03): development identity M_R1
selects M-only direct reconstructed position P(t)=R(t), preserving fixed M8,
live committed Sensitivity, Q0-C, immediate native submit, 1 ms opportunities,
actual-time skip/no-catch-up and all input/causal/lifecycle safeguards. It never
evaluates Finite-Critical Position or IsSettled. Existing bounded Add/Trim history
bookkeeping and capacity faults remain, using the frozen run snapshot solely as
the history safety horizon; Tau/Support do not shape M-R1 position or release.
UP waits for its final mapped reconstruction boundary, then Q0-C evaluates the
endpoint and parks. Same-run DOWN while release is pending joins the same target,
ledger and phase; no completed-chain fractional carry is added. Requested defers
while touching or release-pending. C-Z1 remains EXACT, including C12 and true glide.
ProductionMode remains M-F1; RAW, B, M12 and M-F1 baselines are not redefined.
Active M-R1 displays no position filter, Q0-C, reconstruction completion, no glide,
and Tau/Support N/A; Saved settings stay intact. This stage authorizes isolated
builds/tests/evidence only: no deployment, Stop/Restart of the current Receiver,
Android changes, human game testing or Git write operations. This specific scope
overrides the general runtime-change deployment rule for this stage.

Explicit manual-Save exception (2026-09-18): the user may hot-apply committed
Sensitivity X/Y after a successful disk Save and settings publication. The pair
switches atomically for new real input displacement, including mid-contact.
Existing targets, pending displacement and realized history are never rescaled.
This is a deliberate user configuration change, never automatic adaptation.
Tau/Support and all other Motion shaping parameters remain frozen per run.

Runtime motion feel must be stationary. Once a Motion configuration is selected,
every parameter that affects control feel must remain fixed for that run. The same
physical finger movement under the same fixed configuration must produce the same
motion response. This is required for stable muscle memory, predictable control,
fair A/B comparisons and reproducible experiments.

If present in an algorithm, feel-affecting parameters such as sensitivity, gain,
smoothing strength, filter coefficients, filter window, follower time constant,
damping parameters, output cadence and playout delay must not change according to
finger velocity or acceleration, detected noise, touch sampling rate, packet-arrival
jitter, network conditions, CPU or GPU load, game frame rate, game type, game state,
estimated user intent or any other runtime condition.

In particular, do not implement behavior such as:

```text
Slow movement -> stronger smoothing
Fast movement -> weaker smoothing

Low speed -> one gain / tau / window
High speed -> another gain / tau / window

Detected noise -> automatically increase smoothing
More network jitter -> automatically enlarge playout delay or jitter buffer
Less network jitter -> automatically reduce delay

Low FPS / high FPS -> different Motion parameters
Different games -> automatically selected Motion feel
```

This also prohibits runtime-adaptive sensitivity, gain, smoothing, tau, damping,
FIR coefficients, window length, deadzone, playout delay, jitter-buffer depth and
velocity-, noise-, FPS-, network-, game- or system-load-based Motion mode switching.
A speed threshold that selects one feel for micro-aiming and another for fast turns
is prohibited even if its benchmark or theoretical tradeoff appears better.

Development may compare multiple fixed candidates, for example tau values of 3,
5 and 8 ms, fixed windows of 4, 6 and 8 ms, fixed playout delays of 8, 12 and 16 ms,
or fixed output rates of 250 and 500 Hz. Each candidate must keep its own values
fixed. Offline data, automated tests and human game A/B may then select one fixed
configuration. Experiment modes are explicit development choices; they do not
authorize automatic runtime switching.

Fixed feel does not require a simple algorithm. A measured, deterministic FIR,
fixed-window reconstruction, fixed second-order follower or fixed resampler may be
evaluated and adopted when all feel-affecting parameters remain fixed. The product
goal remains relative displacement semantics with gimbal-like trajectory quality:
smooth motion must not be obtained through velocity control, dynamic gain, glide
or inertia.

Deterministic fallbacks may react to correctness, safety and lifecycle conditions,
including malformed input, missed deadlines, buffer overflow, session or sender-run
invalidation and shutdown. For example, a late ticker may skip an obsolete tick and
resume the same fixed cadence. Such handling must be specified and repeatable; it
must not change the gain, tau, window, delay, mapping or other normal Motion feel
after recovery, and faults must not be used as a pretext for adaptive feel.

This section is the binding project-level rule. `docs/MOTION_ENGINE.md` must conform
to it, and experiment or reference documents must conform to both. An adaptive
candidate is ineligible as a product Motion solution unless the user explicitly
changes this requirement.

## Reference Implementations Are Not Targets

Moonlight TrackpadContext is a reference, a known-smoother baseline and design
evidence. It is not Rightpad's target, upper bound, gold standard, required
architecture or required parameter set. Its fixed-rate output, follower and other
trajectory-shaping choices may inform experiments, but its second-order model,
time constant, caps, glide and output semantics do not become Rightpad requirements.
Glide remains incompatible with the current Rightpad product constraints.

Rightpad should not merely match Moonlight. The long-term goal is to find the best
fixed-feel Motion system under Rightpad's own Relative Mouse, control-quality and
gaming requirements, and where practical to outperform both the current Rightpad
baseline and the current Moonlight reference. This is a product goal, not a claim
that either comparison has already been won. No reference implementation or
experimental candidate preselects the final algorithm.

---

# 6. Forbidden Features Unless Explicitly Approved

Do not add:

## Motion related

- Mouse acceleration
- Dynamic sensitivity
- Speed-based gain
- Adaptive feeling
- Prediction
- AI trajectory prediction
- Inertia simulation
- Glide movement

## Product related

- Accounts
- Cloud synchronization
- Online services
- Multi-device management
- Plugin marketplace
- Automatic game detection

## Input related

- Extra gestures
- Extra buttons
- Unrequested control modes

---

# 7. Architecture Rules

## Security intentionally out of scope

rightpad is a personal-use system for one user, one Android phone, and one
Windows 11 PC on a trusted local network. Do not implement authentication,
pairing, PINs, encryption, TLS/DTLS, tokens, certificates, signatures, HMAC,
anti-replay systems, multi-user permissions, or security handshakes.
Do not add first-sender locking or source authentication/isolation.

Explicitly approved LAN discovery exception (2026-09-12): one Android phone may
automatically find either of two Windows PCs at different locations, normally
not simultaneously on one LAN. Use independent Receiver Discovery v1 UDP 50001;
Touch v2 UDP 50000 stays unchanged. OFFER source IPv4 is authoritative; no fixed
production IP, first-NIC heuristic, manual host selector or cloud. See
docs/DISCOVERY_PROTOCOL.md for the selection and clean target-transition contract.

Packet length/version/field validation, malformed packet rejection, and
timeout/stuck-input fail-safes remain required reliability measures.

Explicitly approved click haptics exception (2026-09-13): independent Windows →
Android Haptic Feedback v1 UDP 50002 confirms only Windows normal-click output.
Android validates current discovery source IP, Sender-owned run and raw UP
identity for stale rejection/dedupe; this is not authentication and does not
change Touch admission. A small independent sender task/listener thread is
authorized for this best-effort side effect. No Android gesture recognition,
input dependency on feedback, background service or haptic setting is added.
See docs/HAPTIC_FEEDBACK_PROTOCOL.md.

Phase 5A.4 restores only the final accepted Touchpad CLICK execution to
performHapticFeedback(HapticFeedbackConstants.CONFIRM), using system tuning.
Receiver confirmation, RPHF bytes, identity validation and dedupe remain unchanged;
never vibrate locally on Touchpad DOWN/MOVE. TouchpadClickFeedback is independent
of ScreenControlFeedback's 10 ms / 255 policy. Failure remains best-effort, with
no one-shot fallback or retry. The user rejected the Phase 5A.3 one-shot feel and
selected system CONFIRM and confirmed blind distinction on the actual device.
Do not adjust waveforms automatically. VIBRATE remains for Screen Controls.

## Android Responsibility

### Approved LR gesture, transport and config (2026-09-20, Phases 6A–6C)

An independent reusable SlideControlLRGesture may express BASE/LEFT/RIGHT/UP,
with horizontal threshold priority, first commit wins, Design A takeover and
downward movement doing nothing to pending Tap/LongPress. Defaults are Left 12 dp,
Right 3 dp, Up 2 dp, Tap Hold 25 ms, Long Press 400 ms, snapshotted at DOWN.
Its per-definition SYSTEM_CLICK feedback uses EFFECT_CLICK on API 29+, 20 ms / 120
on API 26–28, legacy 20 ms otherwise, only on DOWN/first direction commitment.
Phase 6B registers xbox.x.slide_lr (label X) through the same layout/editor/router
as B. Definition mapping is BASE=X, LEFT=DpadLeft, RIGHT=DpadRight, UP=DpadUp;
stable-ID contributions merge in GamepadAggregator. Existing logical bits 2/13/14/11
and the 30-byte v2 type5 packet, dwell, FORCE_NEUTRAL and single Xbox360 device are
reused unchanged. Design A publishes one full replacement. Phase 6C adds separate
SlideControlLRSettings under controls.x, registry wire ID 2 / kind 2 and five
Receiver Controls fields. Disk-first explicit Save publishes one global revision
with B+X. RPCT v2 uses length-prefixed records (B 12 bytes, X 14 bytes; total 62).
Android validates complete B+X before atomic cache replacement and snapshots at
DOWN. v1 B-only transition reading is retained without acknowledging complete
B+X sync. No GAMEPAD_STATE, type6 request, layout, dwell or haptic change is allowed.
Existing B STRONG_ONE_SHOT stays 10 ms / 255 and Touchpad stays system CONFIRM.
See docs/SCREEN_CONTROLS.md for the current contract and validation scope.

### Approved Screen Controls exception (2026-09-19, Phases 1–4)

Phase 5A.2 specifies local generic Screen Control PRESS and first
DIRECTION_COMMIT haptics: fixed 10 ms amplitude 255 one-shot on API 26+, or
legacy 10 ms on older APIs. Do not prefer HEAVY_CLICK or increase the duration
without a new user instruction. VIBRATE is a manifest permission, with
no runtime prompt. LongPress, UP, CANCEL, Tap pulse, editor, Settings and Power
produce no Screen Control haptic. Feedback failure never controls input. Existing
Windows-confirmed Touchpad RPHF semantics remain unchanged; its separate final
effect follows the explicit Phase 5A.4 rule above. Distinct haptic identities must
be confirmed by the human on the actual device before claiming acceptance.

Registered on-screen controls may recognize their own single-finger SlideControl
gestures on Android. The current `xbox.b.slide` instance maps Tap/LongPress to B,
Slide Up to Y and Slide Down to A. Design A replaces held B with Y/A immediately.
This exception does not move mouse gesture recognition or Motion to Android.

Receiver Controls settings are the sole behavior source: defaults are Up 0.7 dp,
Down 3.0 dp, Tap Hold 25 ms and Long Press 400 ms. Explicit successful disk Save
publishes a full config epoch/revision snapshot over the existing UDP 50002
worker/listener. Type6 requests use the existing Android Sender/UDP 50000 and
recover lost updates. Failed Save never publishes. Android caches behavior only
in memory and snapshots it at DOWN; the active gesture remains unchanged.

The generic layout editor supports move, edge/corner resize, integer X/Y/W/H,
Save/Cancel/Reset and bounds validation. Visual Rect equals Hit Rect. Defaults
are square; saved rectangles are allowed. Android persists layout independently
by stable ID. Preserve the current user layout during builds and tests; never
assume a historical test rectangle is still current. Instrumentation snapshots
the user's file, temporarily exercises a fixture and restores the exact file in
finally. Do not reset user layout without instruction.

Generic GAMEPAD_STATE is v2 type5, exactly 30 bytes, carrying full Xbox state,
minimumDwellMs, flags including FORCE_NEUTRAL and zero reserved. Touch types 1–4
remain byte-for-byte unchanged. Held state refreshes every 100 ms; Receiver uses
an independent 300 ms lease and a low-overhead monotonic dwell deadline task.
Ordinary Tap Neutral respects dwell; safety Neutral and new non-Neutral states
bypass the old dwell. Do not increase Android Tap Hold to hide network jitter.

The independent libvirtualhid xbox_360 backend uses ABI2; its failure must not
stop mouse input. Lifecycle release includes cancel/pause/editor/target/run change,
disconnect/lease/Stop/Safe Restart/Dispose/error. No B-specific transport logic,
extra gesture, socket/thread, service, authentication or Motion change is approved
by this exception. Existing shared UDP 50002 and its worker/listener are reused.
See docs/SCREEN_CONTROLS.md, docs/GAMEPAD_PROTOCOL.md and
docs/CONTROL_CONFIG_PROTOCOL.md. Automated ADB/XInput tests are functional evidence.
The user explicitly confirmed final real-person acceptance of Phases 1–6C on
2026-09-20 before authorizing the combined source/docs/tests commit and normal push.
Preserve the accepted parameters; future behavior changes require fresh acceptance.

For the mouse path, Android is only an input sensor.

Android is responsible for:

- Touch capture
- Historical touch samples
- Timestamp collection
- Raw data transmission

Android should NOT decide:

- Mouse movement behavior
- Sensitivity
- Smoothing
- Mouse gesture meaning
- Windows input generation

---

## Windows Receiver Responsibility

Windows receiver owns:

- Input processing
- Motion algorithms
- Gesture recognition
- Configuration
- Mouse output

The receiver is the main control center.

---

# 8. Configuration Rules

User-adjustable parameters should normally exist on Windows receiver.

Reason:

- Easier tuning
- No Android rebuild
- Better testing workflow

Avoid placing important tuning parameters only inside Android.

---

# 9. Engineering Philosophy

## Game-First / Necessary Complexity Rule

rightpad's formal product position is:

> A top-tier gaming touchpad built for games.

It is not a general office touchpad. Product and engineering decisions must
prioritize, in order:

1. Gaming feel
2. Gaming compatibility
3. Stability and reliability
4. Latency and jitter
5. Input consistency
6. Ease of use
7. Development difficulty and code volume
8. Monetary cost

Development difficulty and code volume may affect implementation order,
development phases and test planning. They must not, by themselves, reject a
gaming-grade capability whose necessity and benefit have been demonstrated.

**Practicality First does NOT mean Simplicity First.** It does not mean always
choosing the least code, the easiest implementation or a weaker solution merely
because the stronger solution requires native code, a Windows driver, Virtual
HID, driver signing, an installer component or lower-level Windows APIs.

Necessary complexity is allowed and required when it buys measurable gaming
compatibility, latency, jitter, consistency, reliability, correct Windows input
semantics or user-experience benefits. Do not reject a necessary gaming-grade
feature merely because it is difficult, low-level, driver-based or increases
implementation complexity. Complexity without measurable product benefit is
prohibited.

Large complexity remains evidence-driven. It requires at least one of:

- A verified limitation
- A measured compatibility problem
- A measured performance or input-quality benefit
- A clear current product requirement

“Theoretically better” is not sufficient. Measure the current limitation, define
the expected benefit and validate the candidate with real games and human input
where applicable.

### Monetary Cost vs Dependency Risk

**Practicality First does NOT mean Lowest Monetary Cost.** Monetary cost is not
an optimization target for this personal project. Reasonable spending on
software, drivers, licenses, hardware, development tools and testing tools is
acceptable when it produces measurable improvements in gaming feel, gaming
compatibility, latency, jitter, input consistency, reliability, usability or
future capability.

Do not reject a technically superior solution merely because it costs money.
For rightpad, monetary price is normally lower priority than achieving the
product goal: a top-tier gaming touchpad built for games. This does not justify
buying or adopting something because it is expensive, advanced or complex; the
benefit must still be evidence-driven.

Distinguish monetary price from technical, vendor and deployment dependency
risk. The following remain material architecture factors even when their direct
financial cost is acceptable:

- Vendor dependency and abandonment risk
- License availability and activation reliability
- Offline availability
- Service or broker dependency
- Driver deployment, signing and installation reliability
- Upgrade compatibility and long-term maintainability
- Game and anti-cheat compatibility
- Failure and recovery behavior

A paid Virtual HID, libvirtualhid, commercial driver or machine license must not
be rejected merely because it is paid, driver-based or installation-intensive
when it demonstrably improves Raw Input behavior, HID semantics, gaming
compatibility, latency potential, consistency, reliability or future virtual
gamepad support. Vendor lock-in, activation availability, driver maintenance and
deployment reliability must still be evaluated independently. This rule does not
preselect libvirtualhid or any other candidate.

### Virtual HID

Production mouse backend (approved 2026-09-12): **libvirtualhid Virtual HID Mouse**.
The single product default is `MouseBackendDefaults.Production`. Program passes
the backend explicitly to ReceiverRuntime; normal GUI, quoted-EXE HKCU startup
and the independent launcher without an override inherit that default.
SendInput remains an explicit development/diagnostic compatibility override
(`--dev-mouse-backend sendinput`), never an automatic production fallback.
Do not add a user backend selector, persisted backend setting or silent fallback.
Virtual HID initialization failure must remain Runtime Error with visible LastError
and diagnostics. Keep fake tests independent of the installed native driver.

Real testing has established a structural limitation worth formal evaluation:

```text
Medium-integrity Receiver
→ High-integrity foreground
→ SendInput success count remains normal
→ actual cursor does not move

High-integrity Receiver
→ High-integrity foreground
→ cursor movement works again
```

Adoption evidence: human A/B found no obvious subjective feel degradation;
Virtual HID is Raw Input-visible; Medium Receiver → High-integrity foreground
movement and Single Tap work; SendInput has the measured limitation above;
POC/build/runtime and Driver/Broker/Lifetime license prerequisites are validated.
Objective latency benchmarking is not complete: do not claim lower latency or
higher polling rate. Broader game compatibility, latency, jitter and dependency/
deployment maintenance risk remain evidence-driven. See docs/VIRTUAL_HID_MOUSE_POC.md.

### Avoid Unnecessary Complexity

Prefer:

- Small modules
- Clear responsibilities
- Easy debugging
- Minimal dependencies

Avoid:

- Complex frameworks without need
- Large abstractions
- Premature optimization
- Excessive design patterns

Necessary complexity does not authorize scope expansion. Do not add a framework
for its own sake, plugin system, generic abstraction hierarchy, connection manager,
unnecessary service or IPC, telemetry platform, automatic discovery, profile
system, security system or compatibility layer without a demonstrated current
need.

---

## Measure Before Optimizing

Before adding an optimization, answer:

1. What problem does it solve?
2. How can it be measured?
3. What is the expected improvement?
4. Is there a simpler solution?

If the benefit cannot be demonstrated, do not add the complexity.

---

# 10. Development Process Rules

Before implementing any feature:

1. Confirm that the feature is required.
2. Check existing documentation.
3. Consider the simplest implementation.
4. Explain architectural impact if significant.
5. Do not expand the project scope.

Do not implement features because:

- They may be useful someday.
- Other products have them.
- They look technically impressive.

---

# 11. Documentation Priority

Read documents in this order:

```
AGENTS.md

↓

docs/ARCHITECTURE.md

↓

Related design documents

↓

Reference documents
```

Priority:

```
AGENTS.md
    >
Design documents
    >
Reference documents
```

Reference documents are informational only.

They must not override current requirements.

---

# 12. Coding Rules

Prefer:

- Clear naming
- Small functions
- Predictable behavior
- Comments explaining design reasons

Avoid:

- Unnecessary comments
- Dead code
- Temporary hacks
- Fake placeholder implementations

Do not create:

- Empty classes without purpose
- Unused frameworks
- Future-proof abstractions without current use

---

# 13. Performance Rules

Performance matters.

However:

Do not optimize before measurement.

Avoid premature:

- Lock-free structures
- Custom memory allocators
- Complex threading systems
- Low-level optimization

Use simple implementations first.

Only optimize after profiling shows a real issue.

---

# 14. Testing Rules

Important behavior must be measurable.

Prefer:

- Automated tests
- Logging
- Diagnostics
- Repeatable experiments

Avoid judging input quality only by subjective feeling.

---

# 15. Scope Control

When receiving a task:

Implement only the requested scope.

Do not add:

- Extra features
- UI improvements
- Refactoring unrelated code
- Additional architecture layers

If a better approach requires changing scope:

Explain first.

Do not silently expand implementation.

---

# 16. Current Project Priority Order

Priority:

1. Correct input capture
2. Reliable Android-to-Windows communication
3. Stable motion processing
4. Correct gesture handling
5. Windows mouse output
6. Tuning and diagnostics

Do not prioritize:

- Visual design
- Extra features
- Convenience functions

before core input quality is proven.

---

# 17. Final Principle

rightpad should become:

A precise, predictable, reliable, top-tier gaming touchpad built for games.

The best solution is not the most complicated solution.

The best solution is the least complex design that fully achieves the measured
gaming-grade requirements. Necessary complexity must remain when removing it would
sacrifice demonstrated compatibility, latency, jitter, consistency, reliability,
correct input semantics or user experience.

---

# 18. Android Build / Deploy / Restart / Receiver Recovery Rule

Whenever a rightpad Android APK build succeeds and the configured test Android
device is reachable through ADB, Codex must complete the full deployment and
runtime recovery sequence.

Required sequence:

1. Build the Android APK successfully.
2. Check that the configured Android device is available through ADB.
3. Overwrite/reinstall the newly built APK.
4. Stop the old Android app instance.
5. Relaunch `com.rightpad.capture/.MainActivity`.
6. Verify that MainActivity is resumed and in the foreground.
7. Keep the current Protocol v2 WPF Receiver process and Receiver Runtime running.
   Do not restart Windows to recover from an Android Sender restart.
8. Verify Disconnected/Connected transitions and admission of the new senderRunId.
   If Windows itself needs an initial launch or binary update, use
   `windows/tools/RightpadReceiverTask.ps1 -Mode Start` after the required build;
   complete subsequent Android recovery checks without restarting that Receiver.
9. Run the Receiver in the current Windows user's interactive desktop session,
   with the same active SessionId as explorer.exe, not in the restricted Codex
   SendInput sandbox.
10. Verify the current user, explorer SessionId, Medium integrity, Default desktop,
    and UDP 50000 listener using the launcher `-Mode Status`.
11. Verify that the Android UDP Sender is active and reports no sender error or
    queue overflow.
12. Verify that the existing Receiver accepts the new Sender run and its Touch
    sequence 0 baseline, with unchanged WPF PID and Receiver Runtime RunId.
    Settings, socket and cumulative Receiver statistics must survive.
13. If the current task includes mouse or gesture output, perform a minimal
    end-to-end functional smoke check that Codex can execute.
14. Only after the Android app and Windows Receiver are both restored and the
    end-to-end path is functional may the Android task be reported as ready or
    completed.

Persistent / user-facing Rightpad.Receiver must NOT be launched as a direct or
indirect persistent child of the Codex execution shell. Codex execution jobs may
use Windows Job Objects with KILL_ON_JOB_CLOSE, which can terminate Receiver when
the Codex host is replaced or cleaned up.

Use the project-approved independent interactive Windows task launcher:
`windows/tools/RightpadReceiverTask.ps1`. It uses the `Rightpad Receiver Dev`
Scheduled Task, current-user InteractiveToken, Limited / LUA run level, and no
triggers. Do not fall back to plain Start-Process from the Codex shell for a
Receiver that must remain alive after the tool command returns. Runtime logs stay
under `C:\rightpad-artifacts\receiver-runtime\` for future new runs. Existing
instances keep their original `windows/test-results/receiver-runtime/` logs;
Status checks both record locations without moving files. This is a development
launch mechanism, not a product service, watchdog, or automatic startup feature.

For this project, Android deployment normally includes commands equivalent to:

```text
adb install -r <latest-debug-apk>
adb shell am start -S -n com.rightpad.capture/.MainActivity
```

A successful Gradle build alone is not completion. A successful APK installation
alone is not completion. Verify foreground, presence, new Sender acceptance and
functional E2E on the existing v2 Receiver after Android restart/reinstallation.

Current Protocol v2 behavior (validated 2026-09-09):

- Android creates a random 64-bit senderRunId per UdpTouchSender runtime and
  rotates it on every discovery target transition, clearing Touch queue/session
  and resetting sequence to zero. Same-target confirmed pause/resume retains it.
- onPause stops heartbeat/capture without destroying Sender or resetting sequence;
  onResume probes immediately and enables heartbeat after a fresh discovery OFFER
  confirms the unchanged target, keeping the same runId and sequence.
- Sender recreation gives a new runId and Touch sequence starts at zero.
- Receiver admits an unknown run only on a fully valid HEARTBEAT or DOWN, retires
  the prior ID for its Runtime lifetime, and resets only the input baseline.
- Heartbeat (500 ms) or accepted current-run Touch renews monotonic presence.
  A 2000 ms presence timeout clears stale input once, keeping runId/sequence.
- Android restarts no longer require restarting WPF or ReceiverRuntime.

The v1 development workaround requiring a fresh Receiver has been replaced by
the explicitly approved v2 run/presence behavior. The independent interactive
launcher / Codex Job lifetime rule remains mandatory. Practicality First: add
only functionality solving a current demonstrated problem. This change does not
authorize handshake frameworks, reconnect managers, TCP/ACK/reliable UDP,
retransmission/FEC, pairing/security, unapproved config sync, extra transport
threads/sockets or network optimization beyond the separately approved independent
LAN discovery socket/thread described in docs/DISCOVERY_PROTOCOL.md and the
independent click-feedback side channel in docs/HAPTIC_FEEDBACK_PROTOCOL.md.
The separately approved Screen Controls exception above also reuses that channel
for behavior config sync and the existing Sender for type6 requests, as specified
in docs/CONTROL_CONFIG_PROTOCOL.md; it adds no socket or worker thread.

Do not ask the user to run commands that Codex can run. If no configured test
device is reachable through ADB, explicitly report that deployment and runtime
recovery could not be completed. If Android requires a system-level installation
confirmation that Codex cannot operate, stop only at that permission boundary,
ask the user for that single confirmation, and then continue automatically.

---

# 19. Windows Receiver Change / Mandatory Restart and Verification Rule

This is a permanent engineering rule. It applies whenever a completed change can
alter the actual Windows Receiver runtime code or binary behavior, including but
not limited to:

- C# production code
- WPF XAML
- ResourceDictionary
- View or ViewModel
- Runtime
- Settings
- UDP or Protocol
- Motion or Gesture
- SendInput
- Receiver project configuration
- Receiver executable build output

After any such change, Codex must complete this sequence:

1. Complete the applicable build and tests.
2. Stop the currently running old Receiver.
3. Verify that the old Receiver process has exited.
4. Verify that the old process has released UDP port 50000.
5. Start the latest build through the project-approved independent interactive
   launcher, normally `windows/tools/RightpadReceiverTask.ps1`.
6. Never launch a persistent Receiver as a direct or indirect long-running child
   of the Codex shell.
7. Record and verify the new Receiver PID, or otherwise provide explicit evidence
   that this is a new process instance running the latest build.
8. Verify that the Receiver runs as the current user, in the interactive Session,
   at Medium integrity, on the Default desktop.
9. Verify that `0.0.0.0:50000` is owned by the new Receiver PID.
10. If the Android rightpad app is currently available, wait for the existing
    Protocol v2 heartbeat and verify the header transition from
    `Waiting for Android` to `Connected`.
11. Run the minimum end-to-end smoke check required by the change's scope.
12. Only after all applicable checks pass may the Receiver change be reported as
    verified or complete.

`build succeeded`, `tests passed`, and `a new executable was generated` are each
insufficient on their own. If a change affects actual Receiver runtime code, the
old process must exit and the latest build must start. A Receiver that is already
Connected, still has a PID, owns UDP 50000, or appears healthy does not waive this
restart requirement. Do not attribute later runtime observations to the new code
while a pre-change process is still running.

The mandatory action is Stop -> verify exited and port released -> Start fresh.
A new PID or equally explicit new-instance evidence is part of the verification.
When it is unclear whether a change affects Receiver runtime behavior, perform
the mandatory restart by default.

The restart is not required for changes limited to:

- documentation only
- comments only
- README only
- test instruction files that do not participate in build or runtime
- Git metadata

Protocol v2 keeps the Android and Windows lifecycle rules separate. A Windows
Receiver code change does not justify restarting Android. If Android remains in
the foreground after the Receiver restart, its existing heartbeat must naturally
drive `Waiting for Android` -> `Connected`; this is an important Receiver restart
regression check. If one task changes both Android and Windows runtime code,
restart the Receiver once after its final build, then keep that new Receiver
instance running throughout the Android deployment/restart recovery checks.

Continue using `windows/tools/RightpadReceiverTask.ps1` or an explicitly approved
equivalent independent interactive launcher. Do not return to a persistent
`Codex shell -> Start-Process -> Receiver` chain because it can inherit the Codex
Windows Job lifetime and terminate unexpectedly.
