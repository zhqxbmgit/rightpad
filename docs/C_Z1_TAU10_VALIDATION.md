# RIGHTPAD C-Z1 TAU 10MS LOWER BOUND — IMPLEMENTATION & VALIDATION

**RETIRED FROM SOURCE — 2026-10-15, at the user's request.**

The unused Tau10 range expansion and its six experiment-only test groups have
been removed. Current source again accepts Tau20–100, with default Tau35.
The pre-removal safety gate was reproduced: 42/43 C parameter tests passed;
the stationary saturated-reversal gate failed. That failure is preserved below
and in `windows/test-results/remove-fir-tau10-20261015/baseline-cz1-tests.log`.
The removed test source is archived under that directory's `retired-source/`.
Original numerical evidence under `windows/test-results/cz1-tau10-20261010/`
remains intact. No production binary, saved setting or running Receiver was
changed by this source cleanup. The rest of this document is a historical report;
its source status and rollback/deployment directions are not current instructions.

2026-10-10, Asia/Shanghai. Workspace: `C:\rightpad`.

**IMPLEMENTED AND TESTED — NOT DEPLOYED**

**TAU 10MS NOT APPROVED — VALIDATION FAILED**

The requested range expansion is implemented and its UI/persistence/boundary
regressions pass. Release builds succeed. The stationary-output safety gate
fails on a permitted low-damping, low-acceleration, high-velocity combination.
Production is unchanged. No deployment or interruption approval is requested
for this build. The target has not silently been changed to 12 or 15 ms.

## Local baseline and exact scope

The actual local `Cz1Parameters.TauRange` was `(20, 100)`, matching the value
provided in the request. Default Tau was 35 ms. The working tree already had
uncommitted Android/control-authority changes and M-R1 FIR experiments; the
original C parameter tests and documentation were also modified. These were
snapshotted before work and retained. This audit does not attribute them to the
Tau change or claim the entire working tree is free of FIR.

Task changes:

1. `windows/Rightpad.Receiver/Settings/Cz1Parameters.cs`: `(20, 100)` becomes
   `(10, 100)`; the linear-stability comment changes `.020/.2` to `.010/.4`.
   This is the only production-code change.
2. `windows/Rightpad.Receiver.Tests/Cz1ParameterTests.cs`: reject 9.99 instead
   of 19.99, and include the new lower-bound test cases. Existing uncommitted
   tests remain intact.
3. New `windows/Rightpad.Receiver.Tests/Cz1TauLowerBoundTests.cs`: range/editor/
   persistence, lifecycle, eigenvalue, nonlinear sweep, exact stop-boundary and
   deployment-gate tests; detailed evidence comes from actual production math
   and a fake-output C12 pipeline.
4. `docs/C_Z1_RECEIVER_PARAMETERS.md`: source range and failed deployment status.
5. This new validation report.

No change is necessary in `Cz1SettingsViewModel`, `Cz1ParameterEditor.xaml` or
its keyboard handler, `SettingsFileStore`, `ZhqTrackpadDynamics`, or any motion
pipeline: they already obtain the range from the shared model. UI arrow keys
still use 1 ms steps. Decimal inputs remain supported. The default and six
other parameter ranges remain exactly:

| Parameter | Default | Inclusive range |
|---|---:|---:|
| Tau | 35 ms | 10–100 ms, source only |
| Amax | 80000 counts/s² | 20000–320000 |
| Vmax | 15000 counts/s | 100–30000 |
| Damping | 1 | 0.25–2 |
| Glide deceleration | 120000 counts/s² | 10000–240000 |
| Position stop | 0.5 counts | 0.01–2 |
| Velocity stop | 2 counts/s | 0.1–10 |

For previously valid snapshots all production arithmetic is unchanged: Tau's
range is only admission validation. Default independent Z1 oracle tests and
M-R1 historical fingerprints pass. No runtime coefficients, A/V caps, DT,
reconstruction, sensitivity, rounding, carry, output or scheduling changed.

## Discrete linear derivation

Let h=0.004 seconds, r=h/tau, z=dampingRatio, e=p-target for a fixed target,
and u=h*v. Without caps or the stop snap, the source performs:

```text
u[n+1] = -r² e[n] + (1 - 2zr) u[n]
e[n+1] = (1 - r²) e[n] + (1 - 2zr) u[n]

A = [[1-r², 1-2zr], [-r², 1-2zr]]
trace(A) = 2 - 2zr - r²
det(A)   = 1 - 2zr
lambda² - trace(A) lambda + det(A) = 0
```

The real second-order Schur/Jury conditions are:

```text
1 - trace + det = r² > 0
1 + trace + det = 4 - 4zr - r² > 0
1 - det        = 2zr > 0
```

Thus, for positive tau and damping, the strict condition is **r²+4zr<4**.
At Tau10, r=.4. Its worst damping z=2 yields .16+3.2=3.36, leaving 0.64
in this inequality. The z=2 lower stability boundary is approximately
8.472135955 ms, derived from r<2(sqrt(z²+1)-z); this is a mathematical boundary,
not an approved product lower bound.

| Tau10 damping | Eigenvalues | Spectral radius |
|---:|---|---:|
| 0.25 | 0.82 ± 0.357211422 i | 0.894427191 |
| 1 | 0.785329983, 0.254670017 | 0.785329983 |
| 2 | 0.903836718, −0.663836718 | 0.903836718 |

The maximum damping has a negative eigenmode, so an alternating decaying
component is possible; it is not an unstable discrete period-two cycle.
Its radius margin at Tau10 is approximately 0.096163282. A grid over Tau
10–100 in 0.1 ms increments and damping .25–2 in .01 increments checks
158,576 pairs. All satisfy strict linear stability; the largest radius is
0.9899494936611666, at Tau100 and damping .25.

## Caps and nonlinear behavior

The acceleration cap scales the entire spring-plus-damping vector by
alpha=min(1,Amax/|rawAcceleration|), then velocity is integrated and projected
onto the Vmax disk. A fixed alpha would replace r by sqrt(alpha)*r and damping
by sqrt(alpha)*z. It preserves the frozen-coefficient linear inequality, but
alpha varies with the state. Frozen eigenvalues do not prove the nonlinear
system globally converges or is comfortable to control.

Lower Tau raises the spring term as 1/tau² and the damping term as 1/tau.
At a fixed displacement the Tau10 spring is 12.25 times the Tau35 spring.
When raw acceleration saturates, damping is scaled down along with the spring.
Large overshoot and slow energy removal can follow, especially at damping .25
and Amax20000. Velocity clipping bounds step distance; it does not prove that
the position is monotonic or stationary promptly after the target stops.

Ordinary displacement is bounded by h*Vmax<=120 counts. Stop snaps move less
than the allowed position threshold (at most 2 counts). Two consecutive Java
rounding residuals contribute at most sqrt(2) counts in vector norm. The tested
bound is max(h*Vmax,positionThreshold)+sqrt(2). Observed maximum integer vector
length is 120.747670785 counts, consistent with that bound. Acceleration checks
apply to touching motion; glide deliberately uses its separate deceleration.

An initial test mistakenly used only one rounding residual in this bound.
That test-only bound was corrected, and the final bound uses both residuals.
The exploratory log is retained; no production code changed to make it pass.

## Offline numerical coverage

16,896 trajectories = 6 Tau values × 8 damping values × 32 combinations of
Amax/Vmax/glide/position-stop/velocity-stop endpoints × 11 trajectories:

- Tau: 10, 12, 15, 20, 35, 100 ms.
- Damping: .25, .5, .75, 1, 1.25, 1.5, 1.75, 2.
- Every endpoint of the other five ranges; default parameters additionally
  appear in response measurements and existing full C tests.
- Zero input; 1/2/5/10 counts X steps; 1000/−500 counts diagonal step; fast
  two-axis motion returning to zero; 180° reversal; X/Y reversal; continuous
  small reciprocation; UP glide.
- 30,000 fixed steps (120 seconds of integration) for stationary convergence.
  All sampled cases are finite, respect A/V and integer-output bounds, and
  eventually stop. There are no nonzero outputs or sign flips in their final
  two-second tails. No unbounded oscillation or permanent limit cycle was
  observed. This is sampled evidence, not a proof of every real-valued point.
- Tau10/damping2: all 352 endpoint trajectories pass the numerical checks.
- Real C12 pipeline comparisons at every requested Tau: requests during
  touching, UP pending and glide leave all prior output/times unchanged; Apply
  waits for idle. Duplicate and missed Tick opportunities perform no catch-up.
  Natural carry, next DOWN, interrupted chains, stale generations and Hard
  Reset are covered. M8/Q0-C output and schedules remain identical.
- Exact/adjacent stop values are tested at both endpoints, with minimum and
  maximum damping. Touching comparisons remain strict `<`; glide retains its
  inclusive `<= deceleration*DT + velocityThreshold` rule.

## Failed stationary-output gate

The actual C12 pipeline uses Amax20000, Vmax30000, damping .25, glide10000,
position threshold .01, velocity threshold .1. A held contact moves along
(30t,15t) counts at four-millisecond samples from t=4 through 476 ms, then
returns to (0,0) at t=480 ms and stays held. There is **no UP**, so this is
spring/cap settling rather than authorized glide. C12 finishes reconstruction
of the return at t=492 ms. The target subsequently stays zero.

At Tau10 and t=40 seconds: position error is 554.204926085 counts and speed is
1031.226214867 counts/s. Between 40 and 42 seconds, the pipeline emits 490
nonzero reports and eight axis-sign flips. It last emits at 45.860 seconds and
snaps at 46.040 seconds. The gate explicitly rejects this witnessed prolonged,
reversing stationary output; 40 seconds is a reproducible observation point,
not a newly established universal latency allowance.

| Tau | Last integer output from DOWN | Exact natural snap from DOWN | Time from fixed target to snap |
|---:|---:|---:|---:|
| 10 ms | 45.860 s | 46.040 s | 45.548 s |
| 12 ms | 38.940 s | 39.124 s | 38.632 s |
| 15 ms | 31.312 s | 31.584 s | 31.092 s |
| 20 ms | 23.860 s | 24.216 s | 23.724 s |
| 35 ms | 14.484 s | 15.108 s | 14.616 s |

All eventually converge. The historical 20/35 range also has a long transient
in this broader trajectory, an existing cap/damping corner that the earlier
1000/−500 step test did not expose. Tau10 substantially worsens it. This audit
does not fix that historical behavior or change the other six ranges.

Tau12 passes numerical convergence and the narrow observation of no output at
40–42 seconds; its approximately 38.6-second stationary tail does **not** merit
full deployment approval. Tau15 likewise cannot be approved as a replacement
lower bound based on this result. No alternative lower bound is selected.

## Default-parameter response and implications for M-R1 comparison

These direct-dynamics steps use unchanged six defaults and measure integration
time from a target already supplied to the servo. They exclude network time,
C12 reconstruction, opportunity alignment and game rendering.

| Tau | 1 count first integer | 10 counts 90% / snap | 1000 counts 90% / snap | 1000 counts maximum overshoot | Capped-acceleration steps before 1000 snap |
|---:|---:|---:|---:|---:|---:|
| 35 ms | 56 ms | 140 / 256 ms | 196 / 532 ms | 0 counts | 33 |
| 20 ms | 32 ms | 80 / 168 ms | 156 / 528 ms | 222.490 counts | 70 |
| 15 ms | 24 ms | 60 / 136 ms | 152 / 588 ms | 391.479 counts | 104 |
| 12 ms | 20 ms | 48 / 116 ms | 152 / 632 ms | 479.680 counts | 129 |
| 10 ms | 16 ms | 44 / 104 ms | 152 / 744 ms | 568.000 counts | 155 |

Lower Tau makes small unsaturated targets react faster; it does not necessarily
make large motions settle faster or become more precise. Default damping=1
does not prevent saturation-induced overshoot. One thousand counts is a
synthetic step, not a claim that every human movement overshoots this much.

For a synthetic full-speed 180° reversal, first accelerated to Vmax15000 toward
target +1e6 then reversed to −1e6, all five Tau values reverse velocity on the
47th fixed step, at 188 ms, and continue 1376.32 counts in the old direction
before that reversed step. Amax80000 is the limiting factor; lowering Tau does
not remove velocity inertia, the cap or glide.

C still has 12 ms reconstruction, 4 ms opportunities, position/velocity state,
A/V caps, threshold snaps, Java rounding and UP glide. M-R1 remains direct
P(t)=R(t), M8, Q0-C, 1 ms opportunities and reconstruction completion without
glide. Tau10 does not make C mathematically equivalent to M-R1.

If a future separately approved build passes the safety gate, human testing
should keep all six parameters and sensitivity unchanged and use
35 → 20 → 15 → 12 → 10 ms. At each value test fast cross-screen movement,
slow small-target adjustments, stopping, slight reverse correction and fast
reversal. Change only Tau, allow each busy chain to finish, and keep objective
and human acceptance distinct. That sequence is not an instruction to install
this failed build or automatically set Tau10.

## Build, regression and production protection

All outputs are isolated under
`windows/test-results/cz1-tau10-20261010/`, which is Git-ignored. A custom MSBuild
target uses the existing native DLL without rebuilding it. No live executable
is launched by these tests; Runtime cases use fake native output and independent
loopback endpoints. No ADB or Android build/deployment runs.

| Check | Result |
|---|---|
| Before-change Release build | 0 warnings, 0 errors |
| Before-change C parameter suite | 37/37 pass |
| Before-change M-R1/default Z1 oracle suite | 159/159 pass |
| Final Release build | 0 warnings, 0 errors |
| Final C parameter suite | 42/43 pass; stationary-output deployment gate fails |
| Final M-R1/default Z1 oracle suite | 159/159 pass |
| Final complete Receiver test suite | 739/740 pass; same one safety gate fails |
| Protected production/native/settings file hashes | 12/12 unchanged |

Accepted keyboard/JSON values include 10, 12, 15, 19.99, 20, 35 and 100. Values
9.99 and 100.01 fail admission; invalid JSON falls back to default Tau35.
Legacy JSON without `cz1` loads without rewriting. Apply remains memory-only,
Save remains disk-first, Restore Defaults edits the draft only, and failed Save
retains the applied snapshot. Requested/Active/Pending behavior is unchanged.

Running Receiver: PID **25804**, started **2026-10-10 14:01:59.686697 +08:00**,
`C:\rightpad\windows\Rightpad.Receiver\bin\Release\net8.0-windows\Rightpad.Receiver.exe`.
Broker: PID **5320**, created **2026-10-10 11:41:27.921528 +08:00**.
Broker executable path/command line are unavailable from the process query.
Both PID/start-time identities remain unchanged. No Stop, restart, native device
replacement, pointer setting, sensitivity, driver, Broker, network, Clash,
firewall or routing changes occurred. No Git reset/restore/clean/stash/commit/
push occurred.

Source hashes show all original files except the three explicitly changed
existing files are intact, including Android, ResampledMotion, MotionClock,
ZhqTrackpadDynamics, native output and the pre-existing FIR files. No new FIR
was added. Existing protocol tests pass and Android protocol source is unchanged
by this task.

The isolated Receiver directory contains these eight files, with a hash manifest:

```text
Rightpad.Receiver.exe
Rightpad.Receiver.dll
Rightpad.Receiver.pdb
Rightpad.Receiver.deps.json
Rightpad.Receiver.runtimeconfig.json
Rightpad.VirtualHid.dll
libvirtualhid-LICENSE.txt
lizardbyte-common-LICENSE.txt
```

The native DLL, licenses, deps and runtimeconfig match production exactly. These
are build outputs for review, **not approved deployment files**. Because the
safety gate failed, the conditional production backup/deployment preparation
step has not been entered; a complete deployable package must not be inferred
from build success. Settings have not been set to Tau10.

## Evidence and exact rollback

Evidence root: `C:\rightpad\windows\test-results\cz1-tau10-20261010`.

Key files: `before-source-hashes.json`, `before-source/`, `before-status.txt`,
`before.diff`, `before-protected-hashes.json`, `protection-verification.json`,
`process-identities.json`, `build.log`, `cz1-tests.log`, `all-tests.log`,
`baseline-mr1-tests.log`, `mr1-tests.log`, `nonlinear-sweep.csv`,
`nonlinear-failures.json`, `stationary-tail-tau10.csv` through Tau35,
`stationary-tail-summary.json`, `default-response.csv`, `default-reversal.csv`,
`isolated-build-manifest.json`, `task-only.diff` and `final-source-verification.json`.

Production rollback is **no action**: production binaries/settings have not
changed. Verify the production hash manifest and current Receiver/Broker
identities without stopping them. Do not deploy the isolated outputs.

To undo only this source task later, first verify there are no new edits after
this report using the final hashes. Copy the following original files from
`before-source` back to their corresponding workspace paths using literal
PowerShell paths, without any Git reset/restore/clean/stash:

```text
windows/Rightpad.Receiver/Settings/Cz1Parameters.cs
windows/Rightpad.Receiver.Tests/Cz1ParameterTests.cs
docs/C_Z1_RECEIVER_PARAMETERS.md
```

The original tests/doc snapshot includes the pre-existing uncommitted changes.
Retire only the two files newly created by this task:
`windows/Rightpad.Receiver.Tests/Cz1TauLowerBoundTests.cs` and
`docs/C_Z1_TAU10_VALIDATION.md`. Keep the ignored evidence/snapshots. Do not touch
any other uncommitted or untracked file. Then verify the restored three hashes
against `before-source-hashes.json`. This rollback has not been executed.

If a future validated deployment is authorized, create and verify a fresh full
production-directory and settings backup before the user-authorized interruption,
record current PID/start/command again, and retain a manifest. Its rollback must
stop only that newly deployed Receiver, restore that verified Receiver file set,
leave the Broker/driver/network untouched, preserve current settings unless
explicit settings rollback is requested, relaunch the recorded old command, and
verify hashes/input. This future transaction has not been prepared or run.
