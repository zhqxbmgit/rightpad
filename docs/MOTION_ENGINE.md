# rightpad Motion Engine Design

Current product baseline: Finite-Critical, 1000 Hz, 12 ms reconstruction,
Q0-C, Earned-Settle. M and C have identical behavior. Project requirements in
AGENTS.md are binding; research conclusions live only in
[MOTION_RND_CONCLUSIONS.md](MOTION_RND_CONCLUSIONS.md).

## Product purpose and fixed feel

Rightpad is a top-tier gaming touchpad for one-finger blind portrait use.
Continuous camera quality, low wobble, stable velocity, natural continuity,
micro-control and long-session comfort lead the Motion objectives. Gaming
compatibility, reliability, measurable response and latency also matter.
There is no universal accepted millisecond threshold and no claim that the
current system has eliminated latency or solved wobble.

Finger displacement earns relative mouse displacement. A held stationary target
may finish the short fixed filter settlement; it cannot integrate old velocity,
invent distance, glide, drift indefinitely or behave as a joystick. Prediction,
acceleration, dynamic gain, adaptive smoothing and runtime feel selection are
absent. Tau, Support, kernel coefficients, cadence and playout remain fixed for
the run regardless of velocity, noise, sampling, jitter, CPU/GPU load, FPS or game.
Correctness/lifecycle faults can invalidate input without changing normal feel.

## Current pipeline

```text
Android raw historical/current Touch samples and timestamps
 -> Windows live committed Sensitivity X/Y for each new real sample delta
 -> cumulative earned target
 -> fixed 12 ms reconstruction of known cumulative positions
 -> causal normalized finite-support Finite-Critical convolution
 -> sole canonical Q0-C integer ledger
 -> immediate existing libvirtualhid Move(dx,dy) for nonzero integers
```

Android remains a mouse input sensor. It does not implement mouse Motion,
sensitivity, smoothing or mouse gestures. Registered B/X controls retain their
separately approved local gesture/configuration/haptic responsibilities.

## Reconstruction and actual history

Each independent DOWN anchors Android time to local QPC, establishes raw position
and creates no displacement. Historical samples keep their order and timestamps.
Each real delta is scaled once and accumulated into the earned target; later
updates never rescale old targets, pending displacement or realized history.
Known cumulative sample positions are reconstructed linearly on the fixed 12 ms
timeline. Without a future segment, reconstruction holds the last known endpoint;
it cannot predict from old velocity.

Before admitting a packet, causal history advances using only the trajectory
known before its arrival. Late data cannot rewrite already realized history.
Duplicate timestamps coalesce at the existing point boundary; malformed/backward
timelines enter the deterministic arrival-order fallback until the next DOWN.
The 4096-point input buffer preserves the final target under bounded overflow and
reports loss of intermediate path. It never silently adapts window or delay.

## Finite-Critical

The selected kernel is a fixed causal position convolution with density
`h(u) = u * exp(-u / tau) / (tau^2 * Z)` for `0 <= u <= Support`, where
`Z = 1 - exp(-Support/tau) * (1 + Support/tau)`.
Normalization preserves a constant target. Analytic exponential moments integrate
the realized piecewise linear history; no numerical Motion planner is introduced.
The fixed 4096-segment history covers the product maximum Support and reports
capacity/nonfinite/history errors visibly rather than shortening Support.
Once the full support contains a constant endpoint, output converges to that
already-earned endpoint and parks. This is finite filter settlement, not glide.

## Product settings and run snapshot

| Setting | Default | Legal range | Step |
|---|---:|---:|---:|
| Tau | 24 ms | 8..60 ms | 1 ms |
| Support | 120 ms | 40..300 ms | 5 ms |

Both values come from committed Receiver settings at Runtime Start and remain
frozen in the immutable MotionConfiguration for that run. Current actual Saved
values must be read from settings/Diagnostics; 18/90 is not hardcoded.
There is no enforced ratio or automatic Support=tau*5 rule. Tau is the kernel
time constant; Support bounds causal history and is not a separate fixed delay.
Explicit Save commits disk first. Tau/Support apply only at the next successful
run start. Safe Restart retains its existing recovery/error behavior.

The approved manual-Save exception atomically hot-applies the committed Sensitivity
X/Y pair to new real displacement, including mid-contact. Existing targets,
pending output and history keep their earned scale. Draft/failed Save cannot
change input, and Save alone creates no movement. This is user configuration,
not automatic adaptation.

## Q0-C and native output

CanonicalPositionQuantizer owns cumulative integer I. For each binary64 position
P it classifies `trunc(P-I)` exactly without a second floating residual authority.
Both axes are validated before submission. A successful synchronous native call
precedes committing I; an ambiguous failure aborts the contact/run, without an
automatic retry. Zero dx/dy creates no native Move. Negative/positive reversal and
micro-displacement preserve the same canonical ledger and finite endpoint rules.
Native output is the production libvirtualhid Mouse backend. SendInput remains
only the explicit development diagnostic override, never a silent fallback.

## MotionClock

Product Motion opportunities are fixed 1000 Hz / 1 ms. MotionClock uses QPC
absolute deadlines and a high-resolution Windows waitable timer, not Thread.Sleep.
One actual wake evaluates at most one due Motion tick. A late wake skips obsolete
ticks, evaluates at actual QPC and resumes the same fixed phase; there is no
catch-up packet replay or cadence adaptation. Delay and kernel Support are
converted from durations independently of cadence. 1000 Hz is the opportunity
rate, not a promise of 1000 nonzero packets, hardware polls or game-consumed
reports per second.

## Earned-Settle and lifecycle

UP freezes the last real cumulative target and ends the physical contact without
an immediate movement flush. Reconstruction/kernel/Q0-C continue only until the
already-earned endpoint finishes, then park. A late MOVE/UP cannot add to a released
contact. Same-run DOWN during unfinished settlement joins the old chain, retains
history, ledger and clock phase, and establishes a new raw finger origin without
creating distance. Once fully settled, the next DOWN starts a clean baseline.

Reset, malformed input failure, cancel, sender invalidation, presence expiry and
Receiver Stop abort unearned pending tails and clear motion/session/generation.
Stale scheduled wakes cannot emit after invalidation. Buttons and gamepad state
retain their independent safety release. Stop joins the Motion writer before
disposing the shared native mouse; there is no native batching/pending ledger.

## Profile labels

M is production. C is a future experiment slot with the exact same current path.
Requested and Active remain distinct: changes defer across stationary held
contacts and unfinished earned settlement. New DOWN joining settlement retains
the old active label; the latest request applies only after the chain clears.
The label never rebuilds the kernel or changes gain, cadence, mapping or history.
Type7 wire/admission/order behavior remains in
[MOTION_PROFILE_PROTOCOL.md](MOTION_PROFILE_PROTOCOL.md).

Diagnostics shows Requested Profile, Active Motion Profile, Motion Algorithm
Finite-Critical, actual Active Tau/Support and Native Output Cadence 1000 Hz for
both labels. No removed planner, jerk/clamp or batching fields remain.

## Diagnostics and regression

H1 remains the bounded passive Manual Hitch Flight Recorder with its unchanged
record schema, retention and manual Freeze. Logical Output includes zero events;
NativeSubmit brackets actual managed native movement calls. These observations
do not establish hardware report timing or game consumption, and Android/Windows
clocks are unsynchronized. See [HITCH_FLIGHT_RECORDER_H1.md](HITCH_FLIGHT_RECORDER_H1.md).
The independent low-frequency FlightRecorder and existing opt-in MotionTrace
diagnostics remain; neither changes Motion behavior.

Formal tests retain finite-critical convolution, Q0-C, reconstruction, Earned-Settle,
MotionClock, legal Saved parameters, live sensitivity, protocol, buttons, B/X,
H1 and production M golden fingerprints. Profile M/C Motion Equivalence verifies
exact continuous position, every logical/native event, submission time, settle,
missed-tick behavior and endpoint under identical input/timing/settings.
Debug/Release, native fake and actual-device functional smoke are required after
runtime changes. Functional tests cannot claim subjective human game feel.

Motion R&D is paused. Wobble is deferred to separate work. Camera image tracking
is CLOSED / INCONCLUSIVE. Experimental production code/tests/fixtures/docs,
harnesses and raw data are deleted. All 43 explicitly authorized cleanup targets
are gone; the sole retained research history is MOTION_RND_CONCLUSIONS.md.
