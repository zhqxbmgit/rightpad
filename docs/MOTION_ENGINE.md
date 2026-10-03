# rightpad Motion Engine Design

Current selected baseline (2026-10-03): Touchpad R&D is paused. The deployed
Receiver is explicitly launched as `M_R1`, paired with the normal Android App's
fixed unbuffered acquisition and M/C control. M uses M8, direct P(t)=R(t), Q0-C,
1 ms opportunities, reconstruction-completion release and no glide; C retains
the C-Z1 parameters and C12 described below. Saved Tau/Support do not shape
M-R1 output. The code's `ProductionMode` default remains M-F1, and all M12/M-F1
historical fingerprints remain regression references. The candidate/stage-only
wording below records the original implementation stage, before the later
authorized deployment and baseline selection. It does not describe the current
deployed selection or claim superior game feel.

Production M-F1 source: Finite-Critical, 1000 Hz, 8 ms reconstruction,
Q0-C, Earned-Settle for M. C source is recovered to Phase Z1 (Amax 80000),
with unchanged 12 ms reconstruction. Project requirements in
AGENTS.md are binding; research conclusions live only in
[MOTION_RND_CONCLUSIONS.md](MOTION_RND_CONCLUSIONS.md).

Explicit development identity `M_R1` is the separate Phase M-R1 candidate below;
it does not replace the production identity or historical baseline fingerprints.

## Phase M-R1 direct reconstructed position experiment (2026-10-03)

H-MR1 tests whether removing Finite-Critical position dynamics yields a more
direct relative touchpad character. No conclusion of superior game feel is made.
The candidate is selected only with `--dev-motion-mode M_R1` (or the launcher's
explicit development argument after a separately authorized deployment).

```text
accepted Android raw/historical samples
 -> live committed Sensitivity once per new real delta
 -> cumulative earned target at mapped timestamps
 -> existing fixed M8 causal piecewise-linear reconstruction R(t)
 -> P(t) = R(t), taken from Tick's baseX/baseY before position convolution
 -> unchanged canonical Q0-C successful-submit ledger
 -> immediate existing native Mouse output for nonzero integers
```

Latest target is not directly emitted. It can contain future, unmatured input.
There is no packet-arrival emission, Q0-I residual, ordinary round, double
integration, prediction, new smoothing, correction, velocity control or glide.
The existing 1 ms MotionClock, absolute phase, actual-time evaluation, skipping
missed opportunities and generation fences are unchanged. Known endpoints permit
intermediate positions on every valid opportunity; no future endpoint means hold.

### Minimal capability and causal-history boundary

Profile support, canonical quantization, pending-release lifecycle and causal
history have explicit capabilities. `HasPositionFilter` excludes M-R1;
`MaintainsCausalHistory` does not. Admission still advances known history before
new samples. Stale sampled wakes are clamped to the causal watermark. Pre-first
point holds, duplicate coalescing, mapping/fallback and late-input rules retain
their existing behavior. The 4096-point queue still exposes intermediate-path
loss on overflow while preserving the final target.

To keep this a single mechanism change, the existing `CausalFiniteCritical`
object remains a bounded history recorder via Add/Trim/Reset only. Its 4096-segment
capacity, contiguous/nonfinite checks and visible fault remain. The frozen run's
Saved history horizon is retained; no new history algorithm or capacity is added.
**M-R1 never calls its Position convolution or its IsSettled completion test.**
Tau/Support do not shape candidate output or release time. They remain saved for
filtered baselines and are not Active M-R1 algorithm parameters. Fault handling
can still abort malformed/dense histories; it never adapts the normal trajectory.
The Q0-C class, MotionClock, C dynamics, native bridge and Android are unchanged.

### Release, joining and profile boundaries

The logical states are Idle, Touching and ReleasedAwaitingPlayout; the existing
session/pending flag represents them without a new state framework. UP includes
its final real sample once, seals the endpoint and keeps the existing mapped final
point. It neither flushes the latest target nor adds a second playout delay.
At the first valid tick with the last point mature, Q0-C evaluates that endpoint;
only then does the chain clear/park and apply the latest Requested profile.
Zero integer delta alone is never a completion criterion. There is no filter debt
or fractional-remainder settlement. MotionTrace names reconstruction release,
continuation and completion separately from filter settlement.

Same-run DOWN during pending reconstruction continues the old target, Q0-C ledger,
phase, queue and Active M8, establishing only a new raw coordinate origin. Its
baseline cannot precede the pending tail. DOWN after completion starts fresh;
there is no new completed-chain carry. Earlier completion than M-F1 can change
which rapid re-touches join, an explicit consequence covered by boundary tests.
Touching (including stationary held) and release-pending defer profile requests.
C-Z1 keeps C12 and its exact touching/release/glide/carry semantics. Hard lifecycle
abort clears pending work without output; stale wakes cannot resume it.

### Deterministic evidence and interpretation

`Mr1MotionTests` uses an independent linear reconstruction oracle and binary64
dyadic-rational Q0-C oracle. It checks nonzero intermediate positions, endpoint,
every native delta/time and zero logical events before Clear, not just cleared
zero state. Fractional endpoints retain Q0-C directional history. A missed tick
can skip a reversal and change that legal fractional ledger; correctness is
defined against the actual evaluation sequence, not replaying obsolete ticks.
Integer endpoints are exact; repeated final evaluation emits no extra distance.

Roughness fixtures expose, without repair: timely low-rate linear ramps; irregular
spacing with slopes +2/-3/+1 counts/ms; no-lookahead holds; a late real endpoint
jump from R=4 to R=12 on the next valid tick. The observed output prefix is never
rewritten. These are mechanism evidence, not camera-quality or human acceptance.

The suite covers UP, joining before/at/after completion, live disk-first Save,
duplicates/fallback/late input, causal wake watermark, queue/history capacity,
profile admission and deferral, abort/failure, H1 and truthful Diagnostics/trace.
M-F1 and M-R1 containers compare Active C state/native output EXACT at every
fake-time step, including missed wakes, late/duplicate/fallback input, pending and
gliding interruptions and live Sensitivity. Existing independent C-Z1 oracle
tests and M12/M-F1 fingerprints remain unmodified.

| M-R1 identity | Fingerprint |
|---|---|
| Ordinary | `9280E2AFAF78A13D52A24708697330C08057BA9EE00A5C6F3AFD01D146B3CC68` |
| Missed tick | `F4964FB39C0402429904D8EBEA101C9B0AAB7699B3DE448CC26C3631F72F67FF` |

Fingerprints were pinned after independent oracle validation and a source method/
protected-file comparison, preserved in ignored `windows/test-results/phase-mr1/`.
The first test failure log and its fixture root-cause analysis remain there.
This stage builds/tests only into isolated evidence outputs: no deployment,
Stop/Restart of the live Receiver, Android build, human game test or Git write.
Active Diagnostics reads M-R1 / M8 / Direct reconstructed position / Q0-C /
1 ms opportunities / Reconstruction-completion / No glide / Tau N/A / Support N/A.
Saved Tau/Support remain on disk. Runtime startup reports the actual mode delay.

## Product purpose and fixed feel

Rightpad is a top-tier gaming touchpad for one-finger blind portrait use.
Continuous camera quality, low wobble, stable velocity, natural continuity,
micro-control and long-session comfort lead the Motion objectives. Gaming
compatibility, reliability, measurable response and latency also matter.
There is no universal accepted millisecond threshold and no claim that the
current system has eliminated latency or solved wobble.

For production M, finger displacement earns relative mouse displacement. A held stationary target
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
 -> Active M-F1 fixed 8 ms reconstruction of known cumulative positions
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
Known cumulative sample positions are reconstructed linearly on the fixed M8/C12
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

## Phase M-F1 fixed M-only reconstruction experiment

H-MF1 tests whether M's former fixed 12 ms frontend contributes to latency and
weight. The only M Motion change is reconstruction/playout 12 -> 8 ms. Historical
device sampling (about 4.21 ms/sample, effective 203–236 Hz, aggregate about
222 Hz) motivates this hypothesis; it does not prove 8 ms safe or human accepted.
Only 8 ms is tested. Human game acceptance is a later deployment stage.

ResampledMotion selects the fixed delay at construction/idle ApplyRequestedProfile
from Active, never Requested. Its actual delay ticks remain frozen throughout a
busy chain. MapTime, fallback, DOWN anchor, joining DOWN and ArmClock consume that
same selected delay; interpolation, AdvanceHistory and queue rules are unchanged.
M touching/settling and joining DOWN retain 8 ms until the earned boundary applies
C. C touching/release-pending/glide and interrupted DOWN retain 12 ms until natural
completion applies M. Reset/new sender run safely invalidates old generations.
Other explicit development cadences retain their historical 12 ms delay.

Saved Tau/Support remain read at Runtime Start (current human values 18/90, not
hardcoded); live committed Sensitivity preserves the existing disk-first contract
(current human values 6/6). Finite-Critical, Q0-C, 1 ms opportunities, Earned-Settle,
UP/joining semantics, actual-time evaluation, skip/no-catchup, integer ledger,
immediate native submit and passive H1 schema are unchanged. There is no velocity
prediction, extrapolation, adaptive buffering or runtime delay setting.

Earlier mapped boundaries intentionally can classify a given arrival as late
where M12 did not. The fixed causal policy stays the same: advance only already
known history before admission, never rewrite realized history. Dense history
capacity faults visibly abort with the same M12 error rather than adapting delay.

Test-only Fixtures/M12HistoricalMotion.cs retains the saved pre-M-F1 M12 Motion
mechanics with its class/constructor identifier renamed and a historical C
diagnostic label. It preserves both historical M12 fingerprints. Deterministic
M-F1 comparisons validate ordered reconstruction,
filtered position, every Q0-C native delta/time and equal earned endpoint on a
4 ms translated timely timeline. A discriminating stimulus produces reconstruction
at 9 vs 13 ms and first native output at 10 vs 14 ms, with endpoint 6000 in both.
These are fake-clock fixture results, not measured game or hardware latency.

| Identity | Ordinary fingerprint | Missed-tick fingerprint |
|---|---|---|
| M12 historical / 12 ms | `72FFC866FE2B59C9FB90B9FBA4DE229028EEA6895E6AADC3A815FF0D056C0F49` | `A3719B0D077F924ECEE47B4857B1EEA844EAB01CD3141A7D600374A37B759D08` |
| M-F1 / 8 ms | `ABF432CDEEABA0E9C90A56610DB338B818F154401331A9990715883AE4D5F734` | `514F6145B3864CF48A075EE350BCAC5CD09D67B41FF7BC140F3CA40986FE1D3D` |

M-F1 expected values were pinned only after the saved-Z1 source normalization
proof and deterministic translated behavior review passed. M12 expected values
were preserved unchanged and still execute against the frozen historical source.

## Completed Phase Z2 and C-Z1 recovery

Z2 changed touching maxAcceleration 80000 -> 160000 only. Human validation is
complete: Z1's gimbal character was stronger and preferred; Z2 reduced it. True
glide was not subjectively obvious and did not hinder operation. M-F1 precisely
restores the C core to the saved Z1 source and restores C-Z1 diagnostics/trace/test
identity. Fixed DT/tau/damping/vector caps/glide/rounding/carry/UP/lifecycle and
12 ms reconstruction are the Z1 contract below. First capped Z1 step is velocity
320, position 1.28 and integer delta 1; 640/2.56/3 would identify erroneous Z2.
phase-z1 and phase-z2 evidence are preserved; phase-mf1 evidence is ignored and
unstaged. This stage does not deploy, Stop or Restart the existing live Receiver.

## Phase Z1 reference dynamics and shared profile lifecycle

C-Z1 was "zhq dynamics-faithful, Rightpad gain/transport-normalized" using
[TrackpadContext.java at the pinned zhq revision](https://github.com/zhqxbmgit/zhq/blob/2eff689a2f3a7b645c977900ecfcca7a5069f603/app/src/main/java/com/limelight/binding/input/touch/TrackpadContext.java).
This explicit experiment exception authorizes true glide only for manually
selected C; M-F1 retains the M downstream production semantics with its sole 8 ms offset change. No claim of better game feel is made.

C retains the Rightpad Android/UDP input, current-run/source authority, live
committed Sensitivity, final real UP delta and fixed 12 ms timestamp mapping/
reconstruction. It replaces only the downstream Finite-Critical/Q0-C path.
ZhqTrackpadDynamics owns targetAccum/currentPos/currentVel/lastSent/carryOver and
reference math; ResampledMotion owns scheduling, release markers and lifecycle.

Fixed parameters: DT=0.004 s, tau=0.035 s, dampingRatio=1, maxVelocity=15000,
maxAcceleration=80000, glideDeceleration=120000, POS_THRESHOLD=0.5,
VEL_THRESHOLD=2.0. Touching first computes target-currentPos, distance and speed.
If distance <0.5 and speed <2, it snaps position to target and zeros velocity.
Otherwise it computes acceleration as dist*omega*omega - velocity*2*damping*omega,
vector-caps acceleration, updates velocity, vector-caps velocity, then integrates
position with that new velocity. No overshoot/path/earned-distance clamp,
interpolation, variable DT, substep, prediction or adaptive parameter is added.

The existing MotionClock schedules a fresh phase at DOWN+12 ms and then fixed
4 ms / 250 Hz nominal opportunities. Packet arrival never steps the servo.
Without future samples, reconstruction holds the last known target. A due wake
executes at most one DT=0.004 step and emits its integer delta immediately.
A late wake counts/skips obsolete deadlines and resumes the same absolute phase;
it does not compensate elapsed time or burst. Missed steps may extend glide's
wall-clock duration. M retains its existing 1 ms deadline/missed-wake behavior.

C quantizes currentPos-lastSent with Java Math.round-compatible half-toward-positive
semantics (+0.5 -> +1, -0.5 -> 0), including adjacent representable values.
Its independent integer ledger advances only after a successful native submission.
It never also passes through Q0-C. Zero integer deltas create no native move.

Raw UP ends physical contact and freezes a release marker at the final existing
mapped UP boundary, without a second 12 ms delay. Until that marker, C continues
touching steps. The first due tick at/after the marker enters Gliding before its
step. Glide ignores the target: if speed <=480+2 it zeros velocity without
integrating position and naturally stops; otherwise it proportionally subtracts
480 from speed and integrates the new velocity. It does not finish the earned
target, run Earned-Settle, or force endpoint equality. Endpoint overshoot and
undershoot are expected reference behavior.

DOWN initializes target/currentPos from the existing natural carryOver, zeros
velocity and lastSent, emits no movement and establishes a new phase. DOWN
interrupting release/glide retains Active C, discards the old queue/marker and
increments generation. It does not save a new interrupted fractional remainder
or withdraw already submitted native counts. Only natural shouldStop saves
currentPos-lastSent as carryOver after that tick's successful emission.

Touching, ReleasedAwaitingPlayout and Gliding are all busy chains, including a
stationary held contact. Type7 updates only Requested; latest Requested applies
at natural stop. Active/Requested C retains natural carry. Real C -> M clears
C target, velocity, integer ledger, carry, marker and schedule; M starts clean.
M -> C waits for the M Earned-Settle boundary, including joining DOWN.
Reset, sender-run invalidation, presence expiry, Stop, Dispose and native failure
abort all C state immediately without flush, tail output or ambiguous retry.
Stale-generation wakes produce zero output. Same-run source IP correlation retains
the existing Touch admission contract; it is not a new source isolation rule.

Reference normalization remains explicit: Rightpad input/timestamp reconstruction
and live Sensitivity replace Android view scaling and reference x7 gain; fixed
parameters replace per-contact preference reads; libvirtualhid immediate integer
submission replaces NvConnection/final output scaling, with commit after success;
Rightpad absolute deadlines skip missed opportunities instead of Java scheduled
catch-up; UP waits for the existing mapped marker and includes the final real
UP delta; Rightpad type7/lifecycle fences replace reference ticker ownership.
No reference tap/click/drag/haptic behavior is imported.

Diagnostics separates Requested and Active. Active C-Z1 shows reconstruction
12 ms, zhq-derived servo, Tau 35 ms, Amax 80000, Vmax 15000,
4 ms / 250 Hz nominal dynamics, Java-compatible rounding and true glide; Active
M-F1 shows reconstruction 8 ms, actual Saved Finite-Critical Tau/Support, Q0-C,
1 ms / 1000 Hz and Earned-Settle. MotionTrace
metadata identifies active dynamics and records profile transition QPCs for mixed
exports. H1 records remain unchanged; Output is interpreted under Active profile.

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
H1 and both historical M12 golden fingerprints and independent M-F1 fingerprints. M deferred-request
equivalence verifies exact continuous/kernel/logical/native/time/settle/deadline
state across Saved configurations. C has an independent dynamics/reconstruction
oracle and explicit threshold, rounding, glide/carry, missed-step and lifecycle gates.
Debug/Release and native fake are required for this source-only stage. Actual-device functional smoke and human game acceptance follow later authorized deployment. Functional tests cannot claim subjective human game feel.

C-Z1 is the selected fixed C baseline; M-F1 remains production and M-R1 is the
explicit direct-position development hypothesis. Other Motion R&D
remains paused. Wobble is deferred to separate work. Camera image tracking
is CLOSED / INCONCLUSIVE. Experimental production code/tests/fixtures/docs,
harnesses and raw data are deleted. All 43 explicitly authorized cleanup targets
are gone; the sole retained research history is MOTION_RND_CONCLUSIONS.md.
