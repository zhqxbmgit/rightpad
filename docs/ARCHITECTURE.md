# rightpad System Architecture

## 1. Overview

rightpad is a dedicated Android-to-Windows gaming input system.

The system converts an Android phone touchscreen into a high-quality relative mouse input device.

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

Responsibilities:

- Capture touchscreen input
- Collect historical touch samples
- Attach timestamps
- Create input packets
- Send packets through local network

Android is only an input sensor.

Android does NOT:

- Calculate mouse movement
- Apply sensitivity
- Apply smoothing
- Detect gestures
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

The receiver is the main control center.

---

# 3. Data Flow

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

Possible implementations:

- Initial: standard Windows input API
- Future: virtual HID if required

Do not add driver complexity before compatibility testing.

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
