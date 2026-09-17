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

- Smooth continuous camera motion
- Low wobble and stable velocity
- Natural, gimbal-like visual stability
- Precise micro-control
- Natural long-session control feel
- Fixed, predictable and muscle-memory-consistent behavior
- Measured fast-turn, reversal, stop and latency behavior

The system should feel more stable than a normal touchpad while preserving touchpad behavior.

Rightpad is game-first, but it is not esports-latency-first. Absolute minimum
latency, shortest reversal delay and shortest stop tail are not the highest Motion
priorities when achieving them would sacrifice a meaningful improvement in
continuous visual stability, micro-control or comfort. These response costs remain
important measurements and cannot degrade without bound. A fixed, deterministic
cost is evaluated together with its smoothness benefit through objective evidence
and human game testing; there is no universal millisecond acceptance threshold.

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

After finger movement ends, the target displacement stops changing. The normal
product meaning of `Mouse stops` is that Motion creates no new artificial movement,
does not drift indefinitely and does not behave like a velocity-controlled
joystick. It is not a requirement that every valid fixed filter produce exactly
zero output at the same instant as the final finger sample.

Allowed:

```text id="xq8v3m"
Fixed deterministic filter settling that finishes already-earned displacement
```

Not allowed:

```text id="zv5vfo"
True target is complete

↓

Old velocity creates additional distance beyond that target
```

Do not implement:

- Glide
- Momentum
- Scroll-like inertia

### Settling vs Glide

Settling is completion of previously accumulated real displacement. Once the true
target is fixed, a fixed filter or follower may continue a bounded or converging
response until it reaches that already-existing target. Its duration, coefficients
and endpoint behavior must be fixed, deterministic and measurable, and its effect
on control must be validated with human game testing.

Glide or artificial inertia creates distance that is not required to reach the
real target, commonly by continuing to integrate old velocity. It remains
prohibited. A fixed filter may finish already-earned displacement; it may not
invent new displacement. UP and lifecycle behavior must prevent unbounded or stale
post-contact motion.

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
- Measured wobble and velocity variation in continuous movement

Filtering must NOT create:

- Unmeasured or uncontrolled response cost
- Mouse drift
- Inertia
- Different behavior at different speeds

A stronger fixed filter is eligible when it produces a meaningful measured gain
in smoothness, wobble attenuation, velocity stability or natural camera motion.
Its fixed latency, reversal, stop/settling and UP costs must be reported and tested,
but a nonzero fixed cost does not by itself disqualify the filter.

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

Stop response must be measured and validated together with smoothness and
micro-control. The shortest tail does not automatically win.

Test:

```text id="1z6q7p"
Finger movement

────────────

STOP
```

Expected:

```text id="o7b9gu"
No new artificial distance is created

Any fixed filter settling completes only already-earned displacement
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
- Have a fixed, predictable response

Long or disruptive reversal behavior may make a candidate unacceptable, but a
candidate is not rejected solely because its fixed reversal delay is longer than
the current baseline. Objective measurements and real-game control decide whether
the tradeoff is worthwhile.

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

- Continuous smoothness and wobble attenuation
- Parallel velocity stability and orthogonal motion
- Micro-control and small-displacement completion
- Path preservation, endpoint and UP behavior
- Fast-turn response and direction accuracy
- Reversal response
- Stop and settling distance or duration
- Added fixed latency
- Subjective real-game feel and long-session comfort

Compare fixed candidates as a multi-dimensional Pareto problem. Do not assume that
lowest latency, shortest stop tail or closest resemblance to Moonlight wins. A
candidate must satisfy the fixed-feel and Relative Mouse rules before its quality
tradeoffs are considered.

## Reference Baselines

- RAW is the direct relative-motion reference and historical baseline. It does not
  define the final quality ceiling.
- B is the current human-preferred Rightpad baseline: fixed 250 Hz output
  opportunities, fixed 12 ms playout and timestamp-aware reconstruction. It is a
  baseline for further comparison, not a preselected final Motion Engine.
- Moonlight TrackpadContext is a known-smoother reference and design evidence. Its
  approximate 4 ms ticker, second-order follower, default time constant around
  35 ms, velocity and acceleration caps, glide, quantization and output semantics
  describe that implementation; they are not a required Rightpad architecture or
  parameter set. In particular, its glide remains prohibited for Rightpad.

Rightpad should seek the best fixed-feel Motion design under its own requirements
and, where practical, outperform both B and the current Moonlight reference.
Moonlight is not a target, upper bound or gold standard, and no current evidence
establishes that Rightpad has already surpassed it.

---

# 17. K24 Earned-Settle Lifecycle (2026-09-15; product status updated 2026-09-16)

Mode `RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5_SETTLE` retains the K24-r5
kernel (tau 24 ms, finite support 120 ms), Q0-C, 250 Hz / 4 ms opportunities,
12 ms playout, startup-fixed sensitivity and libvirtualhid output. The original
`RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5` remains the instant-flush control.
This lifecycle began as a research candidate. The selected production configuration
is now the 1000 Hz K24-r5 Earned-Settle mode. The 250 Hz and 500 Hz variants remain
explicit development, regression, benchmark and diagnostic modes. This status
supersedes the earlier prototype and product-selector classifications.

UP includes its last real coordinate, freezes the earned cumulative target and
ends the touch session for Gesture. It emits no immediate Motion flush. The same
MotionClock continues evaluating the original K24 against the realized history
until the full finite support has become constant. The normal Q0-C submission
completes that endpoint, then the clock parks and the completed chain is cleared.
No velocity is integrated after release and no distance beyond the target is
created. Integer endpoints finish exactly; fractional endpoints retain Q0-C's
existing direction/history-dependent sub-count semantics.

A new DOWN from the same Sender during settlement preserves the cumulative
target, realized history, played position, canonical integer ledger and absolute
MotionClock phase at the selected fixed cadence. It establishes only the new raw
touch origin and its fixed 12 ms timestamp mapping. Subsequent sample deltas add
to the existing target;
DOWN itself emits nothing and does not liquidate the previous backlog. Contacts
in an unfinished chain share quantizer state; after full settlement a new contact
starts a fresh chain. MOVE/UP without a new DOWN cannot alter a frozen target.

Sender replacement, presence disconnection, Reset, Stop, disposal and output
failure cancel the old tail under the existing motion lock and generation fence.
They do not pay discarded movement later. The existing no-adaptation rules apply.

Gesture still consumes accepted raw packets independently. Single tap timing,
double-tap drag and drag re-arm are unchanged; button UP occurs on raw UP, not at
Motion settlement completion. Remaining earned Motion can therefore occur after
a drag button has been released, and can overlap the next contact's button state.
This interaction is intentional in the current product Motion lifecycle and
remains covered by regression and game validation.

Validation is bounded build/test and Android-to-Virtual-HID Raw Input comparison,
not a new numerical certification. Evidence:
`windows/test-results/k24-earned-settle-20260915/` (ignored).

# 18. K24 Earned-Settle Cadence A/B (2026-09-15)

The three fixed modes below differ only in output opportunity period. Production
uses 1000 Hz only; 250 Hz and 500 Hz remain explicit development modes:

| Mode | Fixed period |
|---|---|
| `RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5_SETTLE` | 4 ms |
| `RESAMPLED_500HZ_FINITE_CRITICAL_K24_R5_SETTLE` | 2 ms |
| `RESAMPLED_1000HZ_FINITE_CRITICAL_K24_R5_SETTLE` | 1 ms |

All three retain K24-r5, tau 24 ms, support 120 ms, Earned-Settle, Q0-C,
run-fixed sensitivity (9/9 for this A/B), fixed 12 ms playout and libvirtualhid.
The former 250/1000 product selector and its persisted cadence field were removed
when 1000 Hz was selected as the sole production cadence. The Motion page has no
cadence control. Ordinary GUI and launcher starts always use the 1000 Hz mode,
independent of any legacy `motionCadenceHz` JSON field. An explicit
`--dev-motion-mode` remains authoritative for development runs and can select the
fixed 250/500/1000 modes before startup. There is no live cadence switch or runtime
adaptation: the active cadence cannot change in response to velocity, input, jitter
or load.
Playout and kernel support are converted from durations independently of the output period. The existing
continuous convolution, realized history and settlement tests use actual QPC
times; there is no tick-count-based 4 ms kernel/completion approximation.

MotionClock retains its high-resolution waitable timer and absolute QPC phase.
A late wake evaluates once at its actual time, skips overdue opportunities and
arms the next phase-aligned deadline. It does not replay missed ticks or emit
fractional fake events when Q0-C returns zero counts. The phase persists across
contacts that join unfinished settlement, at the selected fixed period.

The optional MotionTrace records the active period and whole-process GC
collection deltas alongside its existing CPU/allocation measurements. Capture
and trace observations must distinguish internal ticks, zero-count Logical
outputs and Raw Input delivery. Nominal 1000 Hz does not require 1000 nonzero
Raw Input reports; frame continuity across render rates is the comparison target.
Each new Receiver Runtime run begins a fresh MotionTrace segment. `BeginRuntimeRun`
refreshes the active MotionMode, RuntimeRunId and PeriodMs, and clears the previous
run ring before the new run can record events, so events from different Runtime
runs cannot be silently mixed.
The low-frequency Flight Recorder snapshots expose the actual run's mode and
cumulative clock/missed-tick counters for trace-OFF cadence verification; they do
not influence scheduling or Motion output.

Bounded cadence tests and Android/Raw Input evidence are stored under
`windows/test-results/k24-settle-cadence-ab-20260915/` (ignored). This is a cadence
A/B, not a new kernel or Q0-C numerical certification. A selected human-test run
uses trace OFF; instrumentation-on CPU is reported with that scope.

The earlier approximately 125 nonzero-report/s observation came from a background
Raw Input observer and does not describe foreground game-style delivery. The N60
control observed approximately 1000 Hz while foreground, approximately 125 Hz
while background, and approximately 1000 Hz again after foreground was restored.
This identifies a foreground/background observation effect; it does not establish
a global Windows 125 Hz limit or a specific responsible Windows kernel component.

With the observer foreground, fixed central 10-second windows measured about
238.5 / 466.2 / 883.5 Hz Raw nonzero delivery at configured 250 / 500 / 1000 Hz,
and about 238.5 / 466.2 / 882.5 Hz independent dispatch. Actual MotionClock rates
were about 250.0 / 499.6 / 996.1 Hz. The remaining difference is primarily Q0-C
zero-count logical ticks plus very few skipped opportunities; this is not evidence
of 1000 nonzero events/s, eliminated latency, or a universal best cadence.

Matched replay of the same accepted real Android traces produced identical final
integer dx/dy in all 18/18 paired 250-vs-1000 runs, with Earned-Settle completing
in all 36/36 arms. Formal cost windows show higher active-motion CPU, allocation
and context-switch cost at 1000 Hz, while recording a very low missed-opportunity
ratio, no catch-up replay, no mouse-output failure, and no runtime error or
disconnect. They remain historical objective evidence for the production decision,
not a claim that 1000 Hz is universally best.

# 19. Current Philosophy

The best Motion Engine is not the most complicated one.

The preferred solution:

- Small
- Deterministic
- Visually smooth and stable in sustained game-camera movement
- Precise and natural in micro-control
- Predictable in latency, reversal, settling and UP behavior
- Easy to tune
- Easy to understand

Complexity must be justified by measurable improvement.
