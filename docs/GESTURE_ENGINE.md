# rightpad Gesture Engine Design

## 1. Purpose

Gesture Engine converts touch timing behavior into mouse button actions.

Current implementation:

- Single tap → Left click: implemented; human validation passed.
- Double tap drag → Left button drag: implemented; current validation is tracked in PROJECT_STATE.md.

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
local LEFT DOWN followed by a timed LEFT UP. The same valid UP arms one subsequent DOWN for Double Tap Drag, without delaying this click.

---

# 3.2 Double Tap Drag

Implemented on Windows; Android and Protocol v2 remain unchanged.

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

A valid first tap has already requested its normal click. Its UP arms one subsequent
accepted DOWN. At that DOWN, `0 <= secondDown.TimestampNs - firstUp.TimestampNs
<= DoubleTapIntervalMs * 1_000_000` starts LEFT hold immediately. Both times are
Android MotionEvent timestamps, never UDP arrival time. No spatial distance limit
applies between contacts. The interval is read from the second DOWN settings snapshot.
The arm is consumed on that DOWN whether it matches, expires, or has backward time.
An expired second contact can finish as a new normal tap and arm the next drag.

A drag contact has no 300 ms duration or 8 px movement limit. Its matching UP releases
LEFT exactly once, then rearms the Double Tap window from that UP's Android
`TouchSample.TimestampNs`. The next DOWN can therefore begin another drag directly
when its nonnegative event-time delta is within the inclusive interval; every matching
drag UP repeats this behavior, including a drag with no MOVE. A new accepted replacement
DOWN ends a lost-UP drag without rearming before establishing the next normal contact.
Wrong-session and rejected stale/duplicate/invalid packets cannot release or arm a drag.

Design reference: [zhq TrackpadContext.java, moonlight-noir](https://github.com/zhqxbmgit/zhq/blob/moonlight-noir/app/src/main/java/com/limelight/binding/input/touch/TrackpadContext.java).
The immediate click / second-DOWN hold and drag-UP rearm interactions are adopted.
This matches `TrackpadContext.touchUpEvent()`, where a drag UP refreshes the last-tap-UP
event time, and supports rapid lift/recontact continuous dragging. The reference motion
engine is not copied.

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

`doubleTapIntervalMs = 130` is the product default (integer 50–1000 ms). Current Windows CLI options (require `--raw-mouse`):
`--tap-max-duration-ms`, `--tap-movement-threshold-px`, `--click-hold-ms`, `--double-tap-interval-ms`. The old `--double-tap-interval` name is rejected.
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

The current contact, one-shot double-tap arm, and dragging state are separate:

```text
IDLE -- DOWN --> candidate
candidate -- any sample out of bounds --> confirmedMove (latched)
candidate -- valid matching UP --> immediate click + arm --> IDLE
candidate / confirmedMove -- invalid matching UP --> IDLE
armed IDLE -- next DOWN inside event-time interval --> consume arm + LEFT DOWN --> DRAGGING
armed IDLE -- next DOWN outside interval/backward --> consume arm --> candidate
DRAGGING -- MOVE --> unchanged RAW movement with LEFT held
DRAGGING -- matching UP --> LEFT UP + arm from Android UP TimestampNs --> armed IDLE
armed IDLE -- repeated DOWN/drag UP inside each new interval --> continuous DRAGGING chain
any state -- lifecycle Reset + button cleanup --> unarmed IDLE / LEFT neutral
```

`FinishCurrentContact` preserves a valid tap arm; external `Reset` clears contact,
arm and drag state. The existing outer cleanup then releases the button.

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

Normal clicks also produce [Haptic Feedback v1](HAPTIC_FEEDBACK_PROTOCOL.md).
Only the final normal-tap branch supplies the accepted UP identity. Each click's
callback runs after its actual successful LEFT DOWN and timer scheduling, including
when delayed in the existing click queue; cancellation discards queued callbacks.
Drag/NoMoveDrag/RearmDrag DOWN and UP never invoke it. The callback only attempts
a bounded enqueue; network send runs independently. A later failed LEFT UP still
causes the existing Runtime Error; this feedback confirms click initiation, not
game consumption. All gesture parameters and state transitions are preserved.

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

`BeginDrag` cancels the click timer and pending clicks, completes an unexpectedly
still-held click with UP, then emits drag DOWN. `EndDrag` releases once through the
existing controller cleanup. A queued old timer callback cannot release a drag.
No new scheduler, movement algorithm, or <25 ms race framework is introduced.

The 2-second input timeout remains diagnostic only, so a stationary drag survives
3 seconds and longer while heartbeats keep presence alive. Sender run change,
2000 ms presence timeout, Receiver Stop, Dispose and output failure clear gesture
qualification and perform the existing best-effort LEFT release. Persistent native
failure cannot guarantee physical release; it remains a visible Runtime Error.

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

The Tap page adds Double Tap Interval, ms, range 50–1000 and step 10. Missing
`doubleTapIntervalMs` in an older settings.json silently uses 130; a present invalid
value falls back with the existing warning. The next normal save writes the new
field while preserving all existing tuning. Drag holds are unaffected by hot updates.

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
