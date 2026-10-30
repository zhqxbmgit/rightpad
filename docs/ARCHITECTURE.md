# rightpad System Architecture

2026-10-29 Mode fixed-region extension: a third purpose uses Android fixed View-space origin/first/second selection and Receiver-owned UP-only pulses, with bounded config/contact/layout snapshots. Limited type10/RPMD v2 on existing workers preserves old uses and all other paths. See [MODE_FIXED_REGIONS.md](MODE_FIXED_REGIONS.md).

Development storage contract (2026-10-22): source, development output and deployed
runtime are separate roots. `artifact-root.txt` selects `C:\rightpad-artifacts`
for future builds, intermediates, task evidence/candidates/recovery and new launcher
logs. The formal Receiver Release location and user configuration stay unchanged.
Existing logs and historical artifacts remain at their original paths. See
[DEVELOPMENT_ARTIFACTS.md](DEVELOPMENT_ARTIFACTS.md) for current entry points,
override precedence, legacy Status lookup and authorization boundaries. This
tooling change does not change input, Motion, protocols or application behavior.

Current Mode shortcut-menu extension (2026-10-20 second round; slot completion
2026-10-21; unbound confirmation and cursor availability 2026-10-22): Receiver now
selects either the preserved single binding below or a six-slot pulse menu using
only the fixed complete Ring. Old appearance value 1 loads as Ring while all
other mode fields and settings are retained. ModeSession owns the menu, pulse
queue and lifecycle under its existing gate. Accepted Touch packets still enter
UdpReceiver's single original Motion/HID movement path exactly once while the menu
is open; ordinary Gesture single-click/double-tap-drag output is withheld before
a mouse button can be generated. A tap on a bound slot executes exactly one
keyboard/gamepad press and release; only after both edges completed does
ModeSession submit one confirmation through the existing Haptic Feedback v1
channel (same RPHF bytes, sender run, raw UP identity, Android CONFIRM and dedupe)
and then close the menu automatically. A tap on an unbound direction submits that
same single confirmation (same qualified UP identity) with no key, gamepad or
mouse output, then closes immediately. One open cycle accepts at most one valid
slot selection, bound or unbound, so repeated or late taps cannot enqueue more
work; the former serial pulse queue is gone and a second Mode DOWN still closes
the menu manually. Center taps emit nothing and keep the menu open. The WPF
non-activating transparent host renders a small white arrow with a black outline
at the same ModeMenuSnapshot CursorX/CursorY used by the Ring (2026-10-22 visible
marker extension). Its silhouette tip is the observed position; DIP geometry uses
the Ring's monitor-origin/DPI transform, renders last, and clips naturally at the
existing overlay bounds. It has no private movement state or cursor read, and is
omitted from the static settings preview. Hiding/closing the menu also removes the
marker. While open, the presenter consumes the latest shared snapshot at WPF
CompositionTarget.Rendering opportunities; its original idle timer is stopped
until the menu closes. The retained arrow changes only its transform, leaving
unchanged Ring labels and decoration intact. Close/fault/Dispose detach the frame
handler; the existing fault/release lifecycle and 2 s presentation timeout remain. The
menu never submits SetCursorPos. A separate read-only system-cursor adapter uses GetCursorPos
to capture the opening anchor and observe the actual Windows position for
highlight and tap authority, so asynchronous Motion output is observed rather
than predicted. Cursor visibility and ClipCursor are not availability vetoes: a
hidden/suppressed cursor still has a real position and a clip constraint does not
invalidate a readable coordinate. A 12 DIP inclusive neutral radius and six
nearest directions select slots independently of decoration and distance. Edge
decoration is clipped rather than moving the anchor; a bounded selection caption
stays visible. Real GetCursorPos/GetMonitorInfo/GetDpiForMonitor failures or a
changed monitor/DPI exit with a fresh-DOWN fence; an explicit new synchronized
Mode contact can retry environmental faults, while output-device failures retain
their latch. Entry releases old drag/pending clicks and records that safety release
separately from slot pulses; exit discards old taps, UP and queued work. Healthy
idle and normal lifts do not time out the menu. Motion parameters, sensitivity, devices and wire
formats are unchanged. Common Save remains disk-first. See
[MODE_SHORTCUT_MENU.md](MODE_SHORTCUT_MENU.md),
[HAPTIC_FEEDBACK_PROTOCOL.md](HAPTIC_FEEDBACK_PROTOCOL.md) and
[the current delivery report](MODE_MENU_COMPAT_DELIVERY_20261022.md). The
2026-10-21 action-delivery report remains as history for its bound-slot behavior;
its unbound and cursor-availability rules are superseded.

Current Mode button contract (2026-10-17): `rightpad.input.mode` now reports
contact facts for a Receiver-owned single keyboard/gamepad binding, with
Momentary or Toggle behavior. It retains its saved geometry and no Mode haptic.
The same settings Save transaction persists `modeControl`; a separate bounded
type8/RPMD contact stream on existing workers/ports handles acknowledgements,
lease and lifecycle generations. An additive driver-backed keyboard extension
preserves mouse/gamepad ABI2, and Mode contributes independently to the existing
single Xbox360 output. Receiver-local M/C and all Motion behavior remain unchanged.
See [MODE_CONTROL.md](MODE_CONTROL.md), [MODE_CONTACT_PROTOCOL.md](MODE_CONTACT_PROTOCOL.md)
and [the delivery report](MODE_CONTROL_DELIVERY_20261017.md). This current contract
supersedes the historical reserved-no-op and Android M/C descriptions below.

Current delivery status (2026-10-16 source audit): Receiver-local M/C and the
C-Z1 panel were deployed on 2026-10-10. The 2026-10-15 delivery deployed the
Android reserved no-op control and remote layout transactions, followed by the
dedicated maximized editor and black, aspect-preserving preview. The user has
accepted the current layout editor appearance. This does not establish new
human game-feel acceptance. See [GIT_DELIVERY_20261016.md](GIT_DELIVERY_20261016.md)
for source/evidence correspondence and the Git-only delivery scope.

Receiver Controls can remotely edit the Android-owned screen layout through
independent RPLY transactions on the existing workers and ports. Drafts remain
Windows-local; Android owns trial expiry, atomic persistence and recovery.
See [REMOTE_LAYOUT_PROTOCOL.md](REMOTE_LAYOUT_PROTOCOL.md). Motion, gesture
behavior settings and all existing packet formats remain independent.

Implemented C-Z1 contract (2026-10-10): Windows owns independently persisted C-Z1
parameter snapshots and a Motion-page panel. Validated full snapshots apply only
under the existing motion gate at completely idle chains; busy input/output is
preserved. No new motion thread, device lifetime, adaptive parameter or Android
change. Exact defaults and all M-R1 behavior remain. See
[C_Z1_RECEIVER_PARAMETERS.md](C_Z1_RECEIVER_PARAMETERS.md).

Implemented mode contract (original source stage 2026-10-08): normal GUI
mode authority is now Receiver-local `RuntimeSettings.TouchMode` (`M_R1`/`C_Z1`).
Motion-page clicks save through SettingsFileStore before publication. Existing
ResampledMotion applies the request only at its safe idle boundary; Selected,
Pending and Active are displayed separately. Android's `rightpad.input.mode`
remains visible, editable and persisted with its existing geometry, but is a
reserved no-op with a static M label. Type7 stays validated legacy telemetry and
cannot select an engine on connection, new run, reconnection or retransmission.
Fixed unbuffered capture, M-R1/C-Z1 dynamics and all other wire formats remain
unchanged. See [RECEIVER_TOUCH_MODE.md](RECEIVER_TOUCH_MODE.md) for the source
contract and validation. Historical stage descriptions below retain their
original dates; the current delivery status above supersedes old authority notes.

Current selected baseline (2026-10-03): Touchpad R&D is paused. The normal
Android App uses fixed unbuffered Mouse DOWN acquisition and the restored M/C
profile control. The selected deployed Receiver runs the existing explicit
`M_R1` identity: M8 reconstruction -> direct P(t)=R(t) -> Q0-C -> immediate
libvirtualhid, with 1 ms opportunities and reconstruction-completion release.
C remains the fixed C-Z1 servo/glide profile with C12 reconstruction. Saved
Tau/Support remain available for filtered baselines and do not shape M-R1.
FIX-2 (2026-10-04) makes normal GUI startup default to M-R1, including bare HKCU
startup and ordinary launcher Start; explicit development arguments still override
that choice. `ProductionMode` remains the historical M-F1 configuration identity.
Historical implementation
scope notes below refer to their original stages. See
[MOTION_ENGINE.md](MOTION_ENGINE.md) and
[MOTION_PROFILE_PROTOCOL.md](MOTION_PROFILE_PROTOCOL.md) for the current contracts.

## 1. Overview

rightpad is a dedicated Android-to-Windows gaming input system.

The system converts an Android phone touchscreen into a high-quality relative
mouse input device with approved single-finger Xbox360 screen controls.

Main goals:

- Low latency
- Stable movement
- Predictable behavior
- Minimal unnecessary complexity

The system consists of two major components:

Production endpoint selection uses independent Receiver Discovery v1 (UDP 50001).
The Android Wi-Fi-bound discovery worker selects the OFFER source IPv4; the
Windows responder advertises only after Touch UDP 50000 is bound. No fixed PC IP,
manual host UI, first-interface heuristic or cloud is used. Two PCs at separate
locations are supported by first-valid selection and current-receiver liveness.
See [DISCOVERY_PROTOCOL.md](DISCOVERY_PROTOCOL.md) for lifecycle and validation.

```text
Android Client

    Touch Capture

        ↓

    UDP Transport


        ↓


Windows Receiver

    Input Processing

    Motion Engine

    Gesture Engine

    Mouse Backend
```

---

# 2. System Components

## Android Client

Approved Screen Controls exception (Phases 1–4): registered on-screen controls
produce logical gamepad actions using their local state machines. A generic
aggregator publishes full gamepad snapshots through the existing Sender socket
and worker. This does not move mouse Motion/Gesture decisions to Android.
Windows owns gamepad authority, serial ordering, lease and the independent
Xbox360 backend. See [GAMEPAD_PROTOCOL.md](GAMEPAD_PROTOCOL.md).
Receiver Controls settings participate in the explicit disk-first Save transaction.
Committed immutable snapshots synchronize over the existing UDP 50002 channel;
Android type6 requests on UDP 50000 recover lost snapshots. Behavior changes apply
on the next control ACTION_DOWN, with layout storage remaining independent.
See [CONTROL_CONFIG_PROTOCOL.md](CONTROL_CONFIG_PROTOCOL.md).

Phase 5A.2 retains independent local Screen Control feedback: the UI adapter emits
PRESS on control DOWN and DIRECTION_COMMIT on first MOVE direction commitment.
ScreenControlFeedback contains best-effort API selection; ScreenControlHapticFeedback
uses fixed Android 10 ms / amplitude 255 one-shot on API 26+, or legacy 10 ms.
The prior HEAVY_CLICK selection has been removed following failed human strength
acceptance. No automatic duration increase or alternate effect is used.
No feedback is emitted for LongPress/UP/CANCEL/Tap pulse or editing. Logical
SlideControlGesture, full-state transport and Windows click confirmation semantics
remain unchanged. Phase 5A.4 restores only the accepted mouse CLICK's final effect
through TouchpadClickFeedback / TouchpadClickHapticFeedback to
View.performHapticFeedback(HapticFeedbackConstants.CONFIRM). There is no Touchpad
one-shot, legacy vibration or fallback. RPHF bytes, validation and dedupe are
unchanged; local Touchpad DOWN/MOVE never trigger vibration. Failures cannot stop
input. VIBRATE remains for Screen Controls without a runtime prompt. Actual blind
distinguishability is a human acceptance criterion, not an API-success assertion.

Phase 6A adds an isolated SlideControlLRGesture with BASE/LEFT/RIGHT/UP logical
states and a small deadline adapter. It does not generalize or modify the existing
B gesture engine. Definitions select STRONG_ONE_SHOT (existing B 10 ms / 255) or
SYSTEM_CLICK (LR EFFECT_CLICK on API 29+, 20 ms / 120 on API 26–28, legacy 20 ms).
Phase 6B registers LR in the existing UI, layout and single-finger router. Its
ScreenControlDefinition maps BASE=X and LEFT/RIGHT/UP to real D-pad button bits.
ScreenControlInstance bridges the two independent logical engines to the existing
UI deadline scheduler. GamepadAggregator unions contributions by stable ID and
batches Design A into one full replacement. The 30-byte type5 packet and existing
C#/native Xbox360 path already support these bits and remain unchanged.
Phase 6C adds separate LR Receiver settings and field editors to the existing
disk-first Save transaction. RPCT v2 carries complete B+X snapshots in 62 bytes,
with length-prefixed 12-byte Slide and 14-byte SlideLR records. Android validates
the whole packet before committing and captures LR configuration at DOWN.
Receiver owns behavior; Android-local layout and all input/haptic contracts stay unchanged.
See [SCREEN_CONTROLS.md](SCREEN_CONTROLS.md) for horizontal priority and timing.

The production layout registry contains the stable ID `rightpad.input.mode`.
The manual M/C profile shell uses it as the manually selected Phase Z1 experiment slot, with
unchanged normalized X/Y/W/H persistence and generic editor operations.
The Activity's TouchCaptureView owns a ScreenControlRouter whose in-memory profile
starts NORMAL (M); matching MODE UP toggles CINEMATIC (C), while cancellation or
pointer loss never toggles. The profile selects its runtime label and transported state; both modes
use the production Touchpad Mouse capture/Sender/Receiver path. No persisted
profile, gamepad contribution or Mode haptic is implemented. The Sender publishes that
same authoritative value as independent
16-byte v2 type7 MOTION_PROFILE_STATE through the existing Sender worker/socket.
Windows validates current-run/source and independent serial ordering and exposes
read-only requested M/C in Diagnostics. New admitted runs start M; ordinary presence timeout
retains the run's profile. See
[MOTION_PROFILE_PROTOCOL.md](MOTION_PROFILE_PROTOCOL.md).
M retains the production path: live committed sensitivity -> fixed M-F1 8 ms
reconstruction -> Saved Finite-Critical -> Q0-C -> immediate libvirtualhid,
at 1000 Hz with Earned-Settle. Phase Z1 C shares the same input, authority,
Sensitivity front end, retains C-only fixed 12 ms reconstruction, then runs fixed zhq servo dynamics
(tau 35 ms, 4 ms / 250 Hz nominal), Java-compatible rounding and true glide.
ZhqTrackpadDynamics owns only math/state; ResampledMotion owns release markers,
profile boundaries and lifecycle fences. The existing MotionClock schedules both
profiles, without a second thread or native batching. C release waits for its
final UP playout marker, then stops attracting the target and glides. Requested
changes defer through the whole active chain; M waits for earned settlement,
C waits for natural glide stop. New DOWN during C release/glide restarts its
phase from the last natural carry and invalidates old targets/markers/wakes.
Phase Z2 human validation is complete: the user preferred Z1 Amax 80000 for its
stronger gimbal character. C source is precisely recovered to that Z1 baseline.
Phase M-F1 changes only M's reconstruction delay from 12 to 8 ms. The idle Active
profile boundary chooses delay; touching/settling/release/glide and joining or
interrupting DOWN never follow Requested early. C remains Z1 with 12 ms delay.
Saved Tau/Support, Q0-C, native output, clock period and lifecycle are unchanged.
This stage builds into isolated evidence directories and does not deploy/restart
the current live Receiver. Historical M12 goldens remain independent references.
Diagnostics identifies the actual Active algorithm, caps and cadence. H1's passive
record schema remains unchanged. See [MOTION_ENGINE.md](MOTION_ENGINE.md).
Research conclusions are consolidated in
[MOTION_RND_CONCLUSIONS.md](MOTION_RND_CONCLUSIONS.md).

Phase M-R1 adds the explicit `--dev-motion-mode M_R1` development identity;
normal production selection remains M-F1. Within this container M follows live
Sensitivity -> fixed 8 ms causal reconstruction -> direct R(t) -> Q0-C -> immediate
libvirtualhid, with the existing 1 ms MotionClock. Position convolution and filter
settlement are bypassed; the existing bounded history recorder, causal watermark,
late admission and queue safeguards remain. UP waits only for its mapped final
reconstruction boundary and Q0-C evaluation. A pending-release joining DOWN keeps
the target, ledger, M8 and phase. Requested changes apply only at idle completion.
C-Z1 retains its exact dynamics, C12, release/glide/carry, rounding and cadence.
Saved Tau/Support remain available to M-F1; M-R1 displays them as inactive.
M12 and M-F1 fingerprints remain independent, and M-R1 has separate oracle-reviewed
fingerprints. This stage uses isolated test outputs and does not deploy or restart
the live Receiver. See MOTION_ENGINE.md for evidence and the history-only boundary.

Responsibilities:

- Capture touchscreen input
- Collect historical touch samples
- Attach timestamps
- Create input packets
- Send packets through local network

For the mouse path, Android is only an input sensor.

Android does NOT:

- Calculate mouse movement
- Apply sensitivity
- Apply smoothing
- Detect mouse gestures (registered SlideControl gestures are the approved exception)
- Generate mouse events

---

## Windows Receiver

Responsibilities:

- Receive input packets
- Validate packet order
- Reconstruct touch stream
- Process motion
- Process gestures
- Apply configuration
- Generate Windows mouse input
- Submit generic full Xbox360 state through the independent libvirtualhid ABI2 backend
- Own committed Controls behavior, config epoch/revision, gamepad lease and minimum dwell

The receiver is the main control center.

### Screen Controls integration contract

`xbox.b.slide` is a registered SlideControl: Tap B, LongPress held B, Slide Up Y,
Slide Down A. Design A changes held B directly to Y/A as one complete state.
Only one finger/owner is active; crossing a control boundary never reassigns it.
The Android layout editor shares one rectangle for drawing and hit testing,
supports move/edge/corner/numeric X/Y/W/H, and persists only validated layout.
Default square and edited rectangles are both valid; Save commits, Cancel discards,
and Reset changes only the draft. Mode uses the same editor and stable geometry;
its manual profile switch produces no input. See [SCREEN_CONTROLS.md](SCREEN_CONTROLS.md).

Receiver disk-first Save publishes Controls defaults 0.7 dp / 3.0 dp / 25 ms /
400 ms and subsequent committed changes. Android's behavior cache is volatile;
each DOWN snapshots it. A failed Save cannot publish or change Android. Config
epoch/revision rejects stale updates, and type6 requests recover dropped pushes
on existing ports/workers. Layout persistence never stores behavior.

Type5 is 30-byte v2 full GAMEPAD_STATE with generic minimumDwellMs/flags/reserved;
Touch types 1–4 are unchanged. Non-Neutral refresh is 100 ms and Receiver lease is
300 ms. Ordinary Neutral waits until the local minimum dwell; safety release and
new non-Neutral replacement are immediate. The independent deadline task does not
use MotionClock or adapt to jitter. Xbox360 errors remain isolated from mouse.

---

# 3. Data Flow

Phase 8A adds a passive Android INPUT/XBOX/CONFIG health row. RPST v1 snapshots
reuse the existing reverse UDP 50002 worker/socket and listener every 500 ms.
Runtime backend state and committed config identities are the source of truth;
Android rejects stale target/run data and expires status after 1500 ms. No input
route, lease, Motion parameter or extra transport worker is introduced. See
[STATUS_PROTOCOL.md](STATUS_PROTOCOL.md).

Normal-click confirmation adds an independent Windows → Android Haptic Feedback
v1 side channel on UDP 50002. Windows alone recognizes a normal click; after its
successful LEFT DOWN and release scheduling, a bounded nonblocking enqueue feeds
an independent UDP worker. Android validates the current target/run and recent
raw UP identity, deduplicates and requests CONFIRM on the UI thread only while
foreground/connected. Feedback loss/failure never controls input correctness.
Touch v2, Discovery v1, RAW motion and gesture tuning remain unchanged. See
[HAPTIC_FEEDBACK_PROTOCOL.md](HAPTIC_FEEDBACK_PROTOCOL.md) for the wire, stale
event, queue and lifecycle contracts.

Complete path:

```text
Finger

 ↓

Android Touch Digitizer

 ↓

MotionEvent

 ↓

Historical Sample Extraction

 ↓

Input Packet Encoding

 ↓

UDP

 ↓

Windows Receiver

 ↓

Packet Validation

 ↓

Touch Session Processing

 ↓

Motion Engine

 ↓

Gesture Engine

 ↓

Mouse Output Backend

 ↓

Windows Input System

 ↓

Game
```

---

# 4. Android Architecture

Current touch diagnostic policy (2026-10-22): ordinary launches create no raw
touch CSV and emit no per-sample or per-packet success logs. A developer launch
with `--ez rightpad.diagnostic.touch true` enables one session, capped at 60 seconds
or 10,000 diagnostic events (samples plus send-success records, full-queue attempts
included), whichever comes first. Only this session creates a dedicated diagnostic
worker and a 256-entry queue. Capture submits unchanged real Touch input before
offering immutable sample references. The worker alone creates/writes/flushes/closes
the CSV and formats detailed logs; no diagnostic disk I/O runs on UI or UDP Sender.
Full buffers drop diagnostics and count losses, never modifying real input. File
errors stop diagnostics only. Pause, target loss/replacement, modal input changes,
detach, close and destroy fence the session and discard queued diagnostic work;
callers never join or drain it. A file operation already in progress can complete
before worker-owned close. Resume and Activity state restoration do not rearm it.
Old files are neither overwritten nor removed. Low-frequency lifecycle, connection
and failure logs remain. Protocol, acquisition, normal Touch queue/copies and
Receiver behavior stay unchanged. See [Android README](../android/README.md) and
[delivery evidence](ANDROID_TOUCH_DIAGNOSTICS_DELIVERY_20261022.md).

Recommended structure:

```text
Android App

├── Touch Capture
│
├── Sample Collector
│
├── Packet Encoder
│
└── Network Sender
```

Avoid:

- Complex UI framework dependency
- Game logic
- Mouse logic
- Gesture logic

The Android application should remain lightweight.

---

# 5. Windows Receiver Architecture

Recommended structure:

```text
Receiver

├── Network Layer
│
├── Input Decoder
│
├── Touch Session Manager
│
├── Motion Engine
│
├── Gesture Engine
│
├── Configuration Manager
│
├── Diagnostics
│
└── Mouse Backend
```

---

# 6. Module Responsibilities

## Network Layer

Only responsible for:

- UDP receiving
- Packet parsing
- Basic validation
- Protocol v2 sender-run admission, retired-run rejection and presence deadlines

It should not contain:

- Motion calculations
- Gesture logic

---

## Touch Session Manager

Responsible for:

- DOWN handling
- MOVE tracking
- UP handling
- Session IDs
- Timeout diagnostics and statistics

The current Prototype uses a 2-second input timeout for diagnostics only. A
timeout does not clear the active touch session, previous X/Y position, or
fractional residual. A stationary held finger may produce no new MotionEvent,
so Touch absence alone cannot distinguish stationary touch from a disconnected
sender. Protocol v2 independently uses heartbeat or accepted Touch presence with
a local monotonic 2000 ms deadline. Presence expiry clears session, previous
position, residual, gesture and pending/held button input but keeps the run's
sequence baseline. A new senderRunId admitted by HEARTBEAT/DOWN also clears that
baseline. Runtime, socket, cumulative counters and settings remain alive.

Android retains one Sender thread, one socket and one Touch queue. It generates
one random 64-bit runId per Sender creation and rotates it on discovery target
transitions, which clear capture/queue and reset Touch sequence. Confirmed
same-target pause/resume preserves identity. A timed poll services 500 ms heartbeat
deadlines outside the Touch queue. Discovery has its own socket/thread/schedule;
it never enters the input hot path. See INPUT_PROTOCOL.md: Protocol v2 CURRENT;
v1 remains historical. No service or new user setting is added.

Receiver owns this processing on its existing sequential background input path.
An immutable presence snapshot and atomic counters are read by WPF at 5 Hz;
packet/heartbeat handling never calls Dispatcher. Expiry also runs while the
socket is idle, without relying on UI polling. Practicality First: this extension
serves explicit connection state and Sender restart recovery only; transport
optimization remains out of scope.

Example:

```text
Session 100

DOWN

MOVE

MOVE

UP


Session 101

DOWN

MOVE
```

A new touch session must never inherit previous coordinates.

---

## Motion Engine

Responsible for:

Input:

```text
Touch position samples
```

Output:

```text
Relative mouse movement
```

Responsibilities:

- Calculate delta movement
- Apply selected motion processing
- Apply sensitivity
- Preserve fractional movement

Does NOT handle:

- Clicks
- Drag
- Buttons

---

## Gesture Engine

Responsible for:

- Tap detection
- Double tap drag

Does NOT modify:

- Mouse movement algorithm
- Sensitivity
- Motion smoothing

---

## Mouse Backend

Responsible for:

- Sending mouse movement
- Sending left button events

Production backend: **libvirtualhid Virtual HID Mouse**. The sole product default
is `MouseBackendDefaults.Production`; Program passes the selected backend explicitly
to ReceiverRuntime. No backend is implicit in the Runtime constructor. Tests inject
fake output; backend-specific tests select their backend explicitly.

Normal GUI, quoted-EXE HKCU login startup and the development launcher without an
override inherit that one default. SendInput remains available through the explicit
`--dev-mouse-backend sendinput` development/diagnostic compatibility override;
`virtualhid` is also accepted. There is no automatic fallback or user backend setting.
Initialization failure enters Runtime Error with LastError and diagnostics.

The adoption decision follows human A/B with no obvious subjective degradation,
Raw Input visibility, working Medium Receiver input into High-integrity foreground,
the measured SendInput limitation there, and validated POC/build/runtime plus
Driver/Broker/Lifetime license prerequisites. It establishes no lower-latency or
higher-polling-rate claim; objective latency benchmarking is still outstanding.
See [VIRTUAL_HID_MOUSE_POC.md](VIRTUAL_HID_MOUSE_POC.md) for the existing bridge,
ownership, dependency and error contracts.

---

# 7. Thread Model

Initial design:

```text
Network Thread

        ↓

Input Processing

        ↓

Motion / Gesture Processing

        ↓

Mouse Output
```

Do not add:

- Multiple worker pools
- Lock-free structures
- Complex schedulers

unless measurement proves they are required.

---

# 8. Configuration Location

All user tuning parameters belong to Windows Receiver.

Examples:

Motion:

- Sensitivity
- Smoothing parameters

Gesture:

- Tap duration
- Double tap interval
- Click hold time

Reason:

- Easier adjustment
- No Android rebuild
- Better testing workflow

---

# 9. Design Principles

## Deterministic Behavior

Same input should produce:

Same output.

Avoid hidden dynamic behavior.

---

## Measure Before Optimize

Do not add:

- New filters
- New prediction systems
- New protocol layers

without measurement.

---

## Game-First Engineering Rule

rightpad is a game-first input system; necessary complexity is justified when it
produces measurable gaming benefit. See `AGENTS.md` for the binding engineering rule.
Monetary cost is not a primary architectural constraint; vendor dependency,
activation reliability, deployment risk and maintainability still are.

---

# 10. Current Non-Goals

Not part of initial architecture:

- Video streaming
- Audio streaming
- Cloud service
- Accounts
- Multi-device support
- iOS client
- Linux support
- Multi-touch gestures
- Gyroscope control
- Automatic game profiles
- AI optimization

These may only be considered after the core input system is proven.
