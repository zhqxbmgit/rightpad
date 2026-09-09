# rightpad Gesture Engine Design

## 1. Purpose

Gesture Engine converts touch timing behavior into mouse button actions.

Current implementation:

- Single tap → Left click: implemented; human validation passed.
- Double tap drag → Left button drag: pending, future design only.

The Gesture Engine does NOT control mouse movement.

---

# 2. Design Principles

## Separation

Gesture processing and motion processing are independent.

Architecture:

```text id="s8az3v"
Raw Touch Stream

        │
        │
        ├───────────────→ Motion Engine
        │                     │
        │                     ↓
        │               Mouse Movement
        │
        │
        └───────────────→ Gesture Engine
                              │
                              ↓
                         Mouse Buttons
```

---

# 3. Supported Gestures

## 3.1 Single Tap

Purpose:

Generate:

```text id="z6j6x8"
Left Mouse Click
```

Sequence:

```text id="y7n9az"
DOWN

↓

UP
```

Conditions:

- Duration <= tapMaxDurationMs
- Movement <= tapMovementThreshold

The Windows `GestureProcessor` consumes decoded, sequence-accepted raw packets
independently of `TouchSessionProcessor`. DOWN stores the session, original X/Y
and Android eventTimeNs. Every MOVE sample, including historical samples in the
middle of a packet, is checked in original order. Bounds are inclusive and
axis-aligned: `abs(x - downX) <= 8` AND `abs(y - downY) <= 8` by default.
Once either axis exceeds the threshold, `confirmedMove` stays true until the
session ends or a new DOWN arrives; returning to the origin cannot restore a tap.
UP must match the session, remain in bounds, and have a nonnegative event-time
duration of at most 300 ms. Wrong-session MOVE/UP are ignored. Duplicate, old and
malformed packets are excluded by the existing accepted-input gate.

RAW movement, including small tap movement and UP's final delta, is not suppressed,
rolled back, or altered. A qualifying UP consumes the candidate and requests one
local LEFT DOWN followed by a timed LEFT UP. There is no double-tap recognition.

---

# 3.2 Double Tap Drag

Pending. The following describes a future feature, not current behavior.

Purpose:

Provide:

```text id="y8w5ef"
Touchpad drag operation
```

Sequence:

```text id="m4g5zk"
First Tap:

DOWN

UP


Second Tap:

DOWN

↓

Left Button Down

↓

Finger Movement

↓

UP

↓

Left Button Up
```

The second tap immediately enters drag mode.

No additional hold delay is required.

---

# 4. Default Parameters

The following parameters belong to Windows Receiver.

Defaults verified read-only in Moonlight Noir `PreferenceConfiguration.java`
and `TrackpadContext.java` (2026-09-08):

```text id="7wj4du"
tapMaxDurationMs = 300

doubleTapIntervalMs = 130

clickHoldMs = 25
```

Current movement parameter:

```text id="40w8na"
tapMovementThresholdPx = 8
```

`doubleTapIntervalMs = 130` is recorded only as a future reference; no double-tap
CLI parameter is implemented. Current Windows CLI options (require `--raw-mouse`):
`--tap-max-duration-ms`, `--tap-movement-threshold-px`, `--click-hold-ms`.
Durations must be positive int32 whole milliseconds; the threshold must be finite
and positive. Defaults are 300 / 8 / 25. WPF v1 supports live settings and local persistence; see RECEIVER_UI.md.

---

# 5. Parameter Explanation

## tapMaxDurationMs

Maximum duration for a tap.

Example:

Valid:

```text id="1x3a9z"
DOWN

(100ms)

UP
```

Invalid:

```text id="h4k8t3"
DOWN

(800ms)

UP
```

because it is likely intentional touch or drag behavior.

---

## doubleTapIntervalMs

Maximum interval between first tap and second tap.

Example:

```text id="0b3x5s"
UP

↓

100ms

↓

DOWN
```

Valid if:

```text id="87p6k2"
100ms <= doubleTapIntervalMs
```

---

## clickHoldMs

Minimum mouse button down duration for a click.

Purpose:

Some software samples mouse buttons periodically.

A very short click may not be detected.

Example:

```text id="1gq8xs"
LEFT DOWN

↓

25ms

↓

LEFT UP
```

---

# 6. Tap Movement Threshold

A tap should not trigger after intentional movement.

Example:

Valid:

```text id="5v8m4j"
DOWN

small finger movement

UP

=

Click
```

Invalid:

```text id="q1f8gz"
DOWN

large movement

UP

=

Not a click
```

This prevents accidental clicks during movement.

---

# 7. Gesture State Machine

Current Single Tap state machine:

```text
IDLE -- DOWN --> candidate
candidate -- any sample out of bounds --> confirmedMove (latched)
candidate -- valid matching UP --> request click --> IDLE
candidate / confirmedMove -- other matching UP --> IDLE
any state -- new accepted DOWN --> new candidate
```

Future Double Tap Drag design (not implemented):

```text id="q7w9i8"
IDLE

 |
 | DOWN
 v

TOUCHING

 |
 |
 +----------------+
 |                |
 | movement       | release
 | exceeds        |
 | threshold      |
 |                |
 v                v

MOVING          TAP_CANDIDATE


                    |
                    |
                    +-----------+
                                |
                                |
                         wait for second tap
                                |
                                |
                                v

                         DOUBLE_TAP_WINDOW


                                |
                                |
                         second DOWN

                                |
                                v

                             DRAGGING
```

---

# 8. Interaction With Motion Engine

Important:

Gesture recognition must not modify motion behavior.

During drag:

```text id="7h2qcm"
Finger movement

↓

Normal Motion Engine

↓

Mouse movement

+

Left Button Held
```

Do NOT:

- Change sensitivity
- Change smoothing
- Add acceleration
- Add special drag mode

Dragging should feel identical to normal movement.

Only the button state changes.

---

# 9. Button Safety

Current Single Tap release is driven by a Windows-local one-shot
`System.Threading.Timer` (default 25 ms); it needs no further Android packet and
never synchronously waits in the UDP receiver. Timer scheduling can release later
than the requested duration. Overlapping tap requests are serialized: each pending
click starts after the previous LEFT UP, preserving each hold and button order.

`LeftButtonController.Dispose` attempts LEFT UP if a button may still be held,
including normal shutdown and exception unwinding. Native button errors are logged;
an asynchronous timer failure cancels the receive loop and causes a nonzero exit.
A failed UP receives a best-effort cleanup attempt; Dispose can attempt release
again if still held. Forced process termination or a persistent native failure
cannot be guaranteed recoverable.

The 2-second input timeout remains diagnostic only and preserves motion/session
state. Sender-disconnect detection and future drag stuck-button handling remain
deferred; no heartbeat, CANCEL wire event, or connection state machine is added.

Future button safety requirements:

Possible causes:

- UDP loss
- App crash
- Network disconnect

Receiver must always be able to:

```text id="1p0l7q"
Release Left Button
Reset Gesture State
```

---

# 10. No Additional Gestures

The initial version intentionally does NOT support:

- Right click
- Scroll
- Two finger gestures
- Three finger gestures
- Pinch zoom
- Long press menu
- Gesture shortcuts

Reason:

The project is a gaming input device, not a general laptop touchpad.

---

# 11. Configuration Philosophy

Gesture parameters should be:

- Stored on Windows Receiver
- Adjustable without rebuilding Android
- Saved locally

WPF v1 auto-saves local settings. Duration/threshold are captured at DOWN; click hold is captured for each click request and retained while queued. Explicit dev CLI tuning remains available.

Android should not contain user tuning values.

---

# 12. Testing Requirements

Gesture testing should include:

## Single Click

Tests:

- Fast tap
- Slow tap
- Accidental movement

---

## Double Tap Drag

Tests:

- Fast double tap
- Slow double tap
- Drag immediately after second tap
- Long drag
- Release after network interruption

---

# 13. Implementation Principle

Keep the Gesture Engine small.

Do not introduce:

- General gesture frameworks
- Machine learning classifiers
- Complex event systems

The required behavior is simple and deterministic.
