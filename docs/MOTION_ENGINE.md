# rightpad Motion Engine Design

## 1. Purpose

Motion Engine converts raw touch movement into relative mouse movement.

Input:

```text id="3j2f7n"
Touch Position Samples
```

Output:

```text id="0n6x0a"
Relative Mouse Movement
```

The Motion Engine is the core component responsible for the physical feeling of the touchpad.

---

# 2. Design Goals

The desired feeling:

- Smooth
- Stable
- Predictable
- Low latency
- No shaking
- Suitable for games

The system should feel more stable than a normal touchpad while preserving touchpad behavior.

---

# 3. Fundamental Rules

## Rule 1: Relative Movement

The system is a relative touchpad.

Meaning:

```text id="u0e5pq"
Finger moves

↓

Mouse moves


Finger stops

↓

Mouse stops
```

It is NOT a joystick.

---

## Rule 2: No Inertia

After finger release:

Allowed:

```text id="xq8v3m"
Almost invisible residual processing delay
```

Not allowed:

```text id="zv5vfo"
Finger stops

↓

Mouse continues moving noticeably
```

Do not implement:

- Glide
- Momentum
- Scroll-like inertia

---

## Rule 3: Deterministic Feel

Runtime Motion behavior must be stationary: the same physical finger movement
under the same selected fixed configuration should produce the same mouse response.
Deterministic feel prohibits all runtime-adaptive motion shaping, not only dynamic
sensitivity and speed-based gain.

Every feel-affecting value used by a selected configuration must remain fixed,
including any mapping, sensitivity, gain, filter coefficients, time constants,
damping parameters, windows, output cadence and playout timing. The implementation
must not alter them in response to the current input or environment.

Avoid:

- Dynamic sensitivity
- Speed-based gain
- Adaptive smoothing, tau, damping, FIR coefficients, windows or deadzones
- Adaptive playout delay or jitter-buffer depth
- Velocity-, noise-, sample-rate-, network-, FPS-, game- or system-load-based
  parameter changes or mode switching
- Game-dependent behavior

For example, this is prohibited:

```text
if speed < threshold:
    tau = 8 ms
else:
    tau = 3 ms
```

So are `slow = heavy filtering, fast = raw` and automatic switching between a
micro-aiming algorithm and a fast-turn algorithm. A favorable benchmark or
theoretical Pareto tradeoff does not override the fixed-feel requirement.

Development may evaluate different fixed configurations through offline data,
automated tests and human game A/B. Each candidate must use fixed values throughout
its run, and the selected runtime configuration must keep those values fixed.

### Reliability Fallbacks Are Not Adaptive Feel

Correctness, safety and lifecycle handling may use fixed deterministic fallbacks
for malformed input, missed deadlines, buffer overflow, session/run invalidation,
shutdown and similar faults. A missed output deadline may skip an obsolete tick and
resume the same fixed cadence. These fallbacks must not modify subsequent normal
gain, tau, coefficients, window, playout delay, mapping or Motion mode.

### Fixed Algorithms May Still Be Sophisticated

This rule does not limit Rightpad to simple algorithms. A fixed-coefficient FIR,
fixed-window reconstruction, fixed second-order follower or fixed resampling design
may be evaluated when its parameters are fixed, its behavior is deterministic and
its benefit is measured. The goal remains relative displacement semantics with
gimbal-like trajectory quality. Do not substitute velocity control, dynamic gain,
glide or inertia for that goal.

---

# 4. Processing Pipeline

Current target pipeline:

```text id="my1w23"
Raw Touch Sample

        ↓

Position Delta Calculation

        ↓

Motion Filtering

        ↓

Sensitivity Scaling

        ↓

Fractional Accumulator

        ↓

Mouse Output
```

---

# 5. Raw Input Handling

Android sends:

```text id="3n2i0y"
x
y
timestamp
sessionId
```

The receiver calculates:

```
dx = currentX - previousX

dy = currentY - previousY
```

The receiver must not depend on Android-generated dx/dy.

---

# 6. Motion Filtering

Filtering exists only to reduce:

- Sensor noise
- Micro jitter
- Unstable sampling artifacts

Filtering must NOT create:

- Noticeable lag
- Mouse drift
- Inertia
- Different behavior at different speeds

---

# 7. Candidate Algorithms

The implementation should support testing multiple algorithms.

Initial candidates:

---

## Mode 0: Raw

Formula:

```
output = delta
```

Purpose:

- Baseline measurement
- Determine whether filtering is actually needed

---

## Mode 1: Short FIR

Concept:

```
output =
currentDelta * A
+
previousDelta * B
```

Example:

```
A = 0.8

B = 0.2
```

Properties:

Advantages:

- Very low delay
- Small memory
- Stable behavior

Disadvantages:

- Limited noise removal

---

## Mode 2: Second Order Follower

A second-order critically damped model may be evaluated.

Reference behavior:

```
Target Position

        ↓

Spring-Damper System

        ↓

Current Position
```

Important:

The reference model must NOT include:

- Glide
- Velocity cap
- Acceleration cap
- Dynamic behavior

The purpose is only to evaluate smooth tracking behavior.

---

# 8. Algorithms Not Allowed by Default

Do not implement:

## Mouse Acceleration

Example:

```
Fast movement
=
higher sensitivity
```

Reason:

Changes physical response.

---

## Adaptive Filtering

Example:

```
Slow movement
=
heavy smoothing

Fast movement
=
light smoothing
```

Reason:

Creates different touch feeling.

---

## No Runtime-Adaptive Feel

Do not implement:

- Adaptive smoothing strength
- Adaptive follower tau or damping
- Adaptive FIR coefficients or window length
- Adaptive deadzone
- Adaptive playout delay or jitter-buffer depth
- Velocity- or acceleration-based mode switching
- Noise- or sampling-rate-based mode switching
- Packet-jitter- or network-based mode switching
- FPS-, game- or system-load-based mode switching

These remain prohibited even when the adaptation is continuous or subtle rather
than a named mode switch. Candidate values may be compared as separate fixed
experiments, but a candidate range must not become a runtime controller.

---

## Prediction

Example:

```
Guess future finger movement
```

Reason:

Can create unwanted movement after stopping.

---

## AI Optimization

Not appropriate for deterministic input.

---

# 9. Sensitivity

Sensitivity is a fixed multiplier.

Formula:

```
mouseDelta =
filteredDelta × sensitivity
```

Example:

```
sensitivity = 1.0
```

means:

```
1 unit touch movement

=

1 unit mouse movement
```

No hidden curves.

---

# 10. Fractional Motion Accumulator

Mouse output often requires integer movement.

Example:

```
0.35
0.42
0.39
```

must not become:

```
0
0
0
```

Instead:

```
accumulator += movement
```

When accumulated value reaches:

```
>= 1
```

send integer movement.

Remaining fraction is preserved.

Benefits:

- Better slow movement
- Less quantization
- No directional drift

---

# 11. Distance Preservation

The Motion Engine should preserve total movement.

Example:

Input:

```
10 pixels
```

should eventually produce:

```
10 units × sensitivity
```

Filtering should not permanently remove movement.

---

# 12. Stop Behavior

Stop response is critical.

Test:

```text id="1z6q7p"
Finger movement

────────────

STOP
```

Expected:

```text id="o7b9gu"
Mouse movement stops immediately
```

Avoid:

```
──────────────→→→
```

---

# 13. Direction Change Behavior

Test:

```text id="g4u4pl"
Move right

↓

Immediately move left
```

The system should:

- Change direction quickly
- Not feel sticky
- Not have a long tail

---

# 14. Performance Requirements

Motion processing runs at high frequency.

Avoid:

- Object allocation in hot path
- Heavy calculations
- Blocking operations

Prefer:

- Simple math
- Small state
- Predictable execution time

---

# 15. Diagnostics

The Motion Engine should expose:

- Input sample rate
- Output rate
- Processing latency
- Current filter mode
- Current sensitivity

The system should be measurable.

---

# 16. Development Method

Do not decide algorithms by theory alone.

Use:

1. Raw baseline
2. Record real touch traces
3. Replay traces
4. Compare algorithms
5. Test real games

Metrics:

- Added latency
- Movement variance
- Stop distance
- Direction accuracy
- Subjective feel

---

# 17. Current Philosophy

The best Motion Engine is not the most complicated one.

The preferred solution:

- Small
- Deterministic
- Low latency
- Easy to tune
- Easy to understand

Complexity must be justified by measurable improvement.
