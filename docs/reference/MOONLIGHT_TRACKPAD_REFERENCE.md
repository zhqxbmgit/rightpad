# Moonlight Trackpad Reference Analysis

## Purpose

This document records analysis results from a previous Moonlight-based Android trackpad implementation.

This document is a reference only.

It is NOT a specification for the new gaming touchpad project.

The new project may reuse useful concepts, but must not inherit unnecessary architecture, features, or constraints from the reference implementation.

---

# 1. Reference Project Summary

The reference implementation is a streaming application with a relative touchpad mode.

Its purpose is different from the new project:

Reference project:

- Remote streaming application
- Needs compatibility with many devices
- Supports general remote control scenarios
- Contains additional gesture and streaming-related logic

New project:

- Dedicated gaming input device
- Single finger only
- Low latency priority
- Windows 11 only
- No video/audio streaming
- No unnecessary features

Therefore:

Do not directly copy the reference architecture.

---

# 2. Valuable Design Concepts

## 2.1 Fixed-rate motion output

The reference implementation does not immediately convert every touch event into mouse movement.

Instead:

```
Touch Input
    ↓
Target Position
    ↓
Fixed Time Update Loop
    ↓
Mouse Output
```

Benefits:

- More stable output timing
- Reduced touch sampling irregularity
- More consistent movement feeling

This concept is valuable.

The new project should evaluate fixed-rate output scheduling, but should not automatically copy the original implementation.

---

## 2.2 Historical MotionEvent Samples

The reference implementation correctly reads Android historical touch samples.

Important concept:

Android touch callback frequency is not equal to actual touch sampling frequency.

A MotionEvent may contain:

```
Historical Sample
Historical Sample
Current Sample
```

Ignoring historical samples can reduce effective input quality.

The new project should preserve this principle.

---

## 2.3 Floating point motion accumulation

The reference implementation preserves fractional movement.

Example:

```
Input movement:
0.37
0.42
0.39

Total:
1.18
```

Instead of discarding:

```
0
0
0
```

it accumulates the fractional remainder.

This prevents:

- Slow movement loss
- Quantization jumps
- Direction drift

The new project should keep this design.

---

# 3. Reference Motion Algorithm

The reference implementation uses a second-order critically damped follower.

Conceptually:

```
Target Position
        ↓
Spring-Damper System
        ↓
Current Position
        ↓
Mouse Movement
```

The model:

```
error = target - current

acceleration =
error * omega²
-
velocity * 2 * damping * omega
```

with:

```
damping ≈ 1
```

This creates smooth tracking behavior.

---

# 4. Advantages of the Second-Order Model

Observed advantages:

- Stable movement
- Smooth constant-speed feeling
- Reduced micro jitter
- Less raw touch noise

This likely contributes significantly to the "joystick-like stability" feeling.

---

# 5. Problems of the Reference Algorithm

The following behaviors are NOT desired for the new project.

## 5.1 Inertia / Glide

The reference implementation supports glide after finger release.

Behavior:

```
Finger stops
    ↓
Mouse continues moving
```

The new project does not want this.

Requirement:

```
Finger stops
    ↓
Mouse stops
```

Any residual motion must be extremely small and practically unnoticeable.

---

## 5.2 Large tracking lag

The reference implementation uses a relatively large smoothing time constant.

Advantages:

- Very stable

Disadvantages:

- Position follows target with delay
- Reduced responsiveness

The new project should investigate shorter response times.

---

## 5.3 Velocity and acceleration limiting

The reference implementation contains:

- Maximum velocity
- Maximum acceleration

These introduce nonlinear behavior.

Potential result:

```
Slow movement feeling
≠
Fast movement feeling
```

The new project should not include these unless real testing proves they are needed.

---

# 6. Important Lesson

The reference implementation demonstrates:

Smoothness does not require complicated AI prediction or adaptive algorithms.

A simple deterministic control model can already provide good input quality.

The new project should prioritize:

```
Predictability
+
Low latency
+
Stable response
```

over:

```
Smart adaptation
+
Complex behavior
```

---

# 7. New Project Direction Compared With Reference

| Feature | Reference | New Project |
|---|---|---|
| Input | Relative touchpad | Relative touchpad |
| Fingers | Multiple possible | Single finger |
| Platform | Android streaming client | Android dedicated controller |
| PC | Streaming host | Windows 11 receiver |
| Motion | Second-order follower | To be evaluated |
| Glide | Supported | Disabled |
| Dynamic behavior | Some | Avoid |
| Historical samples | Used | Keep |
| Fractional accumulator | Used | Keep |
| Gesture | Many | Tap + double tap drag only |
| Network | Streaming protocol | Dedicated low latency protocol |

---

# 8. Final Reference Conclusion

The reference project proves several important ideas:

1. Historical touch samples matter.
2. Fractional motion accumulation improves precision.
3. Fixed-time processing can improve subjective smoothness.
4. Deterministic motion processing is preferable to raw touch forwarding.

However:

The new project should not become a modified Moonlight client.

The goal is a dedicated gaming input system with:

- Lower latency
- Less inertia
- More predictable response
- Simpler architecture
- Fewer unrelated features

Reference concepts should be selectively reused only when they improve measurable input quality.