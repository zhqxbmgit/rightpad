# rightpad System Architecture

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
and Reset changes only the draft. See [SCREEN_CONTROLS.md](SCREEN_CONTROLS.md).

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
