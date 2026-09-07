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
- Reset after timeout

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

## Simplicity First

A smaller system with predictable behavior is preferred over a larger system with uncertain benefits.

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