# rightpad Gesture Engine Design

## 1. Purpose

Gesture Engine converts touch timing behavior into mouse button actions.

Current supported gestures:

- Single tap → Left click
- Double tap drag → Left button drag

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

---

# 3.2 Double Tap Drag

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

Initial values:

```text id="7wj4du"
tapMaxDurationMs = 300

doubleTapIntervalMs = 130

clickHoldMs = 25
```

Advanced:

```text id="40w8na"
tapMovementThreshold
```

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

Initial implementation:

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

The system must guarantee:

No stuck buttons.

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